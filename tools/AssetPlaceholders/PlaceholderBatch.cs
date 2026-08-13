using SkiaSharp;
using SlayIdleRepeat.AssetManifest;
using SlayIdleRepeat.AssetPipeline;
using SlayIdleRepeat.AssetPipeline.Qa;

namespace SlayIdleRepeat.AssetPlaceholders;

/// <summary>How one run behaves.</summary>
/// <param name="OutputDirectory">
/// 🔒 Where the images, the provenance records and the atlas metadata go. Must be under
/// <c>artifacts/</c>. See <see cref="PlaceholderOutput"/> for why that is a rule and not a habit.
/// </param>
/// <param name="RepoCommit">This repository's full 40-hex commit, for the provenance records.</param>
/// <param name="QaThresholds">
/// The register `15` Part F is graded against — the shipped file, verbatim. See
/// <see cref="PlaceholderThresholds.ForQualityAssurance"/>.
/// </param>
/// <param name="Include">
/// Which register rows to consider. Null means all of them. 🔒 A filter narrows what is
/// <em>attempted</em>; it never changes the register totals a report reconciles against, so a
/// filtered run reports plainly that it did not cover the register.
/// </param>
public sealed record PlaceholderBatchOptions(
    string OutputDirectory,
    string RepoCommit,
    ThresholdSet QaThresholds,
    Func<ArtAsset, bool>? Include = null);

/// <summary>
/// Generates one placeholder per runtime art slot in M8-09's register and drives every one of them
/// through `15` §B4's seven steps and Part F's checklist.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Three refusals are honoured, never worked around.</b> A row a ruling cut, a row `15` §C
/// states no delivery size for, and a row §C authorises no pivot for each come back as a
/// <see cref="PlaceholderSkip"/> with the reason named. <see cref="ArtAsset.RequireDeliverySize"/>
/// and <see cref="AssetSpec.Resolve"/> throw by design (steering rule S6), and this type asks the
/// register the same questions <em>before</em> resolving so that the skip is a decision rather than
/// a caught exception — but it never defaults a hole, and the counts go into the report because
/// they are a gap in `15` that O30 needs at M11-01.
/// </para>
/// <para>
/// 🔒 <b>The run is grouped by atlas, and that is a memory decision.</b> `15` Part F item 10 needs
/// the §B4 step 7 pack the asset was placed by, so every member of an atlas has to exist at the same
/// moment. All 641 delivery images at once is roughly 450 MB of native surfaces; the largest single
/// §D2 atlas — <c>atlas_hero</c>, 64 rows at 512×512 — is roughly 67 MB.
/// </para>
/// <para>
/// 🔒 <b>Disposal goes through <see cref="PipelineRun.DistinctOutputs"/>.</b> A step that skips
/// returns the instance it was handed and <see cref="PipelineRun.Intermediates"/> aliases
/// <see cref="PipelineRun.Steps"/>, so one native <see cref="SKBitmap"/> appears several times per
/// run and the obvious <c>foreach (var step in run.Steps) step.Image.Dispose()</c> is a double free.
/// Over hundreds of assets that is loud in both directions.
/// </para>
/// </remarks>
public sealed class PlaceholderBatch
{
    /// <summary>The key a row `15` §D2 assigns no atlas is grouped under.</summary>
    /// <remarks>
    /// 🔒 Not a valid `15` §D1 atlas id, and deliberately not one: it must never be written into a
    /// pack, a file name or a placement, and a name with a space in it cannot be mistaken for one.
    /// </remarks>
    private const string NoAtlasGroup = "(no atlas)";

    private readonly PlaceholderBatchOptions options;
    private readonly AssetPipeline.AssetPipeline pipeline = new();
    private readonly AtlasPackStep packer = new();
    private readonly QaChecklist checklist = new();
    private readonly ThresholdSet pipelineThresholds = PlaceholderThresholds.ForPipeline();

    /// <summary>Creates a batch.</summary>
    /// <param name="options">How the run behaves.</param>
    public PlaceholderBatch(PlaceholderBatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.QaThresholds);
        PlaceholderOutput.RequireArtifactsPath(options.OutputDirectory);

