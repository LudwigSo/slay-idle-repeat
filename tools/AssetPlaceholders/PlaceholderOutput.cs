using System.Globalization;
using System.Text.Json;
using SlayIdleRepeat.AssetManifest;
using SlayIdleRepeat.AssetPipeline;
using SlayIdleRepeat.AssetProvenance;

namespace SlayIdleRepeat.AssetPlaceholders;

/// <summary>
/// Everything this generator writes to disk, and the guard that keeps all of it out of the two
/// directories it must never touch.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Output goes under <c>artifacts/</c>, which is gitignored. Never <c>assets/</c>.</b>
/// M8-01a's delivery scan walks <c>assets/**</c> on the <em>filesystem</em>, not the git index, and
/// demands a provenance record for every <c>.png</c>, <c>.ogg</c> and <c>.wav</c> it finds. A
/// placeholder run under <c>assets/</c> would therefore redden
/// <c>build/ci/Invoke-ProvenanceGate.ps1</c> for every developer on this repository the moment it
/// ran, uncommitted, on their machine — and it would flip
/// <c>DeliveryDeclaration.AwaitingFirstDelivery</c> from a true statement into a false one.
/// </para>
/// <para>
/// 🔒 <b>And never <c>game-data/</c> (ruling A8, M8 2026-08-12).</b> <c>LocalFileContentSource</c>
/// enumerates every <c>*.json</c> there into the <c>ContentSnapshot</c> whose hash
/// <c>ContentHashing.Compute</c> turns into the content version stamp `14` §6 makes load-bearing for
/// replay and <c>CONTENT_VERSION_MISMATCH</c>. One provenance record written there would move it.
/// </para>
/// <para>
/// 🔒 <b>The provenance records go beside the images, not into the store.</b> A placeholder is not a
/// delivery, so its record must not enter <c>assets/provenance/records/</c> and must not count
/// toward the gate's coverage. It is still a real
/// <see cref="ProceduralProvenance"/>, written by M8-01a's own writer and validated by M8-01a's own
/// validator — the first thing in this repository to exercise the <c>procedural</c> kind end to end.
/// </para>
/// </remarks>
public static class PlaceholderOutput
{
    /// <summary>The one repository directory this generator may write into.</summary>
    public const string ArtifactsDirectory = "artifacts";

    /// <summary>The delivery root M8-01a's provenance gate scans. Forbidden here.</summary>
    public const string ForbiddenDeliveryDirectory = "assets";

    /// <summary>The content root ruling A8 forbids. Forbidden here.</summary>
    public const string ForbiddenContentDirectory = "game-data";

    /// <summary>Where the provenance records go, below the output directory.</summary>
    public const string ProvenanceDirectoryName = "provenance";

    /// <summary>Where the atlas metadata goes, below the output directory.</summary>
    public const string AtlasDirectoryName = "atlas";

