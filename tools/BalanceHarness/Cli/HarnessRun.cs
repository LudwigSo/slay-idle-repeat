using System.Diagnostics;
using System.Globalization;
using System.Text;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.BalanceHarness.Diagnostics;
using SlayIdleRepeat.BalanceHarness.Experiments;
using SlayIdleRepeat.BalanceHarness.Guardrails;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.BalanceHarness.Rules;
using SlayIdleRepeat.BalanceHarness.Sweep;

namespace SlayIdleRepeat.BalanceHarness.Cli;

/// <summary>
/// 🔒 `21` §10's CLI — what each of the three commands actually does, and the exit code it produces.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b><c>assert</c> exits non-zero when a guardrail breaches, and on the shipped data it DOES.</b>
/// That is the harness working, not a defect. Nothing here softens a band, scales a build, or excludes
/// a cell to make the exit code zero. `05` §9's numbers are read from
/// <c>tuning/par_power.json</c> and are asserted as authored.
/// </para>
/// <para>
/// 🔒 A guardrail that could not be measured at all exits non-zero too. See
/// <see cref="GuardrailVerdict.Inconclusive"/>: an empty subject set satisfies a "for all" assertion
/// vacuously, and a nightly job that went green because it measured nothing is worse than no job.
/// </para>
/// </remarks>
public static class HarnessRun
{
    /// <summary>Every guardrail passed.</summary>
    public const int ExitSuccess = 0;

    /// <summary>A guardrail breached, or could not be measured.</summary>
    public const int ExitGuardrailBreach = 1;

    /// <summary>The arguments were not understood.</summary>
    public const int ExitBadArguments = 2;

    /// <summary>The tier the two experiments and the diagnostics are measured at.</summary>
    /// <remarks>
    /// `29` §4's base tier: it is the one every chapter's first encounter with a boss happens at, and
    /// it keeps the experiments comparable across chapters. Both experiments are diagnosis, and
    /// running them at all three tiers would triple their cost for a sensitivity question the base
    /// tier already answers.
    /// </remarks>
    public const Tier ExperimentTier = Tier.NORMAL;

    /// <summary>Fights per arm of the two experiments.</summary>
    public const int ExperimentFights = 400;

    /// <summary>Fights per bisection step of the shortfall diagnostic.</summary>
    public const int ShortfallFightsPerStep = 120;

    /// <summary>Fights per row of the elasticity table.</summary>
    public const int ElasticityFights = 400;

    /// <summary>Runs the CLI and returns the process exit code.</summary>
    /// <remarks>
    /// 🔒 <b>The invariant culture is pinned here, not only in <c>Program</c>.</b> The report is a
    /// diffable artefact — a nightly job compares it run to run and a design decision is made off it —
    /// and this checkout's own toolchain output is German. A clear rate that reads <c>62,00%</c> on one
    /// host and <c>62.00%</c> on another is not diffable, and a decimal comma inside a
    /// comma-separated table is worse than not diffable. It is set here rather than only at the
    /// process entry point because <c>SlayIdleRepeat.Core.Tests</c> calls this method directly, and a
    /// report the suite formats differently from the CLI would make every literal-output assertion a
    /// statement about the developer's regional settings. <see cref="CultureInfo.DefaultThreadCurrentCulture"/>
    /// is set too, because the sweep runs on <see cref="Parallel"/>'s thread pool.
    /// </remarks>
    public static int Run(IReadOnlyList<string> args, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);

        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        var options = HarnessOptions.Parse(args, out var error);

        if (options is null)
        {
            output.WriteLine(error);
            output.WriteLine();
            output.WriteLine(HarnessOptions.Usage);
            return ExitBadArguments;
        }

        if (options.ShowUsage)
        {
            output.WriteLine(HarnessOptions.Usage);
            return ExitSuccess;
        }

        var report = new StringBuilder();
        int exitCode;

        // 🔴 The buffered report survives ANY fault below, not only a diagnostics one.
        //    TryWriteDiagnosticsAndExperiments covers the one path M2-16a knew about; every other
        //    line of Execute — the loader, the sweep, and the report writers themselves — was still
        //    able to discard a finished half-hour sweep by throwing before `report` reached a writer.
        //    This is the outermost statement of the same rule: what was measured gets written.
        try
        {
            exitCode = Execute(options, report);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Header(report, "🔴 HARNESS FAULT — the report below is everything that had been measured");
            report.AppendLine(
                "This is NOT a balance finding. The harness could not run to completion; whatever is");
            report.AppendLine(
                "printed above this line was already measured and stands. The exit code is non-zero.");
            report.AppendLine($"{exception.GetType().Name}: {exception.Message}");
            exitCode = ExitGuardrailBreach;
        }

