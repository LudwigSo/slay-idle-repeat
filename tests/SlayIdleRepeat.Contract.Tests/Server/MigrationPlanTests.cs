using Shouldly;
using SlayIdleRepeat.Adapters.Persistence.Postgres;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Server;

/// <summary>The migration runner's pure half: ordering, naming, checksums — no database anywhere.</summary>
public sealed class MigrationPlanTests
{
    private static MigrationScript Script(string name) => new(name, "-- " + name);

    [Fact]
    public void Ordered_sorts_by_the_four_digit_ordinal_not_by_discovery_order()
    {
        var shuffled = new[]
        {
            Script("0003_economy_events.sql"),
            Script("0001_players_and_runs.sql"),
            Script("0002_idempotency.sql"),
        };

        MigrationPlan.Ordered(shuffled).Select(s => s.FileName).ShouldBe(new[]
        {
            "0001_players_and_runs.sql",
            "0002_idempotency.sql",
            "0003_economy_events.sql",
        });
    }

    [Fact]
    public void A_duplicate_ordinal_is_refused_naming_both_files()
    {
        var clash = new[] { Script("0001_players.sql"), Script("0001_runs.sql") };

        var refused = Should.Throw<InvalidOperationException>(() => MigrationPlan.Ordered(clash));

        refused.Message.ShouldContain("0001_players.sql");
        refused.Message.ShouldContain("0001_runs.sql");
    }

    [Fact]
    public void A_gap_in_the_sequence_is_refused_naming_the_missing_ordinal()
    {
        var gapped = new[] { Script("0001_players.sql"), Script("0003_events.sql") };

        Should.Throw<InvalidOperationException>(() => MigrationPlan.Ordered(gapped))
            .Message.ShouldContain("0002");
    }

    [Fact]
    public void A_history_that_does_not_start_at_one_is_refused()
    {
        Should.Throw<InvalidOperationException>(
                () => MigrationPlan.Ordered(new[] { Script("0002_players.sql") }))
            .Message.ShouldContain("0001");
    }

    [Theory]
    [InlineData("0001-players.sql")]
    [InlineData("01_players.sql")]
    [InlineData("0001_Players.sql")]
    [InlineData("0001_players.txt")]
    [InlineData("players.sql")]
    public void A_file_name_outside_the_migration_spelling_is_refused(string name)
    {
        Should.Throw<InvalidOperationException>(() => MigrationPlan.Ordered(new[] { Script(name) }))
            .Message.ShouldContain(name);
    }

