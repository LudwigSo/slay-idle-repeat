using SkiaSharp;
using SlayIdleRepeat.AssetManifest;
using SlayIdleRepeat.AssetPipeline;

namespace SlayIdleRepeat.AssetPlaceholders;

/// <summary>
/// Draws one placeholder onto its `15` §C generation canvas.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The alpha channel is written once, here, and nothing downstream ever changes it.</b> The
/// card is a single fully-opaque axis-aligned rectangle inset by an equal margin on a transparent
/// ground; the cross, the id stamp and the outline ring are all drawn <em>inside</em> it and touch
/// RGB only. That is not decoration — it is what makes `15` Part F item 7 decidable:
/// </para>
/// <list type="number">
///   <item>§B4 step 1 keys against the sampled <em>border</em> colour, and a border that is fully
///   transparent samples nothing, so the step cannot eat the subject.</item>
///   <item>The margin is equal on all four sides and the canvas is even in both axes, so the slack
///   §B4 step 2 halves is even and the subject lands exactly centred rather than half a pixel off.
///   </item>
///   <item>§B4 step 5's resampler is symmetric, so a symmetric alpha pattern resamples to a
///   symmetric one, and item 7's pivot offset comes out at exactly zero rather than nearly zero.
///   </item>
///   <item>§B4 step 4 only writes pixels where it finds a break to bridge, and a closed rectangular
///   ring has none — so it writes nothing, and the alpha it would otherwise set stays as drawn.</item>
/// </list>
/// <para>
/// 🔒 <b>Nothing here is antialiased at the alpha boundary.</b> Every edge of the card is at an
/// integer coordinate and every pixel is either fully opaque or fully transparent. §A3 asks for
/// "true alpha, no halo"; a soft edge would be a halo this generator had drawn itself, and Part F
/// item 6 exists to catch exactly that.
/// </para>
/// <para>
/// 🔒 <b>The inner face of the outline IS antialiased</b>, by one layer at a quarter blend toward
/// the fill. That is deliberate: M8-06 shipped step 4 having never met an antialiased outline, and
/// a hard-edged ring would leave <see cref="ThresholdKeys.OutlineColourTolerance"/> doing no work at
/// all. The blend is on the RGB side of the boundary, inside the opaque card, so it costs the alpha
/// symmetry nothing.
/// </para>
/// </remarks>
public static class PlaceholderRenderer
{
    /// <summary>
    /// The card's inset, as a fraction of the canvas's short edge — one over this.
    /// </summary>
    /// <remarks>
    /// 🔒 A generator's own composition choice, not a number read out of `15`. It is stated here as
    /// a named constant rather than inlined so that a reader can see it is the generator's and not
    /// the doc's. Sixteenths keep the margin a whole number of pixels at every canvas
    /// <see cref="GenerationCanvas"/> produces, which is what keeps the slack even.
    /// </remarks>
    public const int MarginDivisor = 16;

    /// <summary>How far the outline's innermost layer is blended toward the fill.</summary>
    public const double InnerEdgeBlend = 0.25d;

    /// <summary>The word every placeholder carries, so nobody can mistake one for art.</summary>
    public const string Banner = "placeholder";

    /// <summary>The longest line the id stamp wraps to, in characters.</summary>
    private const int MaxLineCharacters = 16;

    /// <summary>Draws one placeholder.</summary>
    /// <param name="spec">The row's manifest-derived spec — the id, the pivot and the delivery size.</param>
    /// <param name="canvas">The `15` §C generation canvas from <see cref="GenerationCanvas"/>.</param>
    /// <returns>
    /// The bitmap, <see cref="SKColorType.Rgba8888"/> / <see cref="SKAlphaType.Unpremul"/> — the
    /// caller owns it and must dispose it; that surface is what `15` §C's straight-alpha delivery
    /// needs and what the pipeline refuses to run without — and whether the id stamp fitted on it.
    /// </returns>
    public static (SKBitmap Image, bool Stamped) Draw(AssetSpec spec, PixelSize canvas)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(canvas);

