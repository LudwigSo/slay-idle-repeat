using SkiaSharp;
using SlayIdleRepeat.AssetManifest;

namespace SlayIdleRepeat.AssetPipeline;

/// <summary>How a run behaves. Everything here is off by default.</summary>
public sealed record PipelineOptions
{
    /// <summary>
    /// Lists the bitmap after each step, in step order, on <see cref="PipelineRun.Intermediates"/>.
    /// </summary>
    /// <remarks>
    /// 🔒 A convenience view, <b>not</b> a retention switch. Every step's output is already on
    /// <see cref="PipelineRun.Steps"/> as <see cref="AssetStepResult.Image"/> whatever this is set
    /// to, so turning it on costs six references and no bitmaps — see <see cref="PipelineRun"/> for
    /// who owns them. It exists so an asset under diagnosis can be walked step by step without
    /// projecting the results.
    /// </remarks>
    public bool CaptureIntermediates { get; init; }

    /// <summary>Nothing captured; steps 1-6 in `15` §B4 order.</summary>
    public static PipelineOptions Default { get; } = new();
}

/// <summary>The record of one asset's trip through `15` §B4 steps 1-6.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>The caller owns every bitmap on this record.</b> A step never disposes what it was handed
/// and never mutates it — <see cref="Raster.From"/> copies on the way in — so the input bitmap is
/// the caller's throughout, and each step's output is the caller's from the moment it is returned.
/// Nothing here is disposed by the pipeline, because the whole point of keeping
/// <see cref="Steps"/> is that a failure in step 4 is diagnosable against step 3's pixels.
/// </para>
/// <para>
/// 🔒 <b>Dispose distinct instances, not every reference.</b> A step that skips returns the bitmap
/// it was given (`15` §B4 step 3 on a non-biome row does exactly this), and
/// <see cref="Intermediates"/> lists the same objects <see cref="Steps"/> already holds, so one
/// bitmap can appear several times across this record and <see cref="Output"/> is always the last
/// step's. M8-10 drives roughly 942 assets: disposing per run matters, and disposing the same
/// native surface twice is how that goes wrong. <see cref="DistinctOutputs"/> is the set to
/// dispose; iterating <see cref="Steps"/> is the double free.
/// </para>
/// </remarks>
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
/// <see cref="PipelineOptions.CaptureIntermediates"/> is set. Empty otherwise. These are the same
/// instances <see cref="Steps"/> carries, not copies of them.
/// </param>
public sealed record PipelineRun(
    string AssetId,
    StepOutcome Outcome,
    string Reason,
    SKBitmap? Output,
    IReadOnlyList<AssetStepResult> Steps,
    IReadOnlyList<SKBitmap> Intermediates)
{
    /// <summary>
    /// Every bitmap this run produced, <b>once each, by reference identity</b>, in step order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>This is the collection to dispose, and <see cref="Steps"/> is not.</b> A step that
    /// skips returns the instance it was handed — `15` §B4 step 3 on a non-biome row does exactly
    /// that, which is 646 of the 942 uncut rows — and <see cref="Intermediates"/> aliases the same
    /// objects again, so the obvious
    /// <c>foreach (var step in run.Steps) step.Image.Dispose()</c> frees the same native surface
    /// twice. Comparison is reference identity on purpose: <see cref="SKBitmap"/> does not override
    /// equality, but a future value-equality wrapper would silently collapse two distinct surfaces
    /// that happen to hold identical pixels, and disposing one of those would leave the other
    /// dangling.
    /// </para>
    /// <para>
    /// 🔒 <b>The caller's own input can appear here</b>, in the one case where the first step skips
    /// and hands it straight back. Nothing in `15` §B4 steps 1-6 skips at position 1 today, but a
    /// caller that disposes its input separately must still not dispose it twice.
    /// </para>
    /// </remarks>
    public IReadOnlyList<SKBitmap> DistinctOutputs
    {
        get
        {
            // HashSet<object>, because ReferenceEqualityComparer implements IEqualityComparer<object>
            // and there is no generic overload to hand Distinct<SKBitmap>.
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var distinct = new List<SKBitmap>(Steps.Count);

            foreach (var step in Steps)
            {
                if (seen.Add(step.Image))
                {
                    distinct.Add(step.Image);
                }
            }

            return distinct;
        }
    }

    /// <summary>
    /// Every deviation every step declared, concatenated in `15` §B4 step order.
    /// </summary>
    /// <remarks>
    /// 🔒 A run-level aggregate so a batch report does not re-derive it 942 times, and so "this
    /// batch took no deviations" is one empty check rather than a fold nobody wrote. Duplicates are
    /// kept: two steps declaring the same id is a fact about the run, and de-duplicating would hide
    /// which steps were affected.
    /// </remarks>
    public IReadOnlyList<DeclaredDeviation> Deviations =>
        [.. Steps.SelectMany(step => step.Deviations)];

    /// <summary>
    /// Every contradiction every step ran into, concatenated in `15` §B4 step order.
    /// </summary>
    /// <remarks>
    /// 🔒 The same aggregate for <see cref="AssetStepResult.Contradictions"/>, and kept apart from
    /// <see cref="Deviations"/> for the reason stated there: a deviation is owed to the toolchain,
    /// a contradiction is owed to a human ruling on `15`.
    /// </remarks>
    public IReadOnlyList<DocContradiction> Contradictions =>
        [.. Steps.SelectMany(step => step.Contradictions)];
}

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
    public AssetPipeline(PipelineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
    }

    /// <summary>The options this pipeline runs with.</summary>
    public PipelineOptions Options { get; }

    /// <summary>Steps 1-6, in `15` §B4 order.</summary>
    public IReadOnlyList<IAssetStep> Steps { get; } =
    [
        new BackgroundRemovalStep(),
        new TrimToCanvasStep(),
        new PaletteQuantiseStep(),
        new OutlineRepairStep(),
        new ResizeStep(),
        new ExportStep(),
    ];

    /// <summary>Runs `15` §B4 steps 1-6 over one asset.</summary>
    /// <param name="image">
    /// The generated image, <see cref="SKColorType.Rgba8888"/> / <see cref="SKAlphaType.Unpremul"/>.
    /// </param>
    /// <param name="asset">The manifest row. A cut row is skipped before a spec is built.</param>
    /// <param name="thresholds">The threshold set. An uncalibrated read throws.</param>
    public PipelineRun Run(SKBitmap image, ArtAsset asset, ThresholdSet thresholds)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(thresholds);

        // 🔒 Before the spec, and before a pixel is read. All 32 `15` §E19 VFX rows also carry no
        // pivot, so a skip that fell out of AssetSpec.Resolve refusing them would be the right
        // outcome for the wrong reason — and silent the day a live row loses its pivot.
        if (asset.Cut is not null)
        {
            return new PipelineRun(
                asset.Id,
                StepOutcome.SkippedCutByRuling,
                $"A ruling cut '{asset.Id}' ({asset.Section}): {asset.Cut}. No `15` §B4 step ran " +
                "and not a pixel was touched.",
                Output: null,
                Steps: [],
                Intermediates: []);
        }

        var spec = AssetSpec.Resolve(asset);
        var results = new List<AssetStepResult>(Steps.Count);
        var intermediates = new List<SKBitmap>();
        var current = image;

        foreach (var step in Steps)
        {
            var result = step.Run(new AssetStepInput(current, spec, thresholds));
            results.Add(result);
            current = result.Image;

            if (Options.CaptureIntermediates)
            {
                intermediates.Add(current);
            }
        }

        return new PipelineRun(asset.Id, StepOutcome.Applied, string.Empty, current, results, intermediates);
    }
}
