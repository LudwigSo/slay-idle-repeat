using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat.Status;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// 🔒 `05` §3.3 / `11` §4.3 — two hero builds, composed into the Ghost Duel
/// <see cref="CombatSimulator.SimulateDuel"/> exposes.
/// </summary>
/// <remarks>
/// <para>
/// `05` §3.3 describes the duel as <em>"two hero-shaped sides"</em> and `11` §4.3 states its bounds
/// — a duration cap converted from seconds by <c>CombatRules.Duel</c>, and an underdog side that
/// wins an exact tie at the timeout. Both were implemented and pinned by <c>PvpDuelTests</c>
/// against the <b>internal</b> <c>CombatSimulator.Simulate(BattlePlan)</c> only; nothing outside
/// <c>Core</c> could run one. This type is the composition <see cref="CombatSimulator.SimulateDuel"/>
/// delegates to, on <see cref="EncounterFight"/>'s and <c>BossFight</c>'s precedent.
/// </para>
/// <para>
/// 🔒 <b>The defending side is a <em>hero</em>, on <see cref="BattleSide.ENEMY"/>.</b>
/// <c>CombatActor</c> puts the defender at <c>FirstEnemy</c> so it satisfies <c>BattlePlan</c>'s
/// "one killable enemy" rule rather than doubling the hero-side hero; <c>Kind</c> stays
/// <see cref="EffectActorKind.HERO"/> so `18` §4's hero-shaped conditions read it correctly (`05`
/// §3.3, <c>PvpDuelTests</c>).
/// </para>
/// <para>
/// ⚠️ <b>No pets.</b> `05` §1's plain <c>Simulate</c> overload carries none either — pets are a gap
/// this task does not close, and inventing a pet-bearing duel signature here would be exactly the
/// kind of unauthorised widening steering S6 forbids.
/// </para>
/// </remarks>
internal static class DuelFight
{
    /// <summary>Runs one Ghost Duel to `11` §4.3's bound and returns `05` §7's replay.</summary>
    /// <param name="battleSeed">`14` §8.1's battle seed. The <c>runSeed</c> never enters this layer.</param>
    /// <param name="attacker">The attacking player's `05` §1 block, before `18` §8.</param>
    /// <param name="attackerLevel">The attacker's Legend Level.</param>
    /// <param name="defender">The Ghost's `05` §1 block — the defending player's build as recorded.</param>
    /// <param name="defenderLevel">The Ghost's Legend Level.</param>
    /// <param name="durationSeconds">
    /// `11` §4.3's duel cap, in seconds — <c>content/combat_caps.json#/pvpMaxFightSeconds</c>, as the
    /// caller read it. Converted to ticks by <c>CombatRules.Duel</c>.
    /// </param>
    /// <param name="attackerIsUnderdog">
    /// `11` §4.3 — <em>"the lower-rated player wins"</em> an exact tie at the timeout. True names the
    /// attacker the lower-rated side; false names the defender.
    /// </param>
    /// <param name="content">The loaded, schema-validated content snapshot.</param>
    /// <param name="attackerEffects">Extra `18` §1 effects the attacker holds for this duel. <c>null</c>/empty for none.</param>
    /// <param name="defenderEffects">Extra `18` §1 effects the Ghost holds for this duel. <c>null</c>/empty for none.</param>
    /// <exception cref="MissingContentException">A document or pointer the fight needs is absent.</exception>
    /// <exception cref="UnauthorisedTunableException">A constant the fight needs is <c>null</c> in the data.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="durationSeconds"/> is outside `05` §3's addressable tick range.</exception>
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
