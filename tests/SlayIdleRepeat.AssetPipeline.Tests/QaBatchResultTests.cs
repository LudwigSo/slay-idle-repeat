using Shouldly;
using SlayIdleRepeat.AssetPipeline.Qa;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// `15` Part F's gate — <em>"Before an asset batch is accepted"</em> — and the one thing it must
/// never do.
/// </summary>
/// <remarks>
/// 🔒 The outcomes here are hand-built rather than produced by the eleven checks. The decision
/// function is a rule about verdicts, and pinning it through a real run would make it depend on
/// eleven implementations at once; when a check later changes what it returns, the case that should
/// go red is that check's, not this one.
/// </remarks>
public sealed class QaBatchResultTests
{
    private const string AssetId = "chr_enemy_astral_brute_idle";

    /// <summary>
    /// 🔒 <b>The S6 assertion at batch level.</b> Nothing failed, and the batch is still not
    /// accepted: a threshold nobody has stated has decided nothing, and a gate that read "no
    /// failures" as "accept" would wave 942 ungraded assets through.
    /// </summary>
    [Fact]
    public void A_batch_is_not_accepted_while_any_item_is_Uncalibrated()
    {
        var result = new QaBatchResult(
            AssetId,
            [
                Outcome(7, QaVerdict.Pass),
                Outcome(10, QaVerdict.Pass),
                Outcome(3, QaVerdict.Uncalibrated),
            ]);

        result.Accepted.ShouldBeFalse();
        result.Decision.ShouldBe(QaDecision.BlockedByUncalibratedThreshold);
        result.Uncalibrated.Select(o => o.ItemNumber).ToArray().ShouldBe([3]);
    }

    /// <summary>
    /// 🔒 The counterweight: <see cref="QaBatchResult.Accepted"/> is not simply always false. Without
    /// this case an implementation returning a constant would satisfy every other case in this file.
    /// </summary>
    [Fact]
    public void A_batch_whose_every_item_passed_is_accepted()
    {
        var result = new QaBatchResult(
            AssetId, [Outcome(7, QaVerdict.Pass), Outcome(10, QaVerdict.Pass)]);

        result.Decision.ShouldBe(QaDecision.Accepted);
        result.Accepted.ShouldBeTrue();
    }

    /// <summary>
    /// 🔒 Assumption A5 at batch level. Five of `15` Part F's eleven items are human, so a real run
    /// always carries at least one <see cref="QaVerdict.HumanGapOnly"/> and can never reach
    /// <see cref="QaDecision.Accepted"/> — the machinery cannot sign off on its own.
    /// </summary>
    [Fact]
    public void A_batch_carrying_a_human_item_awaits_review_rather_than_being_accepted()
    {
        var result = new QaBatchResult(
            AssetId,
            [
                Outcome(7, QaVerdict.Pass),
                Outcome(8, QaVerdict.HumanGapOnly, "a human looks for text"),
            ]);

        result.Decision.ShouldBe(QaDecision.AwaitingHumanReview);
        result.Accepted.ShouldBeFalse();
    }

    [Fact]
    public void A_batch_with_a_failing_item_is_rejected_and_names_it()
    {
        var result = new QaBatchResult(
            AssetId, [Outcome(7, QaVerdict.Pass), Outcome(3, QaVerdict.Fail)]);

        result.Decision.ShouldBe(QaDecision.Rejected);
        result.Accepted.ShouldBeFalse();
        result.Failures.Select(o => o.ItemNumber).ToArray().ShouldBe([3]);
    }

    /// <summary>
    /// 🔒 The stated precedence: a known defect outranks an unknown one. The asset goes back either
    /// way, and reporting "blocked on calibration" would send a reviewer to measure a threshold for
    /// an asset that is already being regenerated.
    /// </summary>
    [Fact]
    public void A_failure_outranks_an_uncalibrated_item_in_the_decision()
    {
        var result = new QaBatchResult(
            AssetId, [Outcome(3, QaVerdict.Uncalibrated), Outcome(7, QaVerdict.Fail)]);

        result.Decision.ShouldBe(QaDecision.Rejected);
        result.Failures.Count.ShouldBe(1);
        result.Uncalibrated.Count.ShouldBe(1);
    }

    /// <summary>
    /// 🔒 Every gap reaches the report, including item 1's — which a mechanical
    /// <see cref="QaVerdict.Pass"/> does not close. This is the list that stops "the pipeline
    /// accepted 942 assets" being read as "942 assets passed Part F".
    /// </summary>
    [Fact]
    public void HumanGaps_surfaces_every_gap_including_one_carried_by_a_passing_item()
    {
        var result = new QaBatchResult(
            AssetId,
            [
                Outcome(1, QaVerdict.Pass, "15 §A4 is a human judgement"),
                Outcome(7, QaVerdict.Pass),
                Outcome(11, QaVerdict.HumanGapOnly, "a human compares three approved assets"),
            ]);

        result.HumanGaps.Count.ShouldBe(2);
        result.HumanGaps.ShouldAllBe(gap => !string.IsNullOrWhiteSpace(gap));
    }

    /// <summary>
    /// 🔒 Steering rule S3: a run holding no outcomes must not read as a clean one. "Every item
    /// passed" over an empty list is vacuously true, and that is precisely the shape of a checklist
    /// that silently stopped running.
    /// </summary>
    [Fact]
    public void A_run_holding_no_outcomes_fails_loudly_rather_than_being_accepted()
    {
        var empty = new QaBatchResult(AssetId, []);

        Should.Throw<InvalidOperationException>(() => empty.Decision);
    }

    private static QaOutcome Outcome(int itemNumber, QaVerdict verdict, string? humanGap = null) =>
        new(verdict, itemNumber, $"item {itemNumber} concluded {verdict}", [], humanGap);
}
