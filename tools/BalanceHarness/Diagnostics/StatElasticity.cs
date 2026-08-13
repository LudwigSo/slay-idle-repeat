using System.Globalization;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.BalanceHarness.Sweep;
using SlayIdleRepeat.BalanceHarness.Guardrails;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.BalanceHarness.Diagnostics;

/// <summary>
/// ⚠️ <b>DIAGNOSIS, NOT ASSERTION.</b> What each stat is worth in an <em>actual boss fight</em>,
/// measured by bumping it and re-running real simulations.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>This is the counterweight to guardrail 6, and it is the whole reason guardrail 6's failure is
/// a finding about the power model rather than about the game.</b> `29` §2.3 has no term for
/// <see cref="StatId.THORNS"/> or <see cref="StatId.HEAL_PCT"/>, so their <em>marginal power</em> is
/// exactly zero. Whether they matter in a fight is a different question, and only the simulator can
/// answer it: THORNS reflects damage in `05` §4.2 and HEAL_PCT multiplies every heal received. This
/// table asks the simulator.
/// </para>
/// <para>
/// 🔒 <b>Measured at the multiple of par where the build is near the clear-rate target, not at par.</b>
/// On the shipped data every cell clears 0% at par, and at 0% every Δ clear rate is identically zero:
/// the table would be fourteen zeros and would prove nothing about any stat. Run where the fight is
/// actually close, a stat that moves the outcome moves the number. The multiple used is stated in the
/// report beside the table.
/// </para>
/// <para>
/// 🔒 <b>Paired seeds.</b> The bumped and unbumped runs use identical <c>battleSeed</c>s (see
/// <see cref="SweepSeeds"/>, which takes no variant), so the Δ is the stat and not the sample.
/// </para>
/// <para>
/// 🔒 <b>What this measures is "what the power model fails to price", and the sign convention follows
/// from that.</b> The bumped statline is placed at the <b>same</b> power target as the baseline, so a
/// stat the model already prices correctly buys nothing: the scalar simply shrinks to pay for it and
/// Δ clear rate lands on zero within noise. A stat the model <em>cannot see</em> — THORNS, HEAL_PCT —
/// costs nothing in the scalar, so whatever it is worth in the fight shows up undiluted. A measurable
/// negative Δ means the model <em>overprices</em> the stat.
/// </para>
/// <para>
/// ⚠️ <b>The step here is deliberately MUCH larger than <see cref="MarginalPowerGuardrail.RelativeStep"/>,
/// and that is a definitional choice with no authored basis.</b> A 1% bump moves a clear rate by far
/// less than the standard error of any sample size this harness can afford — measured at 400 fights,
/// every row of a 1%-step table came back inside ±2.8 pp of zero, which is noise. `05` §9 states no
/// step for either purpose. The ranking uses 1% because it is a derivative; this table uses
/// <see cref="DiagnosticRelativeStep"/> because it is an experiment and has to clear the noise floor.
/// The two numbers are therefore NOT comparable to each other, and the report says so.
/// </para>
/// </remarks>
public static class StatElasticity
{
    /// <summary>⚠️ Harness definition — the diagnostic step, as a fraction of the archetype's own value.</summary>
    public const double DiagnosticRelativeStep = 0.25;

    /// <summary>⚠️ Harness definition — the diagnostic step for a stat the archetype holds at 0.</summary>
    public const double DiagnosticAbsoluteProbeForZero = 0.25;

    /// <summary>Measures Δ clear rate for every combat stat at one cell.</summary>
    /// <param name="runner">The sweep runner.</param>
    /// <param name="chapter">One representative chapter — this is a diagnosis and stays cheap.</param>
    /// <param name="tier">The tier.</param>
    /// <param name="archetype">The build whose stats are bumped.</param>
    /// <param name="powerMultiple">The multiple of par to measure at. See the remarks.</param>
    /// <param name="fights">Fights per row. Every row costs this many simulations.</param>
    public static ElasticityTable Measure(
        SweepRunner runner,
        int chapter,
        Tier tier,
        BuildArchetype archetype,
        double powerMultiple,
        int fights)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(archetype);

        var baseline = runner.RunCell(
            chapter, tier, archetype.Id, archetype.Stats, fights, heroPowerMultiple: powerMultiple);

        var rows = new List<ElasticityRow>(StatIds.Combat.Count);

        foreach (var stat in StatIds.Combat)
        {
            var current = archetype.Stats[stat];
            var usedAbsolute = current == 0.0;
            var step = usedAbsolute ? DiagnosticAbsoluteProbeForZero : current * DiagnosticRelativeStep;

            // 🔒 The bumped statline is scaled to the SAME power target as the baseline, so the row
            // measures what the stat is worth AT A FIXED POWER BUDGET rather than measuring "more
            // stats win more" — which would be true of every stat and would say nothing.
            var bumped = runner.RunCell(
                chapter,
                tier,
                archetype.Id,
                archetype.Stats.With(stat, current + step),
                fights,
                heroPowerMultiple: powerMultiple);

            rows.Add(new ElasticityRow(
                stat,
                step,
                usedAbsolute,
                bumped.ClearRate - baseline.ClearRate,
                bumped.MedianClearedSeconds,
                baseline.MedianClearedSeconds));
        }

        return new ElasticityTable(
            chapter, tier, archetype.Id, powerMultiple, fights, baseline.ClearRate,
            rows.OrderByDescending(r => r.DeltaClearRate).ThenBy(r => r.Stat).ToArray());
    }
}

/// <summary>⚠️ One diagnostic elasticity table.</summary>
/// <param name="Chapter">The chapter measured.</param>
/// <param name="Tier">The tier.</param>
/// <param name="ArchetypeId">The build.</param>
/// <param name="PowerMultiple">The multiple of par it was measured at.</param>
/// <param name="Fights">Fights per row.</param>
/// <param name="BaselineClearRate">The unbumped clear rate.</param>
/// <param name="Rows">Descending by Δ clear rate.</param>
public sealed record ElasticityTable(
    int Chapter,
    Tier Tier,
    string ArchetypeId,
    double PowerMultiple,
    int Fights,
    double BaselineClearRate,
    IReadOnlyList<ElasticityRow> Rows);

/// <summary>⚠️ One stat's measured effect on a real fight.</summary>
/// <param name="Stat">The stat bumped.</param>
/// <param name="Step">By how much.</param>
/// <param name="UsedAbsoluteProbe">True when the archetype holds it at 0 and the labelled absolute probe was used.</param>
/// <param name="DeltaClearRate">Bumped clear rate minus baseline clear rate.</param>
/// <param name="BumpedMedianSeconds">Median cleared duration with the bump.</param>
/// <param name="BaselineMedianSeconds">Median cleared duration without it.</param>
public sealed record ElasticityRow(
    StatId Stat,
    double Step,
    bool UsedAbsoluteProbe,
    double DeltaClearRate,
    double BumpedMedianSeconds,
    double BaselineMedianSeconds)
{
    /// <summary>The row as one report line.</summary>
    public override string ToString() =>
        $"{Stat,-9}{(UsedAbsoluteProbe ? "*" : " ")} step={Step.ToString("0.####", CultureInfo.InvariantCulture),-10} " +
        $"dClearRate={(DeltaClearRate * 100).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture),8}pp  " +
        $"median {Fmt(BaselineMedianSeconds)} -> {Fmt(BumpedMedianSeconds)}";

    private static string Fmt(double seconds) =>
        double.IsNaN(seconds) ? "n/a" : seconds.ToString("0.00", CultureInfo.InvariantCulture) + "s";
}
