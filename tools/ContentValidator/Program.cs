using System.Globalization;
using SlayIdleRepeat.Adapters.Cache.LocalFile;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Application.Services.Content.Tunables;

namespace SlayIdleRepeat.ContentValidator;

/// <summary>
/// The build-time content check `14` §6 requires: schema validation over every JSON file under
/// <c>SlayIdleRepeat.Data</c>, plus the 📐-marker-versus-schema-key audit over the documentation
/// set.
/// </summary>
/// <remarks>
/// Exit code 0 means the content is loadable and the tuning surface has not eroded. Exit code 1
/// means it has, and says exactly where.
/// </remarks>
internal static class Program
{
    private const string BaselineRelativePath = "build/content/tunable-marker-baseline.json";

    private static int Main(string[] args)
    {
        var repositoryRoot = Argument(args, "--repository-root") ?? FindRepositoryRoot();
        var dataRoot = Argument(args, "--data-root") ?? Path.Combine(repositoryRoot, "SlayIdleRepeat.Data");
        var designDocs = Argument(args, "--design-docs") ?? Path.Combine(repositoryRoot, "game-design");
        var baselinePath = Argument(args, "--baseline") ?? Path.Combine(repositoryRoot, BaselineRelativePath);
        var writeBaseline = args.Contains("--write-baseline", StringComparer.Ordinal);

        Console.WriteLine("Content validation (14 §6)");
        Console.WriteLine($"  data root   : {dataRoot}");
        Console.WriteLine($"  design docs : {designDocs}");
        Console.WriteLine($"  baseline    : {baselinePath}");
        Console.WriteLine();

        var source = new LocalFileContentSource(dataRoot);
        var result = ContentLoader.Load(source);

        Console.WriteLine($"Documents   : {source.ListDocuments().Count}");

        foreach (var issue in result.Issues)
        {
            Console.Error.WriteLine($"  ERROR {issue}");
        }

        if (result.Succeeded)
        {
            Console.WriteLine($"Snapshot    : {result.Snapshot!.DocumentPaths.Count} document(s), " +
                              $"stamp {result.Snapshot.Version.Short}…");
        }

        var markers = ScanMarkers(designDocs);
        var citations = ScanCitations(source);

        Console.WriteLine($"📐 markers  : {markers.Count} across " +
                          $"{markers.Select(m => m.Section.DocId).Distinct().Count()} document(s)");
        Console.WriteLine($"Citations   : {citations.Count} " +
                          $"({citations.Count(c => c.GovernsTuningFile)} from tuning schemas)");

        var tuningFiles = source.ListDocuments()
            .Where(p => p.StartsWith("tuning/", StringComparison.Ordinal) &&
                        !p.StartsWith("tuning/experiments/", StringComparison.Ordinal))
            .Select(p => p["tuning/".Length..])
            .ToArray();

        var baseline = writeBaseline ? TunableBaseline.None : ReadBaseline(baselinePath);
        var audit = TunableMarkerAudit.Run(markers, citations, baseline, tuningFiles);

        if (writeBaseline)
        {
            foreach (var section in audit.UnmatchedMarkers)
            {
                var sample = markers.First(m => m.Section == section);
                Console.WriteLine($"  marker {section} (line {sample.Line}): " +
                                  sample.Text[..Math.Min(150, sample.Text.Length)]);
            }

            foreach (var section in audit.UnmarkedCitations)
            {
                var sample = citations.First(c => c.Section == section && c.GovernsTuningFile && c.GovernsNumericKey);
                Console.WriteLine($"  citation {section}: {sample.SchemaPath}{sample.PropertyPointer}");
            }

            BaselineWriter.Write(baselinePath, audit);
            Console.WriteLine($"Baseline    : rewritten with {audit.UnmatchedMarkers.Count} unmatched marker(s) " +
                              $"and {audit.UnmarkedCitations.Count} unmarked citation(s).");
            return 0;
        }

        Console.WriteLine($"Baseline    : {baseline.Count} accepted mismatch(es), recorded {baseline.RecordedOn}");
        Console.WriteLine($"Unmatched   : {audit.UnmatchedMarkers.Count} marker section(s), " +
                          $"{audit.UnmarkedCitations.Count} citation section(s)");

        foreach (var issue in audit.Issues)
        {
            Console.Error.WriteLine($"  ERROR {issue}");
        }

        var failures = result.Issues.Count + audit.Issues.Count;
        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? "Content validation passed."
            : $"Content validation FAILED with {failures} issue(s).");

