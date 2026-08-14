using SkiaSharp;
using SlayIdleRepeat.AssetManifest;
using SlayIdleRepeat.AssetPipeline.Qa;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// Threshold values <b>the `15` Part F cases state</b> for the synthetic fixtures they drive.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>None of these is a calibration and none of them is a default.</b> `15` authorises no value
/// for any of the seventeen keys and <c>assets/pipeline/thresholds.json</c> ships every one null
/// (steering rule S6). A caller may state a value at the call site and say why; that is what this
/// is, and every entry below carries the reason it is sufficient for the closed-form fixture it
/// grades. None of them would mean anything against real generated art — M8-10 is the first batch
/// that could calibrate any of them.
/// </para>
/// <para>
/// 🔒 <b>Separate from <see cref="StatedThresholds"/>, on purpose.</b> The `15` §B4 step cases and
/// the Part F cases drive different fixtures and need different values from the same key: the
/// quantise step's fixture wants a <em>wide</em> <see cref="ThresholdKeys.PaletteMatchTolerance"/>
/// so the nearest §A5 hue is unambiguous, and item 5's fixture wants a <em>narrow</em> one so the
/// same 27.7-unit deviation is a violation. Widening one set until it served both would have made
/// one of the two cases vacuous, silently.
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
            // Item 3. The fixtures are painted without antialiasing, so a conforming outline is
            // exactly #231A2E and sits at distance 0; the black-ringed violation sits at 63.4. Any
            // value between separates them, and 8 is the low end of that range.
            [ThresholdKeys.OutlineColourTolerance] = new NumericThreshold(8d),

            // Item 3. The ring is a constant thickness by construction, so no spread has to be
            // forgiven at all; 1 px of slack around `15` §A3's band is stated to keep the passing
            // case honest about being generous, and still rejects the 8 px violation by 5 px.
            [ThresholdKeys.OutlineWidthUniformityTolerance] = new NumericThreshold(1d),

            // Item 5. Black and white only. `15` §A5 writes "+ neutrals" and enumerates nothing, so
            // this is the smallest list that lets a case run at all — it is not a proposal, and the
            // fixture uses neither.
            [ThresholdKeys.PaletteNeutrals] =
                new ColourListThreshold(["#FFFFFF", "#000000"]),

            // Item 5. The fixture's off-palette pixel is a stated 27.7 units from the nearest of the
            // eight permitted colours; 8 puts it outside and leaves every on-palette pixel, which
            // sits at exactly 0, comfortably inside.
            [ThresholdKeys.PaletteMatchTolerance] = new NumericThreshold(8d),

            // Item 6. The clean fixture has no partial alpha at all, so its ratio is 0; the fringed
            // one is roughly a fifth partial. 0.05 separates them without being a claim about how
            // much fringe real art may carry.
            [ThresholdKeys.HaloMaxFringeRatio] = new NumericThreshold(0.05d),

            // Item 6. The fringe is painted pure white against a #E8C48A subject, so its deviation
            // is over a hundred luminance units. 32 is far below that and far above the zero a
            // clean fixture reports.
            [ThresholdKeys.HaloMaxLuminanceDeviation] = new NumericThreshold(32d),

            // Item 1. The chibi fills 28% of its frame and the single-blob violation fills 1.6%.
            [ThresholdKeys.SilhouetteMinCoverageRatio] = new NumericThreshold(0.05d),

            // Item 1. The chibi fills 75% of its own bounding box and the five-blob fixture 24%;
            // 0.15 clears both, because bounding-box fill is not what either violation is about.
            [ThresholdKeys.SilhouetteMinBoundingBoxFill] = new NumericThreshold(0.15d),

            // Item 1. The chibi is one blob; the violation is five.
            [ThresholdKeys.SilhouetteMaxComponentCount] = new NumericThreshold(4d),

            // Item 1. An empty registry reports the maximum, so any value below 1 passes a first
            // asset; 0.01 keeps the case from claiming anything about how different is different
            // enough.
            [ThresholdKeys.SilhouetteMinDistinguishability] = new NumericThreshold(0.01d),

            // Item 8. Evidence only — the item is human and this gates nothing. The clean fixture's
            // corners are fully transparent and the stamped one's bottom-right is fully opaque.
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

/// <summary>
/// The <see cref="QaSubject"/>s the `15` Part F cases judge, each assembled from a shipped manifest
/// row and one synthetic image.
/// </summary>
/// <remarks>
/// 🔒 Every subject names a real row. `15` Part F items 5, 7 and 10 read the biome palette, the
/// delivery size and pivot, and the §D2 atlas straight off the register, so a fabricated row would
/// make those three cases self-referential — they would assert that the check agrees with a value
/// the case invented.
/// </remarks>
internal static class QaSubjects
{
    /// <summary>A subject over a stated image and spec, with everything else at its neutral value.</summary>
    /// <param name="image">The processed image.</param>
    /// <param name="assetId">A shipped `15` §D1 asset id.</param>
    /// <param name="spec">The spec the pipeline ran against.</param>
    /// <param name="thresholds">The threshold set. Defaults to every Part F value stated.</param>
    /// <param name="fileName">The delivered name. Defaults to the row's id plus <c>.png</c>.</param>
    /// <param name="atlasPack">The `15` §B4 step 7 result, or null when no pack has run.</param>
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
    /// <param name="atlasId">The `15` §D2 atlas the pack is for.</param>
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
