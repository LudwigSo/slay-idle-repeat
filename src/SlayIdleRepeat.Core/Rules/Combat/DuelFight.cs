using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat.Status;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>Two hero builds, composed into the Ghost Duel <see cref="CombatSimulator.SimulateDuel"/> exposes.</summary>
/// <remarks>
/// <para>
/// The defending side is a hero, on <see cref="BattleSide.ENEMY"/>: putting it at
/// <c>FirstEnemy</c> satisfies <c>BattlePlan</c>'s "one killable enemy" rule rather than doubling the
/// hero-side hero, while <c>Kind</c> stays <see cref="EffectActorKind.HERO"/> so hero-shaped conditions
/// read it correctly.
/// </para>
/// <para>No pets: the plain <c>Simulate</c> overload carries none either, and pets are a gap this type does not close.</para>
/// </remarks>
internal static class DuelFight
{
    /// <summary>Runs one Ghost Duel to its bound and returns the replay.</summary>
    /// <param name="battleSeed">The battle seed. The run seed never enters this layer.</param>
    /// <param name="attacker">The attacking player's stat block, before effect aggregation.</param>
    /// <param name="attackerLevel">The attacker's Legend Level.</param>
    /// <param name="defender">The Ghost's stat block — the defending player's build as recorded.</param>
    /// <param name="defenderLevel">The Ghost's Legend Level.</param>
    /// <param name="durationSeconds">The duel cap, in seconds, as the caller read it. Converted to ticks by <c>CombatRules.Duel</c>.</param>
    /// <param name="attackerIsUnderdog">The lower-rated player wins an exact tie at the timeout. True names the attacker the lower-rated side; false names the defender.</param>
    /// <param name="content">The loaded, schema-validated content snapshot.</param>
    /// <param name="attackerEffects">Extra effects the attacker holds for this duel. <c>null</c>/empty for none.</param>
    /// <param name="defenderEffects">Extra effects the Ghost holds for this duel. <c>null</c>/empty for none.</param>
    /// <exception cref="MissingContentException">A document or pointer the fight needs is absent.</exception>
    /// <exception cref="UnauthorisedTunableException">A constant the fight needs is <c>null</c> in the data.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="durationSeconds"/> is outside the addressable tick range.</exception>
    internal static SimulationResult Run(
        ulong battleSeed,
        ActorStats attacker,
        int attackerLevel,
        ActorStats defender,
        int defenderLevel,
        double durationSeconds,
        bool attackerIsUnderdog,
        ContentSnapshot content,
        IReadOnlyList<EffectDefinition>? attackerEffects,
        IReadOnlyList<EffectDefinition>? defenderEffects)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(defender);
        ArgumentNullException.ThrowIfNull(content);

        var caps = CombatCaps.Read(content);
        var statuses = StatusCatalogue.Read(content);

        var actors = new List<ActorPlan>
        {
            new()
            {
                Id = "HERO",
                Index = 0,
                LogId = CombatActor.Hero,
                Side = BattleSide.HERO,
                Kind = EffectActorKind.HERO,
                BaseStats = attacker,
                Level = attackerLevel,
                Effects = ToHeld(attackerEffects),
            },
            new()
            {
                Id = "HERO_DEFENDER",
                Index = CombatActor.FirstEnemy,
                LogId = CombatActor.Enemy(0),
                Side = BattleSide.ENEMY,
                Kind = EffectActorKind.HERO,
                BaseStats = defender,
                Level = defenderLevel,
                Effects = ToHeld(defenderEffects),
            },
        };

        var rules = CombatRules.Duel(durationSeconds, attackerIsUnderdog ? BattleSide.HERO : BattleSide.ENEMY);

        return CombatSimulator.Simulate(new BattlePlan
        {
            BattleSeed = battleSeed,
            Actors = actors,
            Caps = caps.Caps,
            Mitigation = caps.Mitigation,
            WardCapPct = caps.WardCapPct,
            RunCounters = new RunTriggerCounters(),
            Rules = rules,
            Seams = services =>
            {
                var attack = new AttackPipeline(services);
                var timeline = new StatusTimeline(services, attack, statuses);

                return BattleSeams.Strict with
                {
                    Attack = attack,
                    Statuses = timeline,
                    Timeline = timeline,
                };
            },
        });
    }

    /// <summary>Wraps caller-supplied effects as battle-local holdings — <c>null</c> id, minted by the simulator.</summary>
    private static IReadOnlyList<HeldEffect> ToHeld(IReadOnlyList<EffectDefinition>? effects)
    {
        if (effects is null || effects.Count == 0)
        {
            return Array.Empty<HeldEffect>();
        }

        var held = new List<HeldEffect>(effects.Count);
        foreach (var effect in effects)
        {
            held.Add(new HeldEffect(effect));
        }

        return held;
    }
}
