using System.Globalization;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.BalanceHarness.Sweep;

namespace SlayIdleRepeat.BalanceHarness.Experiments;

/// <summary>Two balance measurements — measure, never retune.</summary>
/// <remarks>
/// Both experiments report a difference and stop; neither writes a number anywhere or proposes one.
/// The output is evidence for a design decision, not the decision itself.
/// </remarks>
public static class BalanceExperiments
{
    /// <summary>Thornmaw's phase-3 RAGE effect, whose decay curve is not authored.</summary>
    public const string ThornmawRageEffectId = "BOSS_THORNMAW_P3_RAGE";

    /// <summary>The script that carries it.</summary>
    public const string ThornmawScriptId = "BOSS_THORNMAW";

    /// <summary>The three points of the authored 25-35% adds band.</summary>
    public static IReadOnlyList<double> AddsFractionProbes { get; } = [0.25, 0.30, 0.35];

    /// <summary>
    /// The <c>RAGE</c> hole: A/B Thornmaw at par with the shipped script against one whose phase-3
    /// <c>RAGE</c> is removed.
    /// </summary>
    /// <remarks>
    /// Thornmaw's phase 3 authors a <c>RAGE +30% ATK</c> with a PHASE-scoped duration but no decay
    /// curve, so what runs is a buff at full strength for all of phase 3 — stronger than intended.
    /// Removing the effect is not a proposal to remove it; it bounds how much of the measured duration
    /// is the missing decay. Boss phases are HP bands, so a build that dies before phase 3 never
    /// triggers the effect and the A/B reads a difference of exactly zero for the wrong reason — the
    /// experiment therefore runs once at par (reporting that null result and its cause) and once at
    /// the multiple where the build actually reaches phase 3.
    /// </remarks>
    /// <param name="runner">The sweep runner.</param>
    /// <param name="dataRoot">The <c>game-data</c> root the override is layered over.</param>
    /// <param name="tier">The tier to measure at.</param>
    /// <param name="fights">Fights per arm. The two arms share seeds, so the comparison is paired.</param>
    /// <param name="multipleFor">
    /// The multiple of par for a <c>(chapter, archetype)</c>. Pass <c>(_, _) =&gt; 1.0</c> for the par
    /// arm; pass a <see cref="Diagnostics.ShortfallLookup"/>'s for the phase-reaching arm.
    /// </param>
    /// <param name="multipleLabel">How the multiple is described in the title, e.g. <c>"1.00"</c>.</param>
    public static ExperimentComparison Rage(
        SweepRunner runner,
        string dataRoot,
        Tier tier,
        int fights,
        Func<int, string, double> multipleFor,
        string multipleLabel)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(multipleFor);