        return failures == 0 ? 0 : 1;
    }

    private static IReadOnlyList<TunableMarker> ScanMarkers(string designDocs) =>
        Directory.GetFiles(designDocs, "*.md")
                 .OrderBy(f => f, StringComparer.Ordinal)
                 .SelectMany(f => TunableMarkerScanner.Scan(Path.GetFileName(f), File.ReadAllText(f)))
                 .ToArray();

    private static IReadOnlyList<SchemaCitation> ScanCitations(LocalFileContentSource source)
    {
        var paths = source.ListDocuments();
        var citations = new List<SchemaCitation>();

        foreach (var path in paths.Where(p => p.StartsWith("schema/", StringComparison.Ordinal)))
        {
            if (!JsonContentReader.TryRead(path, source.ReadDocument(path).Span, out var root, out _))
            {
                continue;
            }

            var stem = Path.GetFileName(path).Replace(".schema.json", string.Empty, StringComparison.Ordinal);
            var governsTuning = paths.Contains($"tuning/{stem}.json", StringComparer.Ordinal);
            citations.AddRange(SchemaCitationScanner.Scan(path, root!, governsTuning));
        }

        return citations;
    }

    private static TunableBaseline ReadBaseline(string path)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine(
                $"  ERROR the 📐 baseline '{path}' is missing. The check fails on any mismatch it " +
                "does not record, so running without one is not the same as running clean.");
            return TunableBaseline.None;
        }

        return JsonContentReader.TryRead(path, File.ReadAllBytes(path), out var root, out var issues)
            ? TunableBaseline.FromContent(root!)
            : throw new InvalidOperationException(
                $"The 📐 baseline does not parse: {string.Join("; ", issues)}");
    }

    private static string? Argument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SlayIdleRepeat.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate SlayIdleRepeat.sln above the tool's output.");
    }

    /// <summary>
    /// Rewrites the baseline from the current mismatch set. Run by hand, never by CI: the whole
    /// point of the baseline is that it is a committed, reviewed record.
    /// </summary>
    private static class BaselineWriter
    {
        internal static void Write(string path, TunableAuditReport audit)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var lines = new List<string>
            {
                "{",
                "  \"$comment\": [",
                "    \"14 §6's build-time 📐 check fails on any mismatch NOT recorded here, and equally on\",",
                "    \"an entry here that no longer describes a real mismatch. This file is therefore a\",",
                "    \"measurement of spec debt, not a way to hide it: it can only shrink without a review.\",",
                "    \"Regenerate with: dotnet run --project tools/ContentValidator -- --write-baseline\",",
                "    \"and then WRITE THE REASONS BY HAND. A generated reason is not a reason.\"",
                "  ],",
                "  \"recordedOn\": \"" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "\",",
                "  \"unmatchedMarkers\": [",
            };

            lines.AddRange(Entries(audit.UnmatchedMarkers));
            lines.Add("  ],");
            lines.Add("  \"unmarkedSchemaCitations\": [");
            lines.AddRange(Entries(audit.UnmarkedCitations));
            lines.Add("  ]");
            lines.Add("}");

            File.WriteAllLines(path, lines);
        }

        private static IEnumerable<string> Entries(IReadOnlyList<DocSection> sections) =>
            sections.Select((s, i) =>
                $"    {{ \"doc\": \"{s.DocId}\", \"section\": \"{s.Section}\", " +
                $"\"reason\": \"TODO\", \"closedBy\": \"TODO\" }}{(i == sections.Count - 1 ? string.Empty : ",")}");
    }
}
