using System.Globalization;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.BalanceHarness.Rules;

/// <summary>
/// 🔒 `29` §2.5.3's scaling rule, verbatim: <em>"To place a loadout at power <c>P</c>, multiply
/// <c>maxHp</c>, <c>atk</c>, <c>def</c> by a single scalar <c>s</c> (ratio stats unchanged), solving
/// <c>PlayerPower = P</c> by bisection to within 0.1%; round <c>s</c> to 4 dp. When targeting content
/// <c>(c, t)</c>, the hero's <c>Level</c> := <c>EnemyLevel(c, t)</c> (`05` §6.0); otherwise 40."</em>
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b><c>K_POWER</c> cancels.</b> The rule is stated over <c>PlayerPower</c>, which is
/// <c>K_POWER × PowerIndex</c>, and <c>kPower</c> is authored <c>null</c>. Solving
/// <c>PowerIndex(scaled, level) = P / K_POWER</c> with <see cref="DerivedKPower"/>'s derived constant
/// is the identical equation, and the derived constant is itself
/// <c>1000 / PowerIndex(referenceParBuild)</c> — so the whole thing reduces to a ratio of two power
/// indices and no absolute scale is ever needed.
/// </para>
/// <para>
/// 🔒 <b>Bisection is sound here because <c>PowerIndex</c> is strictly increasing in the scalar.</b>
/// <c>DPS</c> is linear in <c>ATK</c> and <c>EffectiveHP</c> is <c>MAX_HP / (1 − mitigation(DEF))</c>
/// with mitigation increasing in <c>DEF</c>; both grow with <c>s</c>, and the geometric mean of two
/// increasing positive functions increases. `29` §2.1's rejected additive form would <em>not</em>
/// have this property against a stat that only feeds one term, which is one reason the rule names
/// bisection rather than a closed form.
/// </para>
/// <para>
/// ⚠️ <b>The 4-dp rounding is applied after convergence, so the achieved power is not exactly the
/// target.</b> That is the authored rule and not a defect — but it means the achieved-to-target ratio
/// is a real quantity, so <see cref="ScaledLoadout.AchievedRatio"/> carries it and the sweep report
/// prints the worst one. A cell whose hero sits at 1.03 × par is not a cell at par.
/// </para>
/// </remarks>
public static class LoadoutScaling
{
    /// <summary>
    /// The most bisection steps taken before the harness refuses to pretend it converged.
    /// </summary>
    /// <remarks>
    /// A bracket of <c>[lo, hi]</c> halves each step, so 200 steps is far past the point where the
    /// interval is smaller than a <c>double</c>'s spacing — reaching it means the function is not
    /// monotone or the bracket is wrong, and both are bugs that must not be rounded away.
    /// </remarks>
    public const int MaxBisectionSteps = 200;

    /// <summary>The most doublings/halvings used to find a bracket containing the target.</summary>
    public const int MaxBracketSteps = 200;