        this.options = options;
    }

    /// <summary>Runs the whole register.</summary>
    /// <param name="manifest">M8-09's register.</param>
    /// <param name="progress">Called once per generated placeholder, for a CLI to report against.</param>
    public PlaceholderBatchReport Run(AssetManifestSet manifest, Action<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var generated = new List<GeneratedPlaceholder>();
        var skipped = new List<PlaceholderSkip>();
        var failed = new List<PlaceholderFailure>();
        var deviations = new Dictionary<string, int>(StringComparer.Ordinal);
        var contradictions = new Dictionary<string, int>(StringComparer.Ordinal);
        var packs = new List<AtlasPackResult>();

        var considered = manifest.Art.Assets
            .Where(asset => options.Include is null || options.Include(asset))
            .ToArray();

        var generatable = new List<ArtAsset>();
        foreach (var asset in considered)
        {
            var skip = SkipFor(asset);
            if (skip is null)
            {
                generatable.Add(asset);
            }
            else
            {
                skipped.Add(skip);
            }
        }

        foreach (var group in generatable
                     .GroupBy(asset => asset.Atlas ?? NoAtlasGroup, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            RunGroup(group.Key, group, generated, failed, deviations, contradictions, packs, progress);
        }

        return new PlaceholderBatchReport
        {
            ArtRowsInRegister = manifest.Art.Assets.Count,
            AudioRowsInRegister = manifest.Audio.Assets.Count,
            Generated = [.. generated.OrderBy(entry => entry.AssetId, StringComparer.Ordinal)],
            Skipped = [.. skipped.OrderBy(entry => entry.AssetId, StringComparer.Ordinal)],
            Failed = [.. failed.OrderBy(entry => entry.AssetId, StringComparer.Ordinal)],
            Deviations = deviations,
            Contradictions = contradictions,
            Atlases = packs,
        };
    }

    /// <summary>
    /// Why this row gets no placeholder, or null when it gets one.
    /// </summary>
    /// <remarks>
    /// 🔒 The order matches <see cref="AssetSpec.Resolve"/>'s — cut, then size, then pivot — so a
    /// row missing both a size and a pivot is reported under the same reason the pipeline would have
    /// refused it for. Reporting a row under the second of two true reasons would put 95 of them in
    /// the wrong column of the gap O30 reconciles.
    /// </remarks>
    /// <param name="asset">The register row.</param>
    private static PlaceholderSkip? SkipFor(ArtAsset asset)
    {
        if (asset.Cut is not null)
        {
            return new PlaceholderSkip(
                asset.Id,
                asset.Section,
                PlaceholderSkipReason.CutByRuling,
                $"A ruling cut this row: {asset.Cut}. A cut asset is not generated, so it gets no " +
                "placeholder either — a placeholder for an asset nobody will ever draw is a file " +
                "that outlives the ruling that removed it.");
        }

        if (asset.DeliverySize is null)
        {
            return new PlaceholderSkip(
                asset.Id,
                asset.Section,
                PlaceholderSkipReason.NoDeliverySize,
                $"`15` §C states no delivery size for '{asset.Id}' ({asset.Section}), so there is no " +
                "canvas to deliver a placeholder on. The null is the doc authorising no value " +
                "(steering rule S6) and it is not defaulted here; the row is counted and reported " +
                "as a gap in `15` instead. See the register's discrepancy DSC_MISSING_SIZES.");
        }

        return asset.Pivot is null
            ? new PlaceholderSkip(
                asset.Id,
                asset.Section,
                PlaceholderSkipReason.NoPivot,
                $"`15` §C authorises a pivot for characters (bottom-center) and for icons (center) " +
                $"and for nothing else, and '{asset.Id}' ({asset.Section}) carries none. §B4 step 2 " +
                "pads against the pivot, so a placeholder would have to invent a placement the doc " +
                "does not state.")
            : null;
    }

    private void RunGroup(
        string groupKey,
        IEnumerable<ArtAsset> members,
        List<GeneratedPlaceholder> generated,
        List<PlaceholderFailure> failed,
        Dictionary<string, int> deviations,
        Dictionary<string, int> contradictions,
        List<AtlasPackResult> packs,
        Action<string>? progress)
    {
        var processed = new List<ProcessedPlaceholder>();

        try
        {
            foreach (var asset in members.OrderBy(asset => asset.Id, StringComparer.Ordinal))
            {
                try
                {
                    processed.Add(Process(asset, deviations, contradictions));
                    progress?.Invoke(asset.Id);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // 🔒 Recorded and counted, never swallowed. This generator is the pipeline's
                    // first real caller, so a throw here is evidence rather than noise, and a run
                    // that stopped at the first one would find exactly one bug per run.
                    failed.Add(new PlaceholderFailure(
                        asset.Id, asset.Section, exception.GetType().Name, exception.Message));
                }
            }

            var pack = groupKey == NoAtlasGroup ? null : Pack(groupKey, processed);
            if (pack is not null)
            {
                packs.Add(pack);

                // 🔒 Step 7's deviations and contradictions are folded into the same two tallies
                // steps 1-6 report through. CON_ATLAS_PAGE_CAP lives only on the pack result, and a
                // batch report that counted only the per-asset ones would say the run hit no
                // contradiction while §D2 and §C were colliding on every atlas it packed.
                foreach (var deviation in pack.Deviations)
                {
                    deviations[deviation.Id] = deviations.GetValueOrDefault(deviation.Id) + 1;
                }

                foreach (var contradiction in pack.Contradictions)
                {
                    contradictions[contradiction.Id] =
                        contradictions.GetValueOrDefault(contradiction.Id) + 1;
                }

                PlaceholderOutput.WriteAtlasMetadata(
                    options.OutputDirectory,
                    pack,
                    processed.ToDictionary(
                        entry => entry.Asset.Id,
                        entry => new AtlasMember(entry.FileName, entry.Spec.Pivot),
                        StringComparer.Ordinal));
            }

            foreach (var entry in processed)
            {
                generated.Add(new GeneratedPlaceholder(
                    entry.Asset.Id,
                    entry.FileName,
                    entry.Asset.Atlas,
                    entry.EncodedBytes,
                    checklist.Evaluate(new QaSubject(
                        entry.Image,
                        entry.Spec,
                        entry.Asset,
                        entry.FileName,
                        pack,
                        options.QaThresholds,
                        SilhouetteRegistry.Empty))));
            }
        }
        finally
        {
            foreach (var entry in processed)
            {
                entry.Image.Dispose();
            }
        }
    }

    /// <summary>
    /// Draws one placeholder, runs `15` §B4 steps 1-6 over it, and writes what came out.
    /// </summary>
    private ProcessedPlaceholder Process(
        ArtAsset asset,
        Dictionary<string, int> deviations,
        Dictionary<string, int> contradictions)
    {
        var spec = AssetSpec.Resolve(asset);
        var canvas = GenerationCanvas.For(spec.TargetSize);

        using var drawn = PlaceholderRenderer.Draw(spec, canvas);
        var run = pipeline.Run(drawn, asset, pipelineThresholds);

        if (run.Output is null)
        {
            throw new InvalidOperationException(
                $"`15` §B4 produced no output for '{asset.Id}' ({asset.Section}) and reported " +
                $"{run.Outcome}: {run.Reason} An uncut row with a size and a pivot reaching this " +
                "point means the pipeline skipped for a reason this generator did not anticipate.");
        }

        foreach (var deviation in run.Deviations)
        {
            deviations[deviation.Id] = deviations.GetValueOrDefault(deviation.Id) + 1;
        }

        foreach (var contradiction in run.Contradictions)
        {
            contradictions[contradiction.Id] = contradictions.GetValueOrDefault(contradiction.Id) + 1;
        }

        var encoded = run.Steps[^1].EncodedPng
            ?? throw new InvalidOperationException(
                $"`15` §B4 step 6 encoded no PNG for '{asset.Id}'. The export step is the only step " +
                "that produces a file, and a run whose last step carries no bytes has nothing to " +
                "deliver.");

        var fileName = asset.Id + AssetNaming.PngExtension;
        PlaceholderOutput.WriteImage(options.OutputDirectory, fileName, encoded);
        PlaceholderOutput.WriteProvenance(
            options.OutputDirectory, asset, spec, canvas, options.RepoCommit);

        // 🔒 DistinctOutputs, and the final output held back. Steps hand the same instance on when
        // they skip and Intermediates aliases Steps, so disposing per reference is a double free —
        // and the last step's image is the one Part F item 7 and step 7 are about to read.
        foreach (var image in run.DistinctOutputs)
        {
            if (!ReferenceEquals(image, run.Output) && !ReferenceEquals(image, drawn))
            {
                image.Dispose();
            }
        }

        return new ProcessedPlaceholder(asset, spec, fileName, encoded.Length, run.Output);
    }

    /// <summary>Runs `15` §B4 step 7 over one atlas's members.</summary>
    /// <remarks>
    /// 🔒 The pack is keyed on the reference the <em>rows</em> carry (<c>atlas_biome_3</c>), not on
    /// §D2's template id (<c>atlas_biome_{n}</c>). `15` Part F item 10 compares
    /// <see cref="AtlasPackResult.AtlasId"/> against <see cref="ArtAsset.Atlas"/> by ordinal
    /// equality, so a pack labelled with the template would fail all 192 biome-scoped rows.
    /// </remarks>
    private AtlasPackResult? Pack(string atlasId, IReadOnlyList<ProcessedPlaceholder> members) =>
        members.Count == 0
            ? null
            : packer.Run(new AtlasPackInput(
                atlasId,
                [.. members.Select(entry => new AtlasPackEntry(entry.Asset, entry.Image))],
                pipelineThresholds));

    /// <summary>One placeholder after `15` §B4 steps 1-6, held until its atlas is packed.</summary>
    /// <param name="Asset">The register row.</param>
    /// <param name="Spec">Its manifest-derived spec.</param>
    /// <param name="FileName">The delivered `15` §D1 file name.</param>
    /// <param name="EncodedBytes">How many bytes step 6 wrote.</param>
    /// <param name="Image">Step 6's output. Owned by <see cref="RunGroup"/>, disposed by it.</param>
    private sealed record ProcessedPlaceholder(
        ArtAsset Asset, AssetSpec Spec, string FileName, int EncodedBytes, SKBitmap Image);
}
