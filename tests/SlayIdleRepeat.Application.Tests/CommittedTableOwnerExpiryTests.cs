using Shouldly;
using SlayIdleRepeat.Application.Tests.Parity;
using Xunit;

namespace SlayIdleRepeat.Application.Tests;

/// <summary>
/// 🔒 The tasks a committed determinism table says it is <b>still waiting on</b> are still open.
/// </summary>
/// <remarks>
/// <para>
/// <b>The hole this fills.</b> <c>ParityCorpusBaseline.json</c>'s header tells the next reader that
/// two decisions — D45 and D46 — will move every hash in it, and that the re-baseline must be taken
/// with both at once rather than between them.
/// <c>ClientServerParityTests.The_committed_header_says_what_the_corpus_does_not_compare</c> asserts
/// those two task ids <em>appear</em> in the header. A presence assertion over a literal is
/// satisfied forever: the day the two land, the header goes on telling every reader that the
/// re-baseline is ahead of them when it is behind, and nothing goes red to say so. Steering
/// <b>S4</b>: an expiry check tests that the owner is still <b>OPEN</b>, not that its id exists.
/// </para>
/// <para>
/// ⚠️ <b>Deliberately outside the <c>Parity</c> namespace.</b> Everything under it is swept onto the
/// cross-architecture determinism legs by <c>Assert-DeterminismRun.ps1</c>'s
/// <c>FullyQualifiedName~Parity</c> filter. This rule reads a markdown file at the repository root;
/// it proves nothing about floating point, and putting one more file for an emulated ARM64
/// container to find on that job's critical path buys nothing.
/// </para>
/// <para>
/// The list below is transcribed rather than scraped, because the header also names the task that
/// WROTE the table (M5-12, shipped) and prose is not a reliable place to tell "the author" from "the
/// blocker" apart. Both directions are checked instead: an id here that the header stops naming is
/// as much a finding as one whose task has shipped.
/// </para>
/// </remarks>
public sealed class CommittedTableOwnerExpiryTests
{
    /// <summary>A task a committed table defers its own re-baseline to, and what that task is.</summary>
    /// <param name="Owner">The tracker task id, as the header writes it.</param>
    /// <param name="What">The decision it carries, so a failure reads as a sentence.</param>
    private sealed record Blocker(string Owner, string What);

    /// <summary>🔒 Every task <c>ParityCorpusBaseline.json</c>'s header says will move its hashes.</summary>
    private static readonly Blocker[] ParityTableBlockers =
    {
        new("M7-06g", "D45 — HP persists across a whole run, and revive restores 66% on first death"),
        new("M4-16d", "D46 — DMG%/DR% become multiplier stats"),
    };

    [Fact]
    public void The_parity_table_still_names_every_task_it_defers_its_re_baseline_to()
    {
        var header = string.Join(" ", ParityBaseline.Comment);

        ParityTableBlockers.Length.ShouldBe(
            2,
            "the floor under the sweep below: an empty register would report success while watching " +
            "nothing at all.");

        foreach (var blocker in ParityTableBlockers)
        {
            header.ShouldContain(
                blocker.Owner,
                Case.Sensitive,
                $"the header no longer names '{blocker.Owner}' ({blocker.What}). Either the table was " +
                "re-baselined and this entry should have gone with it, or the header lost a warning " +
                "the next reader needs.");
        }
    }

    [Fact]
    public void No_task_the_parity_table_waits_on_has_shipped()
    {
        var rows = TrackerStatus.Rows();

        rows.Count.ShouldBeGreaterThan(
            150,
            $"{TrackerStatus.RelativePath} lists every milestone task; finding almost none means the " +
            "row pattern broke rather than that the tracker emptied — and this rule would then pass " +
            "over nothing.");

        foreach (var blocker in ParityTableBlockers)
        {
            rows.ContainsKey(blocker.Owner).ShouldBeTrue(
                $"the parity table defers its re-baseline to '{blocker.Owner}' ({blocker.What}), " +
                $"which is not a task row in {TrackerStatus.RelativePath}. A blocker that does not " +
                "exist expires when nobody is looking.");

            TrackerStatus.HasShipped(rows[blocker.Owner]).ShouldBeFalse(
                $"'{blocker.Owner}' ({blocker.What}) has shipped, so the committed header's claim " +
                "that this corpus is still waiting on it is now false. The chunk hashes are already " +
                "red if it moved them; this is the arm that says WHY, and it is also what stops the " +
                "warning outliving the work when it did not.");
        }
    }

    /// <summary>
    /// 🔒 The discrimination probe under the arm above (steering S1): a predicate that answered
    /// "still open" for everything would pass that rule over every entry while tracking nothing.
    /// </summary>
    /// <remarks>
    /// Both literals are tasks this register does NOT own, so neither half restates the rule it is
    /// controlling. ⚠️ A hardcoded example of an unstarted task expires when that task ships —
    /// re-point it rather than deleting the arm.
    /// </remarks>
    [Fact]
    public void The_shipped_predicate_separates_a_merged_task_from_an_unstarted_one()
    {
        var rows = TrackerStatus.Rows();

        TrackerStatus.HasShipped(rows["M4-05"]).ShouldBeTrue(
            "M4-05 merged and is marked done, and this register does not own it; a predicate that " +
            "cannot see a shipped task is not watching anything.");

        TrackerStatus.HasShipped(rows["M18-08"]).ShouldBeFalse(
            "M18-08 has not started, and this register does not own it either — so neither half of " +
            "this control restates the rule it is controlling.");
    }
}
