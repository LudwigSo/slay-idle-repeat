using System.Globalization;
using SlayIdleRepeat.BalanceHarness.Rules;
using SlayIdleRepeat.BalanceHarness.Sweep;

namespace SlayIdleRepeat.BalanceHarness.Guardrails;

/// <summary>
/// Guardrails 1, 3 and 4, and the Sporequeen band — the four assertions stated over the sweep's
/// measured fights.
/// </summary>
/// <remarks>
/// Guardrail 2 is not here and must not be added here: it's about perk draft viability, which needs
/// perk content this milestone doesn't have (<c>content/perks/</c> is empty); a stubbed version would
/// report a green guardrail number that nothing measured. Guardrails 3 and 4 are asserted on the
/// cell's median cleared duration, not the sample minimum/maximum: a 10 000-fight sample of a
/// well-tuned boss will always contain some fast roll, so asserting on the extreme would make these a
/// test of tail thickness rather than of the tuning. The raw tail counts
/// (<see cref="CellResult.ClearsUnderHardFloor"/>/<see cref="CellResult.ClearsOverHardCeiling"/>) are
/// still printed beside it, so nothing is hidden by the choice.
/// </remarks>
public static class SweepGuardrails
{
    /// <summary>Guardrail 1 — clear rate at par, per <c>(chapter, tier, archetype)</c>.</summary>
    /// <param name="sweep">The measured cells.</param>
    /// <param name="minimum"><c>par_power.json#/clearRateAtPar/assertionMin</c>, authored 0.62.</param>
    /// <param name="maximum"><c>par_power.json#/clearRateAtPar/assertionMax</c>, authored 0.78.</param>
    /// <remarks>
    /// Measured over the chapter's boss fight at par only, not a full run: the board layer needed to
    /// walk a real run isn't authored yet, and inventing an encounter ladder isn't this harness's call
    /// to make. The measured rate is therefore an upper bound — a real run must also survive the spine
    /// nodes before it reaches this fight, which can only lower the true number.
    /// </remarks>
    public static GuardrailResult ClearRateAtPar(
        IReadOnlyList<CellResult> sweep, double minimum, double maximum)
    {
        ArgumentNullException.ThrowIfNull(sweep);

        // The band in the name is the band that was actually asserted (a parameter), not a hardcoded
        // transcription that could go stale after a retune.
        var name = $"Clear rate at par in [{Pct(minimum)}, {Pct(maximum)}] per (chapter, tier, archetype)";
        var details = new List<string>();
        var breaches = 0;

        foreach (var cell in sweep.OrderBy(c => c.ClearRate))
        {
            var breached = cell.ClearRate < minimum || cell.ClearRate > maximum;
            if (breached)
            {
                breaches++;
            }

            details.Add(
                $"{(breached ? "BREACH" : "  ok  ")} {cell.Key,-30} clearRate=" +
                $"{Pct(cell.ClearRate)} ({Int(cell.ClearCount)}/{Int(cell.FightCount)}) band=" +
                $"[{Pct(minimum)}, {Pct(maximum)}]");
        }

        if (sweep.Count == 0)
        {
            return new GuardrailResult(
                1,
                name,
                GuardrailVerdict.Inconclusive,
                "no cells were swept, so nothing was measured",
                details,
                0);
        }

        var lowest = sweep.MinBy(c => c.ClearRate)!;
        var highest = sweep.MaxBy(c => c.ClearRate)!;

        return new GuardrailResult(
            1,
            name,
            breaches == 0 ? GuardrailVerdict.Pass : GuardrailVerdict.Fail,
            $"{Int(breaches)}/{Int(sweep.Count)} cells outside [{Pct(minimum)}, {Pct(maximum)}]; " +
            $"lowest {Pct(lowest.ClearRate)} at {lowest.Key}, highest {Pct(highest.ClearRate)} at {highest.Key}",
            details,
            sweep.Count);
    }

    /// <summary>Guardrail 3 — no build clears a boss below 12 s at par.</summary>
    public static GuardrailResult HardFloor(IReadOnlyList<CellResult> sweep) =>
        DurationBound(
            3,
            $"No build clears a boss below {Sec(DurationBands.HardFloorSeconds)} at par",
            sweep,
            cell => cell.MedianClearedSeconds < DurationBands.HardFloorSeconds,
            below: true);

    /// <summary>Guardrail 4 — no build needs more than 70 s for a boss at par.</summary>
    public static GuardrailResult HardCeiling(IReadOnlyList<CellResult> sweep) =>
        DurationBound(
            4,
            $"No build needs more than {Sec(DurationBands.HardCeilingSeconds)} for a boss at par",
            sweep,
            cell => cell.MedianClearedSeconds > DurationBands.HardCeilingSeconds,
            below: false);

