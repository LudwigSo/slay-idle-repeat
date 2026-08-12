using SkiaSharp;
using SlayIdleRepeat.AssetManifest;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// A generated fixture image, together with the exact answer it was generated to have.
/// </summary>
/// <remarks>
/// 🔒 Every list here is produced by the same closed-form predicate that painted the pixels, so it
/// is the fixture's <em>stated</em> answer. A case asserts against these, never against whatever
/// the code under test produced — a fixture that measured its own output would agree with any
/// implementation, correct or not.
/// </remarks>
internal sealed record SyntheticFixture
{
    /// <summary>What this fixture is, for a failure message.</summary>
    public required string Description { get; init; }

    /// <summary>The image, always Rgba8888 / Unpremul.</summary>
    public required SKBitmap Image { get; init; }

    /// <summary>The exact colour every background pixel was painted.</summary>
    public required SKColor BackgroundColour { get; init; }

    /// <summary>The exact colour every interior pixel was painted.</summary>
    public required SKColor SubjectColour { get; init; }

    /// <summary>The exact colour every outline pixel was painted — `15` §A3's #231A2E.</summary>
    public required SKColor OutlineColour { get; init; }

    /// <summary>The outline's width in pixels, exact by construction.</summary>
    public required int OutlineWidth { get; init; }

    /// <summary>The subject's bounding box: left/top inclusive, right/bottom exclusive.</summary>
    public required SKRectI ContentBounds { get; init; }

    /// <summary>Every outline pixel.</summary>
    public required IReadOnlyList<SKPointI> OutlinePixels { get; init; }

    /// <summary>Every interior (non-outline subject) pixel.</summary>
    public required IReadOnlyList<SKPointI> InteriorPixels { get; init; }

    /// <summary>Every background pixel.</summary>
    public required IReadOnlyList<SKPointI> BackgroundPixels { get; init; }

    /// <summary>Background pixels more than one pixel away from the subject.</summary>
    public required IReadOnlyList<SKPointI> BackgroundPixelsAwayFromEdge { get; init; }

    /// <summary>Subject-side edge pixels blended toward white — step 1's decontamination target.</summary>
    public IReadOnlyList<SKPointI> HaloedEdgePixels { get; init; } = [];

    /// <summary>Background-side halo pixels, blended toward white.</summary>
    public IReadOnlyList<SKPointI> HaloedBackgroundPixels { get; init; } = [];

    /// <summary>Outline pixels overpainted with the background colour to punch a break.</summary>
    public IReadOnlyList<SKPointI> GapPixels { get; init; } = [];

    /// <summary>Outline pixels far enough from the gap that step 4 must leave them alone.</summary>
    public IReadOnlyList<SKPointI> OutlinePixelsFarFromGap { get; init; } = [];
}

/// <summary>A biome-palette image carrying one deliberately off-palette pixel.</summary>
/// <param name="Image">The image, Rgba8888 / Unpremul.</param>
/// <param name="Palette">The `15` §A5 palette every other pixel was painted from.</param>
/// <param name="OffPalettePixel">Where the off-palette pixel is.</param>
/// <param name="OffPaletteColour">Exactly what was painted there.</param>
/// <param name="ExpectedSnappedColour">The §A5 hue it is unambiguously nearest to.</param>
internal sealed record PaletteFixture(
    SKBitmap Image,
    Palette Palette,
    SKPointI OffPalettePixel,
    SKColor OffPaletteColour,
    SKColor ExpectedSnappedColour);

/// <summary>An opaque rectangle placed off-centre in an oversized transparent frame.</summary>
/// <param name="Image">The image, Rgba8888 / Unpremul.</param>
/// <param name="ContentBounds">The opaque rectangle: left/top inclusive, right/bottom exclusive.</param>
/// <param name="Colour">The exact colour the rectangle was painted.</param>
internal sealed record CanvasFixture(SKBitmap Image, SKRectI ContentBounds, SKColor Colour);

