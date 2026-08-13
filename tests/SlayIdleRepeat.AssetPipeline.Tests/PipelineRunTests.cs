using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// What one run hands M8-10: the distinct bitmaps it owns, and the run-level roll-up of everything
/// its steps declared.
/// </summary>
/// <remarks>
/// 🔒 M8-10 drives roughly 942 assets through this. Both members exist for that caller: one so it
/// can free native surfaces without freeing any of them twice, the other so a batch report does not
/// re-derive the same fold 942 times.
/// </remarks>
public sealed class PipelineRunTests
{
    /// <summary>`15` §B4 numbers the per-asset steps 1 to 6.</summary>
    private const int PerAssetSteps = 6;

    /// <summary>
    /// 🔒 A skipping step returns <b>the instance it was handed</b> — `15` §B4 step 3 on a non-biome
    /// row does exactly that — and <c>Intermediates</c> aliases the same objects again, so
    /// <c>foreach (var step in run.Steps) step.Image.Dispose()</c> frees the same native surface
    /// twice. This is the case that makes the difference visible: fewer distinct outputs than steps,
    /// with the skipped step's image the very same object as its predecessor's.
    /// </summary>
    [Fact]
    public void DistinctOutputs_holds_each_bitmap_once_by_reference_so_a_skipped_step_is_not_freed_twice()
    {
        var fixture = SyntheticAsset.ChibiCutOut();
        var row = ManifestRows.Require(ManifestRows.NonSquareDeliveryMount);

        var run = new AssetPipeline(new PipelineOptions { CaptureIntermediates = true })
            .Run(fixture.Image, row, StatedThresholds.ForSyntheticFixtures());

        var quantise = run.Steps.Single(step => step.Number == 3);
        var trim = run.Steps.Single(step => step.Number == 2);

        run.Steps.Count.ShouldBe(PerAssetSteps);
        quantise.Outcome.ShouldBe(StepOutcome.SkippedNotApplicable);
        quantise.Image.ShouldBeSameAs(trim.Image);

        run.DistinctOutputs.Count.ShouldBeLessThan(run.Steps.Count);
        run.DistinctOutputs.Count.ShouldBe(PerAssetSteps - 1);

        // 🔒 "Distinct" asserted as reference identity, not as a count that happens to be lower:
        // a set that dropped one bitmap for any other reason would satisfy the count alone.
        run.DistinctOutputs
            .Select(image => (object)image)
            .Distinct(ReferenceEqualityComparer.Instance)
            .Count()
            .ShouldBe(run.DistinctOutputs.Count);
        run.DistinctOutputs.ShouldContain(run.Output!);
    }

    /// <summary>
    /// 🔒 Every step's declared deviations, in `15` §B4 step order, so a batch report reads one
    /// member instead of folding six results.
    /// </summary>
    [Fact]
    public void Deviations_aggregates_every_steps_declarations_in_15_B4_step_order()
    {
        var fixture = SyntheticAsset.ChibiCutOut();
        var row = ManifestRows.Require(ManifestRows.NonSquareDeliveryMount);

        var run = new AssetPipeline().Run(
            fixture.Image, row, StatedThresholds.ForSyntheticFixtures());

        run.Deviations.Select(declared => declared.Id).ToArray()
            .ShouldBe([ResizeStep.LanczosDeviationId, ExportStep.PngquantDeviationId]);
        run.Deviations.Count.ShouldBe(
            run.Steps.Sum(step => step.Deviations.Count),
            "the aggregate must not drop or de-duplicate what a step declared");
    }

    /// <summary>
    /// 🔒 The same roll-up for contradictions, kept apart from deviations: a deviation is owed to
    /// whoever maintains this toolchain, a contradiction is owed to a human ruling on `15`.
    /// </summary>
    [Fact]
    public void Contradictions_aggregates_the_15_C_delivery_aspect_collision_a_step_ran_into()
    {
        var fixture = SyntheticAsset.ChibiCutOut();
        var row = ManifestRows.Require(ManifestRows.NonSquareDeliveryMount);

        // The row really is off-square against a square fixture, so there is a collision to roll up.
        row.RequireDeliverySize().Width.ShouldNotBe(row.RequireDeliverySize().Height);

        var run = new AssetPipeline().Run(
            fixture.Image, row, StatedThresholds.ForSyntheticFixtures());

        run.Contradictions.Select(contradiction => contradiction.Id).ToArray()
            .ShouldBe([ResizeStep.DeliveryAspectContradictionId]);
    }
}
