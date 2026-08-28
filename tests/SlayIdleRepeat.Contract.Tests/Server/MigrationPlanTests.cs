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

        // ⚠️ An ordinal cannot be reserved: Ordered() refuses a gap outright, and the runner applies
        // the history at boot — so a file numbered past the next free slot stops the server from
        // starting rather than waiting politely for its neighbours. A migration therefore only ever
        // takes current + 1, and whoever merges second renumbers theirs and this list with it.
        shipped.Select(s => s.FileName).ShouldBe(new[]
        {
            "0001_players_and_runs.sql",
            "0002_idempotency.sql",
            "0003_economy_events.sql",
            "0004_player_messages.sql",
            "0005_moderation.sql",
            "0006_auth.sql",
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

    /// <summary>Each auth table paired with the INSERT that writes its rows.</summary>
    public static TheoryData<string, string> AuthInserts() => new()
    {
        { "auth_devices", PostgresAuthStore.InsertDeviceStatement },
        { "auth_token_families", PostgresAuthStore.InsertFamilyStatement },
        { "auth_refresh_tokens", PostgresAuthStore.InsertRefreshTokenStatement },
        { "auth_account_deletions", PostgresAuthStore.InsertAccountDeletionStatement },
        { "auth_deletion_tombstones", PostgresAuthStore.InsertDeletionTombstoneStatement },
    };

    /// <summary>Each auth table paired with an UPDATE that mutates its rows.</summary>
    public static TheoryData<string, string> AuthUpdates() => new()
    {
        { "auth_token_families", PostgresAuthStore.RevokeFamilyStatement },
        { "auth_refresh_tokens", PostgresAuthStore.RotateRefreshTokenStatement },
        { "auth_devices", PostgresAuthStore.TouchDeviceStatement },
        { "auth_token_families", PostgresAuthStore.RevokeFamiliesForPlayerStatement },
    };

    /// <summary>Each auth table paired with a SELECT that reads its whole row back.</summary>
    /// <remarks>
    /// A partial SELECT belongs in <see cref="AuthPartialSelects"/> instead — the two are compared
    /// differently, and running a whole-row comparison over a one-column read would fail forever.
    /// </remarks>
    public static TheoryData<string, string> AuthFullRowSelects() => new()
    {
        { "auth_devices", PostgresAuthStore.SelectDeviceStatement },
        { "auth_devices", PostgresAuthStore.SelectDeviceForPlayerStatement },
        { "auth_refresh_tokens", PostgresAuthStore.SelectTokensInFamilyStatement },
    };

    /// <summary>Each auth table paired with a SELECT that reads back only some of its columns.</summary>
    public static TheoryData<string, string> AuthPartialSelects() => new()
    {
        { "auth_account_deletions", PostgresAuthStore.SelectAccountDeletionStatement },
        { "auth_account_deletions", PostgresAuthStore.SelectDeletedPlayersStatement },
    };

    /// <summary>
    /// 🔒 Every auth INSERT names exactly the columns its table declares — the same S25 pin the
    /// economy log carries, for the same reason: <c>PostgresAuthStore</c> speaks to a database no
    /// tier here starts, so a column renamed on one side would surface only on a live run.
    /// </summary>
    [Theory]
    [MemberData(nameof(AuthInserts))]
    public void An_auth_insert_names_exactly_the_columns_its_table_declares(string table, string statement)
    {
        var declared = TableColumnsOf("0006_auth.sql", table);
        var written = InsertColumnsOf(statement);

        written.OrderBy(c => c, StringComparer.Ordinal).ShouldBe(
            declared.OrderBy(c => c, StringComparer.Ordinal),
            "0006_auth.sql and PostgresAuthStore." + table + "'s INSERT are the two halves of one "
            + "row. The comparison runs both ways: a column the table gained and the INSERT forgot, "
            + "and a column the INSERT writes that the table never had.");

        ParameterCountOf(statement).ShouldBe(
            written.Length,
            "a column list longer than its VALUES list is a statement Npgsql refuses at execute "
            + "time — which, with no caller yet, is nowhere.");
    }

    /// <summary>🔒 Every auth UPDATE touches only columns its table declares, one parameter each.</summary>
    [Theory]
    [MemberData(nameof(AuthUpdates))]
    public void An_auth_update_touches_only_columns_its_table_declares(string table, string statement)
    {
        var declared = TableColumnsOf("0006_auth.sql", table);
        var touched = ParameterisedColumnsOf(statement);

        touched.ShouldNotBeEmpty(
            "an UPDATE that binds no column by name is either a full-table write or a statement "
            + "this reader no longer understands; both must fail here rather than pass vacuously.");

        touched.Except(declared, StringComparer.Ordinal).ShouldBeEmpty(
            "the revocation and rotation stamps are the only writes these rows ever take, and a "
            + "column name that drifted from the migration would refuse at execute time.");

        ParameterCountOf(statement).ShouldBe(
            touched.Length,
            "one parameter per bound column; a spare @name is a parameter Npgsql never fills.");
    }

    /// <summary>🔒 Every whole-row auth lookup reads back exactly the row the migration declares.</summary>
    [Theory]
    [MemberData(nameof(AuthFullRowSelects))]
    public void An_auth_row_lookup_selects_exactly_the_columns_its_table_declares(
        string table, string statement)
    {
        var declared = TableColumnsOf("0006_auth.sql", table);
        var selected = SelectColumnsOf(statement);

        selected.OrderBy(c => c, StringComparer.Ordinal).ShouldBe(
            declared.OrderBy(c => c, StringComparer.Ordinal),
            "a SELECT that misses a column the table declares reads a row with a field missing, and "
            + "one that names a column the table dropped fails at execute time. Neither is visible "
            + "to a tier that never starts a database, so this pin is the only thing watching.");

        ParameterisedColumnsOf(statement).Except(declared, StringComparer.Ordinal).ShouldBeEmpty(
            "the WHERE clause keys on a column this table declares, or it keys on nothing.");
    }

    /// <summary>
    /// 🔒 The one joined read: it projects a whole family row and keys on the OTHER table's column.
    /// </summary>
    /// <remarks>
    /// Deliberately its own case rather than a row in <see cref="AuthFullRowSelects"/>. The theory
    /// asserts the WHERE keys on a column of the table being projected, which is right for every
    /// single-table read and wrong for a join — and the fix is to say which table each half belongs
    /// to, not to relax the rule for all of them. Both halves must drift-check, because this
    /// statement is the one place the two auth tables are read together.
    /// </remarks>
    [Fact]
    public void The_family_lookup_projects_a_family_and_keys_on_a_refresh_token()
    {
        const string statement = PostgresAuthStore.SelectFamilyForTokenStatement;

        SelectColumnsOf(statement).OrderBy(c => c, StringComparer.Ordinal).ShouldBe(
            TableColumnsOf("0006_auth.sql", "auth_token_families").OrderBy(c => c, StringComparer.Ordinal),
            "the projection is a whole auth_token_families row; a column missing here reads a family "
            + "with a field absent, and one the table dropped refuses at execute time.");

        ParameterisedColumnsOf(statement)
            .Except(TableColumnsOf("0006_auth.sql", "auth_refresh_tokens"), StringComparer.Ordinal)
            .ShouldBeEmpty(
                "the lookup keys on the presented token's digest, which is a refresh-token column. "
                + "If it ever keys on something auth_refresh_tokens does not declare, the join is "
                + "reading a column that is not there.");
    }

    /// <summary>
    /// 🔒 A partial auth lookup names only columns its table declares — the weaker pin, and it says
    /// so.
    /// </summary>
    /// <remarks>
    /// A whole-row comparison cannot apply here: these read one column deliberately. What is still
    /// decidable is that every name they DO read is a real one, which is the half that breaks when
    /// the migration renames something underneath them.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AuthPartialSelects))]
    public void A_partial_auth_lookup_names_only_columns_its_table_declares(
        string table, string statement)
    {
        var declared = TableColumnsOf("0006_auth.sql", table);
        var selected = SelectColumnsOf(statement);

        selected.ShouldNotBeEmpty(
            "a SELECT this reader extracted no column from is a statement it no longer understands, "
            + "and it must fail here rather than pass over an empty set.");

        selected.Except(declared, StringComparer.Ordinal).ShouldBeEmpty(
            "every column read back is one the table declares; a name that drifted from the "
            + "migration refuses at execute time, against a live database nothing here starts.");

        ParameterisedColumnsOf(statement).Except(declared, StringComparer.Ordinal).ShouldBeEmpty(
            "the WHERE clause keys on a column this table declares, or it keys on nothing.");
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

    /// <summary>The column names one statement binds a parameter to — <c>column = @name</c>.</summary>
    private static string[] ParameterisedColumnsOf(string statement) =>
        System.Text.RegularExpressions.Regex
            .Matches(statement, "([a-z][a-z0-9_]*)\\s*=\\s*@")
            .Select(match => match.Groups[1].Value)
            .ToArray();

    /// <summary>The column names one SELECT reads back, with any table alias stripped.</summary>
    /// <remarks>
    /// A joined read spells its columns <c>f.family_id</c>; the migration declares <c>family_id</c>.
    /// Comparing the qualified form against the declared one would fail on every joined statement
    /// for a reason that has nothing to do with drift, so the qualifier comes off here.
    /// </remarks>
    private static string[] SelectColumnsOf(string statement)
    {
        const string select = "SELECT ";

        return statement[(statement.IndexOf(select, StringComparison.Ordinal) + select.Length)
                ..statement.IndexOf(" FROM ", StringComparison.Ordinal)]
            .Split(',')
            .Select(column => column.Trim())
            .Select(column => column[(column.IndexOf('.') + 1)..])
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
