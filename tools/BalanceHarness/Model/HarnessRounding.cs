namespace SlayIdleRepeat.BalanceHarness.Model;

/// <summary>
/// 🔒 The ONE restatement of <c>SlayIdleRepeat.Core.Primitives.DeterminismRounding.Round</c> inside
/// <c>tools/BalanceHarness</c> — the only place `05` §1.1's <b>4</b>-dp accumulation rounding is
/// written out here.
/// </summary>
/// <remarks>
/// <para>
/// <c>DeterminismRounding</c> is <c>internal</c> to <c>SlayIdleRepeat.Core</c> and the harness is not
/// the assembly <c>InternalsVisibleTo</c> names, so the expression has to be written out once here.
/// <c>DeterminismRoundingRuleTests</c>' own remarks name <c>tools/BalanceHarness</c> as deliberately
/// outside its scanned assemblies for exactly this case.
/// </para>
/// <para>
/// 🔒 <b>All three parts matter and none is decoration.</b> Four decimal places is `05` §1.1's
/// accumulation-point rounding; <see cref="MidpointRounding.ToEven"/> is written out because it is
/// <c>Math.Round</c>'s default and a reader must not have to know that; and the trailing
/// <c>+ 0.0</c> normalises <c>-0.0</c> to <c>+0.0</c>, because
/// <c>ActorStats.From</c> <b>rejects</b> a negative zero and a scaled stat that drifts a hair below
/// zero would otherwise blow up the sweep 40 minutes in.
/// </para>
/// <para>
/// ⚠️ This is a restatement, not a second rule. If `05` §1.1's four places ever move, both this and
/// <c>DeterminismRounding.Decimals</c> move together — the duplication is on the record in the
/// M2-16a report.
/// </para>
/// <para>
/// 🔴 <b>One other site in this tool writes the same three-part expression, and cannot route through
/// <see cref="Round"/>.</b> <c>LoadoutScaling.ToPowerIndex</c> rounds `29` §2.5.3's scaling scalar
/// <c>s</c> to <c>scalarDecimalPlaces</c> — an <b>authored</b> precision read off
/// <c>tuning/calibration_builds.json</c>, which happens to be 4 today but is a data value this tool
/// must honour rather than assume. It is a different rule at a data-supplied precision, not a second
/// copy of `05` §1.1's, so it is spelled out there on purpose. Those two are the whole set: nothing
/// else in <c>tools/BalanceHarness</c> rounds.
/// </para>
/// </remarks>
public static class HarnessRounding
{
    /// <summary>`05` §1.1 — the number of decimal places every accumulated value carries.</summary>
    public const int Decimals = 4;

    /// <summary>Rounds one value the way every <c>double</c> that reaches <c>Core</c> must be rounded.</summary>
    public static double Round(double value) =>
        Math.Round(value, Decimals, MidpointRounding.ToEven) + 0.0;
}
