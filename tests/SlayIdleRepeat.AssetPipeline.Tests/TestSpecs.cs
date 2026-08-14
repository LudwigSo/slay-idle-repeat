using SlayIdleRepeat.AssetManifest;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// Specs for the per-step cases, built straight from a shipped manifest row's fields.
/// </summary>
/// <remarks>
/// 🔒 Deliberately NOT through <see cref="AssetSpec.Resolve"/>. A step case must fail for the
/// step's reason; routing it through the resolver would make every one of them also a resolver
/// case, and a resolver bug would light up thirty red tests instead of the three that own it.
/// <c>AssetSpecTests</c> is where <see cref="AssetSpec.Resolve"/> itself is pinned.
/// </remarks>
internal static class TestSpecs
{
    /// <summary>A spec carrying one shipped row's real size, pivot, atlas, biome and palette.</summary>
    /// <param name="id">A `15` §D1 asset id that has both a delivery size and a pivot.</param>
    internal static AssetSpec FromRow(string id)
    {
        var row = ManifestRows.Require(id);
        return new AssetSpec(
            row.Id,
            row.Section,
            row.RequireDeliverySize(),
            row.Pivot ?? throw new InvalidOperationException(
                $"'{id}' carries no pivot, so it cannot back a step case that needs one. Pick a " +
                "row that does, or use AssetSpecTests, which is where a missing pivot belongs."),
            row.Atlas,
            row.Biome,
            row.PaletteColours);
    }

    /// <summary>The same, with a target size the case states instead of the manifest's.</summary>
    /// <param name="id">A `15` §D1 asset id.</param>
    /// <param name="width">The case's stated target width.</param>
    /// <param name="height">The case's stated target height.</param>
    internal static AssetSpec WithTargetSize(string id, int width, int height) =>
        FromRow(id) with { TargetSize = new PixelSize(width, height) };

    /// <summary>The same, with a target size and a pivot the case states.</summary>
    /// <param name="id">A `15` §D1 asset id.</param>
    /// <param name="width">The case's stated target width.</param>
    /// <param name="height">The case's stated target height.</param>
    /// <param name="pivot">One of <see cref="Doc15Pivots.All"/>.</param>
    internal static AssetSpec WithTargetSizeAndPivot(
        string id, int width, int height, string pivot) =>
        FromRow(id) with { TargetSize = new PixelSize(width, height), Pivot = pivot };
}

/// <summary>
/// Threshold values <b>this suite states</b> for the synthetic fixtures it drives.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>None of these is a calibration and none of them is a default.</b> `15` authorises no value
/// for any of the seventeen keys, and <c>assets/pipeline/thresholds.json</c> ships every one of
/// them null for exactly that reason (steering rule S6). What a caller may do — and what this is —
/// is state a value at the call site and say why. Each value below is arbitrary-but-sufficient for
/// the closed-form synthetic fixture the case using it drives, and would mean nothing against real
/// generated art. M8-10 is the first batch that could calibrate any of them.
/// </para>
/// <para>
/// If a step under test starts to need a value these do not cover, add it here with a comment
/// saying what makes it sufficient for the fixture — never by widening one silently.
/// </para>
/// <para>
/// 🔒 <b>Do not merge this with <see cref="QaThresholds"/>, and in particular do not harmonise
/// <see cref="ThresholdKeys.PaletteMatchTolerance"/>.</b> The two sets hold deliberately opposite
/// values for it — 64 here, 8 there — because the `15` §B4 step 3 fixture and the Part F item 5
/// fixture make opposite demands of the same 27.7-unit deviation: the step needs it inside the
/// tolerance so the nearest §A5 hue is unambiguous, and item 5 needs it outside so there is a
/// violation to find. One shared value would silently make one of the two cases vacuous.
/// </para>
/// </remarks>
internal static class StatedThresholds
{
    /// <summary>A set every step can run against, over this suite's fixtures.</summary>
    internal static ThresholdSet ForSyntheticFixtures() =>
        ThresholdSet.Uncalibrated()

            // The fixture background is one exact colour, so anything above zero keys it and
            // nothing near the subject or the outline is within 12 of it.
            .With(ThresholdKeys.BackgroundKeyTolerance, 12d)

