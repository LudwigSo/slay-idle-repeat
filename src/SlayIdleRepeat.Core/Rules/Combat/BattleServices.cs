using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// 🔒 The tick loop's face to its own extensions — what M2-09, M2-10, M2-12 and M2-14 are handed so
/// they can do their half of `05` §3.1 without a second copy of the roster, the log or the clock.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the seams are built from this rather than handed in as values.</b> Every one of them needs
/// something that does not exist until the battle does: M2-09's <c>ResolveAttack</c> writes
/// <c>Hit</c>/<c>Miss</c>/<c>Crit</c> into <em>this fight's</em> log and draws from <em>this fight's</em>
/// stream, M2-10's cadence engine needs the tick, and both must route `05` §3.1's phase check. A seam
/// set constructed before the simulation would have to be given all of that afterwards, through
/// setters — which is how a seam ends up pointing at the previous battle's log.
/// </para>
/// <para>
/// 🔒 <b>Everything here is a reading or a routing, never a second implementation.</b> There is no
/// damage method, no status method and no phase method on this class: those are the seams themselves.
/// What it offers is the four things a seam cannot build for itself — the log, the draw stream, the
/// roster with its `18` §4/§5 context, and the two loop mechanics (<see cref="AfterHpDecrease"/> and
/// <see cref="AdmitSummon"/>) whose rules `05` §3.1 gives to the tick loop.
/// </para>
/// </remarks>
internal sealed class BattleServices
{
    private readonly BattleSimulation _simulation;

    internal BattleServices(BattleSimulation simulation) => _simulation = simulation;

    /// <summary>This fight's log. Append at the moment of the state change (`05` §3.1 step 7).</summary>
    internal CombatLog Log => _simulation.Log;

    /// <summary>
    /// 🔒 This fight's combat draw stream — <c>new DeterministicRng(battleSeed, RngStreams.Combat)</c>
    /// (`14` §8.1). One per battle: a second stream over the same seed would replay the same draws.
    /// </summary>
    internal DeterministicRng Rng => _simulation.Rng;

    /// <summary>`05` §3 / §3.3's bounds for this fight.</summary>
    internal CombatRules Rules => _simulation.Rules;

    /// <summary>
    /// 🔒 `05` §4's two 📐 dials, from <c>content/combat_caps.json#/mitigation</c> — a reading of
    /// the plan, never a restatement. `05` §4 calls them <em>"the two most important balance dials
    /// in the game"</em>, and a <c>120</c> written into the damage formula would be their third
    /// copy: the content build already mirrors the file against <c>tuning/power_model.json</c>.
    /// </summary>
    internal MitigationConstants Mitigation => _simulation.Mitigation;

    /// <summary>🔒 `05` §4.1's 📐 <c>wardCapPct</c>, likewise.</summary>
    internal double WardCapPct => _simulation.WardCapPct;

    /// <summary>Every actor, in `05` §3.1 index order, living and dead, summons appended.</summary>
    internal IReadOnlyList<BattleActor> Actors => _simulation.Actors;

    /// <summary>This battle's trigger registry (`18` §3).</summary>
    internal TriggerRegistry Triggers => _simulation.Triggers;

    /// <summary>The tick being run right now. <c>0</c> during the battle-start pre-tick.</summary>
    internal int Tick => _simulation.Tick;

    /// <summary>
    /// The `18` §4/§5 evaluation context for one holder, assembled from this fight's roster and
    /// clock — the same one the loop uses, not a second reading of the same battle.
    /// </summary>
    internal EffectEvaluationContext ContextFor(
        BattleActor holder, BattleActor? target = null, BattleActor? attacker = null) =>
        _simulation.ContextFor(holder, target, attacker);

    /// <summary>
    /// 🔒 `05` §3.1's phase check — <em>"runs immediately after <b>every</b> boss HP decrease
    /// (attack, DoT tick, thorns, true damage), once ward absorption and the floor are settled"</em>.
    /// </summary>
    /// <remarks>
    /// Routed through the loop rather than called on <see cref="IBossPhases"/> directly, because the
    /// loop also has to notice a death the decrease caused: `05` §3.1 step 6 puts an actor out of play
    /// <em>"at that moment"</em>, and the death <em>resolution</em> then waits for slot 6.
    /// </remarks>
    internal void AfterHpDecrease(BattleActor actor) => _simulation.AfterHpDecrease(actor);

