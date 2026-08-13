namespace SlayIdleRepeat.BalanceHarness.Model;

/// <summary>
/// 🔒 The ONE restatement of <c>SlayIdleRepeat.Core.Primitives.DeterminismRounding.Round</c> inside
/// <c>tools/BalanceHarness</c>. Nothing in this tool rounds any other way.
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
/// </remarks>
public static class HarnessRounding
{
    /// <summary>`05` §1.1 — the number of decimal places every accumulated value carries.</summary>
    public const int Decimals = 4;

    /// <summary>Rounds one value the way every <c>double</c> that reaches <c>Core</c> must be rounded.</summary>
    public static double Round(double value) =>
        Math.Round(value, Decimals, MidpointRounding.ToEven) + 0.0;
}
