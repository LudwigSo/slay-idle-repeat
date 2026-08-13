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
/// A filled square framed by an outline ring of an exactly known width, on a transparent canvas.
/// </summary>
/// <remarks>
/// 🔒 A ring rather than the chibi blob, because `15` §A3 states the outline weight as a band that
/// scales with the canvas (3-4 px at 512 px) and a rasterised circle's radial band is only
/// approximately its nominal thickness. A square ring is exactly its thickness everywhere, so the
/// case can state a band membership rather than a tolerance around one.
/// </remarks>
/// <param name="Image">The image, Rgba8888 / Unpremul.</param>
/// <param name="Canvas">The square canvas's side, in pixels — what §A3's band scales against.</param>
/// <param name="OutlineWidth">The ring's thickness, exact by construction.</param>
/// <param name="OutlineColour">Exactly what the ring was painted.</param>
/// <param name="OutlinePixels">Every ring pixel still painted (the gap's are not here).</param>
/// <param name="InteriorPixels">Every pixel the ring encloses.</param>
/// <param name="GapPixels">Ring pixels punched out to transparent, breaking the enclosure.</param>
internal sealed record OutlinedBoxFixture(
    SKBitmap Image,
    int Canvas,
    int OutlineWidth,
    SKColor OutlineColour,
    IReadOnlyList<SKPointI> OutlinePixels,
    IReadOnlyList<SKPointI> InteriorPixels,
    IReadOnlyList<SKPointI> GapPixels);

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

    /// <summary>The palette fixture's canvas, square.</summary>
    internal const int PaletteCanvas = 32;

    /// <summary>
    /// The outlined-box fixture's canvas, square.
    /// </summary>
    /// <remarks>
    /// 🔒 Chosen so that `15` §A3's "3-4 px at 512 px canvas, scaled proportionally" lands on whole
    /// pixels here: at 256 the band is exactly 1.5-2.0 px, and <see cref="OutlineBoxWidth"/> sits
    /// inside it by construction rather than by rounding. 64 (the chibi's canvas) would put the
    /// band at 0.375-0.5 px, where no whole-pixel ring can conform to §A3 at all.
    /// </remarks>
    internal const int OutlineBoxCanvas = 256;

    /// <summary>
    /// The outlined-box ring's thickness — inside `15` §A3's band at
    /// <see cref="OutlineBoxCanvas"/>, exactly.
    /// </summary>
    internal const int OutlineBoxWidth = 2;

    /// <summary>
    /// A ring thickness far outside `15` §A3's band at <see cref="OutlineBoxCanvas"/> — four times
    /// the band's upper bound, so no plausible uniformity tolerance rescues it.
    /// </summary>
    internal const int OutlineBoxWidthTooWide = 8;

    /// <summary>How many pixels of the ring's top edge <see cref="OutlinedBox"/> punches out.</summary>
    internal const int OutlineBoxGapLength = 6;

    /// <summary>The alpha the halo fixture gives the subject's outermost band.</summary>
    internal const byte FringeSubjectAlpha = 128;

    /// <summary>The alpha the halo fixture gives the ring of background just outside the subject.</summary>
    internal const byte FringeBackgroundAlpha = 64;

    /// <summary>The side of the opaque block <see cref="ChibiCutOutWithCornerSignature"/> stamps.</summary>
    internal const int CornerSignatureSize = 8;

    /// <summary>The side of one blob <see cref="SeparatedBlobs"/> paints.</summary>
    internal const int BlobSize = 8;

    /// <summary>How many blobs fit across one row of the frame, at one blob's spacing between them.</summary>
    internal const int BlobsPerRow = Canvas / (BlobSize * 2);

    /// <summary>How many separated blobs the frame holds in total.</summary>
    internal const int MaxBlobs = BlobsPerRow * BlobsPerRow;

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
        var image = OnPalette(palette);
        var baseHue = SKColor.Parse(palette.Base);
        var offPixel = new SKPointI(PaletteCanvas / 2, PaletteCanvas / 2);

        // Sixteen units off the base hue in every channel: far enough that it is genuinely not one
        // of the six, and near enough that base is unambiguously the closest of the six plus the
        // outline colour. Both are stated here; no case recomputes them from the palette.
        var off = new SKColor(
            (byte)(baseHue.Red + 16), (byte)(baseHue.Green + 16), (byte)(baseHue.Blue + 16));
        image.SetPixel(offPixel.X, offPixel.Y, off);

        return new PaletteFixture(image, palette, offPixel, off, baseHue);
    }

    /// <summary>
    /// The same image with <b>no</b> off-palette pixel: every pixel is one of four `15` §A5 hues or
    /// the §A3 outline colour, so `15` Part F item 5 has nothing to find.
    /// </summary>
    /// <remarks>
    /// 🔒 The passing half of item 5's pair. Without it the only fixture item 5 could run on is the
    /// one built to violate it, and a check hard-wired to fail would look correct.
    /// </remarks>
    /// <param name="palette">The biome palette, read from the shipped manifest row.</param>
    internal static SKBitmap OnPalette(Palette palette)
    {
        const int half = PaletteCanvas / 2;

        var baseHue = SKColor.Parse(palette.Base);
        var shadow = SKColor.Parse(palette.Shadow);
        var accent = SKColor.Parse(palette.Accent);
        var glow = SKColor.Parse(palette.Glow);

        var image = NewBitmap(PaletteCanvas, PaletteCanvas);
        for (var y = 0; y < PaletteCanvas; y++)
        {
            for (var x = 0; x < PaletteCanvas; x++)
            {
                var quadrant = (x < half, y < half) switch
                {
                    (true, true) => baseHue,
                    (false, true) => shadow,
                    (true, false) => accent,
                    (false, false) => glow,
                };

                var onBorder = x == 0 || y == 0 || x == PaletteCanvas - 1 || y == PaletteCanvas - 1;
                image.SetPixel(x, y, onBorder ? OutlineColour : quadrant);
            }
        }

        return image;
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

    /// <summary>
    /// The cut-out chibi with a genuine alpha fringe: the subject's outermost band and the ring of
    /// frame just outside it painted white at partial alpha.
    /// </summary>
    /// <remarks>
    /// 🔒 Distinct from <see cref="ChibiWithHalo"/>, which is fully opaque throughout and models the
    /// halo `15` §B4 step 1 has to remove. `15` Part F item 6 grades what came out the other end, so
    /// its fixture needs pixels that are neither fully on nor fully off — the "semi-transparent
    /// fringe" the item names, which an opaque fixture cannot express at all.
    /// </remarks>
    internal static SyntheticFixture ChibiCutOutWithFringe()
    {
        var shape = LazyShape.Value;
        var fixture = Paint("chibi with a semi-transparent white fringe", SKColors.Transparent);

        foreach (var point in shape.SubjectEdge)
        {
            fixture.Image.SetPixel(point.X, point.Y, new SKColor(255, 255, 255, FringeSubjectAlpha));
        }

        foreach (var point in shape.BackgroundEdge)
        {
            fixture.Image.SetPixel(
                point.X, point.Y, new SKColor(255, 255, 255, FringeBackgroundAlpha));
        }

        return fixture with
        {
            Description = "chibi with a semi-transparent white fringe",
            HaloedEdgePixels = shape.SubjectEdge,
            HaloedBackgroundPixels = shape.BackgroundEdge,
        };
    }

    /// <summary>
    /// The cut-out chibi with an opaque block stamped into the bottom-right corner of the otherwise
    /// transparent frame — the signature-in-a-corner failure `15` Part F item 8's proxy looks for.
    /// </summary>
    /// <returns>The image and the exact rectangle that was stamped.</returns>
    internal static (SKBitmap Image, SKRectI Signature) ChibiCutOutWithCornerSignature()
    {
        var image = ChibiCutOut().Image;
        var signature = new SKRectI(
            Canvas - CornerSignatureSize, Canvas - CornerSignatureSize, Canvas, Canvas);

        for (var y = signature.Top; y < signature.Bottom; y++)
        {
            for (var x = signature.Left; x < signature.Right; x++)
            {
                image.SetPixel(x, y, OutlineColour);
            }
        }

        return (image, signature);
    }

    /// <summary>
    /// A transparent 64x64 frame carrying a stated number of separated opaque squares, in a row.
    /// </summary>
    /// <remarks>
    /// 🔒 The blobs are <see cref="BlobSize"/> apart, which is more than one pixel in every
    /// direction, so they are separate under 8-connectivity as well as 4-. A gap of one pixel would
    /// make the expected component count depend on which connectivity the implementation chose,
    /// and the case would then be pinning an accident.
    /// </remarks>
    /// <param name="count">How many blobs, 1 to <see cref="MaxBlobs"/>.</param>
    /// <returns>The image and how many pixels it painted opaque.</returns>
    internal static (SKBitmap Image, int OpaquePixelCount) SeparatedBlobs(int count)
    {
        if (count < 1 || count > MaxBlobs)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count), count, $"The {Canvas} px frame holds 1 to {MaxBlobs} separated blobs.");
        }

        var image = NewBitmap(Canvas, Canvas);
        var painted = 0;

        for (var blob = 0; blob < count; blob++)
        {
            var left = blob % BlobsPerRow * BlobSize * 2;
            var top = blob / BlobsPerRow * BlobSize * 2;

            for (var y = top; y < top + BlobSize; y++)
            {
                for (var x = left; x < left + BlobSize; x++)
                {
                    image.SetPixel(x, y, SubjectColour);
                    painted++;
                }
            }
        }

        return (image, painted);
    }

    /// <summary>
    /// An opaque rectangle placed exactly where a stated `15` §C pivot puts it on a square canvas.
    /// </summary>
    /// <remarks>
    /// 🔒 The passing half of `15` Part F item 7's pair, and deliberately smaller than the canvas: a
    /// subject filling the frame satisfies every pivot at once, so a check that ignored the pivot
    /// entirely would pass on it.
    /// </remarks>
    /// <param name="canvas">The square canvas's side. Must exceed both content dimensions.</param>
    /// <param name="contentWidth">The rectangle's width. Even, so centring lands on whole pixels.</param>
    /// <param name="contentHeight">The rectangle's height.</param>
    /// <param name="pivot">One of <see cref="Doc15Pivots.All"/>.</param>
    internal static CanvasFixture PivotedSubject(
        int canvas, int contentWidth, int contentHeight, string pivot)
    {
        var left = (canvas - contentWidth) / 2;
        var top = pivot switch
        {
            Doc15Pivots.Center => (canvas - contentHeight) / 2,
            Doc15Pivots.BottomCenter => canvas - contentHeight,
            _ => throw new ArgumentOutOfRangeException(
                nameof(pivot),
                pivot,
                $"`15` §C authorises {string.Join(" and ", Doc15Pivots.All)} and nothing else, so " +
                "there is no correct placement for this one to be the fixture of."),
        };

        var image = NewBitmap(canvas, canvas);
        for (var y = top; y < top + contentHeight; y++)
        {
            for (var x = left; x < left + contentWidth; x++)
            {
                image.SetPixel(x, y, SubjectColour);
            }
        }

        return new CanvasFixture(
            image, new SKRectI(left, top, left + contentWidth, top + contentHeight), SubjectColour);
    }

    /// <summary>
    /// A filled square framed by a ring of exactly known thickness and colour, on a transparent
    /// canvas — `15` Part F item 3's fixture.
    /// </summary>
    /// <param name="outlineWidth">The ring's thickness in pixels.</param>
    /// <param name="outlineColour">
    /// What to paint the ring. Null paints `15` §A3's #231A2E; anything else builds the
    /// wrong-colour violation.
    /// </param>
    /// <param name="gapLength">
    /// How many pixels of the ring's top edge to punch out to transparent, breaking the enclosure.
    /// Zero leaves the ring continuous.
    /// </param>
    internal static OutlinedBoxFixture OutlinedBox(
        int outlineWidth, SKColor? outlineColour = null, int gapLength = 0)
    {
        var ringColour = outlineColour ?? OutlineColour;
        var image = NewBitmap(OutlineBoxCanvas, OutlineBoxCanvas);
        var ring = new List<SKPointI>();
        var interior = new List<SKPointI>();
        var gap = new List<SKPointI>();
        var gapLeft = (OutlineBoxCanvas / 2) - (gapLength / 2);

        for (var y = 0; y < OutlineBoxCanvas; y++)
        {
            for (var x = 0; x < OutlineBoxCanvas; x++)
            {
                var point = new SKPointI(x, y);
                var onRing = x < outlineWidth
                             || y < outlineWidth
                             || x >= OutlineBoxCanvas - outlineWidth
                             || y >= OutlineBoxCanvas - outlineWidth;

                if (!onRing)
                {
                    image.SetPixel(x, y, SubjectColour);
                    interior.Add(point);
                    continue;
                }

                if (y < outlineWidth && x >= gapLeft && x < gapLeft + gapLength)
                {
                    gap.Add(point);
                    continue;
                }

                image.SetPixel(x, y, ringColour);
                ring.Add(point);
            }
        }

        return new OutlinedBoxFixture(
            image, OutlineBoxCanvas, outlineWidth, ringColour, ring, interior, gap);
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
