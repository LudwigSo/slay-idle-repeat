using System.Globalization;
using SlayIdleRepeat.BalanceHarness.Rules;
using SlayIdleRepeat.BalanceHarness.Sweep;

namespace SlayIdleRepeat.BalanceHarness.Guardrails;

/// <summary>
/// 🔒 `05` §9's guardrails 1, 3 and 4, and `17` §1's Sporequeen band — the four assertions stated
/// over the sweep's measured fights.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Guardrail 2 is not here and must not be added here.</b> `05` §9's second guardrail is about
/// perk draft viability, which needs M3-07's perk content; <c>content/perks/</c> is empty and
/// <c>calibration_builds.json</c>'s <c>frozenPerks</c> name ids that do not resolve. It is M2-16b's.
/// A stubbed or vacuous version would report a green guardrail number that nothing measured.
/// </para>
/// <para>
/// 🔒 <b>Guardrails 3 and 4 are asserted on the cell's MEDIAN cleared duration, and the raw tails are
/// reported beside it.</b> The statistic is a harness decision and it is stated here rather than
/// buried: `17` §1 writes the floor, the ceiling and the 35-60 s band in one sentence
/// (<em>"Duration — 35-60 s at par power. Balance guardrail: never below 12 s, never above 70 s"</em>),
/// and the band is unambiguously a central statistic — a 10 000-fight sample of a well-tuned boss will
/// always contain <em>some</em> fast roll. Asserting the floor on the sample minimum would make
/// guardrail 3 a test of the tail's thickness rather than of the tuning. The per-cell counts of clears
/// under 12 s and over 70 s are carried in <see cref="CellResult.ClearsUnderHardFloor"/> and
/// <see cref="CellResult.ClearsOverHardCeiling"/> and printed in the report, so the tail is visible
/// and nothing is hidden by the choice.
/// </para>
/// </remarks>
public static class SweepGuardrails
{
    /// <summary>🔒 `05` §9 guardrail 1 — clear rate at par, per <c>(chapter, tier, archetype)</c>.</summary>
    /// <param name="sweep">The measured cells.</param>
    /// <param name="minimum">🔒 <c>par_power.json#/clearRateAtPar/assertionMin</c>, authored 0.62.</param>
    /// <param name="maximum">🔒 <c>par_power.json#/clearRateAtPar/assertionMax</c>, authored 0.78.</param>
    /// <remarks>
    /// ⚠️ <b>Scope: <em>"clears (c, t)"</em> is measured over the chapter's BOSS FIGHT at par.</b> The
    /// full-run definition needs M3's board layer — <c>game-data/content/chapters/</c> is empty and
    /// node composition is unauthored — and inventing an encounter ladder to walk is exactly the
    /// harness-side invention `05` §9 forbids. The measured rate is therefore an <b>upper bound</b> on
    /// a true run clear rate: a real run must survive `03` §1.1's forty-two spine nodes before it
    /// reaches this fight, and every one of them can only lower the number.
    /// </remarks>
    public static GuardrailResult ClearRateAtPar(
        IReadOnlyList<CellResult> sweep, double minimum, double maximum)
    {
        ArgumentNullException.ThrowIfNull(sweep);

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
                "Clear rate at par in [62%, 78%] per (chapter, tier, archetype)",
                GuardrailVerdict.Inconclusive,
                "no cells were swept, so nothing was measured",
                details,
                0);
        }

        var lowest = sweep.MinBy(c => c.ClearRate)!;
        var highest = sweep.MaxBy(c => c.ClearRate)!;

        return new GuardrailResult(
            1,
            "Clear rate at par in [62%, 78%] per (chapter, tier, archetype)",
            breaches == 0 ? GuardrailVerdict.Pass : GuardrailVerdict.Fail,
            $"{Int(breaches)}/{Int(sweep.Count)} cells outside [{Pct(minimum)}, {Pct(maximum)}]; " +
            $"lowest {Pct(lowest.ClearRate)} at {lowest.Key}, highest {Pct(highest.ClearRate)} at {highest.Key}",
            details,
            sweep.Count);
    }

    /// <summary>🔒 `05` §9 guardrail 3 — no build clears a boss below 12 s at par.</summary>
    public static GuardrailResult HardFloor(IReadOnlyList<CellResult> sweep) =>
        DurationBound(
            3,
            $"No build clears a boss below {Sec(DurationBands.HardFloorSeconds)} at par",
            sweep,
            cell => cell.MedianClearedSeconds < DurationBands.HardFloorSeconds,
            below: true);

    /// <summary>🔒 `05` §9 guardrail 4 — no build needs more than 70 s for a boss at par.</summary>
    public static GuardrailResult HardCeiling(IReadOnlyList<CellResult> sweep) =>
        DurationBound(
            4,
            $"No build needs more than {Sec(DurationBands.HardCeilingSeconds)} for a boss at par",
            sweep,
            cell => cell.MedianClearedSeconds > DurationBands.HardCeilingSeconds,
            below: false);

    /// <summary>
    /// 🔒 `17` §1's kickoff assertion — Sporequeen Vell's median duration at par sits in 35-60 s.
    /// </summary>
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

        // 🔒 Empty is INCONCLUSIVE, not pass. See GuardrailVerdict.Inconclusive: "every clear is above
        // 12 s" is vacuously true of zero clears, and on the shipped data that is every cell.
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