    /// <summary>
    /// 🔒 Places one loadout at one absolute power target.
    /// </summary>
    /// <param name="loadout">The archetype's authored statline.</param>
    /// <param name="targetPowerIndex">
    /// The right-hand side, in <c>PowerIndex</c> units — <see cref="DerivedKPower.TargetPowerIndex"/>
    /// of <c>ParPower(c, t)</c>.
    /// </param>
    /// <param name="level">
    /// 🔒 `29` §2.5.3 — <c>EnemyLevel(c, t)</c> when targeting content, else
    /// <c>scalingRule.defaultLevel</c>. It matters: `05` §4's mitigation denominator carries
    /// <c>20 × attackerLevel</c>, so the same statline scores differently at level 10 and level 100.
    /// </param>
    /// <param name="content">The loaded snapshot — every weight comes from it.</param>
    /// <param name="scaledStats">`29` §2.5.3's <c>scaledStats</c>, as authored.</param>
    /// <param name="tolerance">`29` §2.5.3's <c>bisectionTolerance</c>, as authored (0.001 = 0.1%).</param>
    /// <param name="scalarDecimalPlaces">`29` §2.5.3's <c>scalarDecimalPlaces</c>, as authored (4).</param>
    /// <exception cref="InvalidOperationException">No bracket could be found in <see cref="MaxBracketSteps"/>.</exception>
    public static ScaledLoadout ToPowerIndex(
        StatLine loadout,
        double targetPowerIndex,
        int level,
        ContentSnapshot content,
        IReadOnlyList<StatId> scaledStats,
        double tolerance,
        int scalarDecimalPlaces)
    {
        ArgumentNullException.ThrowIfNull(loadout);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(scaledStats);

        double PowerAt(double scalar) =>
            PowerCalculator.PowerIndex(loadout.ScaledBy(scalar, scaledStats).ToActorStats(), level, content);

        var (low, high) = Bracket(PowerAt, targetPowerIndex);

        var steps = 0;
        var scalar = (low + high) / 2.0;
        for (; steps < MaxBisectionSteps; steps++)
        {
            scalar = (low + high) / 2.0;
            var power = PowerAt(scalar);

            if (Math.Abs((power / targetPowerIndex) - 1.0) <= tolerance)
            {
                break;
            }

            if (power < targetPowerIndex)
            {
                low = scalar;
            }
            else
            {
                high = scalar;
            }
        }

        // 🔒 `29` §2.5.3 — "round s to 4 dp". After convergence, not during: rounding inside the loop
        // would let the bracket collapse onto a rounded value that never meets the tolerance.
        var rounded = Math.Round(scalar, scalarDecimalPlaces, MidpointRounding.ToEven) + 0.0;
        var scaled = loadout.ScaledBy(rounded, scaledStats);
        var achieved = PowerCalculator.PowerIndex(scaled.ToActorStats(), level, content);

        return new ScaledLoadout(scaled, rounded, achieved, targetPowerIndex, level, steps);
    }

    private static (double Low, double High) Bracket(Func<double, double> powerAt, double target)
    {
        var low = 1.0;
        var high = 1.0;

        if (powerAt(1.0) < target)
        {
            for (var i = 0; i < MaxBracketSteps; i++)
            {
                low = high;
                high *= 2.0;
                if (powerAt(high) >= target)
                {
                    return (low, high);
                }
            }
        }
        else
        {
            for (var i = 0; i < MaxBracketSteps; i++)
            {
                high = low;
                low /= 2.0;
                if (powerAt(low) <= target)
                {
                    return (low, high);
                }
            }
        }

        throw new InvalidOperationException(
            $"29 §2.5.3's bisection found no bracket containing a PowerIndex of " +
            $"{target.ToString("R", CultureInfo.InvariantCulture)} within " +
            $"{MaxBracketSteps.ToString(CultureInfo.InvariantCulture)} doublings. PowerIndex is " +
            "strictly increasing in the scalar, so this means the target is not reachable by scaling " +
            "maxHp/atk/def at all — check that the par cell and the derived K_POWER are the ones the " +
            "documents describe rather than widening the search.");
    }
}

/// <summary>The result of placing one loadout at one power target.</summary>
/// <param name="Stats">The scaled statline — what the sweep actually fights with.</param>
/// <param name="Scalar">🔒 `29` §2.5.3's <c>s</c>, rounded to the authored decimal places.</param>
/// <param name="AchievedPowerIndex"><c>PowerIndex</c> of <paramref name="Stats"/> at <paramref name="Level"/>.</param>
/// <param name="TargetPowerIndex">The <c>PowerIndex</c> that was asked for.</param>
/// <param name="Level">🔒 `29` §2.5.3's level — <c>EnemyLevel(c, t)</c> when targeting content.</param>
/// <param name="BisectionSteps">How many halvings the solve took, for the report.</param>
public sealed record ScaledLoadout(
    StatLine Stats,
    double Scalar,
    double AchievedPowerIndex,
    double TargetPowerIndex,
    int Level,
    int BisectionSteps)
{
    /// <summary>
    /// ⚠️ Achieved ÷ target. 1.0 is exactly at par; the 4-dp rounding of <c>s</c> moves it a little.
    /// </summary>
    public double AchievedRatio => AchievedPowerIndex / TargetPowerIndex;
}
