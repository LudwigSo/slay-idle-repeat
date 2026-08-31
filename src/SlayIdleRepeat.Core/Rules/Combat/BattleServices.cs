using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>The tick loop's face to its own extensions — what the damage, status, phase and duel seams are handed so they can do their part without a second copy of the roster, the log or the clock.</summary>
/// <remarks>
/// <para>
/// <b>Why the seams are built from this rather than handed in as values.</b> Every one of them needs
/// something that does not exist until the battle does: the attack resolver writes into <em>this</em>
/// fight's log and draws from <em>this</em> fight's stream, a status engine needs the tick, and both
/// must route the phase check. A seam set constructed before the simulation would have to be given all
/// of that afterwards, through setters — which is how a seam ends up pointing at the previous battle's log.
/// </para>
/// <para>
/// Everything here is a reading or a routing, never a second implementation. There is no damage method,
/// no status method and no phase method on this class — those are the seams themselves. What it offers
/// is the log, the draw stream, the roster with its evaluation context, and the loop mechanics
/// (<see cref="AfterHpDecrease"/>, <see cref="AdmitSummon"/>) whose rules belong to the tick loop.
/// </para>
/// </remarks>
internal sealed class BattleServices
{
    private readonly BattleSimulation _simulation;

    internal BattleServices(BattleSimulation simulation) => _simulation = simulation;

    /// <summary>This fight's log. Append at the moment of the state change.</summary>
    internal CombatLog Log => _simulation.Log;

    /// <summary>This fight's combat draw stream. One per battle: a second stream over the same seed would replay the same draws.</summary>
    internal DeterministicRng Rng => _simulation.Rng;

    /// <summary>The fight's bounds.</summary>
    internal CombatRules Rules => _simulation.Rules;

    /// <summary>The two most important balance dials in the game, from content — a reading of the plan, never a restatement.</summary>
    internal MitigationConstants Mitigation => _simulation.Mitigation;

    /// <summary>The ward pool ceiling, likewise.</summary>
    internal double WardCapPct => _simulation.WardCapPct;

    /// <summary>Every actor, in index order, living and dead, summons appended.</summary>
    internal IReadOnlyList<BattleActor> Actors => _simulation.Actors;

    /// <summary>This battle's trigger registry.</summary>
    internal TriggerRegistry Triggers => _simulation.Triggers;

    /// <summary>The tick being run right now. <c>0</c> during the battle-start pre-tick.</summary>
    internal int Tick => _simulation.Tick;

    /// <summary>The phase this fight's boss is in, or <c>null</c> outside a boss fight. The one reading a phase-scoped duration needs.</summary>
    /// <remarks>
    /// <para>
    /// Every boss aura is phase-scoped, and the duration evaluator has implemented the boundary for a
    /// long time — but its two inputs had no source and every caller left them <c>null</c>, so the
    /// scope silently fell back to battle-scoped inside boss fights too. This is that source, and it is
    /// a reading: it neither stores a phase nor derives one from HP.
    /// </para>
    /// <para>
    /// Both inputs come from here, deliberately: reading both from the same member is what makes it
    /// impossible to wire one and forget the other.
    /// </para>
    /// </remarks>
    internal int? CurrentBossPhase => _simulation.CurrentBossPhase;

    /// <summary>The evaluation context for one holder, assembled from this fight's roster and clock — the same one the loop uses, not a second reading of the same battle.</summary>
    internal EffectEvaluationContext ContextFor(
        BattleActor holder, BattleActor? target = null, BattleActor? attacker = null) =>
        _simulation.ContextFor(holder, target, attacker);

    /// <summary>One actor's stat block against one attack's other party — the conditional standing-effect bucket's per-pair re-aggregation, and the ambient block unchanged for every actor without a context-gated standing effect.</summary>
    /// <param name="actor">Whose stats are being read.</param>
    /// <param name="target">Its current target, when it is the swing's source.</param>
    /// <param name="attacker">The actor hitting it, when it is the swing's defender.</param>
    internal AggregatedStats StatsAgainst(BattleActor actor, BattleActor? target, BattleActor? attacker) =>
        _simulation.StatsAgainst(actor, target, attacker);

    /// <summary>The <c>ON_PHASE_ENTER</c> sweep a phase entry owes, over the boss's own instances in ascending effect-id order.</summary>
    /// <param name="boss">The boss that just entered a phase.</param>
    /// <param name="phase">The phase entered, <c>1..3</c>.</param>
    /// <remarks>
    /// Routed through the loop: the phase controller owns <em>when</em> a phase is entered, but the
    /// trigger routing, the cascade bound and the op seams the entry's effects resolve through are all
    /// the loop's. A seam that resolved them itself would be a second, quieter copy of all three.
    /// </remarks>
    internal void FirePhaseEntry(BattleActor boss, int phase) =>
        _simulation.FirePhaseEntry(boss, phase);

