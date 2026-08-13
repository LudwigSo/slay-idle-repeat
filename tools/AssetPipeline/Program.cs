// The `15` §B4 post-processing pipeline's CLI. The library half is the part that matters —
// M8-10 drives it in-process over the whole manifest — and the entry point exists because the
// other tools/ projects are Exe for the same reason: a human runs a batch by hand.
//
// 🔒 What this CLI deliberately does NOT do is process a batch. Six of M8's eight original tasks
// are capability-blocked, so at the time of writing there is no generated art to run through the
// seven steps; M8-10 is the first task that produces any. Until then the two things a human
// actually needs from a command line are "what does the pipeline consist of" and "which of the
// seventeen holes has anybody calibrated" — and a batch command that silently processed nothing
// would be worse than no batch command at all.
using System.Globalization;
using SlayIdleRepeat.AssetPipeline;

const string usage = """
    SlayIdleRepeat.AssetPipeline — doc `15` §B4's asset post-processing pipeline.

      steps                    List `15` §B4's seven steps and the section each implements.
      thresholds [<file>]      Report which of the seventeen uncalibrated thresholds carry a
                               stated value. Defaults to assets/pipeline/thresholds.json.

    Batch processing arrives with M8-10, which is the first task that generates images to process.
    """;

var command = args.Length > 0 ? args[0] : "help";

switch (command)
{
    case "steps":
        foreach (var step in new AssetPipeline().Steps)
        {
            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{step.Number}. {step.Id,-20} {step.DocReference}"));
        }

        var atlas = new AtlasPackStep();
        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{atlas.Number}. {atlas.Id,-20} {atlas.DocReference}"));
        return 0;

    case "thresholds":
        var path = args.Length > 1 ? args[1] : ThresholdSet.ThresholdsPath;
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"No threshold register at '{path}'.");
            return 1;
        }

        var thresholds = ThresholdSet.LoadFrom(File.ReadAllText(path));
        var calibrated = 0;
        foreach (var key in ThresholdSet.Keys)
        {
            var stated = thresholds.IsCalibrated(key);
            calibrated += stated ? 1 : 0;
            Console.WriteLine($"{(stated ? "stated" : "  null"),-8} {key}");
        }

        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{calibrated} of {ThresholdSet.Keys.Count} calibrated. A null is doc `15` " +
                $"authorising no value (steering rule S6), not a value nobody typed in."));
        return 0;

    default:
        Console.WriteLine(usage);
        return command is "help" or "--help" or "-h" ? 0 : 1;
}
