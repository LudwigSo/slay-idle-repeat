using System.Globalization;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.BalanceHarness.Sweep;

namespace SlayIdleRepeat.BalanceHarness.Cli;

/// <summary>`21` §10's three commands.</summary>
public enum HarnessCommand
{
    /// <summary>The full `05` §9 sweep, every guardrail, both experiments and the report. The default.</summary>
    Sweep,

    /// <summary>🔒 Guardrails only, minimal output, NON-ZERO EXIT on a breach. The CI entry point.</summary>
    Assert,

    /// <summary>The deterministic PR-tier subset — the same shape, a small fight count, no experiments.</summary>
    Fast,
}

/// <summary>Which experiment <c>--experiment</c> asked for.</summary>
public enum ExperimentSelection
{
    /// <summary>Both, which is what <c>sweep</c> does by default.</summary>
    All,

    /// <summary>`05` §5's undecayed Thornmaw RAGE only.</summary>
    Rage,

    /// <summary>`17` §1's <c>addsPowerFraction</c> band only.</summary>
    Adds,

    /// <summary>None — what <c>assert</c> and <c>fast</c> do.</summary>
    None,
}

/// <summary>
/// 🔒 The harness's own argument parser. `30` §6 / `21` §2 forbid a <c>PackageReference</c>, so a CLI
/// parsing library is not available and this is written out.
/// </summary>
/// <remarks>
/// 🔒 <b>An unknown argument is an error, never a warning and never ignored.</b> A nightly job invoked
/// with a mistyped <c>--fights</c> that silently ran the default would report a number nobody asked
/// for, under a name that says it is something else.
/// </remarks>
public sealed record HarnessOptions
{
    /// <summary>🔒 `05` §9 — the documented fights per cell, and the default for <c>sweep</c> and <c>assert</c>.</summary>
    public const int DefaultFights = SweepScope.DocumentedFightsPerCell;

    /// <summary>The <c>fast</c> subset's fights per cell. Small enough for a PR, large enough to move a rate.</summary>
    public const int FastFights = 200;

    /// <summary>Which command to run.</summary>
    public HarnessCommand Command { get; init; } = HarnessCommand.Sweep;

    /// <summary>Fights per <c>(chapter, tier, archetype)</c>.</summary>
    public int Fights { get; init; } = DefaultFights;

    /// <summary>The <c>game-data</c> root. Defaults to the running checkout's.</summary>
    public string DataRoot { get; init; } = GameDataLoader.DataRoot;

    /// <summary>Where to write the report, or <c>null</c> for stdout only.</summary>
    public string? OutputPath { get; init; }

    /// <summary>Chapters to sweep, or <c>null</c> for every authored one.</summary>
    public IReadOnlyList<int>? Chapters { get; init; }

    /// <summary>Tiers to sweep, or <c>null</c> for all three.</summary>
    public IReadOnlyList<Tier>? Tiers { get; init; }

    /// <summary>Archetype ids to sweep, or <c>null</c> for all five authored ones.</summary>
    public IReadOnlyList<string>? Archetypes { get; init; }

    /// <summary>Which experiment to run.</summary>
    public ExperimentSelection Experiment { get; init; } = ExperimentSelection.All;

    /// <summary>Threads. Defaults to the machine's processor count; 1 forces a sequential run.</summary>
    public int Parallelism { get; init; } = Environment.ProcessorCount;

    /// <summary>Set by <c>--help</c>.</summary>
    public bool ShowUsage { get; init; }

    /// <summary>`21` §10's usage block.</summary>
    public static string Usage =>
        """
        BalanceHarness [command] [options]

          sweep    (default)  the full 05 §9 sweep + every guardrail + the report
          assert              guardrails only, minimal output, NON-ZERO EXIT on a breach
          fast                the deterministic PR-tier subset

        Options:
          --fights <n>        fights per (chapter, tier, archetype); default 10000 per 05 §9
          --data <dir>        default: the repo's game-data
          --out <file>        write the report
          --chapters <list>   comma-separated, e.g. 1,4,7
          --tiers <list>      comma-separated NORMAL,HEROIC,MYTHIC
          --archetypes <list> comma-separated build archetype ids
          --experiment <e>    rage | adds   (sweep only; default: both)
          --parallel <n>      degree of parallelism; default: processor count, 1 = sequential
          --help              this text

        Exit codes:
          0  every guardrail passed
          1  a guardrail breached, or could not be measured at all
          2  the arguments were not understood
        """;

