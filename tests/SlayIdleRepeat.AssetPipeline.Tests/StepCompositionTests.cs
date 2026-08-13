using Shouldly;
using SkiaSharp;
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

    /// <summary>
    /// 🔒 <b>The case whose absence let the misread ship.</b> `15` §B4 lists step 2 ("pad to the
    /// target canvas") and step 5 ("Resize -&gt; to the spec size in the manifest") as two steps.
    /// While step 2 padded to the <em>delivery</em> size, step 5's input was already the target: the
    /// Mitchell resample was an identity on every asset, its scale measurement was exactly 1.0, and
    /// <c>DEV_LANCZOS_UNAVAILABLE</c> was declared for a resample that never resampled — while §B4's
    /// own step 5 was dead text. Every per-step case still passed, because each one asserted its own
    /// step in isolation.
    /// </summary>
    /// <remarks>
    /// The triple is what makes step 5 provably live: the run ends at the manifest's size, step 2
    /// did <b>not</b> resize, and step 5's scale is not 1. Any two of the three can be satisfied by
    /// the defect.
    /// </remarks>
    [Fact]
    public void Run_over_a_15_C_generation_canvas_downscales_in_step_5_and_not_in_step_2()
    {
        const int generationCanvas = 1024;
        const int deliveryEdge = 512;

        var row = ManifestRows.Require(ManifestRows.SquareCharacterDeliveringAt512);

        // 🔒 The shipped row really does deliver at 512x512 — otherwise the assertions below would
        // be measuring against a number this case invented.
        row.RequireDeliverySize().Width.ShouldBe(deliveryEdge);
        row.RequireDeliverySize().Height.ShouldBe(deliveryEdge);

        // A subject well inside the generation frame, and centred rather than bottom-aligned: a
        // subject filling the frame would leave step 2 nothing to re-frame, and one already sitting
        // at the row's own bottom-center pivot would let a step 2 that did nothing pass. Touching
        // no edge also keeps `15` §B4 step 1 from sampling the subject as the border colour.
        const int contentWidth = 600;
        const int contentHeight = 400;
        var fixture = SyntheticAsset.PivotedSubject(
            generationCanvas, contentWidth, contentHeight, Doc15Pivots.Center);

        var run = new AssetPipeline().Run(
            fixture.Image, row, StatedThresholds.ForGenerationCanvasFixture());

        var trim = run.Steps.Single(step => step.Number == 2);
        var resize = run.Steps.Single(step => step.Number == 5);

        // (a) The run delivers at the manifest's size.
        run.Output.ShouldNotBeNull();
        run.Output!.Width.ShouldBe(deliveryEdge);
        run.Output.Height.ShouldBe(deliveryEdge);

        // (b) Step 2 re-framed on the generation canvas and did not resize. The re-framing is
        // asserted as the exact bbox the row's bottom-center pivot puts the subject at, so a step 2
        // that returned its input untouched would fail here as well as a step 2 that resized.
        trim.Image.Width.ShouldBe(generationCanvas);
        trim.Image.Height.ShouldBe(generationCanvas);
        Pixels.OpaqueBounds(trim.Image).ShouldBe(new SKRectI(
            (generationCanvas - contentWidth) / 2,
            generationCanvas - contentHeight,
            ((generationCanvas - contentWidth) / 2) + contentWidth,
            generationCanvas));

        // (c) Step 5 actually resampled.
        var scale = resize.Measurements
            .Single(measurement => measurement.Key == ResizeStep.ScaleMeasurement)
            .Value;
        scale.ShouldNotBe(1d);
        scale.ShouldBe(deliveryEdge / (double)generationCanvas);
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
