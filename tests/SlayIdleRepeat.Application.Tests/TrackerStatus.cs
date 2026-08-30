using System.Text.RegularExpressions;
using SlayIdleRepeat.Application.Tests.Content;

namespace SlayIdleRepeat.Application.Tests;

/// <summary>
/// 🔒 <c>IMPLEMENTATION_TRACKER.md</c> read as data: every task row's id and status, and the one
/// predicate that says whether a task has <b>shipped</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is its own type.</b> Steering <b>S4</b> asks that a declared exception expire by
/// itself, and that its expiry check test the owner is still <em>open</em> rather than that the
/// owner <em>exists</em> — an existence check is satisfied forever by a task that merged three
/// milestones ago and closed nothing. Two registers in this assembly owe that predicate now
/// (<c>GearAuthoringGapRegisterTests</c>, and the parity table's header claims about D45 and D46),
/// and a second copy of the parser would be a second answer to "has this task shipped?".
/// </para>
/// <para>
/// ⚠️ <b>A status cell is recognised BY ITS GLYPH, never by its position.</b> Position was the first
/// implementation and it is wrong on real rows: two of the tracker's task rows carry an unescaped
/// pipe inside their notes, so the last <c>|</c>-delimited cell is a fragment of prose and the
/// predicate answers "still open" unconditionally for them — a second arm that cannot fire, inside
/// the mechanism written to end exactly that.
/// </para>
/// <para>
/// ⚠️ The sibling predicate <c>PortCatalogue.OwnersNoLongerOpen</c> in
/// <c>SlayIdleRepeat.Architecture.Tests</c> answers the same question for that assembly's registers.
/// It cannot be shared: it is internal to a different assembly, and a shared parser would need a
/// project neither suite has.
/// </para>
/// </remarks>
internal static partial class TrackerStatus
{
    /// <summary>The tracker, relative to the repository root.</summary>
    internal const string RelativePath = "IMPLEMENTATION_TRACKER.md";

    /// <summary>The status glyph for a task that merged and was verified.</summary>
    private const string Merged = "✅";

    /// <summary>The status glyph for a task that merged and awaits its milestone review.</summary>
    private const string MergedAwaitingReview = "🔍";

    /// <summary>The glyphs a status cell opens with. The first two mean shipped; all seven mark the cell.</summary>
    private static readonly string[] StatusGlyphs =
        [Merged, MergedAwaitingReview, "⬜", "⏳", "🔄", "⛔", "🔴"];

    /// <summary>
    /// Whether a task's status cell says the work landed.
    /// </summary>
    /// <param name="statusCell">The row's status cell, as <see cref="Rows"/> returns it.</param>
    /// <remarks>
    /// Read off the <b>first glyph of the status cell</b>, never the line: a row that is open often
    /// discusses a completed decision in its notes, and a tick anywhere in that prose would read as
    /// a finished task. ✅ is merged and verified, 🔍 is merged and awaiting its milestone review —
    /// both mean the task shipped. Everything else (⬜ unstarted, ⏳ queued, 🔄 in flight, ⛔
    /// blocked, 🔴 open defect) means it has not.
    /// </remarks>
    internal static bool HasShipped(string statusCell)
    {
        ArgumentNullException.ThrowIfNull(statusCell);

        var trimmed = statusCell.TrimStart();

        return trimmed.StartsWith(Merged, StringComparison.Ordinal) ||
               trimmed.StartsWith(MergedAwaitingReview, StringComparison.Ordinal);
    }

    /// <summary>Every task row in the tracker, task id to status cell.</summary>
    /// <remarks>
    /// Read from the tracker rather than transcribed, because a hard-coded copy would go stale in
    /// exactly the silence these registers exist to end. A duplicated id keeps the first row, which
    /// is the one the tables are ordered by.
    /// </remarks>
    internal static IReadOnlyDictionary<string, string> Rows()
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in File.ReadAllLines(Path.Combine(RepoData.RepositoryRoot, RelativePath)))
        {
            var match = TrackerRow().Match(line);

            if (!match.Success)
            {
                continue;
            }

            var status = line.Trim().Trim('|').Split('|')
                .Select(cell => cell.Trim())
                .LastOrDefault(cell => StatusGlyphs.Any(g => cell.StartsWith(g, StringComparison.Ordinal)));

            if (status is not null)
            {
                rows.TryAdd(match.Groups[1].Value, status);
            }
        }

        return rows;
    }

    /// <summary>Every task id the tracker declares, read with the same regex the lookup uses.</summary>
    /// <remarks>
    /// Deliberately derived rather than pinned. The literal it replaced was a proxy for "every row
    /// yields a status", and an equality on a number that legitimately grows can only ever be
    /// repaired by raising it — which is how a guard stops guarding.
    /// </remarks>
    internal static IReadOnlyCollection<string> Ids()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in File.ReadAllLines(Path.Combine(RepoData.RepositoryRoot, RelativePath)))
        {
            var match = TrackerRow().Match(line);

            if (match.Success)
            {
                ids.Add(match.Groups[1].Value);
            }
        }

        return ids;
    }

    [GeneratedRegex(@"^\|\s*(M\d+-\d+[a-z]?)\s*\|")]
    private static partial Regex TrackerRow();
}