        var shipped = BossDocumentOverrides.ReadShipped(dataRoot);
        var withoutRage = GameDataLoader.LoadWith(
            dataRoot,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [BossDocumentOverrides.DocumentPath] =
                    BossDocumentOverrides.WithoutEffect(shipped, ThornmawScriptId, ThornmawRageEffectId),
            });

        var chapter = runner.Bosses.Get(ThornmawScriptId).Chapter
            ?? throw new InvalidOperationException(
                $"{ThornmawScriptId} states no chapter, so it has no par cell to be measured at.");

        var arms = new List<ExperimentArm>();
        foreach (var archetype in runner.Calibration.Archetypes)
        {
            // Same multiple for both arms of one archetype, so the pair differs only in the effect.
            var multiple = multipleFor(chapter, archetype.Id);

            arms.Add(Arm(
                $"{archetype.Id} @{Num(multiple)}x shipped",
                runner.TryRunCell(
                    chapter, tier, archetype.Id, archetype.Stats, fights, heroPowerMultiple: multiple)));
            arms.Add(Arm(
                $"{archetype.Id} @{Num(multiple)}x RAGE removed",
                runner.TryRunCell(
                    chapter, tier, archetype.Id, archetype.Stats, fights, withoutRage, multiple)));
        }

        return new ExperimentComparison(
            $"RAGE hole — {ThornmawScriptId} C{Int(chapter)} {tier} at {multipleLabel} par, " +
            $"shipped vs {ThornmawRageEffectId} removed",
            arms);
    }

    /// <summary><c>addsPowerFraction</c>: every summoning boss at par at 0.25 / 0.30 / 0.35.</summary>
    /// <remarks>
    /// Measures clear-rate and median-duration sensitivity across the band; changes nothing. As with
    /// <see cref="Rage"/>, four of the five summons are <c>ON_PHASE_ENTER</c> phase 2 or 3, so at par
    /// they never fire and all three fractions measure identically — run at par and at the multiple
    /// that reaches the later phases.
    /// </remarks>
    /// <param name="runner">The sweep runner.</param>
    /// <param name="dataRoot">The <c>game-data</c> root the override is layered over.</param>
    /// <param name="tier">The tier to measure at.</param>
    /// <param name="fights">Fights per arm; all three arms share seeds.</param>
    /// <param name="multipleFor">Per-<c>(chapter, archetype)</c> multiple of par. See <see cref="Rage"/>.</param>
    /// <param name="multipleLabel">How the multiple is described in the title.</param>
    public static ExperimentComparison AddsFraction(
        SweepRunner runner,
        string dataRoot,
        Tier tier,
        int fights,
        Func<int, string, double> multipleFor,
        string multipleLabel)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(multipleFor);

        var shipped = BossDocumentOverrides.ReadShipped(dataRoot);
        var summoners = runner.Bosses.Summoners;
        var arms = new List<ExperimentArm>();

        foreach (var fraction in AddsFractionProbes)
        {
            var content = GameDataLoader.LoadWith(
                dataRoot,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [BossDocumentOverrides.DocumentPath] =
                        BossDocumentOverrides.WithAddsPowerFraction(shipped, fraction),
                });

            foreach (var boss in summoners)
            {
                var chapter = boss.Chapter
                    ?? throw new InvalidOperationException(
                        $"{boss.Id} carries addsPowerFraction but states no chapter, so it has no par " +
                        "cell to be measured at.");

                // One archetype per boss keeps this affordable — it is a sensitivity measurement of a
                // boss-side dial, not a per-build sweep. The archetype is the first authored one, so
                // which build it is does not vary across the three probes and the comparison is paired.
                var archetype = runner.Calibration.Archetypes[0];
                var multiple = multipleFor(chapter, archetype.Id);

                arms.Add(Arm(
                    $"{boss.Id} @{Num(multiple)}x frac {Num(fraction)}",
                    runner.TryRunCell(
                        chapter, tier, archetype.Id, archetype.Stats, fights, content, multiple)));
            }
        }

        return new ExperimentComparison(
            $"addsPowerFraction — {Int(summoners.Count)} summoning bosses at {multipleLabel} par " +
            $"({tier}), {string.Join(" / ", AddsFractionProbes.Select(Num))}",
            arms);
    }

    /// <summary>
    /// One arm, or a fault line in its place — an engine fault in one arm must not delete the other
    /// arms' evidence.
    /// </summary>
    private static ExperimentArm Arm(string label, (CellResult? Cell, string? Fault) run) =>
        run.Cell is null
            ? new ExperimentArm($"{label} — ENGINE FAULT: {run.Fault}", double.NaN, double.NaN,
                double.NaN, double.NaN, 0, 0, double.NaN, 0, double.NaN)
            : Arm(label, run.Cell);

    private static ExperimentArm Arm(string label, CellResult cell) =>
        new(label, cell.ClearRate, cell.MedianClearedSeconds, cell.P10ClearedSeconds,
            cell.P90ClearedSeconds, cell.ClearCount, cell.FightCount, MedianAllSeconds(cell),
            cell.MaxBossPhaseReached, cell.Phase3Share);

    /// <summary>
    /// The median over every fight, cleared or not — reported alongside the cleared median because
    /// when nothing clears, the cleared median is <c>NaN</c> for both arms and would hide a real A/B
    /// difference. Not what guardrails 3 and 4 are stated over.
    /// </summary>
    private static double MedianAllSeconds(CellResult cell)
    {
        var all = cell.Fights.Select(f => f.Seconds).ToArray();
        Array.Sort(all);

        return CellResult.Percentile(all, 0.50);
    }

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>One experiment's arms, ready to print.</summary>
/// <param name="Title">What was varied.</param>
/// <param name="Arms">One per variant.</param>
public sealed record ExperimentComparison(string Title, IReadOnlyList<ExperimentArm> Arms);

/// <summary>One arm of an experiment.</summary>
/// <param name="Label">Which variant.</param>
/// <param name="ClearRate">Cleared ÷ fought.</param>
/// <param name="MedianClearedSeconds">Median over cleared fights, <c>NaN</c> when nothing cleared.</param>
/// <param name="P10ClearedSeconds">p10 over cleared fights.</param>
/// <param name="P90ClearedSeconds">p90 over cleared fights.</param>
/// <param name="Clears">How many cleared.</param>
/// <param name="Fights">How many ran.</param>
/// <param name="MedianAllSeconds">Median over ALL fights — see the remarks on the producer.</param>
/// <param name="MaxBossPhase">The highest boss phase any fight in the arm reached.</param>
/// <param name="Phase3Share">The share of fights that reached phase 3.</param>
public sealed record ExperimentArm(
    string Label,
    double ClearRate,
    double MedianClearedSeconds,
    double P10ClearedSeconds,
    double P90ClearedSeconds,
    int Clears,
    int Fights,
    double MedianAllSeconds,
    int MaxBossPhase,
    double Phase3Share)
{
    /// <summary>The arm as one report line.</summary>
    public override string ToString() =>
        $"{Label,-34} clearRate={(ClearRate * 100).ToString("0.00", CultureInfo.InvariantCulture),7}% " +
        $"({Clears.ToString(CultureInfo.InvariantCulture)}/{Fights.ToString(CultureInfo.InvariantCulture)})  " +
        $"medianCleared={Fmt(MedianClearedSeconds),8}  medianAll={Fmt(MedianAllSeconds),8}  " +
        $"p10={Fmt(P10ClearedSeconds),8} p90={Fmt(P90ClearedSeconds),8}  " +
        $"maxPhase={MaxBossPhase.ToString(CultureInfo.InvariantCulture)} " +
        $"phase3={(Phase3Share * 100).ToString("0.0", CultureInfo.InvariantCulture)}%";

    private static string Fmt(double seconds) =>
        double.IsNaN(seconds) ? "n/a" : seconds.ToString("0.00", CultureInfo.InvariantCulture) + "s";
}
