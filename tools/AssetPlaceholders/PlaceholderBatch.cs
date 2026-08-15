using SkiaSharp;
using SlayIdleRepeat.AssetManifest;
using SlayIdleRepeat.AssetPipeline;
using SlayIdleRepeat.AssetPipeline.Qa;

namespace SlayIdleRepeat.AssetPlaceholders;

/// <summary>How one run behaves.</summary>
/// <param name="OutputDirectory">Where the images, provenance records and atlas metadata go. Must be under <c>artifacts/</c>; see <see cref="PlaceholderOutput"/>.</param>
/// <param name="RepoCommit">This repository's full 40-hex commit, for the provenance records.</param>
/// <param name="ThresholdRegisterJson">
/// The contents of <c>assets/pipeline/thresholds.json</c>, verbatim.
/// </param>
/// <param name="Include">
/// Which register rows to consider. Null means all of them. A filter narrows what is attempted; it
/// never changes the register totals a report reconciles against.
/// </param>
public sealed record PlaceholderBatchOptions(
    string OutputDirectory,
    string RepoCommit,
    string ThresholdRegisterJson,
    Func<ArtAsset, bool>? Include = null)
{
    /// <summary>The register the QA gate is graded against.</summary>
    /// <remarks>
    /// Built here and nowhere else, so no caller can hand the gate anything but the shipped file —
    /// passing the pipeline's own processing thresholds here would let the generator grade itself.
    /// </remarks>
    public ThresholdSet QaThresholds { get; } =
        PlaceholderThresholds.ForQualityAssurance(ThresholdRegisterJson);
}

/// <summary>
/// Generates one placeholder per runtime art slot in the register and drives every one of them
/// through the pipeline's processing steps and QA checklist.
/// </summary>
/// <remarks>
/// <para>
/// A row a ruling cut, a row with no delivery size, and a row with no pivot each come back as a
/// <see cref="PlaceholderSkip"/> with the reason named. <see cref="ArtAsset.RequireDeliverySize"/>
/// and <see cref="AssetSpec.Resolve"/> throw by design, and this type asks the register the same
/// questions before resolving so the skip is a decision rather than a caught exception.
/// </para>
/// <para>
/// The run is grouped by atlas, and that is a memory decision: every member of an atlas has to
/// exist at the same moment to be packed and graded together, and drawing every delivery image at
/// once would be far more native memory than a batch run needs to hold.
/// </para>
/// <para>
/// Disposal goes through <see cref="PipelineRun.DistinctOutputs"/> rather than per-step. A step
/// that skips returns the instance it was handed, and <see cref="PipelineRun.Intermediates"/>
/// aliases <see cref="PipelineRun.Steps"/>, so one native <see cref="SKBitmap"/> appears several
/// times per run and the obvious <c>foreach (var step in run.Steps) step.Image.Dispose()</c> is a
/// double free.
/// </para>
/// </remarks>
public sealed class PlaceholderBatch
{
    /// <summary>The key a row with no atlas is grouped under.</summary>
    /// <remarks>Deliberately not a valid atlas id: a name with a space in it cannot be mistaken for one written into a pack, a file name or a placement.</remarks>
    private const string NoAtlasGroup = "(no atlas)";

    /// <summary>A full git object name: 40 hex digits.</summary>
    private const int FullCommitLength = 40;

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
        PlaceholderOutput.RequireArtifactsPath(options.OutputDirectory);

        // Checked once, here, rather than once per asset after it has already been drawn and
        // pushed through the whole pipeline — a run that discovered a typo per asset would fail
        // the same way hundreds of times before reporting it.
        if (options.RepoCommit is null
            || options.RepoCommit.Length != FullCommitLength
            || !options.RepoCommit.All(char.IsAsciiHexDigitLower))
        {
            throw new ArgumentException(
                $"'{options.RepoCommit}' is not a full {FullCommitLength}-hex repository commit. It " +
                "is the only reproducibility anchor a procedural provenance record carries — for " +
                "code-drawn art the anchor is the revision of the generator, where `15` §B0 puts " +
                "the job id and the seed.",
                nameof(options));
        }

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
            var groupGenerated = new List<GeneratedPlaceholder>();
            var groupFailed = new List<PlaceholderFailure>();

