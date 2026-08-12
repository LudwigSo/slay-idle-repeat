using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat.Enemies;

/// <summary>
/// 🔒 `05` §6's <c>EnemyStats(power, archetype)</c> — the six constants the derivation multiplies by.
/// </summary>
/// <remarks>
/// <para>
/// `05` §6, verbatim:
/// <code>
/// MaxHP  = power * 0.60  * archetype.hpCoef
/// ATK    = power * 0.045 * archetype.atkCoef
/// DEF    = power * 0.030 * archetype.defCoef
/// ASPD   = 1.00 * archetype.aspdCoef
/// </code>
/// The four numbers are authored in <c>content/enemies/enemies.json</c> rather than written here,
/// for the reason every other balance constant in this codebase is: a number in code is a number
/// nobody can retune without a build.
/// </para>
/// <para>
/// <see cref="FixedStats"/> holds the six stats `05` §6 gives every archetype the same value:
/// <c>BLOCK 0</c>, <c>PEN 0</c>, <c>DMG% 0</c>, <c>DR% 0</c>, <c>HEAL% 1.0</c> and <c>THORN 0</c>.
/// 🔒 <c>HEAL_PCT</c> is why they are read rather than defaulted — it multiplies <em>all</em> healing
/// received (`05` §4.3), so a defaulted 0 would silently disable every heal an enemy could receive
/// and no test of an enemy's damage output would notice.
/// </para>
/// </remarks>
/// <param name="HpPerPower">`05` §6 — the <c>0.60</c> term.</param>
/// <param name="AtkPerPower">`05` §6 — the <c>0.045</c> term.</param>
/// <param name="DefPerPower">`05` §6 — the <c>0.030</c> term.</param>
/// <param name="BaseAspd">`05` §6 — the <c>1.00</c> term.</param>
/// <param name="RoundingDecimals">`05` §6 — <em>"every term rounded to 4 dp"</em>, as authored.</param>
/// <param name="FixedStats">`05` §6 — the six stats that do not vary by archetype.</param>
internal sealed record EnemyDerivationConstants(
    double HpPerPower,
    double AtkPerPower,
    double DefPerPower,
    double BaseAspd,
    int RoundingDecimals,
    IReadOnlyDictionary<StatId, double> FixedStats);

/// <summary>
/// 🔒 `05` §6 / §6.2 — <c>(power, archetype, chapter, tier)</c> to a complete 14-stat actor block.
/// </summary>
/// <remarks>
/// <para>
/// `05` §6: <em>"The formula is now total — every stat of every enemy is computable from
/// <c>(power, archetype, chapter, tier)</c> with no free variables."</em> This is that sentence made
/// mechanical: the result is an <see cref="ActorStats"/>, which refuses an incomplete block, so
/// "total" is enforced by the type rather than promised by a comment.
/// </para>
/// <para>
/// 🔒 <b>Every term is rounded, at the term.</b> `05` §6 closes with <em>"every term rounded to 4
/// dp (§1.1)"</em> and `05` §1.1 is the locked determinism rule. <see cref="StatRounding"/> is the
/// one implementation of it in this layer and R17 lets <c>Rules.Combat</c> name
/// <c>Rules.Stats</c>, so nothing here restates the rounding.
/// </para>
/// <para>
/// ⚠️ <b><c>power</c> arrives as a parameter and is never derived here.</b> `02` §4.3 owns
/// <c>EnemyPower(i)</c> — <c>ChapterPowerTarget(c) × TierMult(t) × (1 + 0.035 × i) × StageMult(s)</c>
/// — and the node index <c>i</c> belongs to the board's tile resolvers (M3-03). 🔒 `05` §6.3 and
/// `17` §1 both state that a boss's Power <b>already</b> includes <c>StageMult.Boss = 2.20</c> and
/// must not be multiplied again; keeping the derivation on this side of that parameter is what makes
/// the double-multiplication impossible to write here.
/// </para>
/// </remarks>
internal static class EnemyDerivation
{
    /// <summary>
    /// The `18` §8 step name <see cref="StatRounding.Round"/> reports when a term is not finite.
    /// </summary>
    /// <remarks>
    /// It names `05` §6 rather than an `18` §8 step because this is not an `18` §8 aggregation: the
    /// enemy block is built before any effect is collected. A NaN here is an overflowed or
    /// zero-times-infinite Power, and the message should say so.
    /// </remarks>
    internal const string Step = "the 05 §6 EnemyStats derivation";

