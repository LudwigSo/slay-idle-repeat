using SkiaSharp;
using SlayIdleRepeat.AssetManifest;

namespace SlayIdleRepeat.AssetPipeline;

/// <summary>How a run behaves. Everything here is off by default.</summary>
public sealed record PipelineOptions
{
    /// <summary>
    /// Keeps the bitmap after each step, in step order, on <see cref="PipelineRun.Intermediates"/>.
    /// </summary>
    /// <remarks>
    /// Opt-in because M8-10 drives roughly 942 assets through this and holding six extra bitmaps
    /// per asset is not free. On for a single asset under diagnosis.
    /// </remarks>
    public bool CaptureIntermediates { get; init; }

    /// <summary>Nothing captured; steps 1-6 in `15` §B4 order.</summary>
    public static PipelineOptions Default { get; } = new();
}

/// <summary>The record of one asset's trip through `15` §B4 steps 1-6.</summary>
/// <param name="AssetId">The asset's `15` §D1 id.</param>
/// <param name="Outcome">
/// <see cref="StepOutcome.Applied"/>, or <see cref="StepOutcome.SkippedCutByRuling"/> when a ruling
/// removed the asset before any step ran.
/// </param>
/// <param name="Reason">Why, when skipped. Empty otherwise.</param>
/// <param name="Output">The final image, or null when the whole asset was skipped.</param>
/// <param name="Steps">Every step's result, in `15` §B4 order. Empty for a cut asset.</param>
/// <param name="Intermediates">
/// The bitmap after each step, in step order, when
/// <see cref="PipelineOptions.CaptureIntermediates"/> is set. Empty otherwise.
/// </param>
public sealed record PipelineRun(
    string AssetId,
    StepOutcome Outcome,
    string Reason,
    SKBitmap? Output,
    IReadOnlyList<AssetStepResult> Steps,
    IReadOnlyList<SKBitmap> Intermediates);

/// <summary>
/// Composes `15` §B4 steps 1-6, in order, over one asset.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 The orchestrator is a convenience, not the seam. Every step is independently constructible
/// and independently runnable — see <see cref="IAssetStep"/> — so a failure in step 4 is
/// diagnosable without re-running 1-3.
/// </para>
/// <para>
/// 🔒 <b>Step 7 is not here.</b> Atlas packing is many-to-one, runs once per atlas rather than once
/// per asset, and takes step 6's output for every member; see <see cref="AtlasPackStep"/>.
/// </para>
/// <para>
/// 🔒 <b>Cut rows are skipped explicitly.</b> An asset whose <see cref="ArtAsset.Cut"/> is
/// non-null comes back <see cref="StepOutcome.SkippedCutByRuling"/> with the ruling text in
/// <see cref="PipelineRun.Reason"/>, no steps run and not a pixel touched. All 32 `15` §E19 VFX
/// sheet rows are in that state after ruling O8 (VFX are procedural in-engine), and skipping them
/// by accident — because they happen to have no pivot, say — would be indistinguishable from a bug.
/// </para>
/// </remarks>
public sealed class AssetPipeline
{
    /// <summary>A pipeline with <see cref="PipelineOptions.Default"/>.</summary>
    public AssetPipeline()
        : this(PipelineOptions.Default)
    {
    }

    /// <summary>A pipeline with stated options.</summary>
    /// <param name="options">The options.</param>
    public AssetPipeline(PipelineOptions options) => Options = options;

    /// <summary>The options this pipeline runs with.</summary>
    public PipelineOptions Options { get; }

    /// <summary>Steps 1-6, in `15` §B4 order.</summary>
    public IReadOnlyList<IAssetStep> Steps => throw new NotImplementedException();

    /// <summary>Runs `15` §B4 steps 1-6 over one asset.</summary>
    /// <param name="image">
    /// The generated image, <see cref="SKColorType.Rgba8888"/> / <see cref="SKAlphaType.Unpremul"/>.
    /// </param>
    /// <param name="asset">The manifest row. A cut row is skipped before a spec is built.</param>
    /// <param name="thresholds">The threshold set. An uncalibrated read throws.</param>
    public PipelineRun Run(SKBitmap image, ArtAsset asset, ThresholdSet thresholds) =>
        throw new NotImplementedException();
}