/// <summary>
/// Builds the fixture images this suite runs on, in code, at run time.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 No binary fixture is committed. Six of M8's eight original tasks are capability-blocked, so
/// there is no real generated asset to test against; synthetic images with a closed-form answer are
/// the expected approach here, not a compromise. What they prove is that the mechanics are correct
/// on a known input. What they do <b>not</b> prove is that any threshold is right for real
/// Midjourney output — that is M8-10's job, and it is why every threshold ships null.
/// </para>
/// <para>
/// 🔒 Everything is <see cref="SKColorType.Rgba8888"/> / <see cref="SKAlphaType.Unpremul"/>.
/// Straight alpha is `15` §C's delivery format and round-trips bit-for-bit; a premultiplied surface
/// destroys colour in low-alpha pixels, which is exactly the halo `15` §B4 step 1 must remove.
/// </para>
/// <para>
/// The chibi shape is a union of two circles — a body and a smaller head — with the outline defined
/// as the band between the shape and the shape eroded by <see cref="OutlineWidth"/>. That makes the
/// outline width exact by construction rather than approximate, which a stroked path with
/// antialiasing off would not be.
/// </para>
/// </remarks>
internal static class SyntheticAsset
{
    /// <summary>The fixture canvas, square.</summary>
    internal const int Canvas = 64;

    /// <summary>The outline's width in pixels, exact by construction.</summary>
    internal const int OutlineWidth = 3;

    /// <summary>How far the halo blends the edge toward white, as a fraction.</summary>
    internal const double HaloBlend = 0.5;

    private const double BodyCentreX = 32d;
    private const double BodyCentreY = 40d;
    private const double BodyRadius = 16d;
    private const double HeadCentreX = 32d;
    private const double HeadCentreY = 20d;
    private const double HeadRadius = 12d;

    /// <summary>The opaque background every chibi fixture starts from.</summary>
    internal static SKColor BackgroundColour { get; } = new(0x4A, 0x90, 0xD9);

    /// <summary>The subject's fill colour.</summary>
    internal static SKColor SubjectColour { get; } = new(0xE8, 0xC4, 0x8A);

    /// <summary>`15` §A3's outline colour, taken from the production constant, not retyped.</summary>
    internal static SKColor OutlineColour => Doc15Authorised.OutlineColour;

    /// <summary>The gap window punched into the head's outline.</summary>
    private static readonly SKRectI GapWindow = new(30, 6, 35, 13);

    /// <summary>How far from the gap window a pixel must be to count as "far from the gap".</summary>
    private const double FarFromGapDistance = 16d;

    private static readonly Lazy<Shape> LazyShape = new(BuildShape);

    /// <summary>A chibi blob on an opaque, exactly-known background.</summary>
    internal static SyntheticFixture Chibi() => Paint("chibi on an opaque background", BackgroundColour);

    /// <summary>
    /// The same chibi with the background already keyed out — what `15` §B4 step 2 onward see.
    /// </summary>
    internal static SyntheticFixture ChibiCutOut() =>
        Paint("chibi with the background already transparent", SKColors.Transparent);

    /// <summary>
    /// The same chibi with a deliberate halo: the outermost subject band and the ring of background
    /// touching it are blended <see cref="HaloBlend"/> of the way toward white.
    /// </summary>
    internal static SyntheticFixture ChibiWithHalo()
    {
        var shape = LazyShape.Value;
        var fixture = Paint("chibi with a white halo", BackgroundColour);

        foreach (var point in shape.SubjectEdge)
        {
            fixture.Image.SetPixel(point.X, point.Y, TowardWhite(OutlineColour, HaloBlend));
        }

        foreach (var point in shape.BackgroundEdge)
        {
            fixture.Image.SetPixel(point.X, point.Y, TowardWhite(BackgroundColour, HaloBlend));
        }

        return fixture with
        {
            Description = "chibi with a white halo",
            HaloedEdgePixels = shape.SubjectEdge,
            HaloedBackgroundPixels = shape.BackgroundEdge,
        };
    }

    /// <summary>
    /// The same chibi with a break punched in the head's outline — the outline pixels inside
    /// <see cref="GapWindow"/> overpainted with the background colour.
    /// </summary>
    internal static SyntheticFixture ChibiWithOutlineGap()
    {
        var shape = LazyShape.Value;
        var fixture = Paint("chibi with a gap in its outline", BackgroundColour);

        foreach (var point in shape.Gap)
        {
            fixture.Image.SetPixel(point.X, point.Y, BackgroundColour);
        }

        return fixture with
        {
            Description = "chibi with a gap in its outline",
            GapPixels = shape.Gap,
            OutlinePixelsFarFromGap = shape.OutlineFarFromGap,
        };
    }