    /// <summary>
    /// Refuses any path that is not under <c>artifacts/</c>, or that is under <c>assets/</c> or
    /// <c>game-data/</c>.
    /// </summary>
    /// <remarks>
    /// 🔒 Checked on the <b>path segments</b> of the resolved absolute path, so
    /// <c>artifacts/../assets</c> is refused rather than accepted on the strength of the word
    /// "artifacts" appearing in it. Comparison is ordinal: <c>.gitignore</c> ignores
    /// <c>artifacts/</c> lower-case, and on a case-sensitive filesystem <c>Artifacts/</c> is a
    /// different, tracked directory.
    /// </remarks>
    /// <param name="path">The directory a run would write into.</param>
    /// <returns>The resolved absolute path.</returns>
    public static string RequireArtifactsPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var full = Path.GetFullPath(path);
        var segments = full.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var forbidden in new[] { ForbiddenDeliveryDirectory, ForbiddenContentDirectory })
        {
            if (segments.Contains(forbidden, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"'{full}' is under '{forbidden}/'. A placeholder run writes nothing there: " +
                    $"'{ForbiddenDeliveryDirectory}/' is the delivery root M8-01a's provenance gate " +
                    "scans on the filesystem, so an image dropped in it fails the gate for everyone, " +
                    $"and '{ForbiddenContentDirectory}/' is enumerated into the ContentSnapshot whose " +
                    "hash `14` §6 makes load-bearing for replay (ruling A8). Output belongs under " +
                    $"'{ArtifactsDirectory}/', which is gitignored.");
            }
        }

        return segments.Contains(ArtifactsDirectory, StringComparer.Ordinal)
            ? full
            : throw new InvalidOperationException(
                $"'{full}' is not under '{ArtifactsDirectory}/'. Every image, provenance record and " +
                "atlas file this generator writes is scaffolding a real asset overwrites, and none " +
                $"of it is ever committed — '{ArtifactsDirectory}/' is the gitignored directory that " +
                "guarantees it. The check is on the path's segments, so a relative path that climbs " +
                "back out is refused too.");
    }

    /// <summary>
    /// Removes a run's output directory and everything in it, so a run's output is exactly what its
    /// report describes.
    /// </summary>
    /// <remarks>
    /// 🔒 <see cref="RequireArtifactsPath"/> runs first, so a mistyped root deletes nothing: the
    /// only directories this can remove are ones under <c>artifacts/</c>, which is gitignored.
    /// </remarks>
    /// <param name="outputDirectory">The run's output directory.</param>
    public static void Clear(string outputDirectory)
    {
        var root = RequireArtifactsPath(outputDirectory);

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Writes one delivered PNG-32.</summary>
    /// <param name="outputDirectory">The run's output directory.</param>
    /// <param name="fileName">The `15` §D1 file name.</param>
    /// <param name="encoded">`15` §B4 step 6's bytes.</param>
    public static void WriteImage(string outputDirectory, string fileName, byte[] encoded)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(encoded);

        var root = RequireArtifactsPath(outputDirectory);
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, fileName), encoded);
    }

    /// <summary>
    /// Writes one <c>procedural</c> provenance record, through M8-01a's writer and validator.
    /// </summary>
    /// <param name="outputDirectory">The run's output directory.</param>
    /// <param name="asset">The register row.</param>
    /// <param name="spec">Its manifest-derived spec.</param>
    /// <param name="canvas">The `15` §C generation canvas it was drawn on.</param>
    /// <param name="repoCommit">This repository's full 40-hex commit.</param>
    public static void WriteProvenance(
        string outputDirectory, ArtAsset asset, AssetSpec spec, PixelSize canvas, string repoCommit)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(canvas);

        var record = RecordFor(asset, spec, canvas, repoCommit);
        var problems = RecordValidator.Validate(record, AssetMedium.Art);

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"The provenance record this generator wrote for '{asset.Id}' is malformed: " +
                $"{string.Join("; ", problems)}. M8-01a's validator is the only judge of that, and a " +
                "record written past it would be evidence of nothing.");
        }

        var directory = Path.Combine(RequireArtifactsPath(outputDirectory), ProvenanceDirectoryName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, asset.Id + ProvenanceStore.RecordExtension),
            ProvenanceStore.WriteRecord(record));
    }

    /// <summary>
    /// The <c>procedural</c> record for one placeholder: the generator, the commit, and every
    /// parameter that fixed the pixels.
    /// </summary>
    /// <remarks>
    /// 🔒 <see cref="ProceduralProvenance.RepoCommit"/> stands where `15` §B0 puts the job id and
    /// the seed. There is no job and no seed here — the reproducibility anchor for code-drawn output
    /// is the revision of the code, and a seed field would be a hole waiting for a plausible value.
    /// The parameters are the ones a reader would need to reproduce the file byte for byte.
    /// </remarks>
    public static ProceduralProvenance RecordFor(
        ArtAsset asset, AssetSpec spec, PixelSize canvas, string repoCommit)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(canvas);

        return new ProceduralProvenance
        {
            AssetId = asset.Id,
            // 🔒 Null, and required to be. An art record's tool is named by its kind; M8-01a's
            // validator refuses tooling on an art record outright.
            Tooling = null,
            Generator = typeof(PlaceholderBatch).Assembly.GetName().Name
                        ?? nameof(SlayIdleRepeat.AssetPlaceholders),
            RepoCommit = repoCommit,
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["generationCanvas"] = Number(canvas.Width) + "x" + Number(canvas.Height),
                ["deliverySize"] =
                    Number(spec.TargetSize.Width) + "x" + Number(spec.TargetSize.Height),
                ["pivot"] = spec.Pivot,
                ["atlas"] = asset.Atlas ?? "(none — 15 §D2 assigns this row no atlas)",
                ["biome"] = asset.Biome ?? "(none — this row names no 15 §A5 biome)",
                ["marginDivisor"] = Number(PlaceholderRenderer.MarginDivisor),
                ["outlineWidthPx"] = Number(PlaceholderRenderer.OutlineWidth(canvas)),
                ["innerEdgeBlend"] = Number(PlaceholderRenderer.InnerEdgeBlend),
                [ThresholdKeys.BackgroundKeyTolerance] =
                    Number(PlaceholderThresholds.BackgroundKeyTolerance),
                [ThresholdKeys.MatteDecontaminationStrength] =
                    Number(PlaceholderThresholds.MatteDecontaminationStrength),
                [ThresholdKeys.OutlineColourTolerance] =
                    Number(PlaceholderThresholds.OutlineColourTolerance),
                [ThresholdKeys.OutlineGapClosureRadius] =
                    Number(PlaceholderThresholds.OutlineGapClosureRadius),
                [ThresholdKeys.PaletteMatchTolerance] =
                    Number(PlaceholderThresholds.PaletteMatchTolerance),
                [ThresholdKeys.PaletteNeutrals] = PlaceholderThresholds.PaletteNeutrals.Count == 0
                    ? "(empty — this generator writes no neutral; 15 §A5 enumerates none)"
                    : string.Join(" ", PlaceholderThresholds.PaletteNeutrals),
                [ThresholdKeys.ResizeSharpenRadius] =
                    Number(PlaceholderThresholds.ResizeSharpenRadius),
                [ThresholdKeys.ExportColourBudget] =
                    Number(PlaceholderThresholds.ExportColourBudget),
                [ThresholdKeys.ExportMaxMeanError] =
                    Number(PlaceholderThresholds.ExportMaxMeanError),
            },
        };
    }

    /// <summary>
    /// Writes one atlas's `15` §B4 step 7 result: its pages, every placement, and the `15` §C pivot
    /// §C says is <em>"declared in the atlas metadata"</em>.
    /// </summary>
    /// <param name="outputDirectory">The run's output directory.</param>
    /// <param name="pack">The pack result.</param>
    /// <param name="members">
    /// The placed assets by `15` §D1 id, for the file name and pivot each placement belongs to. A
    /// placement whose member is absent is a loud failure: the metadata's whole job is to point at
    /// a real file.
    /// </param>
    public static void WriteAtlasMetadata(
        string outputDirectory,
        AtlasPackResult pack,
        IReadOnlyDictionary<string, AtlasMember> members)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(members);

        var byId = members;
        var directory = Path.Combine(RequireArtifactsPath(outputDirectory), AtlasDirectoryName);
        Directory.CreateDirectory(directory);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("atlas", pack.AtlasId);
            writer.WriteString(
                "_doc",
                "15 §B4 step 7 over M8-10's placeholders. NOT a delivery: every image this points " +
                "at is scaffolding a real asset overwrites. 15 §C: the pivot is \"declared in the " +
                "atlas metadata\", which is this file.");
            writer.WriteNumber("pageCount", pack.Pages.Count);

            writer.WriteStartArray("placements");
            foreach (var placement in pack.Placements)
            {
                var member = byId.TryGetValue(placement.AssetId, out var found)
                    ? found
                    : throw new InvalidOperationException(
                        $"`15` §B4 step 7 placed '{placement.AssetId}' in {pack.AtlasId} and no " +
                        "member of this pack carries that id. Atlas metadata that names a file " +
                        "nobody delivered is worse than none.");

                writer.WriteStartObject();
                writer.WriteString("assetId", placement.AssetId);
                writer.WriteString("file", member.FileName);
                writer.WriteString("pivot", member.Pivot);
                writer.WriteNumber("page", placement.PageIndex);
                writer.WriteNumber("x", placement.X);
                writer.WriteNumber("y", placement.Y);
                writer.WriteNumber("width", placement.Width);
                writer.WriteNumber("height", placement.Height);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("exclusions");
            foreach (var exclusion in pack.Exclusions)
            {
                writer.WriteStartObject();
                writer.WriteString("assetId", exclusion.AssetId);
                writer.WriteString("reason", exclusion.Reason);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("contradictions");
            foreach (var contradiction in pack.Contradictions)
            {
                writer.WriteStartObject();
                writer.WriteString("id", contradiction.Id);
                writer.WriteString("first", contradiction.FirstReference);
                writer.WriteString("second", contradiction.SecondReference);
                writer.WriteString("detail", contradiction.Detail);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        File.WriteAllBytes(
            Path.Combine(directory, pack.AtlasId + ".json"), buffer.ToArray());
    }

    /// <summary>
    /// Invariant-culture formatting for every number that reaches a file. 🔒 A provenance record
    /// written on a machine whose culture spells a decimal point as a comma is a record nobody
    /// else's reader parses.
    /// </summary>
    /// <param name="value">The value to format.</param>
    private static string Number(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);
}

/// <summary>What the atlas metadata records about one placed asset beyond its placement.</summary>
/// <param name="FileName">The delivered `15` §D1 file name.</param>
/// <param name="Pivot">
/// The row's `15` §C pivot. §C says it is <em>"declared in the atlas metadata"</em>, and this is
/// that declaration — which is also why `15` Part F item 7 verifies it against the pixels rather
/// than against this file.
/// </param>
public sealed record AtlasMember(string FileName, string Pivot);
