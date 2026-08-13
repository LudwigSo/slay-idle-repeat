using System.Globalization;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.BalanceHarness.Sweep;

namespace SlayIdleRepeat.BalanceHarness.Diagnostics;

/// <summary>
/// ⚠️ <b>DIAGNOSIS, NOT ASSERTION.</b> How far a build's power has to move from
/// <c>ParPower(c, t)</c> before it reaches `05` §9's 70% clear-rate target — the quantitative form of
/// "guardrail 1 failed, and by how much".
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>This changes nothing and recommends nothing.</b> `21` §3.2 makes retuning a design decision;
/// the harness reports evidence. What this produces is a number — <em>"ARCH_CRIT reaches 70% at
/// 4.6 × par in Chapter 1 Normal"</em> — which is exactly the evidence a design decision about the
/// par table, the boss coefficients or `02` §4.3's stage multiplier needs, and which a bare
/// <em>"clear rate 0%"</em> does not give.
/// </para>
/// <para>
/// It is also what makes the guardrail-6 elasticity table informative. At par nothing clears, so
/// every stat's Δ clear rate is identically 0 and a table of zeros says nothing about whether THORNS
/// matters in a real fight. Measured at the multiple where the build is near the target, a stat that
/// moves the fight moves the number.
/// </para>
/// <para>
/// The search is a bisection on the hero's power multiple, which is monotone in clear rate up to
/// sampling noise; the sample size is small on purpose, so the answer is a <b>bracket</b> and is
/// reported to two decimal places rather than as a precise figure.
/// </para>
/// </remarks>
public static class ClearRateCalibration
{
    /// <summary>The lowest multiple of par the search considers.</summary>
    public const double MinMultiple = 0.25;

    /// <summary>The highest multiple of par the search considers.</summary>
    public const double MaxMultiple = 64.0;

    /// <summary>Bisection steps. 12 halvings of [0.25, 64] resolve the multiple to about 0.02.</summary>
    public const int Steps = 12;

    /// <summary>
    /// Finds the multiple of <c>ParPower(c, t)</c> at which a build's boss clear rate first reaches
    /// <paramref name="targetClearRate"/>.
    /// </summary>
    /// <remarks>
    /// 🔴 A step the engine cannot simulate ENDS the search rather than killing the run, and the probe
    /// comes back carrying the fault. Chapter 8's <c>BOSS_DICELORD_P3_ALL_IN</c> is the live case: the
    /// search raises the hero's power until the fight reaches boss phase 3, and phase 3 is exactly
    /// where that script faults. Reporting "the shortfall could not be measured, because the engine
    /// faults above N × par" is a finding; crashing is not.
    /// </remarks>
    public static ShortfallProbe Find(
        SweepRunner runner,
        int chapter,
        Tier tier,
        BuildArchetype archetype,
        double targetClearRate,
        int fightsPerStep)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(archetype);

        var low = MinMultiple;
        var high = MaxMultiple;
        var multiple = high;
        var rate = 0.0;

        for (var i = 0; i < Steps; i++)
        {
            multiple = (low + high) / 2.0;

            var (cell, fault) = runner.TryRunCell(
                chapter, tier, archetype.Id, archetype.Stats, fightsPerStep,
                heroPowerMultiple: multiple);

            if (cell is null)
            {
                return new ShortfallProbe(
                    chapter, tier, archetype.Id, multiple, rate, targetClearRate, fightsPerStep, fault);
            }

            rate = cell.ClearRate;

            if (rate < targetClearRate)
            {
                low = multiple;
            }
            else
            {
                high = multiple;
            }
        }

        return new ShortfallProbe(
            chapter, tier, archetype.Id, multiple, rate, targetClearRate, fightsPerStep, null);
    }
}

/// <summary>⚠️ One diagnostic probe. Evidence for a design decision; not a guardrail.</summary>
/// <param name="Chapter">The chapter.</param>
/// <param name="Tier">The tier.</param>
/// <param name="ArchetypeId">The build.</param>
/// <param name="Multiple">The multiple of <c>ParPower(c, t)</c> the search converged on.</param>
/// <param name="ClearRateAtMultiple">The clear rate measured there.</param>
/// <param name="TargetClearRate">What was being searched for.</param>
/// <param name="FightsPerStep">Sample size per bisection step — small, so this is a bracket.</param>
/// <param name="Fault">🔴 The engine fault that ended the search early, or <c>null</c>.</param>
public sealed record ShortfallProbe(
    int Chapter,
    Tier Tier,
    string ArchetypeId,
    double Multiple,
    double ClearRateAtMultiple,
    double TargetClearRate,
    int FightsPerStep,
    string? Fault)
{
    /// <summary>The probe as one report line.</summary>
    public override string ToString() =>
        Fault is not null
            ? $"C{Chapter.ToString(CultureInfo.InvariantCulture)} {Tier,-6} {ArchetypeId,-18} " +
              $"NOT MEASURABLE — the engine faults at " +
              $"{Multiple.ToString("0.00", CultureInfo.InvariantCulture)} x par: {Fault}"
            : $"C{Chapter.ToString(CultureInfo.InvariantCulture)} {Tier,-6} {ArchetypeId,-18} reaches " +
              $"{(TargetClearRate * 100).ToString("0", CultureInfo.InvariantCulture)}% at " +
              $"{Multiple.ToString("0.00", CultureInfo.InvariantCulture)} x par " +
              $"(measured {(ClearRateAtMultiple * 100).ToString("0.0", CultureInfo.InvariantCulture)}% over " +
              $"{FightsPerStep.ToString(CultureInfo.InvariantCulture)} fights)";
}
