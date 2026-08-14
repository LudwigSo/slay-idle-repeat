using Shouldly;
using SkiaSharp;
using SlayIdleRepeat.AssetManifest;
using SlayIdleRepeat.AssetPipeline;
using Xunit;

namespace SlayIdleRepeat.AssetPlaceholders.Tests;

/// <summary>
/// The four claims the renderer makes about the pixels it writes, each of which a `15` §B4 step or
/// a Part F item depends on.
/// </summary>
public sealed class PlaceholderRendererTests
{
    [Fact]
    public void A_placeholder_is_drawn_on_the_straight_alpha_surface_the_pipeline_requires()
    {
        var (spec, canvas) = Sample(SampleRows.CurrencyIcon);
        using var drawn = PlaceholderRenderer.Draw(spec, canvas).Image;

        // `15` §C's delivery format is straight alpha, and Raster.From REFUSES any other surface
        // rather than converting one — so a premultiplied bitmap here would fail every asset.
        drawn.ColorType.ShouldBe(SKColorType.Rgba8888);
        drawn.AlphaType.ShouldBe(SKAlphaType.Unpremul);
        drawn.Width.ShouldBe(canvas.Width);
        drawn.Height.ShouldBe(canvas.Height);
    }

    [Fact]
    public void Every_pixel_is_fully_opaque_or_fully_transparent_and_never_in_between()
    {
        foreach (var id in SampleRows.Generatable)
        {
            var (spec, canvas) = Sample(id);
            using var drawn = PlaceholderRenderer.Draw(spec, canvas).Image;

            var all = Inspected(drawn, canvas, id);
            var partial = all.Count(pixel => pixel.Alpha is not (0 or byte.MaxValue));

            partial.ShouldBe(
                0,
                $"'{id}' was drawn with {partial} partially transparent pixels. `15` §A3 asks for " +
                "\"true alpha, no halo\"; a soft edge here is a fringe this generator drew itself, " +
                "and Part F item 6 exists to catch exactly that.");
        }
    }

    [Fact]
    public void The_border_of_the_canvas_is_entirely_transparent()
    {
        foreach (var id in SampleRows.Generatable)
        {
            var (spec, canvas) = Sample(id);
            using var drawn = PlaceholderRenderer.Draw(spec, canvas).Image;

            // 🔒 BackgroundRemovalStep keys against the most common OPAQUE colour on the border. An
            // opaque border pixel would hand step 1 a key colour that also occurs in the subject,
            // and the flood fill would eat the card.
            var opaqueBorder = Inspected(drawn, canvas, id)
                .Where(pixel => pixel.Alpha > 0)
                .Where(pixel => pixel.X == 0
                                || pixel.Y == 0
                                || pixel.X == canvas.Width - 1
                                || pixel.Y == canvas.Height - 1)
                .Take(1)
                .ToArray();

            opaqueBorder.ShouldBeEmpty(
                $"'{id}' has an opaque pixel on the canvas border, so `15` §B4 step 1 would " +
                "sample a key colour off the subject itself and the flood fill would eat the card.");
        }
    }

    [Fact]
    public void The_alpha_bounding_box_is_symmetric_in_both_axes()
    {
        foreach (var id in SampleRows.Generatable)
        {
            var (spec, canvas) = Sample(id);
            using var drawn = PlaceholderRenderer.Draw(spec, canvas).Image;

            var opaque = Inspected(drawn, canvas, id).Where(pixel => pixel.Alpha > 0).ToArray();
            opaque.Length.ShouldBeGreaterThan(0, $"'{id}' was drawn entirely transparent.");

            var left = opaque.Min(pixel => pixel.X);
            var right = opaque.Max(pixel => pixel.X);
            var top = opaque.Min(pixel => pixel.Y);
            var bottom = opaque.Max(pixel => pixel.Y);

            // 🔒 This is what makes Part F item 7 come out at exactly zero rather than nearly zero.
            // §B4 step 5's resampler is symmetric, so a symmetric alpha pattern resamples to a
            // symmetric one and the content lands exactly where §C's pivot puts it.
            left.ShouldBe(
                canvas.Width - 1 - right,
                $"'{id}' is not horizontally symmetric: the card runs [{left}, {right}] on a " +
                $"{canvas} canvas, so `15` §B4 step 2 has an odd slack to halve.");
            top.ShouldBe(canvas.Height - 1 - bottom);
        }
    }

    [Fact]
    public void The_stamp_names_the_asset_and_says_it_is_a_placeholder()
    {
        var spec = AssetSpec.Resolve(SampleRows.Require(SampleRows.BiomeEnemy));
        var lines = PlaceholderRenderer.StampLines(spec);

        lines[0].ShouldBe(PlaceholderRenderer.Banner);
        lines[^1].ShouldBe($"{spec.TargetSize.Width}x{spec.TargetSize.Height}");

        // The id must be reconstructible from the middle lines — a wrap that dropped a segment
        // would produce a stamp naming a different asset.
        string.Join("_", lines.Skip(1).Take(lines.Count - 2)).ShouldBe(spec.Id);
    }

