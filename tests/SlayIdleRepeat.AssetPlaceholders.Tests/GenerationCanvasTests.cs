using Shouldly;
using SlayIdleRepeat.AssetManifest;
using Xunit;

namespace SlayIdleRepeat.AssetPlaceholders.Tests;

/// <summary>
/// The <c>CON_DELIVERY_ASPECT</c> ruling, as arithmetic over every delivery size the shipped
/// register actually holds.
/// </summary>
/// <remarks>
/// 🔒 Quantified over the real register rather than over a list of sizes typed here, so a ninth
/// delivery size arriving in `15` §C is covered the day it lands. Every case carries an S3 floor on
/// how many distinct sizes it found: a register that stopped yielding sizes would otherwise make
/// all four of these pass over nothing.
/// </remarks>
public sealed class GenerationCanvasTests
{
    /// <summary>
    /// How many distinct `15` §C delivery sizes the shipped register holds among its generatable
    /// rows. 🔒 Measured, 2026-08-14: 96×96, 128×128, 192×192, 256×256, 512×384, 512×512, 640×640,
    /// 1024×1024.
    /// </summary>
    private const int DistinctDeliverySizeFloor = 8;

    [Fact]
    public void Every_delivery_size_in_the_register_generates_at_exactly_the_same_aspect()
    {
        var sizes = DeliverySizes();
        sizes.Count.ShouldBeGreaterThanOrEqualTo(DistinctDeliverySizeFloor);

        foreach (var delivery in sizes)
        {
            var canvas = GenerationCanvas.For(delivery);

            // Cross-multiplied, which is exactly the comparison ResizeStep makes before it decides
            // whether to emit CON_DELIVERY_ASPECT. Two divisions could agree by rounding where the
            // step's integer arithmetic does not.
            ((long)canvas.Width * delivery.Height).ShouldBe(
                (long)canvas.Height * delivery.Width,
                $"'{delivery}' generates on '{canvas}', which is a different aspect ratio — so " +
                "ResizeStep would resample non-uniformly and report a doc contradiction this " +
                "generator, not `15`, had caused.");
        }
    }

    [Fact]
    public void Every_generation_canvas_is_strictly_larger_than_its_delivery_size()
    {
        var sizes = DeliverySizes();
        sizes.Count.ShouldBeGreaterThanOrEqualTo(DistinctDeliverySizeFloor);

        foreach (var delivery in sizes)
        {
            var canvas = GenerationCanvas.For(delivery);

            canvas.Width.ShouldBeGreaterThan(
                delivery.Width,
                $"'{delivery}' would generate on '{canvas}', so `15` §B4 step 5 would be an " +
                "identity resample and the step this batch exists to exercise would do nothing.");
            canvas.Height.ShouldBeGreaterThan(delivery.Height);
        }
    }

    [Fact]
    public void Every_generation_canvas_is_even_in_both_axes()
    {
        var sizes = DeliverySizes();
        sizes.Count.ShouldBeGreaterThanOrEqualTo(DistinctDeliverySizeFloor);

        foreach (var delivery in sizes)
        {
            var canvas = GenerationCanvas.For(delivery);

            (canvas.Width % 2).ShouldBe(
                0,
                $"'{delivery}' generates on '{canvas}', whose odd width leaves `15` §B4 step 2 an " +
                "odd slack to halve — so the subject lands half a pixel off centre and Part F " +
                "item 7 measures exactly that.");
            (canvas.Height % 2).ShouldBe(0);
        }
    }

    [Fact]
    public void A_delivery_long_edge_of_1024_or_more_takes_doc_15_C_upscaled_canvas()
    {
        // §C: "Generation resolution 1024×1024 (upscale to 2048 for bosses and backgrounds)".
        GenerationCanvas.For(new PixelSize(1024, 1024)).Width
            .ShouldBe(GenerationCanvas.UpscaledLongEdge);
        GenerationCanvas.For(new PixelSize(1080, 1440)).Height
            .ShouldBe(GenerationCanvas.UpscaledLongEdge);

        // And the row immediately below the threshold does not.
        GenerationCanvas.For(new PixelSize(640, 640)).Width
            .ShouldBe(GenerationCanvas.BaselineLongEdge);
    }

    [Fact]
    public void A_delivery_size_with_no_pixels_is_refused_rather_than_generated_at_some_default()
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(
            () => GenerationCanvas.For(new PixelSize(0, 512)));

        thrown.Message.ShouldContain("no aspect to generate at", Case.Sensitive);
    }

    /// <summary>
    /// Every distinct `15` §C delivery size among the register's generatable rows — the uncut ones
    /// §C gives both a size and a pivot.
    /// </summary>
    private static IReadOnlyList<PixelSize> DeliverySizes() =>
    [
        .. PlaceholderFiles.Shipped.ActiveArt
            .Where(asset => asset is { DeliverySize: not null, Pivot: not null })
            .Select(asset => asset.DeliverySize!)
            .Distinct()
            .OrderBy(size => size.Width)
            .ThenBy(size => size.Height),
    ];
}