    /// <summary>Sporequeen Vell's median duration at par sits in 35-60 s.</summary>
    /// <param name="sweep">The measured cells; the Chapter 7 ones are selected by boss id.</param>
    /// <remarks>
    /// Selected on <c>BOSS_SPOREQUEEN_VELL</c> rather than on <c>chapter == 7</c>, so a sweep that
    /// silently fought the wrong script cannot satisfy this by accident.
    /// </remarks>
    public static GuardrailResult SporequeenBand(IReadOnlyList<CellResult> sweep)
    {
        ArgumentNullException.ThrowIfNull(sweep);

        const string bossId = "BOSS_SPOREQUEEN_VELL";
        var cells = sweep.Where(c => string.Equals(c.BossId, bossId, StringComparison.Ordinal)).ToArray();
        var details = new List<string>();
        var measured = 0;
        var breaches = 0;

        foreach (var cell in cells)
        {
            if (cell.ClearCount == 0)
            {
                details.Add($" NO DATA {cell.Key,-30} 0 of {Int(cell.FightCount)} fights cleared");
                continue;
            }

            measured++;
            var median = cell.MedianClearedSeconds;
            var breached = median < DurationBands.ParMinSeconds || median > DurationBands.ParMaxSeconds;
            if (breached)
            {
                breaches++;
            }

            details.Add(
                $"{(breached ? "BREACH" : "  ok  ")} {cell.Key,-30} median={Sec(median)} band=" +
                $"[{Sec(DurationBands.ParMinSeconds)}, {Sec(DurationBands.ParMaxSeconds)}] " +
                $"p10={Sec(cell.P10ClearedSeconds)} p90={Sec(cell.P90ClearedSeconds)} " +
                $"clears={Int(cell.ClearCount)}/{Int(cell.FightCount)}");
        }

        var name =
            $"{bossId} median duration at par in [{Sec(DurationBands.ParMinSeconds)}, " +
            $"{Sec(DurationBands.ParMaxSeconds)}]";

        if (cells.Length == 0)
        {
            return new GuardrailResult(
                0, name, GuardrailVerdict.Inconclusive,
                $"{bossId} was not among the swept bosses at all", details, 0);
        }

        if (measured == 0)
        {
            return new GuardrailResult(
                0, name, GuardrailVerdict.Inconclusive,
                $"none of the {Int(cells.Length)} {bossId} cells cleared a single fight at par, so " +
                "there is no median to place in the band",
                details,
                0);
        }

        return new GuardrailResult(
            0,
            name,
            breaches == 0 ? GuardrailVerdict.Pass : GuardrailVerdict.Fail,
            $"{Int(breaches)}/{Int(measured)} measurable cells outside the band " +
            $"({Int(cells.Length - measured)} cells cleared nothing)",
            details,
            measured);
    }

    private static GuardrailResult DurationBound(
        int number,
        string name,
        IReadOnlyList<CellResult> sweep,
        Func<CellResult, bool> isBreach,
        bool below)
    {
        ArgumentNullException.ThrowIfNull(sweep);

        var details = new List<string>();
        var measured = 0;
        var breaches = 0;

        foreach (var cell in sweep)
        {
            if (cell.ClearCount == 0)
            {
                details.Add($" NO DATA {cell.Key,-30} 0 of {Int(cell.FightCount)} fights cleared");
                continue;
            }

            measured++;
            var breached = isBreach(cell);
            if (breached)
            {
                breaches++;
            }

            details.Add(
                $"{(breached ? "BREACH" : "  ok  ")} {cell.Key,-30} median={Sec(cell.MedianClearedSeconds)} " +
                $"min={Sec(cell.MinClearedSeconds)} max={Sec(cell.MaxClearedSeconds)} " +
                $"under12s={Int(cell.ClearsUnderHardFloor)} over70s={Int(cell.ClearsOverHardCeiling)} " +
                $"clears={Int(cell.ClearCount)}/{Int(cell.FightCount)}");
        }

        // Empty is Inconclusive, not pass — see GuardrailVerdict.Inconclusive.
        if (measured == 0)
        {
            return new GuardrailResult(
                number,
                name,
                GuardrailVerdict.Inconclusive,
                $"none of the {Int(sweep.Count)} swept cells cleared a single fight at par, so there " +
                "is no cleared duration to bound. An empty set satisfies this assertion vacuously and " +
                "is deliberately NOT reported as a pass",
                details,
                0);
        }

        var extreme = below
            ? sweep.Where(c => c.ClearCount > 0).MinBy(c => c.MedianClearedSeconds)!
            : sweep.Where(c => c.ClearCount > 0).MaxBy(c => c.MedianClearedSeconds)!;

        return new GuardrailResult(
            number,
            name,
            breaches == 0 ? GuardrailVerdict.Pass : GuardrailVerdict.Fail,
            $"{Int(breaches)}/{Int(measured)} measurable cells breached; " +
            $"{(below ? "fastest" : "slowest")} median {Sec(extreme.MedianClearedSeconds)} at " +
            $"{extreme.Key} ({Int(sweep.Count - measured)} cells cleared nothing)",
            details,
            measured);
    }

    private static string Pct(double value) =>
        (value * 100.0).ToString("0.00", CultureInfo.InvariantCulture) + "%";

    private static string Sec(double value) =>
        double.IsNaN(value) ? "n/a" : value.ToString("0.00", CultureInfo.InvariantCulture) + "s";

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
}