    /// <summary>
    /// 🔒 `05` §6 — the complete stat block for one enemy.
    /// </summary>
    /// <param name="power">
    /// `02` §4.3's <c>EnemyPower(i)</c> for this encounter. For a boss it already carries
    /// <c>StageMult.Boss</c>; for an elite, pass the value <see cref="ElitePower"/> produced.
    /// </param>
    /// <param name="archetype">The `05` §6.1 row.</param>
    /// <param name="constants">The `05` §6 derivation constants, as authored.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="power"/> is negative or not finite.</exception>
    /// <exception cref="ArgumentException">
    /// A fixed stat is missing from <paramref name="constants"/>, or a derived term is not a finite,
    /// 4-decimal-place value.
    /// </exception>
    internal static ActorStats Derive(double power, ArchetypeRow archetype, EnemyDerivationConstants constants)
    {
        ArgumentNullException.ThrowIfNull(archetype);
        ArgumentNullException.ThrowIfNull(constants);
        RequireFinitePower(power, nameof(power));

        var values = new Dictionary<StatId, double>(StatIds.Combat.Count)
        {
            [StatId.MAX_HP] = Round(StatId.MAX_HP, power * constants.HpPerPower * archetype.HpCoef),
            [StatId.ATK] = Round(StatId.ATK, power * constants.AtkPerPower * archetype.AtkCoef),
            [StatId.DEF] = Round(StatId.DEF, power * constants.DefPerPower * archetype.DefCoef),
            [StatId.ASPD] = Round(StatId.ASPD, constants.BaseAspd * archetype.AspdCoef),
            [StatId.CRIT] = Round(StatId.CRIT, archetype.Crit),
            [StatId.CDMG] = Round(StatId.CDMG, archetype.CritDamage),
            [StatId.DODGE] = Round(StatId.DODGE, archetype.Dodge),
            [StatId.LIFESTEAL] = Round(StatId.LIFESTEAL, archetype.Lifesteal),
        };

        // A plain loop with a `continue`, not `Where(…)`: this runs per enemy per battle, and the
        // predicate would allocate an iterator plus a closure capturing `values` on every call.
        foreach (var stat in StatIds.Combat)
        {
            if (values.ContainsKey(stat))
            {
                continue;
            }

            if (!constants.FixedStats.TryGetValue(stat, out var fixedValue))
            {
                throw new ArgumentException(
                    $"05 §6 gives every archetype the same {stat}, and content/enemies/enemies.json " +
                    "states none. 05 §2: 'an unstated stat is a bug, not a zero' — and HEAL_PCT is the " +
                    "case that proves it, because its authored value is 1.0 and a defaulted 0 disables " +
                    "every heal the actor could receive.",
                    nameof(constants));
            }

            values[stat] = Round(stat, fixedValue);
        }

        return ActorStats.From(values);

        double Round(StatId stat, double value) => StatRounding.Round(value, stat, Step);
    }

    /// <summary>
    /// 🔒 `05` §6.2 — <c>Elite = base archetype × 2.2 power</c>.
    /// </summary>
    /// <remarks>
    /// The multiplier is authored (<c>elites.powerMultiplier</c>), not written here, and the product
    /// is rounded like every other term of §6. ⚠️ It is the <b>elite</b> multiplier and nothing else:
    /// a boss is not an elite, and `05` §6.3 forbids re-multiplying a boss's Power by its own stage
    /// multiplier.
    /// </remarks>
    /// <param name="power">`02` §4.3's <c>EnemyPower(i)</c> for the encounter.</param>
    /// <param name="powerMultiplier">`05` §6.2's authored elite multiplier.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="power"/> is negative or not finite, or <paramref name="powerMultiplier"/> is
    /// not a positive finite number.
    /// </exception>
    internal static double ElitePower(double power, double powerMultiplier)
    {
        RequireFinitePower(power, nameof(power));

        if (!double.IsFinite(powerMultiplier) || powerMultiplier <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(powerMultiplier), powerMultiplier,
                "05 §6.2's elite power multiplier is a positive finite number. A zero or negative " +
                "multiplier would make an elite weaker than the grunt standing next to it.");
        }

        // 🔒 Rounded to 05 §1.1's places like every other term of §6, but NOT through
        // StatRounding.Round: that method names a StatId in its failure message, and the value here
        // is an 02 §4.3 Power, not a stat. An overflow reported as "produced ∞ for MAX_HP" sends the
        // reader to a formula that was never evaluated.
        var elite = Math.Round(power * powerMultiplier, StatRounding.Decimals) + 0.0;

        if (!double.IsFinite(elite))
        {
            throw new ArgumentOutOfRangeException(
                nameof(power), power,
                "05 §6.2's elite multiplier overflowed 02 §4.3's EnemyPower(i). This is a Power, not a " +
                "stat: the overflow is in the chapter/tier/stage product the caller handed in.");
        }

        return elite;
    }

    private static void RequireFinitePower(double power, string parameterName)
    {
        if (!double.IsFinite(power) || power < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, power,
                "02 §4.3's EnemyPower(i) is a non-negative finite number. A NaN or infinite Power is " +
                "an overflow in the chapter/tier/stage product upstream, and it is far easier to trace " +
                "from here than from a stat block that refuses to round.");
        }
    }
}
