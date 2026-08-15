using SkiaSharp;
using SlayIdleRepeat.AssetManifest;
using SlayIdleRepeat.AssetPipeline.Qa;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>Threshold values these cases state for the synthetic fixtures they drive.</summary>
/// <remarks>
/// <para>
/// None of these is a calibration or a default; the shipped thresholds file ships every key null.
/// Every entry below carries the reason it is sufficient for the closed-form fixture it grades, but
/// none of them would mean anything against real generated art.
/// </para>
/// <para>
/// Kept separate from <see cref="StatedThresholds"/> on purpose: the step-level cases and these
/// Part F cases drive different fixtures and need different values from the same key — the
/// quantise step's fixture wants a <em>wide</em> <see cref="ThresholdKeys.PaletteMatchTolerance"/>
/// so the nearest hue is unambiguous, while item 5's fixture wants a <em>narrow</em> one so the same
/// deviation is a violation. Sharing one value would make one of the two cases vacuous, silently.
/// </para>
/// </remarks>
internal static class QaThresholds
{
    /// <summary>Every value the Part F cases state, keyed by <see cref="ThresholdKeys"/>.</summary>
    /// <remarks>
    /// Held as a table rather than a fluent chain so that <see cref="Except"/> can leave exactly one
    /// key null. Pinning "an uncalibrated check reports Uncalibrated and names <em>which</em> key"
    /// needs a set in which precisely one hole is open; a set with several open would let the check
    /// name any of them and still pass.
    /// </remarks>
    internal static IReadOnlyDictionary<string, ThresholdValue> Stated { get; } =
        new Dictionary<string, ThresholdValue>(StringComparer.Ordinal)
        {
            // Item 3: conforming outline sits at colour distance 0, the black-ringed violation at 63.4.
            [ThresholdKeys.OutlineColourTolerance] = new NumericThreshold(8d),

            // Item 3: the ring is constant thickness by construction; 1px of slack keeps the passing
            // case honest while still rejecting the 8px violation by 5px.
            [ThresholdKeys.OutlineWidthUniformityTolerance] = new NumericThreshold(1d),

            // Item 5: smallest neutrals list that lets a case run at all; the fixture uses neither.
            [ThresholdKeys.PaletteNeutrals] =
                new ColourListThreshold(["#FFFFFF", "#000000"]),

            // Item 5: the off-palette pixel is 27.7 units from the nearest permitted colour; 8 puts
            // it outside while every on-palette pixel (distance 0) stays comfortably inside.
            [ThresholdKeys.PaletteMatchTolerance] = new NumericThreshold(8d),

            // Item 6: the clean fixture's ratio is 0, the fringed one roughly a fifth; 0.05 separates them.
            [ThresholdKeys.HaloMaxFringeRatio] = new NumericThreshold(0.05d),

            // Item 6: the fringe is pure white against #E8C48A (over 100 luminance units); 32 is far
            // below that and far above a clean fixture's zero.
            [ThresholdKeys.HaloMaxLuminanceDeviation] = new NumericThreshold(32d),

            // Item 1: the chibi fills 28% of its frame, the single-blob violation 1.6%.
            [ThresholdKeys.SilhouetteMinCoverageRatio] = new NumericThreshold(0.05d),

            // Item 1: the chibi fills 75% of its bounding box, the five-blob fixture 24%; 0.15 clears
            // both since bounding-box fill isn't what either violation is about.
            [ThresholdKeys.SilhouetteMinBoundingBoxFill] = new NumericThreshold(0.15d),

            // Item 1: the chibi is one blob, the violation is five.
            [ThresholdKeys.SilhouetteMaxComponentCount] = new NumericThreshold(4d),

            // Item 1: an empty registry reports the maximum, so any value below 1 passes a first asset.
            [ThresholdKeys.SilhouetteMinDistinguishability] = new NumericThreshold(0.01d),

            // Item 8: evidence only, the item is human. Clean fixture's corners are transparent, the
            // stamped one's bottom-right is opaque.
            [ThresholdKeys.WatermarkCornerOpacityCeiling] = new NumericThreshold(0.5d),
        };

    /// <summary>Every Part F value stated.</summary>
    internal static ThresholdSet All() => Build(omitted: null);

    /// <summary>Every Part F value stated except one, which is left uncalibrated.</summary>
    /// <param name="omitted">The <see cref="ThresholdKeys"/> constant to leave null.</param>
    internal static ThresholdSet Except(string omitted) => Stated.ContainsKey(omitted)
        ? Build(omitted)
        : throw new ArgumentOutOfRangeException(
            nameof(omitted),
            omitted,
            "Only a key the Part F cases state can be omitted from their set — omitting one they " +
            "never state would produce a set identical to All() and a case that proves nothing.");

    private static ThresholdSet Build(string? omitted)
    {
        var set = ThresholdSet.Uncalibrated();
        foreach (var (key, value) in Stated)
        {
            if (string.Equals(key, omitted, StringComparison.Ordinal))
            {
                continue;
            }

            set = value switch
            {
                NumericThreshold number => set.With(key, number.Value),
                ColourListThreshold colours => set.WithColours(key, colours.Colours),
                _ => throw new InvalidOperationException(
                    $"'{key}' is stated as {value.GetType().Name}, which this builder cannot apply."),
            };
        }

        return set;
    }
}

/// <summary>The <see cref="QaSubject"/>s these cases judge, each assembled from a shipped manifest row and one synthetic image.</summary>
/// <remarks>
/// Every subject names a real row: some checks read the biome palette, delivery size/pivot, and
/// atlas straight off the register, so a fabricated row would make those cases self-referential.
/// </remarks>
internal static class QaSubjects
{
    /// <summary>A subject over a stated image and spec, with everything else at its neutral value.</summary>
    /// <param name="image">The processed image.</param>
    /// <param name="assetId">A shipped asset id.</param>
    /// <param name="spec">The spec the pipeline ran against.</param>
    /// <param name="thresholds">The threshold set. Defaults to every Part F value stated.</param>
    /// <param name="fileName">The delivered name. Defaults to the row's id plus <c>.png</c>.</param>
    /// <param name="atlasPack">The atlas pack result, or null when no pack has run.</param>
    /// <param name="registry">The accepted silhouettes. Defaults to none.</param>
    internal static QaSubject For(
        SKBitmap image,
        string assetId,
        AssetSpec spec,
        ThresholdSet? thresholds = null,
        string? fileName = null,
        AtlasPackResult? atlasPack = null,
        SilhouetteRegistry? registry = null) =>
        new(
            image,
            spec,
            ManifestRows.Require(assetId),
            fileName ?? assetId + AssetNaming.PngExtension,
            atlasPack,
            thresholds ?? QaThresholds.All(),
            registry ?? SilhouetteRegistry.Empty);

    /// <summary>A single-page pack holding one placement for each stated asset.</summary>
    /// <param name="atlasId">The atlas the pack is for.</param>
    /// <param name="assetIds">The assets placed on it.</param>
    internal static AtlasPackResult PackHolding(string atlasId, params string[] assetIds)
    {
        var placements = assetIds
            .Select((id, index) => new AtlasPlacement(id, 0, index * 128, 0, 128, 128))
            .ToArray();

        return new AtlasPackResult(
            atlasId,
            [new AtlasPage(0, Doc15Authorised.MaxSingleTextureWidth, 128, placements)],
            placements,
            [],
            [],
            []);
    }
}
