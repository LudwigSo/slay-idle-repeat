using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Application.Services.Content.Tunables;

namespace SlayIdleRepeat.ContentValidator;

/// <summary>
/// Assembles the 📐 audit from a checkout: scan the design docs, scan the schemas, read the
/// baseline, run the audit.
/// </summary>
/// <remarks>
/// 🔒 This exists so there is exactly <b>one</b> wiring. When the CI tool and the test that guards
/// it each compose their own, the test proves the algorithm over a composition assembled inside the
/// test and never exercises the one CI runs — the tool's doc glob, its <c>governsTuningFile</c>
/// derivation or its baseline path can all break with the test still green.
/// <para>
/// It takes paths rather than an <c>IContentSourcePort</c> because it is a build-time audit of a
/// repository, not a runtime read of content: it is the one piece of the pipeline that reads
/// <c>game-design/</c>, which does not ship.
/// </para>
/// <para>
/// 🔒 Which is exactly why it lives <b>here</b> and not in <c>SlayIdleRepeat.Application</c>. `23`'s
/// preamble names the filesystem among the dependencies that must sit behind a port, and
/// <c>IContentSourcePort</c> is that seam. This was the only place in <c>Application</c> touching
/// <c>System.IO</c>, and its <c>ScanMarkers</c>/<c>ScanCitations</c> were public and callable from
/// an M1 use case — a use case that would then be enumerating <c>game-design/*.md</c>, on a
/// player's phone, over markdown that never ships. The <em>pure</em> algorithms it composes —
/// <c>TunableMarkerScanner.Scan</c>, <c>SchemaCitationScanner.Scan</c>, <c>TunableMarkerAudit.Run</c>
/// and the <c>TunableModel</c> types, all total functions over <c>string</c> and
/// <c>ContentValue</c> — stay in <c>Application</c>, where the unit tests reach them.
/// </para>
/// </remarks>
public static class TunableAuditComposition
{
    /// <summary>Runs the audit over a checkout.</summary>
    /// <param name="dataRoot">The <c>game-data</c> directory.</param>
    /// <param name="designDocsRoot">The <c>game-design</c> directory.</param>
    /// <param name="baselinePath">The committed 📐 baseline.</param>
    public static TunableAuditReport Run(string dataRoot, string designDocsRoot, string baselinePath) =>
        Run(dataRoot, designDocsRoot, ReadBaseline(baselinePath));

    /// <summary>Runs the audit against a baseline already in hand.</summary>
    public static TunableAuditReport Run(string dataRoot, string designDocsRoot, TunableBaseline baseline)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(designDocsRoot);
        ArgumentNullException.ThrowIfNull(baseline);

        var markers = ScanMarkers(designDocsRoot);
        var tuningFileNames = TuningFileNames(dataRoot);
        var citations = ScanCitations(dataRoot, tuningFileNames);

        return TunableMarkerAudit.Run(markers, citations, baseline, tuningFileNames);
    }

    /// <summary>Every 📐 marker in the documentation set, documents in ordinal order.</summary>
    public static IReadOnlyList<TunableMarker> ScanMarkers(string designDocsRoot) =>
        Directory.GetFiles(designDocsRoot, "*.md")
                 .OrderBy(f => f, StringComparer.Ordinal)
                 .SelectMany(f => TunableMarkerScanner.Scan(Path.GetFileName(f), File.ReadAllText(f)))
                 .ToArray();

    /// <summary>The bare names of the canonical tuning files, e.g. <c>forge.json</c>.</summary>
    public static IReadOnlyCollection<string> TuningFileNames(string dataRoot) =>
        Relative(dataRoot)
            .Where(ContentLayout.IsTuningFile)
            .Select(p => p[ContentLayout.TuningDirectory.Length..])
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Every doc-section citation in the schema set.</summary>
    public static IReadOnlyList<SchemaCitation> ScanCitations(
        string dataRoot, IReadOnlyCollection<string> tuningFileNames)
    {
        var citations = new List<SchemaCitation>();

        foreach (var relative in Relative(dataRoot).Where(ContentLayout.IsSchema).OrderBy(p => p, StringComparer.Ordinal))
        {
            var bytes = File.ReadAllBytes(Path.Combine(dataRoot, relative));
            if (!JsonContentReader.TryRead(relative, bytes, out var schema, out _))
            {
                continue;
            }

            var governed = ContentLayout.DataFileNameGovernedBy(relative);
            citations.AddRange(SchemaCitationScanner.Scan(
                relative, schema!, tuningFileNames.Contains(governed, StringComparer.Ordinal)));
        }

        return citations;
    }

    /// <summary>Reads the committed baseline. A missing one is not the same as a clean run.</summary>
    public static TunableBaseline ReadBaseline(string baselinePath)
    {
        if (!File.Exists(baselinePath))
        {
            throw new FileNotFoundException(
                $"The 📐 baseline '{baselinePath}' is missing. The check fails on any mismatch it " +
                "does not record, so running without one is not the same as running clean.",
                baselinePath);
        }

        if (!JsonContentReader.TryRead(baselinePath, File.ReadAllBytes(baselinePath), out var root, out var issues))
        {
            throw new InvalidOperationException($"The 📐 baseline does not parse: {string.Join("; ", issues)}");
        }

        return TunableBaseline.FromContent(root!);
    }

    private static IEnumerable<string> Relative(string dataRoot) =>
        Directory.EnumerateFiles(dataRoot, "*.json", SearchOption.AllDirectories)
                 .Select(f => Path.GetRelativePath(dataRoot, f).Replace('\\', '/'));
}