    /// <summary>
    /// A 32x32 image painted entirely from one biome's `15` §A5 palette plus the §A3 outline
    /// colour, with exactly one pixel nudged off-palette toward nothing in particular.
    /// </summary>
    /// <param name="palette">The biome palette, read from the shipped manifest row.</param>
    internal static PaletteFixture BiomePalette(Palette palette)
    {
        const int size = 32;
        const int half = size / 2;
        var offPixel = new SKPointI(half, half);

        var baseHue = SKColor.Parse(palette.Base);
        var shadow = SKColor.Parse(palette.Shadow);
        var accent = SKColor.Parse(palette.Accent);
        var glow = SKColor.Parse(palette.Glow);

        var image = NewBitmap(size, size);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var quadrant = (x < half, y < half) switch
                {
                    (true, true) => baseHue,
                    (false, true) => shadow,
                    (true, false) => accent,
                    (false, false) => glow,
                };

                var onBorder = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                image.SetPixel(x, y, onBorder ? OutlineColour : quadrant);
            }
        }

        // Sixteen units off the base hue in every channel: far enough that it is genuinely not one
        // of the six, and near enough that base is unambiguously the closest of the six plus the
        // outline colour. Both are stated here; no case recomputes them from the palette.
        var off = new SKColor(
            (byte)(baseHue.Red + 16), (byte)(baseHue.Green + 16), (byte)(baseHue.Blue + 16));
        image.SetPixel(offPixel.X, offPixel.Y, off);

        return new PaletteFixture(image, palette, offPixel, off, baseHue);
    }

    /// <summary>
    /// An opaque rectangle placed off-centre in an oversized transparent frame — `15` §B4 step 2's
    /// input.
    /// </summary>
    /// <param name="contentWidth">The rectangle's width.</param>
    /// <param name="contentHeight">The rectangle's height.</param>
    internal static CanvasFixture OffCentreSubject(int contentWidth, int contentHeight)
    {
        // Deliberately off-centre in both axes and touching neither edge, so a trim that silently
        // did nothing would land the content somewhere the case does not expect.
        const int left = 5;
        const int top = 3;

        var image = NewBitmap(Canvas, Canvas);
        for (var y = top; y < top + contentHeight; y++)
        {
            for (var x = left; x < left + contentWidth; x++)
            {
                image.SetPixel(x, y, SubjectColour);
            }
        }

        return new CanvasFixture(
            image,
            new SKRectI(left, top, left + contentWidth, top + contentHeight),
            SubjectColour);
    }

    /// <summary>
    /// A 4x4 image whose centre pixel is exactly <c>#80FF0000</c> in straight alpha — the probe
    /// `15` §C's "PNG-32 straight alpha" claim is checked with.
    /// </summary>
    internal static (SKBitmap Image, SKPointI Probe, SKColor Colour) SemiTransparentProbe()
    {
        var colour = new SKColor(0xFF, 0x00, 0x00, 0x80);
        var probe = new SKPointI(2, 2);

        var image = NewBitmap(4, 4);
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                image.SetPixel(x, y, new SKColor(0x00, 0x40, 0x80, 0xFF));
            }
        }

        image.SetPixel(probe.X, probe.Y, colour);
        return (image, probe, colour);
    }

    /// <summary>A solid opaque square, for the atlas packer, which cares only about sizes.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    internal static SKBitmap SolidBlock(int width, int height)
    {
        var image = NewBitmap(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image.SetPixel(x, y, SubjectColour);
            }
        }

        return image;
    }

    /// <summary>A new empty bitmap in the one colour type and alpha type this pipeline uses.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    internal static SKBitmap NewBitmap(int width, int height) =>
        new(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));

    private static SyntheticFixture Paint(string description, SKColor background)
    {
        var shape = LazyShape.Value;
        var image = NewBitmap(Canvas, Canvas);

        foreach (var point in shape.Background)
        {
            image.SetPixel(point.X, point.Y, background);
        }

        foreach (var point in shape.Interior)
        {
            image.SetPixel(point.X, point.Y, SubjectColour);
        }

        foreach (var point in shape.Outline)
        {
            image.SetPixel(point.X, point.Y, OutlineColour);
        }

        return new SyntheticFixture
        {
            Description = description,
            Image = image,
            BackgroundColour = background,
            SubjectColour = SubjectColour,
            OutlineColour = OutlineColour,
            OutlineWidth = OutlineWidth,
            ContentBounds = shape.ContentBounds,
            OutlinePixels = shape.Outline,
            InteriorPixels = shape.Interior,
            BackgroundPixels = shape.Background,
            BackgroundPixelsAwayFromEdge = shape.BackgroundAwayFromEdge,
        };
    }

    private static SKColor TowardWhite(SKColor colour, double fraction) => new(
        (byte)Math.Round(colour.Red + ((255 - colour.Red) * fraction)),
        (byte)Math.Round(colour.Green + ((255 - colour.Green) * fraction)),
        (byte)Math.Round(colour.Blue + ((255 - colour.Blue) * fraction)),
        colour.Alpha);

    private static Shape BuildShape()
    {
        var inside = new bool[Canvas, Canvas];
        var core = new bool[Canvas, Canvas];

        for (var y = 0; y < Canvas; y++)
        {
            for (var x = 0; x < Canvas; x++)
            {
                var toBody = Distance(x, y, BodyCentreX, BodyCentreY);
                var toHead = Distance(x, y, HeadCentreX, HeadCentreY);
                inside[x, y] = toBody <= BodyRadius || toHead <= HeadRadius;
                core[x, y] = toBody <= BodyRadius - OutlineWidth || toHead <= HeadRadius - OutlineWidth;
            }
        }

        var outline = new List<SKPointI>();
        var interior = new List<SKPointI>();
        var background = new List<SKPointI>();
        var backgroundAwayFromEdge = new List<SKPointI>();
        var subjectEdge = new List<SKPointI>();
        var backgroundEdge = new List<SKPointI>();
        var left = Canvas;
        var top = Canvas;
        var right = 0;
        var bottom = 0;

        for (var y = 0; y < Canvas; y++)
        {
            for (var x = 0; x < Canvas; x++)
            {
                var point = new SKPointI(x, y);
                var touchesOther = TouchesDifferentRegion(inside, x, y);

                if (!inside[x, y])
                {
                    background.Add(point);
                    if (touchesOther)
                    {
                        backgroundEdge.Add(point);
                    }
                    else
                    {
                        backgroundAwayFromEdge.Add(point);
                    }

                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + 1);
                bottom = Math.Max(bottom, y + 1);

                if (core[x, y])
                {
                    interior.Add(point);
                    continue;
                }

                outline.Add(point);
                if (touchesOther)
                {
                    subjectEdge.Add(point);
                }
            }
        }

        var gap = outline.Where(p => GapWindow.Contains(p.X, p.Y)).ToArray();
        var gapCentreX = (GapWindow.Left + GapWindow.Right) / 2d;
        var gapCentreY = (GapWindow.Top + GapWindow.Bottom) / 2d;
        var far = outline
            .Where(p => Distance(p.X, p.Y, gapCentreX, gapCentreY) > FarFromGapDistance)
            .ToArray();

        return new Shape(
            outline, interior, background, backgroundAwayFromEdge, subjectEdge, backgroundEdge,
            gap, far, new SKRectI(left, top, right, bottom));
    }

    private static bool TouchesDifferentRegion(bool[,] inside, int x, int y)
    {
        var self = inside[x, y];
        return Differs(inside, x - 1, y, self)
               || Differs(inside, x + 1, y, self)
               || Differs(inside, x, y - 1, self)
               || Differs(inside, x, y + 1, self);
    }

    private static bool Differs(bool[,] inside, int x, int y, bool self) =>
        x >= 0 && y >= 0 && x < Canvas && y < Canvas && inside[x, y] != self;

    private static double Distance(int x, int y, double centreX, double centreY)
    {
        var dx = x + 0.5 - centreX;
        var dy = y + 0.5 - centreY;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private sealed record Shape(
        IReadOnlyList<SKPointI> Outline,
        IReadOnlyList<SKPointI> Interior,
        IReadOnlyList<SKPointI> Background,
        IReadOnlyList<SKPointI> BackgroundAwayFromEdge,
        IReadOnlyList<SKPointI> SubjectEdge,
        IReadOnlyList<SKPointI> BackgroundEdge,
        IReadOnlyList<SKPointI> Gap,
        IReadOnlyList<SKPointI> OutlineFarFromGap,
        SKRectI ContentBounds);
}