        if (canvas.Width <= 0 || canvas.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(canvas), canvas, "A canvas with no pixels has nothing to draw on.");
        }

        var colours = PlaceholderPalette.For(spec.PaletteColours);

        // 🔒 `checked`. This method is public and takes an arbitrary PixelSize; past roughly
        // 23,170² the product wraps negative and `new byte[]` throws something that names neither
        // the canvas nor the asset.
        var pixels = new byte[checked(canvas.Width * canvas.Height * 4)];

        var margin = Math.Max(1, Math.Min(canvas.Width, canvas.Height) / MarginDivisor);
        var card = new SKRectI(margin, margin, canvas.Width - margin, canvas.Height - margin);

        if (card.Width <= 0 || card.Height <= 0)
        {
            throw new InvalidOperationException(
                $"A {canvas} canvas inset by {margin} px on every side leaves no card to draw. " +
                $"{nameof(GenerationCanvas)} produces nothing this small, so a caller has built one " +
                "by hand.");
        }

        var outline = OutlineWidth(canvas);

        Fill(pixels, canvas, card, colours.Fill);
        DrawCross(pixels, canvas, card, colours.Cross, outline);
        var stamped = DrawStamp(pixels, canvas, card, colours.Stamp, spec, outline);
        DrawOutline(pixels, canvas, card, colours.Outline, colours.Fill, outline);

        return (ToBitmap(pixels, canvas), stamped);
    }

    /// <summary>
    /// `15` §A3's outline weight at this canvas: <em>"3–4 px at 512 px canvas, scaled
    /// proportionally"</em>, taken at the band's lower bound.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 The band comes from <see cref="Doc15Authorised.OutlineWidthBandFor"/> rather than from a
    /// number retyped here — `15` states it once and M8-06 already transcribed it once.
    /// </para>
    /// <para>
    /// 🔒 <b>Taken on the canvas's WIDTH, because that is the basis `15` Part F item 3 grades
    /// against</b> (<c>OutlineConformanceCheck</c> reads <c>OutlineWidthBandFor(image.Width)</c> on
    /// the delivered image). §B4 step 5's downscale is uniform, so a stroke at the band's lower
    /// bound on a canvas of width <c>W</c> arrives at the band's lower bound on a delivery of width
    /// <c>w</c> — exactly in §A3's 3-4 px band at every size in the register. Sizing it off the
    /// short edge instead, as this used to, drew mounts (1024×768 canvas, 512×384 delivery) a third
    /// under the band by the time they were delivered.
    /// </para>
    /// </remarks>
    /// <param name="canvas">The generation canvas.</param>
    public static int OutlineWidth(PixelSize canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        var (min, _) = Doc15Authorised.OutlineWidthBandFor(canvas.Width);

        // 🔒 A floor of one pixel, and it is not a calibration: `15` §A3's band scales
        // proportionally with the canvas, so below roughly 170 px it rounds to zero, and a ring of
        // no pixels is not an outline — the card would have none at all and `15` §B4 step 4 would
        // have nothing to find. Every canvas GenerationCanvas produces is 1024 wide or more, where
        // the band's own lower bound is 6, so this never fires on a real run; Draw is public and
        // takes an arbitrary size, which is the only way to reach it.
        return Math.Max(1, (int)Math.Round(min, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// The lines the id stamp is laid out as: the banner, the id broken at its `15` §D1 underscores,
    /// and the delivery size.
    /// </summary>
    /// <remarks>
    /// 🔒 Broken at underscores rather than at a character count, because §D1's segments are the
    /// meaningful units — <c>chr_enemy_clockwork_skirmisher_attack</c> read across four lines is
    /// still the id, and the same string cut mid-word at 16 characters is not.
    /// </remarks>
    /// <param name="spec">The row's spec.</param>
    public static IReadOnlyList<string> StampLines(AssetSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var lines = new List<string> { Banner };
        var current = string.Empty;

        foreach (var segment in spec.Id.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? segment : current + "_" + segment;
            if (candidate.Length <= MaxLineCharacters || current.Length == 0)
            {
                current = candidate;
                continue;
            }

            lines.Add(current);
            current = segment;
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        lines.Add($"{spec.TargetSize.Width}x{spec.TargetSize.Height}");
        return lines;
    }

    /// <summary>
    /// Fills the card with one flat colour: one row built once, then block-copied down.
    /// </summary>
    /// <remarks>
    /// 🔒 The card is up to 3.6 M pixels at `15` §C's 2048 canvas and the batch draws hundreds of
    /// them, so writing four bytes at a time here was the single largest source of per-pixel work in
    /// a run. The bytes written are identical.
    /// </remarks>
    private static void Fill(byte[] pixels, PixelSize canvas, SKRectI card, SKColor colour)
    {
        var row = new byte[card.Width * 4];
        for (var index = 0; index < row.Length; index += 4)
        {
            row[index] = colour.Red;
            row[index + 1] = colour.Green;
            row[index + 2] = colour.Blue;
            row[index + 3] = byte.MaxValue;
        }

        for (var y = card.Top; y < card.Bottom; y++)
        {
            Buffer.BlockCopy(
                row, 0, pixels, (((y * canvas.Width) + card.Left) * 4), row.Length);
        }
    }

    /// <summary>
    /// The two diagonals of the card, the universal "there is no art here" mark.
    /// </summary>
    /// <remarks>
    /// Drawn by perpendicular distance to each diagonal rather than by stepping a line, so both
    /// strokes are the same width whatever the card's aspect and both survive `15` §B4 step 5's
    /// downscale to 96×96.
    /// </remarks>
    private static void DrawCross(
        byte[] pixels, PixelSize canvas, SKRectI card, SKColor colour, int width)
    {
        var halfWidth = width / 2d;
        double spanX = card.Width - 1;
        double spanY = card.Height - 1;
        var normal = Math.Sqrt((spanX * spanX) + (spanY * spanY));

        if (normal <= 0d)
        {
            return;
        }

        // 🔒 Compared as cross products against a scaled threshold rather than as distances: the
        // division by `normal` is loop-invariant, and both numerators are affine in x, so each row
        // steps by ±spanY instead of recomputing. Identical pixels; billions fewer divisions across
        // a full register run.
        var limit = halfWidth * normal;

        for (var y = card.Top; y < card.Bottom; y++)
        {
            double localY = y - card.Top;
            var falling = -(localY * spanX);
            var rising = (spanX * spanY) - (localY * spanX);

            for (var x = card.Left; x < card.Right; x++, falling += spanY, rising -= spanY)
            {
                if (Math.Abs(falling) <= limit || Math.Abs(rising) <= limit)
                {
                    Write(pixels, canvas, x, y, colour);
                }
            }
        }
    }

    /// <summary>Stamps the id on the card, or reports that it did not fit.</summary>
    /// <returns>False when the card is too small to carry the stamp at any whole scale.</returns>
    private static bool DrawStamp(
        byte[] pixels, PixelSize canvas, SKRectI card, SKColor colour, AssetSpec spec, int outline)
    {
        var lines = StampLines(spec);
        var padding = outline * 2;
        var availableWidth = card.Width - (padding * 2);
        var availableHeight = card.Height - (padding * 2);

        var textWidth = lines.Max(StampFont.MeasureWidth);
        var textHeight = (lines.Count * StampFont.LineAdvance)
                         - (StampFont.LineAdvance - StampFont.GlyphHeight);

        if (textWidth <= 0 || textHeight <= 0 || availableWidth <= 0 || availableHeight <= 0)
        {
            return false;
        }

        var scale = Math.Min(availableWidth / textWidth, availableHeight / textHeight);
        if (scale < 1)
        {
            // The card is too small to carry a legible stamp at all. Drawing an illegible smear
            // would be worse than leaving the card unstamped — and the caller reports it, so
            // PlaceholderBatchReport.Unstamped names every asset this happened to rather than the
            // run silently claiming every placeholder is stamped.
            return false;
        }

        var blockLeft = card.Left + ((card.Width - (textWidth * scale)) / 2);
        var blockTop = card.Top + ((card.Height - (textHeight * scale)) / 2);

        for (var line = 0; line < lines.Count; line++)
        {
            var text = lines[line];
            var lineLeft = blockLeft + (((textWidth - StampFont.MeasureWidth(text)) * scale) / 2);
            var lineTop = blockTop + (line * StampFont.LineAdvance * scale);

            for (var index = 0; index < text.Length; index++)
            {
                DrawGlyph(
                    pixels,
                    canvas,
                    card,
                    colour,
                    text[index],
                    lineLeft + (index * StampFont.GlyphAdvance * scale),
                    lineTop,
                    scale);
            }
        }

        return true;
    }

    private static void DrawGlyph(
        byte[] pixels,
        PixelSize canvas,
        SKRectI card,
        SKColor colour,
        char character,
        int left,
        int top,
        int scale)
    {
        for (var cellY = 0; cellY < StampFont.GlyphHeight; cellY++)
        {
            for (var cellX = 0; cellX < StampFont.GlyphWidth; cellX++)
            {
                if (!StampFont.Ink(character, cellX, cellY))
                {
                    continue;
                }

                for (var y = 0; y < scale; y++)
                {
                    for (var x = 0; x < scale; x++)
                    {
                        var pixelX = left + (cellX * scale) + x;
                        var pixelY = top + (cellY * scale) + y;

                        // 🔒 Clipped to the card, never to the canvas. A glyph that spilled past the
                        // card would write an opaque pixel outside the rectangle, and the alpha
                        // bounding box `15` Part F item 7 measures would stop being the rectangle.
                        if (card.Contains(pixelX, pixelY))
                        {
                            Write(pixels, canvas, pixelX, pixelY, colour);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// The card's outline: <paramref name="width"/> layers inward from the edge, `15` §A3's colour
    /// on all but the innermost, which is blended a quarter of the way toward the fill.
    /// </summary>
    private static void DrawOutline(
        byte[] pixels, PixelSize canvas, SKRectI card, SKColor outline, SKColor fill, int width)
    {
        var inner = PlaceholderPalette.Blend(outline, fill, InnerEdgeBlend);

        for (var layer = 0; layer < width; layer++)
        {
            var colour = layer == width - 1 ? inner : outline;
            var left = card.Left + layer;
            var top = card.Top + layer;
            var right = card.Right - 1 - layer;
            var bottom = card.Bottom - 1 - layer;

            if (left > right || top > bottom)
            {
                return;
            }

            for (var x = left; x <= right; x++)
            {
                Write(pixels, canvas, x, top, colour);
                Write(pixels, canvas, x, bottom, colour);
            }

            for (var y = top; y <= bottom; y++)
            {
                Write(pixels, canvas, left, y, colour);
                Write(pixels, canvas, right, y, colour);
            }
        }
    }

    private static void Write(byte[] pixels, PixelSize canvas, int x, int y, SKColor colour)
    {
        var offset = (((y * canvas.Width) + x) * 4);
        pixels[offset] = colour.Red;
        pixels[offset + 1] = colour.Green;
        pixels[offset + 2] = colour.Blue;

        // 🔒 Fully opaque, always. Every call site writes inside the card, and the card is what the
        // alpha bounding box is. A partially transparent pixel here would be a fringe of this
        // generator's own making — the halo `15` §B4 step 1 and Part F item 6 exist to catch.
        pixels[offset + 3] = byte.MaxValue;
    }

    private static SKBitmap ToBitmap(byte[] pixels, PixelSize canvas)
    {
        var bitmap = new SKBitmap(
            new SKImageInfo(canvas.Width, canvas.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        var destination = bitmap.GetPixels(out var length);
        if (destination == IntPtr.Zero || (long)length < (long)bitmap.RowBytes * canvas.Height)
        {
            bitmap.Dispose();
            throw new InvalidOperationException(
                $"Skia allocated no writable {canvas} surface for a placeholder.");
        }

        var rowLength = canvas.Width * 4;
        for (var y = 0; y < canvas.Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(
                pixels, y * rowLength, IntPtr.Add(destination, y * bitmap.RowBytes), rowLength);
        }

        return bitmap;
    }
}