    [Fact]
    public void A_biome_row_is_drawn_only_from_its_own_doc_15_A5_hues_and_the_A3_outline()
    {
        var asset = SampleRows.Require(SampleRows.BiomeEnemy);
        asset.PaletteColours.ShouldNotBeNull("this case is about a biome-scoped row.");

        var (spec, canvas) = Sample(SampleRows.BiomeEnemy);
        using var drawn = PlaceholderRenderer.Draw(spec, canvas).Image;

        var authorised = asset.PaletteColours!.Hues
            .Select(hex => SKColor.Parse(hex))
            .Append(SKColor.Parse(Doc15Authorised.OutlineColourHex))
            .ToArray();

        var inner = PlaceholderPalette.Blend(
            SKColor.Parse(Doc15Authorised.OutlineColourHex),
            SKColor.Parse(asset.PaletteColours.Base),
            PlaceholderRenderer.InnerEdgeBlend);

        var offPalette = Inspected(drawn, canvas, SampleRows.BiomeEnemy)
            .Where(pixel => pixel.Alpha > 0)
            .Select(pixel => new SKColor(pixel.Red, pixel.Green, pixel.Blue))
            .Distinct()
            .Where(colour => colour != inner && !authorised.Contains(colour))
            .ToArray();

        offPalette.ShouldBeEmpty(
            "a biome card is drawn from `15` §A5's own hues plus §A3's outline plus the one " +
            "antialiased inner-edge layer, and nothing else — so `15` §B4 step 3 snaps every " +
            $"visible pixel to itself. Found: [{string.Join(", ", offPalette)}].");
    }

    [Fact]
    public void The_outline_colour_tolerance_separates_every_fill_from_every_antialiased_edge()
    {
        // 🔒 This is the arithmetic PlaceholderThresholds.OutlineColourTolerance is fixed by, checked
        // against the register's real palettes rather than against the numbers in its remarks. If a
        // ninth biome arrives whose base sits between the two bands, step 4 would read either the
        // whole card as outline or none of the soft edge — and this case says so before a batch does.
        var outline = SKColor.Parse(Doc15Authorised.OutlineColourHex);
        var fills = PlaceholderFiles.Shipped.Art.Biomes
            .Select(biome => SKColor.Parse(biome.Palette.Base))
            .Append(SKColor.Parse(PlaceholderPalette.NeutralFillHex))
            .ToArray();

        fills.Length.ShouldBeGreaterThanOrEqualTo(9, "eight `15` §A5 biomes plus this tool's neutral.");

        var furthestEdge = fills.Max(fill => PlaceholderPalette.RgbDistance(
            outline, PlaceholderPalette.Blend(outline, fill, PlaceholderRenderer.InnerEdgeBlend)));
        var nearestFill = fills.Min(fill => PlaceholderPalette.RgbDistance(outline, fill));

        furthestEdge.ShouldBeLessThan(
            PlaceholderThresholds.OutlineColourTolerance,
            "the furthest antialiased inner-edge pixel sits outside the tolerance, so `15` §B4 " +
            "step 4 reads none of the soft edge as outline.");
        nearestFill.ShouldBeGreaterThan(
            PlaceholderThresholds.OutlineColourTolerance,
            "the nearest card fill sits inside the tolerance, so `15` §B4 step 4 reads the whole " +
            "card as outline and spends its run closing concavities in a shape that is not one.");
    }

    private static (AssetSpec Spec, PixelSize Canvas) Sample(string id)
    {
        var spec = AssetSpec.Resolve(SampleRows.Require(id));
        return (spec, GenerationCanvas.For(spec.TargetSize));
    }

    /// <summary>
    /// Every pixel of a drawn placeholder, with an S3 floor on how many were actually inspected.
    /// </summary>
    /// <remarks>
    /// 🔒 Every case below asserts an <em>absence</em> — no partial alpha, no opaque border pixel —
    /// and an absence over an empty sweep is vacuously true. <see cref="Pixels"/> is a hand-rolled
    /// stride walk over <see cref="SKBitmap.Bytes"/>, so a buffer that came back empty or loop
    /// bounds that regressed would leave all three cases green while claiming to have looked at four
    /// million pixels.
    /// </remarks>
    /// <param name="drawn">The placeholder.</param>
    /// <param name="canvas">The canvas it should cover, exactly.</param>
    /// <param name="id">The asset id, for the failure message.</param>
    private static IReadOnlyList<(int X, int Y, byte Red, byte Green, byte Blue, byte Alpha)> Inspected(
        SKBitmap drawn, PixelSize canvas, string id)
    {
        var all = Pixels(drawn).ToArray();

        all.Length.ShouldBe(
            canvas.Width * canvas.Height, $"'{id}' yielded {all.Length} pixels to inspect.");

        return all;
    }

    /// <summary>
    /// Every pixel of a bitmap, read out of one managed copy of its buffer.
    /// </summary>
    /// <remarks>
    /// 🔒 <c>SKBitmap.GetPixel</c> is a P/Invoke per call, and the largest sample row generates on a
    /// 2048×2048 canvas — four million interop transitions per pass, several passes per case. The
    /// bytes are Rgba8888 and the rows carry a stride, so the offset arithmetic is explicit.
    /// </remarks>
    private static IEnumerable<(int X, int Y, byte Red, byte Green, byte Blue, byte Alpha)> Pixels(
        SKBitmap bitmap)
    {
        var bytes = bitmap.Bytes;
        var stride = bitmap.RowBytes;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var offset = (y * stride) + (x * 4);
                yield return (
                    x, y, bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3]);
            }
        }
    }

}
