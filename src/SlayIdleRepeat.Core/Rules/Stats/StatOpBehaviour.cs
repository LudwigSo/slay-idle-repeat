using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// 🔒 M2-03's implementation of `18` §8 steps 6 and 9 — the seam M2-07 left open, filled.
/// </summary>
/// <remarks>
/// <para>
/// It replaces <c>UnimplementedStatOps</c>, which refused both steps because neither op was
/// implementable as authored: <c>STAT_CONVERT</c> had one <c>stat</c> key for <em>"stat A into stat
/// B"</em>, and <c>STAT_CAP_OVERRIDE</c>'s one authored <c>capKind</c> — <c>HEAL_CEILING</c> — is
/// not one of `05` §1's six stat caps at all. Both were closed by `18` §10's extension procedure:
/// see <see cref="EffectDefinition.ToStat"/> and <see cref="StatCapKind"/>.
/// </para>
/// <para>
/// ⚠️ <b>Why this class is in <c>Rules/Stats/</c> and not with the other 41 ops.</b> R17 makes
/// <c>Rules.Effects</c> the bottom of the intra-<c>Rules</c> layering, and
/// <see cref="IStatOpBehaviour"/>'s signature is written in <c>ActorStats</c>, <c>StatCaps</c> and
/// <c>StatDelta</c> — all <c>Rules.Stats</c>. An implementation under <c>Rules/Effects/Ops/</c>
/// would make the bottom layer name the one above it. So the <b>arithmetic</b> is with the other ops
/// (<see cref="StatOps"/>) and only the plumbing is here. (M2-02 moves the three `18` seams out of
/// <c>Rules/Stats/</c> in wave 4; when it does, this class can follow them and the split closes.)
/// </para>
/// <para>
/// 🔒 <b>Stateless and shared.</b> Nothing is cached: `18` §8 re-aggregates whenever the build or
/// the fight changes — `05` §3.1's <c>SYS_ENRAGE</c> adds a <c>STAT_MULT</c> every second from 70 s —
/// so a memoised conversion would be wrong one tick later, identically on client and server, which
/// `14` §8.2's determinism job would not catch either.
/// </para>
/// </remarks>
internal sealed class StatOpBehaviour : IStatOpBehaviour
{
    /// <summary>The single instance.</summary>
    internal static StatOpBehaviour Instance { get; } = new();

    private StatOpBehaviour()
    {
    }

    /// <inheritdoc />
    /// <remarks>
    /// 🔒 <b>Every conversion reads <paramref name="postAdditive"/> and nothing else.</b> That block
    /// is frozen by the caller, so `18` §8 step 6's <em>"reads post-step-5 values"</em> is enforced
    /// rather than trusted: two conversions off the same source both take their percentage of the
    /// same number, whatever order they are applied in. The deltas are returned as a flat, signed
    /// list in effect-id order — <c>PK_TURTLE</c>'s "20% of DEF into ATK" is a <c>-x</c> on DEF and a
    /// <c>+x</c> on ATK — because a mutation would let the second conversion see the first's output.
    /// </remarks>
    public IReadOnlyList<StatDelta> Convert(
        IReadOnlyList<EffectDefinition> conversions, ActorStats postAdditive, IEffectValueReader values)
    {
        ArgumentNullException.ThrowIfNull(conversions);
        ArgumentNullException.ThrowIfNull(postAdditive);
        ArgumentNullException.ThrowIfNull(values);

        var deltas = new List<StatDelta>(conversions.Count * 2);

        foreach (var effect in conversions)
        {
            ArgumentNullException.ThrowIfNull(effect, nameof(conversions));

            var conversion = StatOps.Conversion(effect, values.EffectiveValue(effect));

            // 🔒 A conversion between the 26-stat DSL vocabulary and the 14-stat actor block is
            //    refused, not silently dropped: "convert 20% of GOLD_PCT into ATK" would otherwise
            //    add a real ATK bonus out of a stat this pipeline does not hold, or take from one.
            RequireCombat(effect, conversion.From, "source");
            RequireCombat(effect, conversion.To, "destination");

            var (fromDelta, toDelta) = StatOps.Deltas(
                conversion, postAdditive[conversion.From], effect.Id);

            deltas.Add(new StatDelta(conversion.From, fromDelta));
            deltas.Add(new StatDelta(conversion.To, toDelta));
        }

        return deltas;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// 🔒 <b>Only <see cref="StatCapKind.STAT_MAX"/> touches the table, and that is the ruling.</b>
    /// <c>HEAL_CEILING</c> bounds <c>Heal()</c> (`05` §4.3), not a stat — `18` §7.6's
    /// <em>Avatar of War</em> is <em>"you can no longer be healed above 80% Max HP"</em> (`09` §4),
    /// which is a rule about healing and not a ceiling on <c>MAX_HP</c>. Folding it into the cap
    /// table would cap the hero's Max HP at 0.8, i.e. delete the hero. It is passed over here and
    /// read by M2-09 through <see cref="HealCeilingFraction"/>.
    /// </para>
    /// <para>
    /// <see cref="StatCapKind.REDIRECT_EXCESS"/> is likewise not a cap-table change — it moves value
    /// between two stats — and is applied by <see cref="RedirectCappedExcess"/> after the caps land.
    /// </para>
    /// </remarks>
    public StatCaps OverrideCaps(
        IReadOnlyList<EffectDefinition> overrides, StatCaps declared, IEffectValueReader values)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(values);

        var caps = declared;

        foreach (var effect in overrides)
        {
            ArgumentNullException.ThrowIfNull(effect, nameof(overrides));

            var over = StatOps.CapOverride(effect, values.EffectiveValue(effect));

            if (over.Kind != StatCapKind.STAT_MAX)
            {
                continue;
            }

            RequireCombat(effect, over.Stat, "capped");
            caps = caps.With(over.Stat, over.Value);
        }

        return caps;
    }

