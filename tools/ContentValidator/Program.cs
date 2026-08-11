using System.Globalization;
using SlayIdleRepeat.Adapters.Content.LocalFile;
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
/// means it has, and says exactly where. Nothing else is an exit code: an unhandled exception that
/// escaped to the runtime would print a stack trace naming no document and hand CI a code nobody
/// specified.
/// </remarks>
internal static class Program
{
    private const string BaselineRelativePath = "build/content/tunable-marker-baseline.json";

    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"  ERROR content validation could not complete: {exception.Message}");
            return 1;
        }
    }

    private static int Run(string[] args)
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

        var baseline = writeBaseline
            ? TunableBaseline.None
            : TunableAuditComposition.ReadBaseline(baselinePath);

        var audit = TunableAuditComposition.Run(dataRoot, designDocs, baseline);

        Console.WriteLine($"📐 markers  : {TunableAuditComposition.ScanMarkers(designDocs).Count} marker(s)");
        Console.WriteLine($"Unmatched   : {audit.UnmatchedMarkers.Count} marker section(s), " +
                          $"{audit.UnmarkedCitations.Count} citation section(s)");

        if (writeBaseline)
        {
            BaselineWriter.Write(baselinePath, audit);
            Console.WriteLine($"Baseline    : rewritten with {audit.UnmatchedMarkers.Count} unmatched marker(s) " +
                              $"and {audit.UnmarkedCitations.Count} unmarked citation(s). Now write the reasons.");
            return 0;
        }

        Console.WriteLine($"Baseline    : {baseline.Count} accepted mismatch(es), recorded {baseline.RecordedOn}");

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
    /// Rewrites the baseline's SHAPE from the current mismatch set. Run by hand, never by CI: the
    /// whole point of the baseline is that it is a committed, reviewed record, and a generated
    /// reason is not a reason.
    /// </summary>
    /// <remarks>
    /// 🔒 It writes <c>kind: "unreviewed"</c> and <b>no owner at all</b>. It used to write the
    /// literal <c>"closedBy": "TODO"</c>, which every check in the repository accepted — the only
    /// assertion in reach was <c>ClosedBy.Length &gt; 0</c>, and <c>"TODO"</c> has a length. The
    /// reader now refuses an unreviewed entry by name, so a regenerated file nobody hand-edited
    /// fails the very next run instead of shipping a baseline of anonymous debt.
    /// </remarks>
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
                "    \"Regenerate the SHAPE with: dotnet run --project tools/ContentValidator -- --write-baseline\",",
                "    \"and then WRITE THE REASONS BY HAND. A generated reason is not a reason.\",",
                "    \"Every entry below is kind 'unreviewed', which the reader REFUSES. Replace each with\",",
                "    \"either kind 'specDebt' + a closedBy naming a task in IMPLEMENTATION_TRACKER.md, or\",",
                "    \"kind 'outOfScope' and NO closedBy, because nothing closes it.\"",
                "  ],",

                // UTC, not local time: a committed artefact must not carry the author's timezone.
                "  \"recordedOn\": \"" + DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "\",",
                "  \"unmatchedMarkers\": [",
            };

            lines.AddRange(Entries(audit.UnmatchedMarkers));
            lines.Add("  ],");
            lines.Add("  \"unmarkedSchemaCitations\": [");
            lines.AddRange(Entries(audit.UnmarkedCitations));
            lines.Add("  ]");
            lines.Add("}");

            // LF, explicitly: WriteAllLines would emit CRLF on Windows and LF on Linux, so the same
            // regeneration would produce a different committed file per machine.
            File.WriteAllText(path, string.Join('\n', lines) + '\n');
        }

        private static IEnumerable<string> Entries(IReadOnlyList<DocSection> sections) =>
            sections.Select((s, i) =>
                $"    {{ \"doc\": \"{s.DocId}\", \"section\": \"{s.Section}\", " +
                $"\"kind\": \"{TunableBaseline.UnreviewedKind}\", " +
                "\"reason\": \"UNREVIEWED — write this by hand.\" " +
                $"}}{(i == sections.Count - 1 ? string.Empty : ",")}");
    }
}
