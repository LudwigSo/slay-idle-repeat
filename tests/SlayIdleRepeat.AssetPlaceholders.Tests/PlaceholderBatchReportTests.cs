using Shouldly;
using SlayIdleRepeat.AssetPipeline.Qa;
using Xunit;

namespace SlayIdleRepeat.AssetPlaceholders.Tests;

/// <summary>
/// The report's own arithmetic, over hand-built outcomes — no images, no pipeline.
/// </summary>
/// <remarks>
/// 🔒 These are the two claims a full batch run cannot exercise, because a real run never reaches
/// them: the batch decision's precedence needs a <see cref="QaDecision.Rejected"/> asset beside an
/// <see cref="QaDecision.AwaitingHumanReview"/> one, and the empty-batch refusal needs an empty
/// batch. Both are exactly the shapes a defect would take.
/// </remarks>
public sealed class PlaceholderBatchReportTests
{
    [Fact]
    public void A_rejected_asset_outranks_an_asset_merely_awaiting_a_human()
    {
        // 🔒 QaDecision is declared Accepted, Rejected, BlockedByUncalibratedThreshold,
        // AwaitingHumanReview. Taking Max() over the enum would rank "a human has not looked yet"
        // above "an item failed", and a batch holding a rejected asset would report as merely
        // awaiting review. This case is why Severity is an explicit map.
        Report([Graded(QaVerdict.HumanGapOnly), Graded(QaVerdict.Fail)])
            .Decision
            .ShouldBe(QaDecision.Rejected);

        Report([Graded(QaVerdict.HumanGapOnly), Graded(QaVerdict.Uncalibrated)])
            .Decision
            .ShouldBe(QaDecision.BlockedByUncalibratedThreshold);

        Report([Graded(QaVerdict.Fail), Graded(QaVerdict.Uncalibrated)])
            .Decision
            .ShouldBe(QaDecision.Rejected);
    }

    [Fact]
    public void A_run_that_generated_nothing_has_no_decision_rather_than_a_clean_one()
    {
        // Steering S3. "Nothing failed" over an empty batch is vacuously true, and that is the
        // shape of a generator that silently produced no output.
        var thrown = Should.Throw<InvalidOperationException>(() => Report([]).Decision);

        thrown.Message.ShouldContain("no `15` Part F decision to report", Case.Sensitive);
    }

    [Fact]
    public void A_report_reconciles_only_when_every_art_row_is_accounted_for()
    {
        var report = Report([Graded(QaVerdict.Pass)]) with
        {
            ArtRowsInRegister = 3,
            Skipped =
            [
                new PlaceholderSkip("a", "E1", PlaceholderSkipReason.CutByRuling, "cut"),
                new PlaceholderSkip("b", "E1", PlaceholderSkipReason.NoPivot, "no pivot"),
            ],
        };

        report.Accounted.ShouldBe(3);
        report.Reconciles.ShouldBeTrue();

        (report with { ArtRowsInRegister = 4 }).Reconciles.ShouldBeFalse();
    }

    [Fact]
    public void Skips_are_counted_per_reason_and_not_lumped_together()
    {
        var report = Report([]) with
        {
            Skipped =
            [
                new PlaceholderSkip("a", "E19", PlaceholderSkipReason.CutByRuling, "cut"),
                new PlaceholderSkip("b", "E19", PlaceholderSkipReason.CutByRuling, "cut"),
                new PlaceholderSkip("c", "E10", PlaceholderSkipReason.NoDeliverySize, "no size"),
            ],
        };

        report.SkippedFor(PlaceholderSkipReason.CutByRuling).ShouldBe(2);
        report.SkippedFor(PlaceholderSkipReason.NoDeliverySize).ShouldBe(1);
        report.SkippedFor(PlaceholderSkipReason.NoPivot).ShouldBe(0);
    }

    [Fact]
    public void Only_the_two_fully_mechanical_items_are_reported_as_mechanical_failures()
    {
        // A Fail on item 1 is an uncalibrated gate's business, not this generator's, and must not
        // land in the list that says "your generator is broken".
        var report = Report(
        [
            new QaOutcome(QaVerdict.Fail, 1, "silhouette", [], "gap"),
            new QaOutcome(QaVerdict.Fail, 7, "canvas", [], HumanGap: null),
        ]);

        report.MechanicalFailures.Count.ShouldBe(1);
        report.MechanicalFailures[0].Outcome.ItemNumber.ShouldBe(7);
    }

    private static QaOutcome Graded(QaVerdict verdict) =>
        new(verdict, 7, "a hand-built outcome", [], HumanGap: verdict == QaVerdict.HumanGapOnly ? "gap" : null);

    /// <summary>
    /// A report holding one placeholder per outcome. 🔒 Each outcome becomes its own asset, so the
    /// batch-level precedence is exercised across assets rather than within one.
    /// </summary>
    private static PlaceholderBatchReport Report(IReadOnlyList<QaOutcome> outcomes) =>
        new()
        {
            ArtRowsInRegister = outcomes.Count,
            AudioRowsInRegister = 0,
            Generated =
            [
                .. outcomes.Select((outcome, index) => new GeneratedPlaceholder(
                    $"icon_test_{index}",
                    $"icon_test_{index}.png",
                    "atlas_ui",
                    1,
                    new QaBatchResult($"icon_test_{index}", [outcome]))),
            ],
            Skipped = [],
            Failed = [],
            Deviations = new Dictionary<string, int>(StringComparer.Ordinal),
            Contradictions = new Dictionary<string, int>(StringComparer.Ordinal),
            Atlases = [],
        };
}
