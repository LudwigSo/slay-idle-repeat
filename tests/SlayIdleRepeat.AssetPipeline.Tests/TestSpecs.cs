using SlayIdleRepeat.AssetManifest;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>Specs for the per-step cases, built straight from a shipped manifest row's fields.</summary>
/// <remarks>
/// Deliberately not through <see cref="AssetSpec.Resolve"/>: a step case must fail for the step's
/// reason, so a resolver bug should light up only the resolver's own cases (<c>AssetSpecTests</c>),
/// not every step case at once.
/// </remarks>
internal static class TestSpecs
{
    /// <summary>A spec carrying one shipped row's real size, pivot, atlas, biome and palette.</summary>
    /// <param name="id">A shipped asset id that has both a delivery size and a pivot.</param>
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
    /// <param name="id">A shipped asset id.</param>
    /// <param name="width">The case's stated target width.</param>
    /// <param name="height">The case's stated target height.</param>
    internal static AssetSpec WithTargetSize(string id, int width, int height) =>
        FromRow(id) with { TargetSize = new PixelSize(width, height) };

    /// <summary>The same, with a target size and a pivot the case states.</summary>
    /// <param name="id">A shipped asset id.</param>
    /// <param name="width">The case's stated target width.</param>
    /// <param name="height">The case's stated target height.</param>
    /// <param name="pivot">One of <see cref="Doc15Pivots.All"/>.</param>
    internal static AssetSpec WithTargetSizeAndPivot(
        string id, int width, int height, string pivot) =>
        FromRow(id) with { TargetSize = new PixelSize(width, height), Pivot = pivot };
}

/// <summary>Threshold values this suite states for the synthetic fixtures it drives.</summary>
/// <remarks>
/// <para>
/// None of these is a calibration or a default; the shipped thresholds file ships every key null.
/// Each value below is arbitrary-but-sufficient for the closed-form synthetic fixture the case
/// using it drives, and would mean nothing against real generated art.
/// </para>
/// <para>
/// If a step under test starts to need a value these do not cover, add it here with a comment
/// saying what makes it sufficient for the fixture — never by widening one silently.
/// </para>
/// <para>
/// Do not merge this with <see cref="QaThresholds"/>, and in particular do not harmonise
/// <see cref="ThresholdKeys.PaletteMatchTolerance"/>: the two sets hold deliberately opposite
/// values for it (64 here, 8 there) because the step fixture and the item 5 fixture make opposite
/// demands of the same deviation. One shared value would silently make one of the two cases vacuous.
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

            // Smallest neutrals list that lets a case run at all — not a proposal.
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

    /// <summary>The same set with the export compression keys left uncalibrated, for the composed run over a full generation canvas.</summary>
    /// <remarks>
    /// Not a weakening: the uncalibrated export path is a shipped, tested path, and this case is
    /// about which step changes the image's size, which export does not. What it buys is time — the
    /// managed median cut's cost is superlinear in colour count, and this frame is sixteen times the
    /// pixels every other case here drives.
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
