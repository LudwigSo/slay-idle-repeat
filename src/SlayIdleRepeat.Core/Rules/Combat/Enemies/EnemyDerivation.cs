using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat.Enemies;

/// <summary>
/// <c>EnemyStats(power, archetype)</c> — the six constants the derivation multiplies by.
/// </summary>
/// <remarks>
/// <para>
/// <code>
/// MaxHP  = power * 0.60  * archetype.hpCoef
/// ATK    = power * 0.045 * archetype.atkCoef
/// DEF    = power * 0.030 * archetype.defCoef
/// ASPD   = 1.00 * archetype.aspdCoef
/// </code>
/// The four numbers are authored in <c>content/enemies/enemies.json</c> rather than written here: a
/// number in code is a number nobody can retune without a build.
/// </para>
/// <para>
/// <see cref="FixedStats"/> holds the six stats every archetype gives the same value. They are read
/// rather than defaulted because <c>HEAL_PCT</c> multiplies all healing received: a defaulted 0
/// would silently disable every heal an enemy could receive and no damage-output test would notice.
/// </para>
/// </remarks>
/// <param name="HpPerPower">The <c>0.60</c> term.</param>
/// <param name="AtkPerPower">The <c>0.045</c> term.</param>
/// <param name="DefPerPower">The <c>0.030</c> term.</param>
/// <param name="BaseAspd">The <c>1.00</c> term.</param>
/// <param name="RoundingDecimals">Every term's rounding, as authored.</param>
/// <param name="FixedStats">The six stats that do not vary by archetype.</param>
internal sealed record EnemyDerivationConstants(
    double HpPerPower,
    double AtkPerPower,
    double DefPerPower,
    double BaseAspd,
    int RoundingDecimals,
    IReadOnlyDictionary<StatId, double> FixedStats);

/// <summary>
/// <c>(power, archetype, chapter, tier)</c> to a complete 14-stat actor block.
/// </summary>
/// <remarks>
/// <para>
/// Every stat of every enemy is computable from these four inputs with no free variables: the
/// result is an <see cref="ActorStats"/>, which refuses an incomplete block, so "total" is enforced
/// by the type rather than promised by a comment.
/// </para>
/// <para>
/// <c>power</c> arrives as a parameter and is never derived here: a boss's power already includes
/// its stage multiplier and must not be multiplied again, so keeping the derivation on this side of
/// the parameter makes the double-multiplication impossible to write here.
/// </para>
/// </remarks>
internal static class EnemyDerivation
{
    /// <summary>
    /// The step name <see cref="StatRounding.Round"/> reports when a term is not finite.
    /// </summary>
    /// <remarks>
    /// A NaN here is an overflowed or zero-times-infinite Power, and the message should say so
    /// rather than naming an effect-aggregation step, since the enemy block is built before any
    /// effect is collected.
    /// </remarks>
    internal const string Step = "the 05 §6 EnemyStats derivation";

    /// <summary>
    /// The complete stat block for one enemy.
    /// </summary>
    /// <param name="power">
    /// The enemy power for this encounter. For a boss it already carries its stage multiplier; for
    /// an elite, pass the value <see cref="ElitePower"/> produced.
    /// </param>
    /// <param name="archetype">The archetype row.</param>
    /// <param name="constants">The derivation constants, as authored.</param>
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
    /// <c>Elite = base archetype * elite power multiplier</c>.
    /// </summary>
    /// <remarks>
    /// The multiplier is authored, not written here. It is the elite multiplier and nothing else — a
    /// boss is not an elite and must not be re-multiplied by its own stage multiplier here.
    /// </remarks>
    /// <param name="power">The enemy power for the encounter.</param>
    /// <param name="powerMultiplier">The authored elite multiplier.</param>
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

        // Deliberately not StatRounding.Round: that method names a StatId in its failure message, and
        // the value here is a Power, not a stat. DeterminismRounding is the stat-agnostic statement.
        var elite = DeterminismRounding.Round(power * powerMultiplier);

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