    /// <summary>
    /// 🔒 `05` §4.3's <c>ON_HEAL</c> — fired <b>after</b> the HP is applied, with both readings in
    /// hand, which is what makes `18` §2.2's <c>HEAL_AMOUNT</c> and <c>OVERHEAL_AMOUNT</c> value
    /// modes readable (<c>TriggerRegistry</c>).
    /// </summary>
    /// <param name="actor">The recipient — `05` §4.3's <c>target</c>, and the trigger's holder.</param>
    /// <param name="healed">`05` §4.3's <c>healed</c>, after <c>HEAL%</c> and the Max-HP clip.</param>
    /// <param name="overheal">
    /// `05` §4.3's <c>overheal</c> — <em>"discarded unless an effect consumes it"</em>
    /// (<c>PK_TRANSFUSION</c>). Fired even when <paramref name="healed"/> is 0, because a heal into
    /// a full bar is exactly when the overheal is largest.
    /// </param>
    internal void AfterHeal(BattleActor actor, double healed, double overheal) =>
        _simulation.AfterHeal(actor, healed, overheal);

    /// <summary>
    /// 🔒 `05` §4.1's ward <b>expiry</b> — <em>"segment expiry silently removes its remainder
    /// (<c>StatusExpired</c>), and does <b>not</b> fire <c>WardBroken</c>"</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Routed through the loop rather than left to each caller, because the distinction it encodes
    /// is the one `18` §6's <c>until: WARD_BROKEN</c> terminator is built on (`17` §4's Ossify).
    /// A second expiry path that emitted <c>WardBroken</c> would end that DR buff on a timer nobody
    /// watched, and nothing in the log would look wrong.
    /// </para>
    /// <para>
    /// ⚠️ <b>The tick loop does not call this.</b> `05` §3.1 puts expiries in <b>slot 2</b>, which is
    /// <c>IStatusTimeline.ExpireDue</c> and M2-10's; a ward segment's timer is a `18` §6 duration
    /// like any other. M2-09 owns the mechanism and states it here so that M2-10 routes rather than
    /// reimplements.
    /// </para>
    /// </remarks>
    /// <param name="actor">The actor whose pool is being swept.</param>
    /// <returns>How many segments were dropped.</returns>
    internal int ExpireWards(BattleActor actor) => _simulation.ExpireWards(actor);

    /// <summary>
    /// 🔒 `05` §4.1's ward grant <b>with an expiry</b> — the form `18` §6's durations need, which
    /// <c>IAttackPipeline.GrantWard</c> cannot express because M2-03 declared it without a duration
    /// and M2-10 is coding against that signature.
    /// </summary>
    /// <param name="target">The actor receiving the ward.</param>
    /// <param name="amount">The authored ward, before the pool and per-source clips.</param>
    /// <param name="sourceCapPct">`18` §2.2's per-instance ceiling, or <c>null</c> where none is authored.</param>
    /// <param name="sourceEffectId">The `18` §8 id the segment carries.</param>
    /// <param name="expiresAtTick">The tick the segment's timer runs out on.</param>
    internal void GrantWard(
        BattleActor target, double amount, double? sourceCapPct, string sourceEffectId, int expiresAtTick) =>
        _simulation.GrantWard(target, amount, sourceCapPct, sourceEffectId, expiresAtTick);

    /// <summary>
    /// 🔒 `05` §3.1's summon entry rule — <em>"summons enter at the end of the enemy index list with
    /// a full attack cooldown (<c>1.0 / ASPD</c> — they never attack on their spawn tick) and become
    /// targetable at the next targeting evaluation."</em>
    /// </summary>
    /// <param name="plan">
    /// The spawned actor without its <see cref="ActorPlan.Index"/> or <see cref="ActorPlan.LogId"/>;
    /// both are assigned here, and a log id is never reused (<c>CombatActor</c>).
    /// </param>
    /// <returns>The live actor, already in the roster.</returns>
    internal BattleActor AdmitSummon(ActorPlan plan) => _simulation.AdmitSummon(plan);
}