    /// <summary>
    /// Parses an argument vector. Returns <c>null</c> and an error on anything not understood.
    /// </summary>
    public static HarnessOptions? Parse(IReadOnlyList<string> args, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        error = null;
        var options = new HarnessOptions();
        var index = 0;

        if (args.Count > 0 && !args[0].StartsWith("--", StringComparison.Ordinal))
        {
            switch (args[0])
            {
                case "sweep": options = options with { Command = HarnessCommand.Sweep }; break;
                case "assert":
                    options = options with
                    {
                        Command = HarnessCommand.Assert,
                        Experiment = ExperimentSelection.None,
                    };
                    break;
                case "fast":
                    options = options with
                    {
                        Command = HarnessCommand.Fast,
                        Fights = FastFights,
                        Experiment = ExperimentSelection.None,
                    };
                    break;
                default:
                    error = $"'{args[0]}' is not a command. The commands are sweep, assert and fast.";
                    return null;
            }

            index = 1;
        }

        var given = new HashSet<string>(StringComparer.Ordinal);

        for (; index < args.Count; index++)
        {
            var name = args[index];

            if (string.Equals(name, "--help", StringComparison.Ordinal))
            {
                return options with { ShowUsage = true };
            }

            // 🔒 `--flag=value` is refused by NAME rather than falling through to the value handling
            // below, which would otherwise report "expects a value" about an option this harness does
            // not have — a message that says the flag is real and the value is missing when neither is
            // true, and that does not name the actual fix.
            var equals = name.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0 && name.StartsWith("--", StringComparison.Ordinal))
            {
                error =
                    $"'{name}' uses --flag=value, which this harness does not read. Pass the value as " +
                    $"its own argument: '{name[..equals]} {name[(equals + 1)..]}'.";
                return null;
            }

            // 🔒 A repeated option is refused, not last-wins. `--fights 10000 --fights 200` is a
            // copy-paste left over from local testing, and silently running the last one is the same
            // "a mistyped flag became a silently wrong number" failure the unknown-option case refuses
            // one token earlier — except here the flag is real, so nothing else would ever say so.
            if (!given.Add(name))
            {
                error =
                    $"'{name}' was given more than once. Only the last would have taken effect, which " +
                    "is indistinguishable from a copy-paste that meant two different options.";
                return null;
            }

            if (index + 1 >= args.Count)
            {
                error = $"'{name}' expects a value and none followed it.";
                return null;
            }

            var value = args[++index];

            // 🔒 An option name is never a value. Without this, `--out --help` silently writes a report
            // to a file called "--help" and `--data --parallel 8` runs against a data root that does not
            // exist — the same failure class the type remarks refuse for an unknown option, arriving one
            // token later. The numeric options happen to catch it because "--parallel" is not a number;
            // --data, --out and --archetypes take any string and would not.
            if (value.StartsWith("--", StringComparison.Ordinal))
            {
                error =
                    $"'{name}' expects a value and '{value}' is an option name. If it were taken as the " +
                    $"value, '{value}' would also be silently dropped as an option.";
                return null;
            }

            switch (name)
            {
                case "--fights":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fights) ||
                        fights <= 0)
                    {
                        error = $"--fights '{value}' is not a positive whole number.";
                        return null;
                    }

                    options = options with { Fights = fights };
                    break;

                case "--data":
                    options = options with { DataRoot = value };
                    break;

                case "--out":
                    options = options with { OutputPath = value };
                    break;

                case "--chapters":
                    var chapterParts = Split(value);
                    if (chapterParts.Length == 0)
                    {
                        error = EmptyList(name, value);
                        return null;
                    }

