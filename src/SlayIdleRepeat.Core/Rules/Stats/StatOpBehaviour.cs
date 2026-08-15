using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// The implementation of <c>STAT_CONVERT</c> and <c>STAT_CAP_OVERRIDE</c> behaviour.
/// </summary>
/// <remarks>
/// Lives in <c>Rules/Stats/</c> rather than with the other ops, since <see cref="IStatOpBehaviour"/>'s
/// signature is written in <c>Rules.Stats</c> types and an implementation under
/// <c>Rules/Effects/Ops/</c> would make the bottom layer name the one above it — so the arithmetic
/// lives with the other ops (<see cref="StatOps"/>) and only the plumbing is here. Stateless and
/// shared: nothing is cached, since aggregation re-runs whenever the build or the fight changes, and
/// a memoised conversion would go stale mid-fight.
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
    /// Every conversion reads <paramref name="postAdditive"/> and nothing else — that block is
    /// frozen by the caller, so two conversions off the same source both take their percentage of
    /// the same number regardless of application order. Deltas are returned as a flat, signed list
    /// rather than applied by mutation, so the second conversion never sees the first's output.
    /// </remarks>
    public IReadOnlyList<StatDelta> Convert(
        IReadOnlyList<EffectDefinition> conversions, ActorStats postAdditive, IEffectValueReader values)
    {
        ArgumentNullException.ThrowIfNull(conversions);
        ArgumentNullException.ThrowIfNull(postAdditive);
        ArgumentNullException.ThrowIfNull(values);

        if (conversions.Count == 0)
        {
            return [];
        }

        var deltas = new List<StatDelta>(conversions.Count * 2);

        foreach (var effect in conversions)
        {
            ArgumentNullException.ThrowIfNull(effect, nameof(conversions));

            var conversion = StatOps.Conversion(effect, values.EffectiveValue(effect));

            // A conversion across the combat/non-combat stat boundary is refused, not silently
            // dropped: it would otherwise add or take a real combat bonus from a stat this pipeline
            // doesn't hold.
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
    /// Only <see cref="StatCapKind.STAT_MAX"/> touches the table. <c>HEAL_CEILING</c> bounds
    /// healing, not a stat — folding it into the cap table would cap the hero's Max HP at 0.8
    /// instead. It's read separately through <see cref="HealCeilingFraction"/>.
    /// <see cref="StatCapKind.REDIRECT_EXCESS"/> is likewise not a cap-table change, since it moves
    /// value between two stats, and is applied by <see cref="RedirectCappedExcess"/> after the caps
    /// land.
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

        if (overrides.Count == 0)
        {
            return [];
        }

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

    /// <inheritdoc />
    /// <remarks>A <c>STAT_CAP_OVERRIDE HEAL_CEILING</c> changes no stat at all.</remarks>
    public double? HealCeilingFraction(
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

    /// <remarks>
    /// Fatal here, while steps 4/5/7/8 merely skip a non-combat stat and report it. The asymmetry is
    /// deliberate: a conversion or cap crossing the combat/non-combat boundary would silently change
    /// the build's power, which a skip cannot do.
    /// </remarks>
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
