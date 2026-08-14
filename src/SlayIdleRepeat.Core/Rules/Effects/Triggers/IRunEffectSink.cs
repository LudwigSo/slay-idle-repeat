using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>
/// 🔒 `18` §2.5's combat-context exception, as a seam: where a <b>combat</b> trigger's <b>run/board</b>
/// op goes instead of being resolved.
/// </summary>
/// <remarks>
/// <para>
/// `18` §2.5: <em>"a combat trigger may emit a run/board op — the sanctioned case is the Dicelord's
/// Scramble firing <c>MODIFY_DIE_FACE</c> from a <c>PERIODIC</c> trigger. The simulator still never
/// resolves it: it appends a <c>RunEffectQueued</c> event to the combat log and the run controller
/// applies the queued ops <b>in log order when the battle resolves</b>. In a PvP duel the queue is
/// discarded."</em>
/// </para>
/// <para>
/// ═══ 🔒 <b>WHY THIS IS AN INTERFACE AND NOT A CALL TO <c>CombatLog</c></b> ═══
/// </para>
/// <para>
/// R17 fixes the layering inside <c>Rules</c> as
/// <c>Rules.Combat ▶ Rules.Stats ▶ Rules.Effects</c>. <c>Rules.Effects</c> is the <b>bottom</b>: it
/// may not name <c>Rules.Stats</c> or <c>Rules.Combat</c>, and the combat log lives in
/// <c>Rules.Combat</c>. A trigger that called <c>CombatLog.AppendRunEffectQueued</c> directly would
/// invert that and put a cycle between the two namespaces with every architecture rule green — the
/// exact failure <c>IEffectActorView</c>'s remarks flagged as a milestone-level decision.
/// <c>IntraRulesLayeringRuleTests</c> now fails the build on it.
/// </para>
/// <para>
/// So the effects layer declares the shape and <b>M2-08 implements it in <c>Rules.Combat</c></b>, as
/// a one-line adapter over <c>CombatLog.AppendRunEffectQueued(tick, sourceId, effectIndex,
/// argument)</c>. The parameters below are chosen to make that adapter a translation and not a
/// decision.
/// </para>
/// <para>
/// ⚠️ <b>What the implementer owes.</b> <c>CombatLog</c> takes a <c>ushort</c> <b>battle-local effect
/// index</b> — the position of the effect's authored id in the battle's effect table, which `18` §8
/// orders ordinally — because no string fits a <c>ushort</c>. That table is the tick loop's, so the
/// lookup happens on the implementing side; this seam hands over the <see cref="EffectDefinition"/>
/// itself, which is the only thing that can be looked up. The actor is handed over as an
/// <see cref="IEffectActorView"/> for the same reason: mapping it to `05` §7's byte actor id is
/// <c>CombatActor</c>'s layout, and <c>CombatActor</c> is in <c>Rules.Combat</c>.
/// </para>
/// <para>
/// 🔒 <b>Nothing consumes the queue in M2, and that is the finished state.</b> Draining it is M3's
/// and discarding it in a duel is M2-14's — see <see cref="TriggerRouting"/>, which answers the
/// duel case here so that neither task has to rediscover it.
/// </para>
/// </remarks>
internal interface IRunEffectSink
{
    /// <summary>
    /// Records that a combat trigger fired an effect whose op is `18` §2.5's, for the run controller
    /// to apply when the battle resolves.
    /// </summary>
    /// <param name="tick">The `05` §3 tick the trigger fired on.</param>
    /// <param name="source">The actor whose effect fired — the Dicelord, for Scramble.</param>
    /// <param name="effect">
    /// The effect. The implementer resolves it to the battle-local effect index the combat log
    /// carries.
    /// </param>
    /// <param name="argument">
    /// The op's <b>one</b> runtime-resolved argument, already rounded to 4 dp (`05` §1.1) — for
    /// Scramble, which die face the roll picked. <c>0</c> when every argument is authored.
    /// </param>
    void QueueRunEffect(int tick, IEffectActorView source, EffectDefinition effect, double argument);
}
