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
    /// A view, not a retention switch: every step's output is already reachable via
    /// <see cref="PipelineRun.Steps"/>, so enabling this costs references only, no extra bitmaps.
    /// </remarks>
    public bool CaptureIntermediates { get; init; }

    /// <summary>Nothing captured.</summary>
    public static PipelineOptions Default { get; } = new();
}

/// <summary>The record of one asset's trip through the pipeline steps.</summary>
/// <remarks>
/// <para>
/// The caller owns every bitmap on this record — no step disposes or mutates what it's handed —
/// so nothing here is disposed by the pipeline.
/// </para>
/// <para>
/// Dispose distinct instances, not every reference: a step that skips returns the bitmap it was
/// given, and <see cref="Intermediates"/> aliases the same objects <see cref="Steps"/> holds, so one
/// bitmap can appear several times across this record. <see cref="DistinctOutputs"/> is the set to
/// dispose; iterating <see cref="Steps"/> double-frees.
/// </para>
/// </remarks>
/// <param name="AssetId">The asset's id.</param>
/// <param name="Outcome">
/// <see cref="StepOutcome.Applied"/>, or <see cref="StepOutcome.SkippedCutByRuling"/> when a ruling
/// removed the asset before any step ran.
/// </param>
/// <param name="Reason">Why, when skipped. Empty otherwise.</param>
/// <param name="Output">The final image, or null when the whole asset was skipped.</param>
/// <param name="Steps">Every step's result, in order. Empty for a cut asset.</param>
/// <param name="Intermediates">
/// The bitmap after each step, when <see cref="PipelineOptions.CaptureIntermediates"/> is set.
/// Empty otherwise. Same instances as <see cref="Steps"/> carries, not copies.
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
    /// This is the collection to dispose, not <see cref="Steps"/> — a skipping step returns the
    /// instance it was handed, and <see cref="Intermediates"/> aliases the same objects again, so
    /// disposing every <see cref="Steps"/> entry double-frees. Comparison is reference identity on
    /// purpose: <see cref="SKBitmap"/> does not override equality, and a future value-equality
    /// wrapper could collapse two distinct surfaces that hold identical pixels.
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

    /// <summary>Every deviation every step declared, in step order.</summary>
    /// <remarks>Duplicates are kept: two steps declaring the same id is a fact about the run.</remarks>
    public IReadOnlyList<DeclaredDeviation> Deviations =>
        [.. Steps.SelectMany(step => step.Deviations)];

    /// <summary>Every contradiction every step ran into, in step order.</summary>
    public IReadOnlyList<DocContradiction> Contradictions =>
        [.. Steps.SelectMany(step => step.Contradictions)];
}

/// <summary>Composes the asset-processing steps, in order, over one asset.</summary>
/// <remarks>
/// <para>
/// Every step is independently constructible and runnable — see <see cref="IAssetStep"/> — so a
/// failure mid-run is diagnosable without re-running earlier steps.
/// </para>
/// <para>
/// Atlas packing is not here: it's many-to-one, runs once per atlas rather than once per asset,
/// and takes the last step's output for every member; see <see cref="AtlasPackStep"/>.
/// </para>
/// <para>
/// A cut asset (<see cref="ArtAsset.Cut"/> non-null) comes back
/// <see cref="StepOutcome.SkippedCutByRuling"/> with the ruling text in
/// <see cref="PipelineRun.Reason"/>, no steps run and not a pixel touched.
/// </para>
/// </remarks>
public sealed class AssetPipeline
{
    public AssetPipeline()
        : this(PipelineOptions.Default)
    {
    }

    public AssetPipeline(PipelineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
    }

    public PipelineOptions Options { get; }

    public IReadOnlyList<IAssetStep> Steps { get; } =
    [
        new BackgroundRemovalStep(),
        new TrimToCanvasStep(),
        new PaletteQuantiseStep(),
        new OutlineRepairStep(),
        new ResizeStep(),
        new ExportStep(),
    ];

    /// <summary>Runs every step over one asset.</summary>
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

        // Checked before a spec is built so a cut row is refused as intended, not as a side effect
        // of AssetSpec.Resolve rejecting its missing pivot.
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

        try
        {
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
        }
        catch
        {
            // A mid-run throw leaves no PipelineRun for the caller to dispose through, so the
            // surfaces already produced would otherwise leak. Freed here instead; the caller's own
            // input is excluded since a skipping step could hand it straight back.
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance) { image };
            foreach (var result in results)
            {
                if (seen.Add(result.Image))
                {
                    result.Image.Dispose();
                }
            }

            throw;
        }

        return new PipelineRun(asset.Id, StepOutcome.Applied, string.Empty, current, results, intermediates);
    }
}
