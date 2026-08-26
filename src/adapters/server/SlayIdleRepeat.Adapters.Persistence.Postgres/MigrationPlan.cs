using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

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
public static partial class MigrationPlan
{
    /// <summary>
    /// The process-wide advisory lock key migrations run under, so two API instances starting
    /// together apply the history once. An arbitrary pinned constant; changing it makes two builds
    /// stop excluding each other, so it never changes casually.
    /// </summary>
    public const long AdvisoryLockKey = 0x5349525F4D494752; // "SIR_MIGR" as ASCII bytes.

    [GeneratedRegex(@"^(?<ordinal>\d{4})_[a-z0-9_]+\.sql$")]
    private static partial Regex FileNamePattern();

    /// <summary>The scripts in apply order, validated: parseable names, ordinals exactly 1..N.</summary>
    /// <param name="scripts">The discovered scripts, in any order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scripts"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A name does not parse, an ordinal repeats, or the sequence has a gap.</exception>
    public static IReadOnlyList<MigrationScript> Ordered(IEnumerable<MigrationScript> scripts)
    {
        ArgumentNullException.ThrowIfNull(scripts);

        var byOrdinal = new SortedDictionary<int, MigrationScript>();

        foreach (var script in scripts)
        {
            var match = FileNamePattern().Match(script.FileName);

            if (!match.Success)
            {
                throw new InvalidOperationException(
                    "'" + script.FileName + "' is not a migration file name. The spelling is " +
                    "NNNN_lower_snake.sql — four digits, an underscore, a lower-case snake name — " +
                    "because the four digits ARE the apply order and anything the pattern cannot " +
                    "read would be applied at whatever position string sorting happened to give it.");
            }

            var ordinal = int.Parse(match.Groups["ordinal"].Value, CultureInfo.InvariantCulture);

            if (byOrdinal.TryGetValue(ordinal, out var taken))
            {
                throw new InvalidOperationException(
                    "'" + script.FileName + "' and '" + taken.FileName + "' both claim ordinal " +
                    ordinal + ". Two migrations in one slot would apply in discovery order — " +
                    "different per machine, recorded as one history.");
            }

            byOrdinal[ordinal] = script;
        }

        var expected = 1;

        foreach (var ordinal in byOrdinal.Keys)
        {
            if (ordinal != expected)
            {
                throw new InvalidOperationException(
                    "The migration history is missing ordinal " + expected.ToString("D4", CultureInfo.InvariantCulture) +
                    " (found " + ordinal.ToString("D4", CultureInfo.InvariantCulture) + " instead). A gap is a file " +
                    "deleted from the middle of an applied history: databases that ran it disagree " +
                    "with databases that never can.");
            }

            expected++;
        }

        return byOrdinal.Values.ToArray();
    }

    /// <summary>The SHA-256 of the script's UTF-8 text, as lowercase hex — what the history table pins an applied file to.</summary>
    /// <param name="sql">The script text.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sql"/> is null.</exception>
    public static string ChecksumOf(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        // The logical text, not the checkout's line endings: a Windows-built binary embedding CRLF
        // must agree with the CI build embedding LF, or the second of the two to reach a database
        // refuses to boot over a "changed" file nobody changed.
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql.Replace("\r\n", "\n"))))
            .ToLowerInvariant();
    }
}
