using System.Globalization;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.BalanceHarness.Sweep;
using SlayIdleRepeat.BalanceHarness.Guardrails;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.BalanceHarness.Diagnostics;

/// <summary>
/// Diagnosis, not assertion: what each stat is worth in an actual boss fight, measured by bumping it
/// and re-running real simulations — the counterweight to guardrail 6's power-model math, for stats
/// (like <see cref="StatId.THORNS"/> or <see cref="StatId.HEAL_PCT"/>) the model assigns zero marginal
/// power to but that still act in a fight.
/// </summary>
/// <remarks>
/// Measured near the clear-rate target rather than at par, where every cell clears 0% and every Δ
/// would be identically zero. Bumped and unbumped runs share a <c>battleSeed</c> (see
/// <see cref="SweepSeeds"/>) so the Δ is the stat and not sampling noise. The bumped statline is scaled
/// to the SAME power target as the baseline, so a stat the model already prices correctly buys
/// nothing and lands near zero, while a stat the model cannot see shows up undiluted — a measurable
/// negative Δ means the model overprices the stat. The step is deliberately much larger than
/// <see cref="MarginalPowerGuardrail.RelativeStep"/>: a 1% bump is inside the noise floor of any
/// affordable sample, so the two numbers are not comparable to each other.
/// </remarks>
public static class StatElasticity
{
    /// <summary>Harness definition — the diagnostic step, as a fraction of the archetype's own value.</summary>
    public const double DiagnosticRelativeStep = 0.25;

    /// <summary>Harness definition — the diagnostic step for a stat the archetype holds at 0.</summary>
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

            // Scaled to the same power target as the baseline, so this measures what the stat is
            // worth at a fixed power budget rather than "more stats win more".
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

/// <summary>One diagnostic elasticity table.</summary>
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

/// <summary>One stat's measured effect on a real fight.</summary>
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