    // NIST FIPS 180-4 vectors: SHA-256("") and SHA-256("abc"), as published.
    [Theory]
    [InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    public void The_checksum_is_sha256_lowercase_hex_against_the_published_vectors(string sql, string expected)
    {
        MigrationPlan.ChecksumOf(sql).ShouldBe(expected);
    }

    [Fact]
    public void The_advisory_lock_key_is_pinned()
    {
        // "SIR_MIGR" as big-endian ASCII. Two builds disagreeing on this stop excluding each other,
        // so it is a wire-like constant, pinned to its value rather than to its derivation.
        MigrationPlan.AdvisoryLockKey.ShouldBe(0x5349525F4D494752L);
    }

    /// <summary>
    /// 🔒 Steering S3: the shipped history this runner exists for, by identity. A resource rename or
    /// a broken embed manifests here as the missing file's name, not as a quietly shorter list.
    /// </summary>
    [Fact]
    public void The_shipped_history_is_exactly_the_files_this_build_carries_in_order()
    {
        var shipped = MigrationPlan.Ordered(PostgresMigrations.All());

        // ⚠️ 0005 and 0006 are held by tasks running concurrently with the one that added 0007, and
        // Ordered() refuses a gap outright — so until those land this fact is RED on the ordinal
        // rule, not on this list. Whoever integrates them adds their two names here; if either task
        // ships without a migration, 0007 is renumbered down instead.
        shipped.Select(s => s.FileName).ShouldBe(new[]
        {
            "0001_players_and_runs.sql",
            "0002_idempotency.sql",
            "0003_economy_events.sql",
            "0004_player_messages.sql",
            "0007_moderation.sql",
        });

        shipped.ShouldAllBe(
            s => s.Sql.Contains("CREATE TABLE", StringComparison.Ordinal),
            "every file creates its tables; an empty or truncated embed would apply cleanly and "
            + "leave the schema silently short.");
    }

    /// <summary>
    /// 🔒 The economy log's append names exactly the columns its table declares — the one drift a
    /// live database would catch and nothing here otherwise could.
    /// </summary>
    /// <remarks>
    /// Steering S25. <c>PostgresEconomyEventLog</c> has no production caller yet: the appends ride
    /// inside the accepted command's one transaction, which the unit-of-work task composes. So a
    /// column renamed on one side of the pair would sit undetected until that task's first live
    /// run. The set comparison runs both ways — a column the table gained and the append forgot,
    /// and a column the append writes that the table never had.
    /// </remarks>
    [Fact]
    public void The_economy_log_append_names_exactly_the_columns_its_table_declares()
    {
        var declared = TableColumnsOf("0003_economy_events.sql", "economy_events")
            // GENERATED ALWAYS AS IDENTITY: the database writes it, so the append must not.
            .Where(column => column != "id")
            .ToArray();

        var written = InsertColumnsOf(PostgresEconomyEventLog.AppendStatement);

        written.OrderBy(c => c, StringComparer.Ordinal).ShouldBe(
            declared.OrderBy(c => c, StringComparer.Ordinal),
            "0003_economy_events.sql and PostgresEconomyEventLog.AppendStatement are the two halves "
            + "of one row, and no test reaches the pair through a database.");

        ParameterCountOf(PostgresEconomyEventLog.AppendStatement).ShouldBe(
            written.Length,
            "a column list longer than its VALUES list is a statement Npgsql refuses at execute "
            + "time — which, with no caller, is nowhere.");
    }

    /// <summary>The column names one CREATE TABLE declares, in declaration order.</summary>
    private static string[] TableColumnsOf(string fileName, string table)
    {
        var sql = MigrationPlan.Ordered(PostgresMigrations.All())
            .Single(s => s.FileName == fileName).Sql;

        var body = sql[(sql.IndexOf("CREATE TABLE " + table + " (", StringComparison.Ordinal)
            + ("CREATE TABLE " + table + " (").Length)..];
        body = body[..body.IndexOf(");", StringComparison.Ordinal)];

        // The first token of each definition line. Table constraints (UNIQUE, PRIMARY KEY, CHECK)
        // are spelled upper-case and drop out; column names are lower snake by this repo's schema.
        return body.Split('\n')
            .Select(line => line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty)
            .Where(token => System.Text.RegularExpressions.Regex.IsMatch(token, "^[a-z][a-z0-9_]*$"))
            .ToArray();
    }

    /// <summary>The column names one INSERT writes.</summary>
    private static string[] InsertColumnsOf(string statement)
    {
        var open = statement.IndexOf('(');

        return statement[(open + 1)..statement.IndexOf(')', open)]
            .Split(',')
            .Select(column => column.Trim())
            .ToArray();
    }

    /// <summary>How many <c>@name</c> placeholders one statement carries.</summary>
    private static int ParameterCountOf(string statement) =>
        System.Text.RegularExpressions.Regex.Matches(statement, "@[a-z]+").Count;

    [Fact]
    public void The_message_table_is_indexed_the_way_the_inbox_reads_it()
    {
        var messages = MigrationPlan.Ordered(PostgresMigrations.All())
            .Single(s => s.FileName == "0004_player_messages.sql");

        // Whitespace-normalized so a reformat cannot break it; the statement itself must be there,
        // not just the column pair somewhere in a comment.
        var normalized = System.Text.RegularExpressions.Regex
            .Replace(messages.Sql, @"\s+", " ")
            .ToLowerInvariant();

        normalized.ShouldContain(
            "create index",
            Case.Sensitive,
            "the inbox reads by owner and expiry; without the index every read is a table scan.");
        normalized.ShouldContain("on player_messages (player_id, expires_at_utc)", Case.Sensitive);
    }
}
