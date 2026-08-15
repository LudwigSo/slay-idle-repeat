using SlayIdleRepeat.AssetPipeline;

namespace SlayIdleRepeat.AssetPlaceholders;

/// <summary>The processing values this generator states so the pipeline's steps can run at all, and the refusal to state any value the QA gate grades against.</summary>
/// <remarks>
/// <para>
/// Two threshold sets, and the split is the whole point: <see cref="ForPipeline"/> states the
/// values a processing step needs to run (reading them uncalibrated throws), while
/// <see cref="ForQualityAssurance"/> hands the QA gate the shipped register untouched. Turning an
/// uncalibrated QA verdict into a pass by typing a number into a threshold here would let a
/// placeholder batch — which is not art and which no human has reviewed — grade itself.
/// </para>
/// </remarks>
public static class PlaceholderThresholds
{
    /// <summary>Background key tolerance. Zero: exact match only.</summary>
    /// <remarks>
    /// This generator writes exact bytes and leaves the border fully transparent, so the background
    /// removal step samples no border colour at all and falls back to "already transparent". A
    /// real Midjourney batch will need a measured non-zero value; this is not it.
    /// </remarks>
    public const double BackgroundKeyTolerance = 0d;

    /// <summary>Matte decontamination strength. One: the maximum.</summary>
    /// <remarks>
    /// The outline band is uniform colour, so both fringe layers reconstruct to the colour they
    /// already carry and the step is a no-op whatever the strength — the maximum at least exercises
    /// the step's arithmetic at its most aggressive.
    /// </remarks>
    public const double MatteDecontaminationStrength = 1d;

    /// <summary>Outline-colour tolerance. 90 channel units, fixed by arithmetic.</summary>
    /// <remarks>
    /// Has to sit above every antialiased outline pixel this generator draws and below every card
    /// fill. Measured across the biome palette, the furthest inner-edge pixel from the outline
    /// colour is around 66 units and the nearest fill is around 120; 90 is the midpoint, clearing
    /// both bands by more than 20 units. A fact about this generator's own palette, not a
    /// calibration of the real art pipeline's antialiasing needs.
    /// </remarks>
    public const double OutlineColourTolerance = 90d;

    /// <summary>Gap-closure radius. Two pixels.</summary>
    /// <remarks>
    /// The outline drawn here is a closed ring with no break, so closure has nothing to bridge and
    /// comes back having written nothing. Two pixels is small against the ring's own width and the
    /// card's interior, which keeps the closing from proposing a repair that swallows the subject.
    /// </remarks>
    public const double OutlineGapClosureRadius = 2d;

    /// <summary>Palette match tolerance. Eight channel units.</summary>
    /// <remarks>
    /// Every fill, cross and stamp pixel on a biome card is a literal palette hue, at distance zero;
    /// the antialiased outline layer is 30-66 units off-palette and is meant to be counted as such.
    /// Eight is small enough to keep that separation unambiguous.
    /// </remarks>
    public const double PaletteMatchTolerance = 8d;

    /// <summary>Export colour budget. 256 — the size of a PNG-8 palette.</summary>
    /// <remarks>Not invented: 256 is what a palette PNG holds and what pngquant's own <c>--colors</c> defaults to.</remarks>
    public const double ExportColourBudget = 256d;

    /// <summary>Export accept/reject floor for the median cut. Eight channel units of mean error.</summary>
    /// <remarks>
    /// A placeholder holds fewer than ten distinct colours before resampling, so a 256-colour median
    /// cut should be near-lossless. Eight is generous enough that resampled edges don't trip it and
    /// tight enough that a genuinely damaging reduction would still be rejected.
    /// </remarks>
    public const double ExportMaxMeanError = 8d;

    /// <summary>Unsharp-mask radius for the resize step. One pixel of sigma.</summary>
    /// <remarks>
    /// The smallest sigma that produces a kernel with real taps on both sides; a wider one would
    /// spread the card's hard edge into a visible ring.
    /// </remarks>
    public const double ResizeSharpenRadius = 1d;

    /// <summary>Neutral list for palette matching. Empty, and that is a statement rather than a hole.</summary>
    /// <remarks>
    /// This generator writes no neutral at all on a biome card — the fill, cross and stamp are
    /// biome hues and the outline is the fixed outline colour — so the stated list is empty because
    /// the batch truly contains none. Not a claim about what the real palette's neutrals are.
    /// </remarks>
    public static IReadOnlyList<string> PaletteNeutrals { get; } = [];

    /// <summary>The threshold set the pipeline's processing steps run against: the shipped register, with the values above stated over it.</summary>
    public static ThresholdSet ForPipeline() => ThresholdSet.Uncalibrated()
        .With(ThresholdKeys.BackgroundKeyTolerance, BackgroundKeyTolerance)
        .With(ThresholdKeys.MatteDecontaminationStrength, MatteDecontaminationStrength)
        .With(ThresholdKeys.OutlineColourTolerance, OutlineColourTolerance)
        .With(ThresholdKeys.OutlineGapClosureRadius, OutlineGapClosureRadius)
        .With(ThresholdKeys.PaletteMatchTolerance, PaletteMatchTolerance)
        .WithColours(ThresholdKeys.PaletteNeutrals, PaletteNeutrals)
        .With(ThresholdKeys.ResizeSharpenRadius, ResizeSharpenRadius)
        .With(ThresholdKeys.ExportColourBudget, ExportColourBudget)
        .With(ThresholdKeys.ExportMaxMeanError, ExportMaxMeanError);

    /// <summary>The threshold set the QA checklist is graded against: the shipped register, untouched.</summary>
    /// <remarks>
    /// Every key is null here, including the ones <see cref="ForPipeline"/> states. "What did this
    /// generator run the steps with" and "what has anybody calibrated the acceptance gate to" are
    /// different questions, and today the answer to the second is nothing.
    /// </remarks>
    /// <param name="registerJson">The contents of <c>assets/pipeline/thresholds.json</c>, read verbatim.</param>
    public static ThresholdSet ForQualityAssurance(string registerJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerJson);
        return ThresholdSet.LoadFrom(registerJson);
    }
}