                    var chapters = new List<int>();
                    foreach (var part in chapterParts)
                    {
                        if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chapter))
                        {
                            error = $"--chapters '{part}' is not a whole number.";
                            return null;
                        }

                        chapters.Add(chapter);
                    }

                    if (Repeated(chapterParts) is { } repeatedChapter)
                    {
                        error = RepeatedEntry(name, value, repeatedChapter);
                        return null;
                    }

                    options = options with { Chapters = chapters };
                    break;

                case "--tiers":
                    var tierParts = Split(value);
                    if (tierParts.Length == 0)
                    {
                        error = EmptyList(name, value);
                        return null;
                    }

                    var tiers = new List<Tier>();
                    foreach (var part in tierParts)
                    {
                        if (!Enum.TryParse<Tier>(part, ignoreCase: true, out var tier))
                        {
                            error =
                                $"--tiers '{part}' is not a tier. The three are " +
                                $"{string.Join(", ", Model.Tiers.All)}.";
                            return null;
                        }

                        tiers.Add(tier);
                    }

                    if (Repeated(tiers.Select(t => t.ToString()).ToArray()) is { } repeatedTier)
                    {
                        error = RepeatedEntry(name, value, repeatedTier);
                        return null;
                    }

                    options = options with { Tiers = tiers };
                    break;

                case "--archetypes":
                    var archetypeParts = Split(value);
                    if (archetypeParts.Length == 0)
                    {
                        error = EmptyList(name, value);
                        return null;
                    }

                    if (Repeated(archetypeParts) is { } repeatedArchetype)
                    {
                        error = RepeatedEntry(name, value, repeatedArchetype);
                        return null;
                    }

                    options = options with { Archetypes = archetypeParts };
                    break;

                case "--experiment":
                    switch (value.ToUpperInvariant())
                    {
                        case "RAGE": options = options with { Experiment = ExperimentSelection.Rage }; break;
                        case "ADDS": options = options with { Experiment = ExperimentSelection.Adds }; break;
                        default:
                            error = $"--experiment '{value}' is not an experiment. The two are rage and adds.";
                            return null;
                    }

                    break;

                case "--parallel":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parallel) ||
                        parallel <= 0)
                    {
                        error = $"--parallel '{value}' is not a positive whole number.";
                        return null;
                    }

                    options = options with { Parallelism = parallel };
                    break;

                default:
                    error = $"'{name}' is not an option this harness understands.";
                    return null;
            }
        }

        return options;
    }

    /// <summary>The scope this options object describes, against the authored catalogues.</summary>
    public SweepScope ToScope(ParPowerTable parPower, CalibrationBuilds calibration)
    {
        ArgumentNullException.ThrowIfNull(parPower);
        ArgumentNullException.ThrowIfNull(calibration);

        return new SweepScope(
            Chapters ?? parPower.Chapters,
            Tiers ?? Model.Tiers.All,
            Archetypes ?? calibration.Archetypes.Select(a => a.Id).ToArray(),
            Fights,
            Parallelism);
    }

    private static string[] Split(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The first entry that appears twice in a list, or <c>null</c> when all are distinct.</summary>
    private static string? Repeated(IReadOnlyList<string> entries)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (!seen.Add(entry))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// 🔒 A repeated list entry is refused, because the second copy is not a second measurement.
    /// </summary>
    /// <remarks>
    /// <c>--chapters 1,1,4</c> builds the <c>(chapter, tier, archetype)</c> cell twice, and
    /// <c>SweepSeeds.CellSeed</c> is a pure function of that key — so both copies run the SAME seeded
    /// fights and the per-cell table prints the identical line twice. It reads as two cells agreeing
    /// with each other, which is the one thing it cannot be, and it doubles the sweep's cost for it.
    /// This is <see cref="EmptyList"/>'s rule on the other side: a subject set that is not the one the
    /// caller believes they asked for is refused where it is still nameable.
    /// </remarks>
    private static string RepeatedEntry(string name, string value, string entry) =>
        $"{name} '{value}' lists '{entry}' more than once. The repeat sweeps the same cell under the " +
        "same key with the same seeds, so it prints a duplicate row that reads as corroboration and " +
        "is the identical run — drop it rather than paying twice for it.";

    /// <summary>
    /// 🔒 An empty list narrows the sweep to nothing rather than to everything.
    /// </summary>
    /// <remarks>
    /// <c>--chapters ,,</c> splits to zero entries, and a zero-length <c>Chapters</c> is NOT the same as
    /// the <c>null</c> that means "every authored one": it produces a scope of zero cells, so every
    /// guardrail comes back <see cref="Guardrails.GuardrailVerdict.Inconclusive"/> over a subject set
    /// nobody chose. That is the same failure the Inconclusive verdict exists to make loud, so it is
    /// refused at the argument boundary where it is still nameable.
    /// </remarks>
    private static string EmptyList(string name, string value) =>
        $"{name} '{value}' lists nothing. An empty list sweeps zero cells rather than all of them — " +
        $"leave {name} off to sweep every authored one.";
}
