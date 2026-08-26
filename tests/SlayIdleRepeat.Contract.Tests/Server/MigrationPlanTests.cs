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
    public void The_shipped_history_is_exactly_the_four_M5_05_files_in_order()
    {
        var shipped = MigrationPlan.Ordered(PostgresMigrations.All());

        shipped.Select(s => s.FileName).ShouldBe(new[]
        {
            "0001_players_and_runs.sql",
            "0002_idempotency.sql",
            "0003_economy_events.sql",
            "0004_player_messages.sql",
        });

        shipped.ShouldAllBe(
            s => s.Sql.Contains("CREATE TABLE", StringComparison.Ordinal),
            "every M5-05 file creates its tables; an empty or truncated embed would apply cleanly "
            + "and leave the schema silently short.");
    }

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
