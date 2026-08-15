using SkiaSharp;

namespace SlayIdleRepeat.AssetPipeline;

/// <summary>What a step did.</summary>
public enum StepOutcome
{
    /// <summary>The step ran and changed (or deliberately confirmed) the image.</summary>
    Applied,

    /// <summary>The step does not apply to this asset — e.g. a non-biome row skipping palette work.</summary>
    SkippedNotApplicable,

    /// <summary>A ruling removed the asset. Not a pixel was touched.</summary>
    SkippedCutByRuling,
}

/// <summary>One number a step measured, kept as evidence rather than folded into a pass/fail.</summary>
/// <param name="Key">A stable name, e.g. <c>outlineWidthPx</c>.</param>
/// <param name="Value">The measured value.</param>
/// <param name="Unit">The unit, e.g. <c>px</c>, <c>ratio</c>, <c>count</c>.</param>
/// <param name="DocReference">The section the measurement serves, e.g. <c>15 §A3</c>.</param>
public sealed record StepMeasurement(string Key, double Value, string Unit, string DocReference);

/// <summary>
/// A place where this pipeline knowingly does something other than the stated requirement, because
/// the no-native-binaries rule leaves no managed way to do the stated thing.
/// </summary>
/// <remarks>
/// A deviation is data that travels with the result, not a comment in a source file. Two are known
/// at design time: the resize step's stated resampler (Lanczos), which SkiaSharp does not have, and
/// the export step's stated quantiser (pngquant), which is a native binary.
/// </remarks>
/// <param name="Id">A stable id, e.g. <c>DEV_LANCZOS_UNAVAILABLE</c>.</param>
/// <param name="DocReference">What the doc says, by section.</param>
/// <param name="Requirement">The requirement, in the doc's own words where possible.</param>
/// <param name="Taken">What was done instead.</param>
/// <param name="Why">Why the stated thing was not possible here.</param>
public sealed record DeclaredDeviation(
    string Id, string DocReference, string Requirement, string Taken, string Why);

/// <summary>Two requirements that cannot both be satisfied, surfaced rather than silently resolved.</summary>
/// <remarks>
/// The known one: one atlas per category against a max single texture size — a large category
/// alone can exceed the texture cap, making multi-page arithmetically unavoidable with no
/// authorised paging convention.
/// </remarks>
/// <param name="Id">A stable id, e.g. <c>CON_ATLAS_PAGE_CAP</c>.</param>
/// <param name="FirstReference">The first section, e.g. <c>15 §D2</c>.</param>
/// <param name="SecondReference">The second section, e.g. <c>15 §C</c>.</param>
/// <param name="Detail">What each says and why they collide, with the arithmetic.</param>
public sealed record DocContradiction(
    string Id, string FirstReference, string SecondReference, string Detail);

/// <summary>What every pipeline step carries, whether it is per-asset or batch.</summary>
public interface IPipelineStep
{
    /// <summary>The step's ordinal, 1-7.</summary>
    int Number { get; }

    /// <summary>A stable slug, e.g. <c>background-removal</c>.</summary>
    string Id { get; }

    /// <summary>Where the step's requirement comes from.</summary>
    string DocReference { get; }
}

/// <summary>One asset in, one asset out.</summary>
/// <remarks>
/// <para>
/// Each implementation is independently constructible and runnable: a failure in one step must be
/// diagnosable without re-running earlier ones, which is why no step takes the orchestrator and no
/// step reads another step's state.
/// </para>
/// <para>
/// A step owns nothing it did not create: <see cref="AssetStepInput.Image"/> is read through
/// <see cref="Raster.From"/>, which copies, so it is never mutated and never disposed here;
/// <see cref="AssetStepResult.Image"/> is a fresh bitmap the caller owns, except on a skip, where it
/// is the input instance handed straight back.
/// </para>
/// </remarks>
public interface IAssetStep : IPipelineStep
{
    /// <summary>Runs this step alone.</summary>
    /// <param name="input">The image, its spec, and the thresholds.</param>
    AssetStepResult Run(AssetStepInput input);
}

/// <summary>Many assets in, one atlas out. Many-to-one by nature, so it does not share <see cref="IAssetStep"/>.</summary>
public interface IAtlasStep : IPipelineStep
{
    /// <summary>Packs one atlas.</summary>
    /// <param name="input">The atlas id and its candidate members.</param>
    AtlasPackResult Run(AtlasPackInput input);
}

/// <summary>What a per-asset step is given.</summary>
/// <remarks>
/// <paramref name="Image"/> is always <see cref="SKColorType.Rgba8888"/> /
/// <see cref="SKAlphaType.Unpremul"/>: a premultiplied surface anywhere in the chain destroys
/// colour in low-alpha pixels, which is exactly the halo the background-removal step removes.
/// </remarks>
/// <param name="Image">The image entering this step.</param>
/// <param name="Spec">The asset's manifest-derived spec.</param>
/// <param name="Thresholds">The threshold set. Reading an uncalibrated key throws.</param>
public sealed record AssetStepInput(SKBitmap Image, AssetSpec Spec, ThresholdSet Thresholds);

/// <summary>What a per-asset step returns.</summary>
/// <param name="Number">The ordinal of the step that produced this.</param>
/// <param name="Id">The step's slug.</param>
/// <param name="Outcome">Applied, or which kind of skip.</param>
/// <param name="Image">
/// The step's output — and, when the outcome is a skip, <b>the input bitmap itself</b>, the same
/// instance, not a copy. The caller owns it either way.
/// </param>
/// <param name="Reason">Why, when skipped or when a deviation was taken. Empty otherwise.</param>
/// <param name="Measurements">Numbers the step measured, as evidence.</param>
/// <param name="Deviations">Deviations the step knowingly took.</param>
/// <param name="EncodedPng">
/// The export step's encoded bytes. Null for every other step — only the export step produces a
/// file, and a second result type for one step buys nothing.
/// </param>
public sealed record AssetStepResult(
    int Number,
    string Id,
    StepOutcome Outcome,
    SKBitmap Image,
    string Reason,
    IReadOnlyList<StepMeasurement> Measurements,
    IReadOnlyList<DeclaredDeviation> Deviations,
    byte[]? EncodedPng = null)
{
    /// <summary>Requirements this step found it could not both satisfy, empty for a step that hit none.</summary>
    /// <remarks>
    /// Its own member, not folded into <see cref="Deviations"/>: a <see cref="DeclaredDeviation"/>
    /// says "the doc asked for something this toolchain cannot do"; a <see cref="DocContradiction"/>
    /// says "the doc asks for two things that cannot both be true" and needs a human to rule on it.
    /// </remarks>
    public IReadOnlyList<DocContradiction> Contradictions { get; init; } = [];
}
