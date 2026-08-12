using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// C1 — every `15` §B4 step stands on its own, and the orchestrator runs 1-6 in the doc's order.
/// </summary>
/// <remarks>
/// 🔒 A failure in step 4 must be diagnosable without re-running 1-3. That is only true if each
/// step is constructible and runnable with nothing else in the room, which is what the theory
/// below actually exercises — the orchestrator is a convenience on top, not the seam.
/// </remarks>
public sealed class StepCompositionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Each_15_B4_step_runs_alone_and_reports_its_own_ordinal(int number)
    {
        var fixture = SyntheticAsset.Chibi();
        var step = StepFor(number);
        var input = new AssetStepInput(
            fixture.Image,
            TestSpecs.FromRow(ManifestRows.NonBiomeUiIcon),
            StatedThresholds.ForSyntheticFixtures());

        var result = step.Run(input);

        result.Number.ShouldBe(number);
        result.Id.ShouldBe(step.Id);
    }

    [Fact]
    public void Step_7_runs_alone_and_reports_its_own_ordinal()
    {
        var step = new AtlasPackStep();
        var entry = new AtlasPackEntry(
            ManifestRows.Require(ManifestRows.NonBiomeUiIcon), SyntheticAsset.SolidBlock(96, 96));

        var result = step.Run(
            new AtlasPackInput("atlas_ui", [entry], StatedThresholds.ForSyntheticFixtures()));

        step.Number.ShouldBe(7);
        result.AtlasId.ShouldBe("atlas_ui");

        // 🔒 "Runnable alone" means it did the work, not that it returned a shape. A packer that
        // handed back an empty result would satisfy the two assertions above, both of which read
        // values the caller supplied.
        result.Placements.Select(placement => placement.AssetId).ToArray()
            .ShouldBe([ManifestRows.NonBiomeUiIcon]);
        result.Exclusions.ShouldBeEmpty();
    }

    [Fact]
    public void The_orchestrator_composes_exactly_15_B4_steps_1_to_6_in_order()
    {
        var pipeline = new AssetPipeline();

        var numbers = pipeline.Steps.Select(step => step.Number).ToArray();

        numbers.ShouldBe(new[] { 1, 2, 3, 4, 5, 6 });
    }

    /// <summary>
    /// 🔒 The order is asserted as the exact <see cref="AssetStepResult.Number"/> sequence, not as
    /// "six results came back". Six results in the wrong order is the failure this case exists for:
    /// quantising before the background is keyed, or sharpening before the outline is repaired,
    /// both produce six results and the wrong picture.
    /// </summary>
    [Fact]
    public void Run_reports_15_B4_steps_1_to_6_in_the_order_the_doc_lists_them()
    {
        var fixture = SyntheticAsset.Chibi();
        var row = ManifestRows.Require(ManifestRows.NonBiomeUiIcon);

        var run = new AssetPipeline().Run(
            fixture.Image, row, StatedThresholds.ForSyntheticFixtures());

        run.Steps.Select(step => step.Number).ToArray().ShouldBe(new[] { 1, 2, 3, 4, 5, 6 });
    }

    private static IAssetStep StepFor(int number) => number switch
    {
        1 => new BackgroundRemovalStep(),
        2 => new TrimToCanvasStep(),
        3 => new PaletteQuantiseStep(),
        4 => new OutlineRepairStep(),
        5 => new ResizeStep(),
        6 => new ExportStep(),
        _ => throw new ArgumentOutOfRangeException(
            nameof(number), number, "`15` §B4 numbers the per-asset steps 1 to 6."),
    };
}
