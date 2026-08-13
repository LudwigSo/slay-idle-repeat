// The placeholder generator's CLI (M8-10). The library half is what matters — the batch, the
// renderer and the report are all public so the suite drives them directly — and this entry point
// exists because generating ~641 images is a thing a human runs once, in the foreground, and reads
// the totals of. It is an Exe for the same reason every other tools/ project is.
//
// 🔒 It writes to artifacts/ and refuses to write anywhere else. See PlaceholderOutput.
using System.Globalization;
using SlayIdleRepeat.AssetManifest;
using SlayIdleRepeat.AssetPipeline;
using SlayIdleRepeat.AssetPipeline.Qa;
using SlayIdleRepeat.AssetPlaceholders;

const string usage = """
    SlayIdleRepeat.AssetPlaceholders — one placeholder per runtime art slot in `15` §E.

      generate <repoRoot> <repoCommit> [--section <E-section>]
                               Draw a placeholder for every uncut art row that `15` §C gives a
                               delivery size AND a pivot, drive it through `15` §B4's seven steps,
                               pack the `15` §D2 atlases and run Part F over the result.
                               Output goes to artifacts/placeholders/ and nowhere else.

      plan <repoRoot>          Report what a run WOULD do — how many rows are generatable, and how
                               many are skipped for each of the three reasons — without drawing
                               anything.

    🔒 Nothing this tool writes is ever committed. artifacts/ is gitignored, and a placeholder under
    assets/ would fail M8-01a's provenance gate for everybody.
    """;

var command = args.Length > 0 ? args[0] : "help";

switch (command)
{
    case "plan":
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("plan needs a repository root.");
            return 1;
        }

        var manifest = AssetManifestReader.Load(Path.Combine(args[1], "game-data"));

        // The partition, without drawing anything — a plan that drew 641 images would not be a plan.
        var partition = manifest.Art.Assets
            .Select(PlanReasonFor)
            .GroupBy(reason => reason, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        Console.WriteLine($"art rows in the register : {manifest.Art.Assets.Count}");
        Console.WriteLine($"audio rows (out of scope): {manifest.Audio.Assets.Count}");
        foreach (var group in partition)
        {
            Console.WriteLine($"{group.Key,-26}: {group.Count()}");
        }

        return 0;
    }

    case "generate":
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("generate needs a repository root and a 40-hex repository commit.");
            return 1;
        }

        var root = args[1];
        var section = SectionFilter(args);
        var manifest = AssetManifestReader.Load(Path.Combine(root, "game-data"));
        var thresholdsPath = Path.Combine(root, ThresholdSet.ThresholdsPath);

        if (!File.Exists(thresholdsPath))
        {
            Console.Error.WriteLine($"No threshold register at '{thresholdsPath}'.");
            return 1;
        }

        var batch = new PlaceholderBatch(new PlaceholderBatchOptions(
            Path.Combine(root, "artifacts", "placeholders"),
            args[2],
            PlaceholderThresholds.ForQualityAssurance(File.ReadAllText(thresholdsPath)),
            Include: section is null
                ? null
                : asset => string.Equals(asset.Section, section, StringComparison.Ordinal)));

        var drawn = 0;
        var started = DateTime.UtcNow;
        var result = batch.Run(manifest, _ =>
        {
            drawn++;
            if (drawn % 25 == 0)
            {
                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  … {drawn} drawn, {(DateTime.UtcNow - started).TotalSeconds:0} s elapsed"));
            }
        });

        Report(result);
        return result.Failed.Count == 0 && result.MechanicalFailures.Count == 0 ? 0 : 1;
    }

    default:
        Console.WriteLine(usage);
        return command is "help" or "--help" or "-h" ? 0 : 1;
}

static string? SectionFilter(string[] args)
{
    var index = Array.IndexOf(args, "--section");
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static string PlanReasonFor(ArtAsset asset) => asset switch
{
    { Cut: not null } => "cut by a ruling",
    { DeliverySize: null } => "no `15` §C delivery size",
    { Pivot: null } => "no `15` §C pivot",
    _ => "generatable",
};

static void Report(PlaceholderBatchReport report)
{
    Console.WriteLine();
    Console.WriteLine("── `15` §E register ────────────────────────────────────");
    Console.WriteLine($"art rows                     : {report.ArtRowsInRegister}");
    Console.WriteLine($"audio rows (out of scope)    : {report.AudioRowsInRegister}");
    Console.WriteLine();
    Console.WriteLine("── this run ────────────────────────────────────────────");
    Console.WriteLine($"generated                    : {report.Generated.Count}");
    Console.WriteLine(
        $"skipped, cut by a ruling     : {report.SkippedFor(PlaceholderSkipReason.CutByRuling)}");
    Console.WriteLine(
        $"skipped, no §C delivery size : {report.SkippedFor(PlaceholderSkipReason.NoDeliverySize)}");
    Console.WriteLine(
        $"skipped, no §C pivot         : {report.SkippedFor(PlaceholderSkipReason.NoPivot)}");
    Console.WriteLine($"failed                       : {report.Failed.Count}");
    Console.WriteLine(
        $"accounted / register         : {report.Accounted} / {report.ArtRowsInRegister} " +
        $"({(report.Reconciles ? "reconciles" : "DOES NOT RECONCILE")})");

    foreach (var failure in report.Failed)
    {
        Console.WriteLine($"  ✗ {failure.AssetId} ({failure.Section}) {failure.ExceptionType}: " +
                          failure.Message);
    }

    Console.WriteLine();
    Console.WriteLine("── `15` §B4 deviations and contradictions ──────────────");
    foreach (var (id, count) in report.Deviations.OrderBy(e => e.Key, StringComparer.Ordinal))
    {
        Console.WriteLine($"  deviation      {id,-28} × {count}");
    }

    foreach (var (id, count) in report.Contradictions.OrderBy(e => e.Key, StringComparer.Ordinal))
    {
        Console.WriteLine($"  contradiction  {id,-28} × {count}");
    }

    Console.WriteLine();
    Console.WriteLine("── `15` §D2 atlases ────────────────────────────────────");
    foreach (var pack in report.Atlases.OrderBy(p => p.AtlasId, StringComparer.Ordinal))
    {
        Console.WriteLine(
            $"  {pack.AtlasId,-20} {pack.Placements.Count,4} placements over {pack.Pages.Count} page(s)");
    }

    if (report.Generated.Count == 0)
    {
        Console.WriteLine();
        Console.WriteLine("No placeholder was generated, so `15` Part F graded nothing.");
        return;
    }

    Console.WriteLine();
    Console.WriteLine("── `15` Part F ─────────────────────────────────────────");
    Console.WriteLine($"batch decision               : {report.Decision}");

    foreach (var item in Enumerable.Range(1, Doc15PartF.ItemCount))
    {
        var verdicts = report.Generated
            .SelectMany(placeholder => placeholder.Qa.Outcomes)
            .Where(outcome => outcome.ItemNumber == item)
            .GroupBy(outcome => outcome.Verdict)
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Key} × {group.Count()}");

        Console.WriteLine($"  item {item,2}                    : {string.Join(", ", verdicts)}");
    }

    Console.WriteLine(
        $"mechanical failures (7, 10)  : {report.MechanicalFailures.Count}");
    foreach (var failure in report.MechanicalFailures)
    {
        Console.WriteLine(
            $"  ✗ {failure.AssetId} item {failure.Outcome.ItemNumber}: {failure.Outcome.Reason}");
    }
}