        var text = report.ToString();
        output.Write(text);

        if (options.OutputPath is not null && !TryWriteOutputFile(options.OutputPath, text, output))
        {
            // 🔴 The report reached stdout but not the file the caller asked for. A nightly job
            //    diffing the artefact would otherwise compare against a stale file and call it
            //    "no change" — the quietest possible way for this tool to be wrong.
            exitCode = exitCode == ExitSuccess ? ExitGuardrailBreach : exitCode;
        }

        return exitCode;
    }

    /// <summary>
    /// Writes the report to <c>--out</c>, reporting a filesystem refusal rather than throwing it.
    /// </summary>
    /// <remarks>
    /// A missing parent directory or a read-only path is a fact about the caller's arguments, not a
    /// finding about the game, and it arrives AFTER the report has already been written to stdout —
    /// so letting it propagate would replace one of the three documented exit codes with an unhandled
    /// exception, over a failure that cost the run nothing.
    /// </remarks>
    private static bool TryWriteOutputFile(string path, string text, TextWriter output)
    {
        try
        {
            File.WriteAllText(path, text);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            output.WriteLine();
            output.WriteLine(
                $"🔴 --out '{path}' could not be written ({exception.GetType().Name}: " +
                $"{exception.Message}). The report above went to stdout and is complete; only the " +
                "file copy is missing, and the exit code is non-zero so a job cannot mistake a stale " +
                "artefact for this run's.");
            return false;
        }
    }

    private static int Execute(HarnessOptions options, StringBuilder report)
    {
        var totalStopwatch = Stopwatch.StartNew();

        var content = GameDataLoader.Load(options.DataRoot);
        var runner = new SweepRunner(content);
        var scope = options.ToScope(runner.ParPower, runner.Calibration);
        var sweep = runner.Run(scope);

        var parHeroes = sweep.Cells
            .Select(c => ParHeroDef.From(c.Chapter, c.Tier, c.ArchetypeId, c.Hero.Stats))
            .ToArray();

        var guardrails = new List<GuardrailResult>
        {
            SweepGuardrails.ClearRateAtPar(sweep.Cells, runner.ParPower.ClearRateMin, runner.ParPower.ClearRateMax),
            SweepGuardrails.HardFloor(sweep.Cells),
            SweepGuardrails.HardCeiling(sweep.Cells),
            MitigationGuardrail.Evaluate(
                new MitigationModel(MitigationDials.Read(content)),
                runner.Enemies,
                runner.Bosses,
                runner.ParPower,
                parHeroes),
            MarginalPowerGuardrail.Evaluate(
                content, runner.Calibration.Archetypes, runner.Calibration.DefaultLevel),
        };

        var sporequeen = SweepGuardrails.SporequeenBand(sweep.Cells);
        var diagnosticsFaulted = false;

        if (options.Command == HarnessCommand.Assert)
        {
            WriteAssertOutput(report, guardrails, sporequeen, scope, sweep);
        }
        else
        {
            diagnosticsFaulted = WriteFullReport(
                report, options, runner, scope, sweep, guardrails, sporequeen, content);
        }

        totalStopwatch.Stop();
        report.AppendLine(
            $"total wall clock: {totalStopwatch.Elapsed.TotalSeconds:0.00} s");

        // 🔴 An engine fault counts as a non-pass. A sweep that could not simulate some of its cells
        // has graded its guardrails over a subject set nobody chose, and a nightly job that went green
        // on that is exactly the failure the Inconclusive verdict exists to prevent.
        var breached = guardrails.Any(g => !g.Passed) || !sporequeen.Passed || sweep.Faults.Count > 0
            || diagnosticsFaulted;

        return breached ? ExitGuardrailBreach : ExitSuccess;
    }

    private static void WriteAssertOutput(
        StringBuilder report,
        IReadOnlyList<GuardrailResult> guardrails,
        GuardrailResult sporequeen,
        SweepScope scope,
        SweepResult sweep)
    {
        report.AppendLine($"scope: {scope}  ({sweep.FightCount} fights, {sweep.Elapsed.TotalSeconds:0.0} s)");

        foreach (var fault in sweep.Faults)
        {
            report.AppendLine($"[ENGINE FAULT] {fault}");
        }

        foreach (var guardrail in guardrails)
        {
            report.AppendLine(
                $"[{guardrail.Verdict.ToString().ToUpperInvariant(),-12}] guardrail {guardrail.Number}: " +
                $"{guardrail.Name} — {guardrail.Summary}");
        }

        report.AppendLine(
            $"[{sporequeen.Verdict.ToString().ToUpperInvariant(),-12}] {sporequeen.Name} — {sporequeen.Summary}");
    }

    /// <summary>Writes the full report. Returns true when the diagnostics section faulted.</summary>
    private static bool WriteFullReport(
        StringBuilder report,
        HarnessOptions options,
        SweepRunner runner,
        SweepScope scope,
        SweepResult sweep,
        IReadOnlyList<GuardrailResult> guardrails,
        GuardrailResult sporequeen,
        Core.Content.ContentSnapshot content)
    {
        Header(report, "BALANCE HARNESS — 05 §9");
        report.AppendLine($"command            : {options.Command}");
        report.AppendLine($"data root          : {options.DataRoot}");
        report.AppendLine($"content version    : {content.Version}");
        report.AppendLine($"scope              : {scope}");
        report.AppendLine($"fights simulated   : {sweep.FightCount}");
        report.AppendLine($"parallelism        : {sweep.DegreeOfParallelism} thread(s)");
        report.AppendLine($"sweep wall clock   : {sweep.Elapsed.TotalSeconds:0.00} s");
        report.AppendLine(
            $"K_POWER (derived)  : {runner.KPower.Value:0.####}  " +
            $"= {runner.Calibration.ReferenceParBuildTargetPower:0} / PowerIndex(referenceParBuild@L" +
            $"{runner.KPower.ReferenceLevel}) = {runner.KPower.ReferencePowerIndex:0.####}   " +
            $"(29 §2.1 expects ≈ {content.ReadDouble(DerivedKPower.ExpectedMagnitudePointer):0.#})");
        report.AppendLine(
            $"EnemyPower(42)     : ParPower × (1 + {NodePower.PerNodePowerGrowth} × " +
            $"{NodePower.BossNodeIndex}) × {NodePower.BossStageMultiplier} = ParPower × " +
            $"{NodePower.BossPower(1.0):0.####}   [02 §4.3 — 📐 authored in NO game-data file]");
        report.AppendLine(
            $"bosses swept       : {string.Join(", ", runner.Bosses.Campaign.Select(b => b.Id))}");
        report.AppendLine(
            $"excluded from sweep: {string.Join(", ", runner.Bosses.Excluded.Select(b => b.Id))} " +
            "(no chapter — carries fixedPower/fixedLevel, so 02 §4.3's EnemyPower(i) is undefined for it)");

        if (sweep.Faults.Count > 0)
        {
            Header(report, "🔴 ENGINE FAULTS — cells the simulator could not run at all");
            report.AppendLine(
                "These are NOT balance findings. The guardrails below are graded over a smaller subject");
            report.AppendLine(
                "set than was asked for, and the exit code is non-zero because of it.");
            foreach (var fault in sweep.Faults)
            {
                report.AppendLine(fault);
            }
        }

        Header(report, "PER-CELL RESULTS (clear rate at par; durations over CLEARED fights)");
        report.AppendLine(
            "                                      clear     clears        median      p10      p90      min      max  <12s  >70s   scalar   par×");
        foreach (var cell in sweep.Cells)
        {
            report.AppendLine(
                $"{cell.Key,-30} {cell.ClearRate * 100,8:0.00}% {cell.ClearCount,6}/{cell.FightCount,-6} " +
                $"{Sec(cell.MedianClearedSeconds),9} {Sec(cell.P10ClearedSeconds),8} {Sec(cell.P90ClearedSeconds),8} " +
                $"{Sec(cell.MinClearedSeconds),8} {Sec(cell.MaxClearedSeconds),8} " +
                $"{cell.ClearsUnderHardFloor,5} {cell.ClearsOverHardCeiling,5} " +
                $"{cell.Hero.Scalar,8:0.####} {cell.Hero.AchievedRatio,6:0.####}");
        }

        Header(report, "PER-FIGHT SIMULATION COST vs 05's < 5 ms budget");
        var contendedMedian = sweep.Cells.Select(c => c.MedianCostMicroseconds).ToArray();
        Array.Sort(contendedMedian);
        report.AppendLine(
            $"UNCONTENDED (single thread, cell {sweep.UncontendedCostSampleCell}, " +
            $"{sweep.UncontendedCostSampleMicroseconds.Count} fights): " +
            $"median {sweep.UncontendedMedianMilliseconds:0.000} ms · " +
            $"p90 {sweep.UncontendedP90Milliseconds:0.000} ms · max {sweep.UncontendedMaxMilliseconds:0.000} ms");
        report.AppendLine(
            $"CONTENDED   ({sweep.DegreeOfParallelism} threads, median-of-cell-medians): " +
            $"{CellResult.Percentile(contendedMedian, 0.50) / 1000.0:0.000} ms · " +
            $"p90 {CellResult.Percentile(contendedMedian, 0.90) / 1000.0:0.000} ms · " +
            $"max {Ms(MaxOverCells(sweep, c => c.MaxCostMicroseconds) / 1000.0)}");
        report.AppendLine(
            "⚠️ the contended numbers are what a parallel sweep measures, not what 05's budget is about.");

        foreach (var guardrail in guardrails)
        {
            Guardrail(report, guardrail);
        }

        Guardrail(report, sporequeen);

        return options.Command == HarnessCommand.Sweep
            && TryWriteDiagnosticsAndExperiments(report, options, runner, scope, sweep);
    }

    /// <summary>
    /// 🔴 The diagnostics and the two experiments, with an engine fault REPORTED rather than thrown.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>Everything above this point is a completed measurement, and an exception here would throw
    /// all of it away.</b> The report is accumulated into a <see cref="StringBuilder"/> and only written
    /// to stdout and to <c>--out</c> after <c>Execute</c> returns, so an unhandled fault in a diagnostic
    /// discards a finished 10 000-fight-per-cell sweep — 120 cells and roughly half an hour of
    /// simulation — and leaves a stack trace in its place. That is precisely the failure
    /// <see cref="SweepRunner.TryRunCell"/> exists to prevent one layer down, and the diagnostics were
    /// the one path still uncovered: <c>StatElasticity.Measure</c> calls <c>RunCell</c> directly, and
    /// <c>ClearRateCalibration</c>'s own remarks record that raising a hero's power is exactly what
    /// walks a fight into the boss phase where a script faults.
    /// </para>
    /// <para>
    /// 🔒 The fault is a non-pass, so <c>Execute</c> still exits non-zero: a run whose report is
    /// incomplete must not look like a run that had nothing to report. The catch filter is
    /// <see cref="SweepRunner.TryRunCell"/>'s, for its reason — an <see cref="OutOfMemoryException"/> is
    /// not a finding about the game and there is nothing useful to write after one.
    /// </para>
    /// </remarks>
    private static bool TryWriteDiagnosticsAndExperiments(
        StringBuilder report,
        HarnessOptions options,
        SweepRunner runner,
        SweepScope scope,
        SweepResult sweep)
    {
        try
        {
            var probes = WriteDiagnostics(report, runner, scope, sweep);

            // 🔴 Both experiments target boss PHASE mechanics, and `17` §1's phases are HP bands. The
            // second arm of each is run at the multiple where the build actually reaches phase 3 —
            // otherwise the effect under test never fires and the A/B reports a difference of zero
            // that reads like "it does not matter" and means "it never happened".
            WriteExperiments(report, options, runner, new ShortfallLookup(probes));

            return false;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Header(report, "🔴 DIAGNOSTICS FAULT — the sweep and every guardrail above are complete");
            report.AppendLine(
                "This is NOT a balance finding and it does not change a guardrail verdict. A diagnostic");
            report.AppendLine(
                "or an experiment could not run to completion; everything printed above it stands.");
            report.AppendLine($"{exception.GetType().Name}: {exception.Message}");

            return true;
        }
    }

    private static IReadOnlyList<ShortfallProbe> WriteDiagnostics(
        StringBuilder report, SweepRunner runner, SweepScope scope, SweepResult sweep)
    {
        Header(report, "⚠️ DIAGNOSIS (not an assertion): power shortfall against the 70% target");
        report.AppendLine(
            "The multiple of ParPower(c,t) at which each build first reaches the authored 0.70 clear-rate");
        report.AppendLine(
            "target. 21 §3.2 keeps retuning with design: this is evidence, not a proposal.");

        // Over the SWEPT chapters and archetypes rather than every authored one, so that a narrowed
        // --chapters run does not silently pay for a full-game diagnostic it did not ask for.
        var probes = new List<ShortfallProbe>();
        foreach (var chapter in scope.Chapters)
        {
            foreach (var archetypeId in scope.ArchetypeIds)
            {
                probes.Add(ClearRateCalibration.Find(
                    runner, chapter, ExperimentTier, runner.Calibration.Archetype(archetypeId),
                    runner.ParPower.ClearRateTarget, ShortfallFightsPerStep));
            }
        }

        foreach (var probe in probes)
        {
            report.AppendLine(probe.ToString());
        }

        var median = probes.Select(p => p.Multiple).OrderBy(m => m).ToArray();
        report.AppendLine(
            $"median shortfall over {probes.Count} probes: {CellResult.Percentile(median, 0.50):0.00} × par " +
            $"(range {median[0]:0.00} – {median[^1]:0.00})");

        // The elasticity table is measured where the fight is close, because at par nothing clears and
        // every delta would be identically zero. The multiple comes from the probe above.
        //
        // ARCH_TANK_THORNS when it is in scope: it is the ONLY archetype that holds THORNS above 0, so
        // it is the only one whose THORNS row is a 1% step of a real value rather than the labelled
        // absolute probe — and THORNS is half of what this table exists to say something about.
        var elasticityProbe =
            probes.FirstOrDefault(p => string.Equals(p.ArchetypeId, "ARCH_TANK_THORNS", StringComparison.Ordinal))
            ?? probes[0];
        var elasticityArchetype = runner.Calibration.Archetype(elasticityProbe.ArchetypeId);

        var table = StatElasticity.Measure(
            runner, elasticityProbe.Chapter, ExperimentTier, elasticityArchetype,
            elasticityProbe.Multiple, ElasticityFights);

        Header(report, "⚠️ DIAGNOSIS (not an assertion): empirical stat elasticity in a REAL fight");
        report.AppendLine(
            $"C{table.Chapter} {table.Tier} {table.ArchetypeId} at {table.PowerMultiple:0.00} × par " +
            $"(baseline clear rate {table.BaselineClearRate * 100:0.0}%, {table.Fights} fights per row).");
        report.AppendLine(
            "Measured at that multiple and NOT at par, because at par nothing clears and every delta would");
        report.AppendLine(
            "be identically zero — a table of zeros would say nothing about THORNS or HEAL_PCT.");
        report.AppendLine(
            $"⚠️ step = +{StatElasticity.DiagnosticRelativeStep * 100:0}% of the archetype's own value " +
            $"(+{StatElasticity.DiagnosticAbsoluteProbeForZero:0.##} absolute where it holds the stat at 0, " +
            "marked *).");
        report.AppendLine(
            $"⚠️ NOT guardrail 6's +{MarginalPowerGuardrail.RelativeStep * 100:0}% step: a 1% bump moves a " +
            "clear rate by less than the standard error of");
        report.AppendLine(
            "   any affordable sample. Both steps are harness definitions with no authored basis, and the");
        report.AppendLine(
            "   two tables are therefore NOT numerically comparable to each other.");
        report.AppendLine(
            "The bumped build is re-scaled to the SAME power target, so a stat the power model prices");
        report.AppendLine(
            "correctly buys nothing (the scalar pays for it) and a stat the model cannot see shows undiluted.");
        foreach (var row in table.Rows)
        {
            report.AppendLine(row.ToString());
        }

        report.AppendLine();
        report.AppendLine(
            $"sweep cells: {sweep.Cells.Count}; every cell's boss: " +
            $"{string.Join(", ", sweep.Cells.Select(c => c.BossId).Distinct().Order(StringComparer.Ordinal))}");
        report.AppendLine(
            $"🔴 highest boss phase reached anywhere in the sweep: " +
            $"{Phase(MaxOverCells(sweep, c => c.MaxBossPhaseReached))} (17 §1's phases are HP bands: " +
            "phase 2 at 66% boss HP, phase 3 at 33%)");

        return probes;
    }

    private static void WriteExperiments(
        StringBuilder report, HarnessOptions options, SweepRunner runner, ShortfallLookup shortfall)
    {
        // 🔒 Two passes for each experiment. The first is `05` §9's own condition — at par — and reports
        // the null result together with the reason it is null. The second is at each build's own
        // measured shortfall multiple, which is the only place the effect under test actually fires.
        var passes = new (Func<int, string, double> Multiple, string Label)[]
        {
            ((_, _) => 1.0, "1.00 ×"),
            (shortfall.Multiple, "each build's own measured shortfall ×"),
        };

        if (options.Experiment is ExperimentSelection.All or ExperimentSelection.Rage)
        {
            foreach (var (multiple, label) in passes)
            {
                var rage = BalanceExperiments.Rage(
                    runner, options.DataRoot, ExperimentTier, ExperimentFights, multiple, label);
                Header(report, "⚠️ EXPERIMENT (measure, never retune): " + rage.Title);
                report.AppendLine(
                    "05 §5 states no RAGE decay curve and M2-10 left it null. Thornmaw's phase 3 authors a");
                report.AppendLine(
                    "PHASE-scoped RAGE +30% ATK, so what runs is an UNDECAYED buff for the whole phase.");
                report.AppendLine(
                    "🔴 An arm whose maxPhase is below 3 never fired the effect at all — read the phase");
                report.AppendLine(
                    "   columns before reading a zero difference as 'the RAGE does not matter'.");
                foreach (var arm in rage.Arms)
                {
                    report.AppendLine(arm.ToString());
                }
            }
        }

        if (options.Experiment is ExperimentSelection.All or ExperimentSelection.Adds)
        {
            foreach (var (multiple, label) in passes)
            {
                var adds = BalanceExperiments.AddsFraction(
                    runner, options.DataRoot, ExperimentTier, ExperimentFights, multiple, label);
                Header(report, "⚠️ EXPERIMENT (measure, never retune): " + adds.Title);
                report.AppendLine(
                    "17 §1 gives a 25–35% band and names no number; all five summoners ship at the 0.30 midpoint.");
                report.AppendLine(
                    "🔴 Four of the five summons are ON_PHASE_ENTER phase 2 or 3 — check maxPhase before");
                report.AppendLine(
                    "   reading an identical row as insensitivity to the fraction.");
                foreach (var arm in adds.Arms)
                {
                    report.AppendLine(arm.ToString());
                }
            }
        }
    }

    private static void Guardrail(StringBuilder report, GuardrailResult guardrail)
    {
        Header(
            report,
            guardrail.Number == 0
                ? $"[{guardrail.Verdict.ToString().ToUpperInvariant()}] {guardrail.Name}"
                : $"[{guardrail.Verdict.ToString().ToUpperInvariant()}] GUARDRAIL {guardrail.Number.ToString(CultureInfo.InvariantCulture)} — {guardrail.Name}");
        report.AppendLine(guardrail.Summary);
        report.AppendLine($"subjects evaluated: {guardrail.SubjectCount}");
        foreach (var detail in guardrail.Details)
        {
            report.AppendLine(detail);
        }
    }

    private static void Header(StringBuilder report, string title)
    {
        report.AppendLine();
        report.AppendLine(new string('=', 110));
        report.AppendLine(title);
        report.AppendLine(new string('=', 110));
    }

    private static string Sec(double seconds) =>
        double.IsNaN(seconds) ? "n/a" : seconds.ToString("0.00", CultureInfo.InvariantCulture) + "s";

    private static string Ms(double milliseconds) =>
        double.IsNaN(milliseconds)
            ? NoCells
            : milliseconds.ToString("0.000", CultureInfo.InvariantCulture) + " ms";

    private static string Phase(double phase) =>
        double.IsNaN(phase) ? NoCells : ((int)phase).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// 🔴 The maximum of a per-cell reading, or <see cref="double.NaN"/> when the sweep produced no
    /// cell at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>Every cell can fault.</b> <c>SweepRunner.TryRunCell</c> records a fault and drops the
    /// cell, so a systemic engine regression or a <c>--data</c> snapshot that breaks every fight
    /// leaves <c>sweep.Cells</c> empty while <c>sweep.Faults</c> is full. <c>Enumerable.Max</c> on
    /// that throws <em>"Sequence contains no elements"</em> — a message that names neither the rule
    /// nor the input, from inside the writer of a report whose whole purpose at that moment is to
    /// show the faults. <c>CellResult.Percentile</c> already answers <c>NaN</c> for the same reason
    /// and this is the same answer for the same question.
    /// </para>
    /// </remarks>
    private static double MaxOverCells(SweepResult sweep, Func<CellResult, double> reading)
    {
        var max = double.NaN;

        foreach (var cell in sweep.Cells)
        {
            var value = reading(cell);

            if (double.IsNaN(max) || value > max)
            {
                max = value;
            }
        }

        return max;
    }

    /// <summary>What a reading over zero cells reads as — never a number that looks measured.</summary>
    private const string NoCells = "n/a — every swept cell faulted";
}