            // Full strength: the fixture halo is a stated 50% blend toward white with no other
            // contamination in it, so there is nothing for a partial un-mix to protect.
            .With(ThresholdKeys.MatteDecontaminationStrength, 1d)

            // The fixtures are painted with no antialiasing, so outline pixels are exactly
            // #231A2E; 8 leaves room for a step that resamples before it measures.
            .With(ThresholdKeys.OutlineColourTolerance, 8d)

            // The punched gap is at most seven pixels across, so 8 covers it and still refuses to
            // treat a limb-sized opening as a break.
            .With(ThresholdKeys.OutlineGapClosureRadius, 8d)

            // The synthetic outline is a constant three pixels wide by construction, so any
            // positive spread passes; 1 keeps the case honest about being generous.
            .With(ThresholdKeys.OutlineWidthUniformityTolerance, 1d)

            // Black and white only. `15` §A5 says "+ neutrals" and enumerates nothing, so this is
            // the smallest list that lets a case run at all — it is not a proposal.
            .WithColours(ThresholdKeys.PaletteNeutrals, ["#FFFFFF", "#000000"])

            // The palette fixture's off-palette pixel is a stated 27.7 units from its nearest hue
            // and 91 from the next nearest, so 64 separates the two unambiguously.
            .With(ThresholdKeys.PaletteMatchTolerance, 64d)

            // One pixel, on a 64 px fixture. Enough for an unsharp mask to change something.
            .With(ThresholdKeys.ResizeSharpenRadius, 1d)

            // The fixtures hold single-digit colour counts, so 256 never forces a loss.
            .With(ThresholdKeys.ExportColourBudget, 256d)

            // With a colour budget that cannot force a loss, any positive floor is met.
            .With(ThresholdKeys.ExportMaxMeanError, 1d)

            // QA-checklist keys. Phase 1b's cases own these; they are stated here so a step case
            // never fails for a key it does not use.
            .With(ThresholdKeys.HaloMaxFringeRatio, 0.05d)
            .With(ThresholdKeys.HaloMaxLuminanceDeviation, 32d)
            .With(ThresholdKeys.SilhouetteMinCoverageRatio, 0.05d)
            .With(ThresholdKeys.SilhouetteMinBoundingBoxFill, 0.2d)
            .With(ThresholdKeys.SilhouetteMaxComponentCount, 4d)
            .With(ThresholdKeys.SilhouetteMinDistinguishability, 0.01d)
            .With(ThresholdKeys.WatermarkCornerOpacityCeiling, 0.5d);

    /// <summary>
    /// The same set with the two `15` §B4 step 6 keys left uncalibrated, for the composed run over
    /// a full `15` §C generation canvas.
    /// </summary>
    /// <remarks>
    /// 🔒 Not a weakening: step 6's uncalibrated path is a shipped, tested path — it emits
    /// <see cref="ExportStep.PngquantDeviationId"/> saying the compression half did not run — and
    /// the composed case is about which step changes the image's size, which step 6 does not. What
    /// it buys is time: the managed median cut is the one stage whose cost is superlinear in the
    /// colour count, and a 1024x1024 frame is sixteen times the pixels every other case here
    /// drives. <c>ExportStepTests</c> is where the calibrated path is pinned.
    /// </remarks>
    internal static ThresholdSet ForGenerationCanvasFixture() =>
        ThresholdSet.Uncalibrated()
            .With(ThresholdKeys.BackgroundKeyTolerance, 12d)
            .With(ThresholdKeys.MatteDecontaminationStrength, 1d)
            .With(ThresholdKeys.OutlineColourTolerance, 8d)

            // 1, not the 8 above: the fixture is a plain rectangle with no outline to repair at
            // all, so any positive radius closes nothing, and the smallest one keeps a
            // 1024x1024 morphological pass off the clock.
            .With(ThresholdKeys.OutlineGapClosureRadius, 1d)
            .With(ThresholdKeys.OutlineWidthUniformityTolerance, 1d)
            .WithColours(ThresholdKeys.PaletteNeutrals, ["#FFFFFF", "#000000"])
            .With(ThresholdKeys.PaletteMatchTolerance, 64d)
            .With(ThresholdKeys.ResizeSharpenRadius, 1d);
}