            try
            {
                RunGroup(
                    group.Key,
                    group,
                    groupGenerated,
                    groupFailed,
                    deviations,
                    contradictions,
                    packs,
                    progress);

                generated.AddRange(groupGenerated);
                failed.AddRange(groupFailed);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Atlas packing and QA both run per group and can throw after the asset loop, so
                // without this a single bad atlas would discard the whole report.
                //
                // The group's own partial results are DISCARDED rather than merged: an asset
                // graded before the throw would otherwise appear in both columns and the report
                // would account for more rows than the register holds.
                foreach (var member in group)
                {
                    failed.Add(new PlaceholderFailure(
                        member.Id, member.Section, exception.GetType().Name, exception.Message));
                }
            }
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
    /// The order matches <see cref="AssetSpec.Resolve"/>'s — cut, then size, then pivot — so a row
    /// missing both a size and a pivot is reported under the same reason the pipeline would refuse
    /// it for. Public so the CLI's <c>plan</c> command asks this rather than re-deriving the same
    /// three-way split.
    /// </remarks>
    /// <param name="asset">The register row.</param>
    public static PlaceholderSkip? SkipFor(ArtAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

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

    /// <summary>
    /// Draws, processes, packs and grades one atlas's worth of rows.
    /// </summary>
    /// <remarks>
    /// The two outcome lists are the group's own, merged into the batch's lists only on success:
    /// everything after the asset loop can throw, and the caller records every member of a failed
    /// group as failed, so appending directly here would double-count an asset.
    /// </remarks>
    /// <param name="groupKey">The atlas reference, or <see cref="NoAtlasGroup"/>.</param>
    /// <param name="members">The rows in this group.</param>
    /// <param name="generated">This group's graded placeholders. Written, never read.</param>
    /// <param name="failed">This group's per-asset failures. Written, never read.</param>
    /// <param name="deviations">The batch's deviation tally.</param>
    /// <param name="contradictions">The batch's contradiction tally.</param>
    /// <param name="packs">The batch's pack results.</param>
    /// <param name="progress">Called once per drawn placeholder.</param>
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
            // Ordered by id so `processed`, and therefore the atlas metadata and the progress
            // output, read the same way every run.
            foreach (var asset in members.OrderBy(asset => asset.Id, StringComparer.Ordinal))
            {
                try
                {
                    processed.Add(Process(asset, deviations, contradictions));
                    progress?.Invoke(asset.Id);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // Recorded and counted, never swallowed, so a run finds more than one bug per run.
                    failed.Add(new PlaceholderFailure(
                        asset.Id, asset.Section, exception.GetType().Name, exception.Message));
                }
            }

            var pack = groupKey == NoAtlasGroup ? null : Pack(groupKey, processed);
            if (pack is not null)
            {
                packs.Add(pack);

                // Atlas packing's own deviations and contradictions are folded into the same two
                // tallies the per-asset steps report through, so the batch report doesn't miss ones
                // that only show up at pack time.
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
                    entry.Stamped,
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

    /// <summary>Draws one placeholder, runs the pipeline's processing steps over it, and writes what came out.</summary>
    private ProcessedPlaceholder Process(
        ArtAsset asset,
        Dictionary<string, int> deviations,
        Dictionary<string, int> contradictions)
    {
        var spec = AssetSpec.Resolve(asset);
        var canvas = GenerationCanvas.For(spec.TargetSize);

        var (rendered, stamped) = PlaceholderRenderer.Draw(spec, canvas);
        using var drawn = rendered;
        var run = pipeline.Run(drawn, asset, pipelineThresholds);
        SKBitmap? kept = null;

        try
        {
            if (run.Output is null)
            {
                throw new InvalidOperationException(
                    $"`15` §B4 produced no output for '{asset.Id}' ({asset.Section}) and reported " +
                    $"{run.Outcome}: {run.Reason} An uncut row with a size and a pivot reaching " +
                    "this point means the pipeline skipped for a reason this generator did not " +
                    "anticipate.");
            }

            if (ReferenceEquals(run.Output, drawn))
            {
                throw new InvalidOperationException(
                    $"`15` §B4 handed this generator's own input straight back for '{asset.Id}': " +
                    "every one of steps 1-6 skipped. The caller owns that instance and is about to " +
                    "dispose it, so the atlas pack and Part F would read a freed surface. Nothing " +
                    "in §B4 skips at position 1 today, and this is what says so out loud if it ever " +
                    "does.");
            }

            foreach (var deviation in run.Deviations)
            {
                deviations[deviation.Id] = deviations.GetValueOrDefault(deviation.Id) + 1;
            }

            foreach (var contradiction in run.Contradictions)
            {
                contradictions[contradiction.Id] =
                    contradictions.GetValueOrDefault(contradiction.Id) + 1;
            }

            var encoded = run.Steps[^1].EncodedPng
                ?? throw new InvalidOperationException(
                    $"`15` §B4 step 6 encoded no PNG for '{asset.Id}'. The export step is the only " +
                    "step that produces a file, and a run whose last step carries no bytes has " +
                    "nothing to deliver.");

            var fileName = asset.Id + AssetNaming.PngExtension;
            PlaceholderOutput.WriteImage(options.OutputDirectory, fileName, encoded);
            PlaceholderOutput.WriteProvenance(
                options.OutputDirectory, asset, spec, canvas, options.RepoCommit);

            kept = run.Output;
            return new ProcessedPlaceholder(
                asset, spec, fileName, encoded.Length, stamped, run.Output);
        }
        finally
        {
            // In a `finally`, because the statements above can throw and RunGroup records the
            // failure and moves to the next asset. Steps hand the same instance on when they skip
            // and Intermediates aliases Steps, so disposing per reference is a double free — this
            // disposes every surface except the kept output and the original input. On the throw
            // path `kept` is still null, so nothing survives.
            foreach (var image in run.DistinctOutputs)
            {
                if (!ReferenceEquals(image, kept) && !ReferenceEquals(image, drawn))
                {
                    image.Dispose();
                }
            }
        }
    }

    /// <summary>Packs one atlas's members.</summary>
    /// <remarks>
    /// The pack is keyed on the reference the rows carry (e.g. <c>atlas_biome_3</c>), not on a
    /// template id (<c>atlas_biome_{n}</c>) — the QA naming check compares
    /// <see cref="AtlasPackResult.AtlasId"/> against <see cref="ArtAsset.Atlas"/> by ordinal
    /// equality, so a pack labelled with the template would fail every biome-scoped row.
    /// </remarks>
    private AtlasPackResult? Pack(string atlasId, IReadOnlyList<ProcessedPlaceholder> members) =>
        members.Count == 0
            ? null
            : packer.Run(new AtlasPackInput(
                atlasId,
                [.. members.Select(entry => new AtlasPackEntry(entry.Asset, entry.Image))],
                pipelineThresholds));

    /// <summary>One placeholder after processing, held until its atlas is packed.</summary>
    /// <param name="Asset">The register row.</param>
    /// <param name="Spec">Its manifest-derived spec.</param>
    /// <param name="FileName">The delivered file name.</param>
    /// <param name="EncodedBytes">How many bytes the export step wrote.</param>
    /// <param name="Stamped">Whether the id stamp fitted on the card.</param>
    /// <param name="Image">The processed output. Owned by <see cref="RunGroup"/>, disposed by it.</param>
    private sealed record ProcessedPlaceholder(
        ArtAsset Asset,
        AssetSpec Spec,
        string FileName,
        int EncodedBytes,
        bool Stamped,
        SKBitmap Image);
}