    /// <summary>Resolves the one effect a <c>RANDOM_OUTCOME</c> drew, once <see cref="IBossOutcomes"/> has found it among the holder's holdings.</summary>
    /// <param name="holder">The actor whose roll it was.</param>
    /// <param name="effect">The winning row's effect. It carries no trigger of its own.</param>
    internal void ResolveOutcome(BattleActor holder, EffectDefinition effect) =>
        _simulation.ResolveOutcome(holder, effect);

    /// <summary>One effect's position in the battle's effect table, the value a telegraph/run-effect-queued event carries.</summary>
    /// <param name="effect">The effect being named.</param>
    /// <returns>Its 0-based position in the opening roster's table.</returns>
    /// <remarks>
    /// A reading of the one table, never a second one: the loop builds it once from the opening roster
    /// in ordinal order and its positions are inside every committed log hash; a seam that rebuilt the
    /// same expression for itself would be a second table kept identical by hand, re-sorting the whole
    /// roster on every emission.
    /// </remarks>
    internal ushort EffectIndexOf(EffectDefinition effect) => _simulation.EffectIndexOf(effect);

    /// <summary>The phase check — runs immediately after every boss HP decrease (attack, DoT tick, thorns, true damage), once ward absorption and the floor are settled.</summary>
    /// <remarks>
    /// Routed through the loop rather than called on <see cref="IBossPhases"/> directly, because the
    /// loop also has to notice a death the decrease caused: an actor goes out of play at that moment,
    /// and the death resolution itself waits for the death-resolution slot.
    /// </remarks>
    internal void AfterHpDecrease(BattleActor actor) => _simulation.AfterHpDecrease(actor);

    /// <summary><c>ON_LETHAL</c> — "would take fatal damage". Routed through the loop for <see cref="AfterHpDecrease"/>'s reason.</summary>
    /// <param name="actor">
    /// The defender whose post-absorption hit would take it to <c>&lt;= 0</c> HP. The caller
    /// (<c>AttackPipeline.ApplyToHp</c>) decides that before calling — this method only fires.
    /// </param>
    internal void FireLethal(BattleActor actor) => _simulation.FireLethal(actor);

    /// <summary><c>ON_HEAL</c> — fired after the HP is applied, with both readings in hand.</summary>
    /// <param name="actor">The recipient — the trigger's holder.</param>
    /// <param name="healed">The amount healed, after heal-percent and the Max-HP clip.</param>
    /// <param name="overheal">The discarded remainder, unless an effect consumes it. Fired even when <paramref name="healed"/> is 0, since a heal into a full bar is exactly when the overheal is largest.</param>
    internal void AfterHeal(BattleActor actor, double healed, double overheal) =>
        _simulation.AfterHeal(actor, healed, overheal);

    /// <summary>Ward expiry — segment expiry silently removes its remainder (<c>StatusExpired</c>), and does not fire <c>WardBroken</c>.</summary>
    /// <remarks>
    /// <para>
    /// Routed through the loop rather than left to each caller, because the distinction it encodes is
    /// the one a "until ward broken" duration terminator is built on. A second expiry path that emitted
    /// <c>WardBroken</c> would end that buff on a timer nobody watched, with nothing in the log looking wrong.
    /// </para>
    /// <para>
    /// The tick loop calls this in the expiry slot: a ward segment's timer is a duration like any
    /// other, so it is swept beside <see cref="IStatusTimeline.ExpireDue"/> rather than inside it — a
    /// status-scoped implementation would be a no-op for a fight that grants a shield but holds no status.
    /// </para>
    /// </remarks>
    /// <param name="actor">The actor whose pool is being swept.</param>
    /// <returns>How many segments were dropped.</returns>
    internal int ExpireWards(BattleActor actor) => _simulation.ExpireWards(actor);

    /// <summary>A ward grant with an expiry — the form duration-bearing wards need, which <c>IAttackPipeline.GrantWard</c> cannot express since it carries no duration.</summary>
    /// <param name="target">The actor receiving the ward.</param>
    /// <param name="amount">The authored ward, before the pool and per-source clips.</param>
    /// <param name="sourceCapPct">The per-instance ceiling, or <c>null</c> where none is authored.</param>
    /// <param name="sourceEffectId">The effect id the segment carries.</param>
    /// <param name="expiresAtTick">The tick the segment's timer runs out on, or <c>null</c> for a segment with none.</param>
    internal void GrantWard(
        BattleActor target, double amount, double? sourceCapPct, string sourceEffectId, int? expiresAtTick) =>
        _simulation.GrantWard(target, amount, sourceCapPct, sourceEffectId, expiresAtTick);

    /// <summary>The summon entry rule — summons enter at the end of the enemy index list with a full attack cooldown (they never attack on their spawn tick) and become targetable at the next targeting evaluation.</summary>
    /// <param name="plan">
    /// The spawned actor without its <see cref="ActorPlan.Index"/> or <see cref="ActorPlan.LogId"/>;
    /// both are assigned here, and a log id is never reused.
    /// </param>
    /// <returns>The live actor, already in the roster.</returns>
    internal BattleActor AdmitSummon(ActorPlan plan) => _simulation.AdmitSummon(plan);
}
