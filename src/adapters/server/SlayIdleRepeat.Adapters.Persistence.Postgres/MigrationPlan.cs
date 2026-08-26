namespace SlayIdleRepeat.Adapters.Persistence.Postgres;

/// <summary>One numbered migration file: its file name and its SQL text.</summary>
/// <param name="FileName">The bare file name, e.g. <c>0001_players_and_runs.sql</c>.</param>
/// <param name="Sql">The file's full SQL text.</param>
public sealed record MigrationScript(string FileName, string Sql);

/// <summary>The pure half of the migration runner: ordering, naming and checksums, with no database anywhere.</summary>
/// <remarks>
/// A file name is <c>NNNN_name.sql</c> — four digits, an underscore, a lower-case snake name. The
/// ordinals must run 1..N with no duplicate and no gap: a gap is a file somebody deleted from the
/// middle of an applied history, and a duplicate is two migrations fighting over one slot.
/// </remarks>
public static class MigrationPlan
{
    /// <summary>
    /// The process-wide advisory lock key migrations run under, so two API instances starting
    /// together apply the history once. An arbitrary pinned constant; changing it makes two builds
    /// stop excluding each other, so it never changes casually.
    /// </summary>
    public const long AdvisoryLockKey = 0x5349525F4D494752; // "SIR_MIGR" as ASCII bytes.

    /// <summary>The scripts in apply order, validated: parseable names, ordinals exactly 1..N.</summary>
    /// <param name="scripts">The discovered scripts, in any order.</param>
    /// <exception cref="InvalidOperationException">A name does not parse, an ordinal repeats, or the sequence has a gap.</exception>
    public static IReadOnlyList<MigrationScript> Ordered(IEnumerable<MigrationScript> scripts) =>
        throw new NotImplementedException("M5-05 phase 3 implements the migration plan.");

    /// <summary>The SHA-256 of the script's UTF-8 text, as lowercase hex — what the history table pins an applied file to.</summary>
    /// <param name="sql">The script text.</param>
    public static string ChecksumOf(string sql) =>
        throw new NotImplementedException("M5-05 phase 3 implements the migration plan.");
}
