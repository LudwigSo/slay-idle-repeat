using SlayIdleRepeat.AssetManifest;
using SlayIdleRepeat.AssetProvenance;

// The provenance CLI (M8-01a). build/ci/Invoke-ProvenanceGate.ps1 runs `check`; the other two
// verbs exist for the generation sessions that will author the records by hand.
//
// A thin I/O shell on purpose, the same way tools/ContentValidator is: every rule lives in
// ProvenanceGate / RecordValidator / ProvenanceStore, where SlayIdleRepeat.AssetProvenance.Tests
// exercises it. A CI-only check nobody unit-tests is a check that drifts.
return Cli.Run(args);

internal static class Cli
{
    private const int Pass = 0;
    private const int Fail = 1;
    private const int Usage = 2;

    internal static int Run(string[] args)
    {
        var verb = args.Length > 0 ? args[0] : "check";

        try
        {
            return verb switch
            {
                "check" => Check(args),
                "show" => Show(args),
                "template" => Template(args),
                "--help" or "-h" or "help" => PrintUsage(Pass),
                _ => PrintUsage(Usage, $"unknown verb '{verb}'."),
            };
        }
        catch (ProvenanceFormatException exception)
        {
            Console.Error.WriteLine($"PROVENANCE STORE ERROR: {exception.Message}");
            return Fail;
        }
        catch (AssetManifestFormatException exception)
        {
            Console.Error.WriteLine($"ASSET MANIFEST ERROR: {exception.Message}");
            return Fail;
        }
    }

    /// <summary>Runs the gate and prints every violation. Exit 0 pass, 1 fail.</summary>
    private static int Check(string[] args)
    {
        var root = RepositoryRoot(args);
        var manifest = AssetManifestReader.Load(Option(args, "--data-root") ?? Path.Combine(root, "game-data"));
        var store = ProvenanceStore.Load(Option(args, "--store") ?? ProvenanceStore.RootFor(root));
        var deliveryRoot = Option(args, "--delivery-root") ?? DeliveredAssets.RootFor(root);
        var delivered = DeliveredAssets.Scan(deliveryRoot);

        var report = ProvenanceGate.Run(manifest, store, delivered);

        Console.WriteLine("=== Asset provenance gate (15 §B0, §G · 20 §2.1, §6) ===");
        Console.WriteLine($"Register    : {report.RegisteredAssets} ids ({report.ActiveAssets} uncut, {report.CutAssets} cut)");
        Console.WriteLine($"Delivered   : {report.DeliveredAssets} file(s) under {deliveryRoot}, " +
                          $"filling {report.CoveredSlots} uncut slot(s)");
        Console.WriteLine($"Records     : {report.Records} in {ProvenanceStore.StoreDirectory}");
        Console.WriteLine($"Tool licences confirmed in writing: " +
                          $"{store.Licences.Licences.Count(l => l.IsConfirmed)} of {store.Licences.Licences.Count}");
        Console.WriteLine();
        Console.WriteLine(report.Headline());
        Console.WriteLine();

        foreach (var violation in report.Violations)
        {
            Console.Error.WriteLine(violation.ToString());
        }

        if (!report.Passed)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Asset provenance gate FAILED with {report.Violations.Count} problem(s).");
            return Fail;
        }

        Console.WriteLine("Asset provenance gate passed.");
        return Pass;
    }

    /// <summary>Prints one record, as stored.</summary>
    private static int Show(string[] args)
    {
        if (args.Length < 2)
        {
            return PrintUsage(Usage, "`show` needs an asset id.");
        }

        var store = ProvenanceStore.Load(Option(args, "--store") ?? ProvenanceStore.RootFor(RepositoryRoot(args)));
        var record = store.Find(args[1]);

        if (record is null)
        {
            Console.Error.WriteLine(
                $"No provenance record for '{args[1]}'. The store holds {store.Records.Count}.");
            return Fail;
        }

        Console.WriteLine(ProvenanceStore.WriteRecord(record));
        return Pass;
    }

    /// <summary>Prints an empty record of one kind, for a generation session to fill in. Placeholder values are visibly not data.</summary>
    private static int Template(string[] args)
    {
        if (args.Length < 3)
        {
            return PrintUsage(Usage, "`template` needs an asset id and a kind.");
        }

        var (assetId, kind) = (args[1], args[2]);
        var tooling = HasFlag(args, "--audio") ? new AudioTooling("<tool>", "<version>") : null;

        ProvenanceRecord record = kind switch
        {
            MidjourneyProvenance.KindName => new MidjourneyProvenance
            {
                AssetId = assetId, Tooling = tooling,
                JobId = "<job id>", Prompt = "<full positive prompt, 15 §B1 scaffold included>",
                Seed = "<seed>", Sref = "<--sref value>", AspectRatio = "<--ar>",
                Style = "<--style>", Stylize = "<--s>", ModelVersion = "<model version>",
                Date = "<YYYY-MM-DD>",
            },
            ProceduralProvenance.KindName => new ProceduralProvenance
            {
                AssetId = assetId, Tooling = tooling,
                Generator = "<generator name>", RepoCommit = "<40-hex commit>",
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal),
            },
            Cc0Provenance.KindName => new Cc0Provenance
            {
                AssetId = assetId, Tooling = tooling,
                Source = "<pack or library>", Url = "<https://…>",
                Licence = "<SPDX id or licence text>", DateRetrieved = "<YYYY-MM-DD>",
            },
            _ => throw new ProvenanceFormatException(
                assetId,
                $"cannot be templated as kind '{kind}': it is not one of " +
                $"{MidjourneyProvenance.KindName}, {ProceduralProvenance.KindName}, " +
                $"{Cc0Provenance.KindName}."),
        };

        Console.WriteLine(ProvenanceStore.WriteRecord(record));
        return Pass;
    }

    private static string RepositoryRoot(string[] args) =>
        Option(args, "--repository-root") ?? Directory.GetCurrentDirectory();

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static bool HasFlag(string[] args, string name) => Array.IndexOf(args, name) >= 0;

    private static int PrintUsage(int exitCode, string? problem = null)
    {
        if (problem is not null)
        {
            Console.Error.WriteLine($"ERROR: {problem}");
        }

        Console.WriteLine(
            """
            SlayIdleRepeat.AssetProvenance — provenance records for generated assets (15 §B0/§G, 20 §2.1/§6).

              check     [--repository-root R] [--data-root D] [--store S] [--delivery-root A]
                        Every delivered asset has a provenance record, and every record names a
                        known, uncut asset. Exit 0 pass, 1 fail.

              show      <assetId> [--repository-root R] [--store S]
                        Print one record.

              template  <assetId> <midjourney|procedural|cc0> [--audio]
                        Print an empty record of that kind for a generation session to fill in.
                        --audio adds 20 §2.1's tool + version, which audio records must carry.
            """);

        return exitCode;
    }
}