    /// <inheritdoc />
    public IReadOnlyList<StatDelta> RedirectCappedExcess(
        IReadOnlyList<EffectDefinition> overrides,
        ActorStats preCap,
        StatCaps effective,
        IEffectValueReader values)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(preCap);
        ArgumentNullException.ThrowIfNull(effective);
        ArgumentNullException.ThrowIfNull(values);

        var deltas = new List<StatDelta>();

        foreach (var effect in overrides)
        {
            ArgumentNullException.ThrowIfNull(effect, nameof(overrides));

            var over = StatOps.CapOverride(effect, values.EffectiveValue(effect));

            if (over.Kind != StatCapKind.REDIRECT_EXCESS)
            {
                continue;
            }

            RequireCombat(effect, over.Stat, "redirected");
            RequireCombat(effect, over.ToStat!.Value, "destination");

            var amount = StatOps.RedirectedAmount(
                preCap[over.Stat], effective.Maximum(over.Stat), over.Value, effect.Id);

            if (amount != 0.0)
            {
                deltas.Add(new StatDelta(over.ToStat.Value, amount));
            }
        }

        return deltas;
    }

    /// <summary>
    /// 🔒 `18` §7.6's <c>HEAL_CEILING</c> — the fraction of Max HP above which the actor cannot be
    /// healed — or <c>null</c> where the build authors none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Not part of <see cref="IStatOpBehaviour"/>, because it is not part of `18` §8.</b> It
    /// is a bound on <c>Heal()</c> (`05` §4.3), which M2-09 owns, and it is exposed here so that the
    /// one op that carries it has exactly one reader. A <c>STAT_CAP_OVERRIDE HEAL_CEILING</c>
    /// therefore changes no stat at all — <c>Avatar of War</c>'s other clause, the ×1.20
    /// <c>STAT_MULT</c>, is what moves ATK.
    /// </para>
    /// <para>
    /// 🔒 <b>The lowest ceiling wins.</b> `18` §6's stacking modes govern how repeat applications of
    /// one effect combine and say nothing about two different effects both bounding healing; taking
    /// the minimum is the only reading under which a second restriction cannot loosen the first,
    /// which is what "you can no longer be healed above" means. Recorded as a ruling — no document
    /// states it, because no second <c>HEAL_CEILING</c> is authored.
    /// </para>
    /// </remarks>
    /// <param name="overrides">The <c>STAT_CAP_OVERRIDE</c> effects, in effect-id order.</param>
    /// <param name="values">The same value reader `18` §8's steps use.</param>
    internal static double? HealCeilingFraction(
        IReadOnlyList<EffectDefinition> overrides, IEffectValueReader values)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(values);

        double? lowest = null;

        foreach (var effect in overrides)
        {
            ArgumentNullException.ThrowIfNull(effect, nameof(overrides));

            var over = StatOps.CapOverride(effect, values.EffectiveValue(effect));

            if (over.Kind == StatCapKind.HEAL_CEILING && (lowest is null || over.Value < lowest))
            {
                lowest = over.Value;
            }
        }

        return lowest;
    }

    private static void RequireCombat(EffectDefinition effect, StatId stat, string role)
    {
        if (!StatIds.IsCombat(stat))
        {
            throw new ArgumentException(
                $"'{effect.Id}' names {stat} as its {role} stat, and 18 §2.1's vocabulary is 26 stats " +
                $"wide while 05 §1's actor block holds 14. The 12 non-combat stats are run and meta " +
                "modifiers with no place in a fight, so a conversion or a cap across the boundary " +
                "would move a real combat number into or out of a stat this pipeline does not hold.",
                nameof(effect));
        }
    }
}
