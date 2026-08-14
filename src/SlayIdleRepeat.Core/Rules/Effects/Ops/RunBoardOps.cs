using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>
/// 🔒 `18` §2.5's thirteen run and board ops — <b>declared, validated, and deliberately not
/// resolved</b>.
/// </summary>
/// <remarks>
/// <para>
/// §2.5's opening line is the whole specification of this file: <em>"These are resolved by the run
/// controller, never by the combat simulator."</em> That controller is M3's, over M1-05's <c>Run</c>
/// aggregate. <b>A declared op with no resolver is the correct end state for M2</b>, not an
/// omission — a placeholder controller here would be a guess at two things that do not exist.
/// </para>
/// <para>
/// 🔒 <b>The combat-context exception, which is what this class actually implements.</b> §2.5:
/// <em>"a combat trigger <b>may</b> emit a run/board op — the sanctioned case is the Dicelord's
/// Scramble firing <c>MODIFY_DIE_FACE</c> from a <c>PERIODIC</c> trigger. The simulator still never
/// resolves it: it appends a <c>RunEffectQueued</c> event to the combat log (`05` §7) and the run
/// controller applies the queued ops <b>in log order when the battle resolves</b> — after the
/// outcome is fixed, before <c>ON_BATTLE_END</c> effects are granted. In a PvP duel the queue is
/// discarded."</em> So the op's whole combat-side behaviour is: queue, and touch nothing else.
/// </para>
/// <para>
/// ⚠️ <b>Their parameters are genuinely under-specified, and none is invented here.</b> §2.5 writes
/// <b>no</b> JSON example for any op but <c>MODIFY_DIE_FACE</c>, and promises things the eight-part
/// shape has no keys for: which currency <c>GRANT_CURRENCY</c> grants, which perk <c>GRANT_PERK</c>
/// picks, and <c>MODIFY_SHOP</c>'s <em>"slot count, price multiplier, forced rarity"</em> — three
/// numbers against one <c>value</c>. Those keys are M3's to author <b>when it builds the
/// resolvers</b>, because only a resolver knows what it needs. Adding them now would be five
/// vocabularies nobody has agreed, in the file everything else keys on (steering S6). This is the
/// one place M2-03's four `18` §10 extensions stop: those four ops have consumers landing in M2 and
/// these thirteen do not.
/// </para>
/// <para>
/// ⚠️ <b>The count is 13, not 12.</b> §2.5's table has twelve <em>rows</em> because
/// <c>APPLY_CURSE</c> / <c>CLEANSE_CURSE</c> share one, and they are two ops with opposite meanings.
/// The M2 kickoff record says "the 12 run/board ops"; that is a row count.
/// </para>
/// </remarks>
internal static class RunBoardOps
{
    /// <summary>
    /// Queues one `18` §2.5 op for the run controller, and does nothing else at all.
    /// </summary>
    /// <returns>
    /// The one runtime-resolved argument handed to the queue — <c>0</c> for every op whose arguments
    /// are all authored, which today is all thirteen.
    /// </returns>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Why the argument is 0.</b> `05` §7's <c>RunEffectQueued</c> carries at most one
    /// runtime-resolved scalar, <em>"the one runtime-resolved argument"</em> — for Scramble, which
    /// die face the roll picked. Nothing in M2 resolves such an argument: the Dicelord is M2-13's
    /// and the draw that picks the face is its own. Passing <c>effect.Value</c> instead would be
    /// wrong in a way that reads right, because an authored value is already on the effect and M3
    /// rebuilds the effect table to read it — sending it twice would let the two disagree.
    /// </para>
    /// <para>
    /// ⚠️ This is the seam M2-13 widens if the Dicelord needs its face index in the log: it passes
    /// the resolved face through <see cref="IRunEffectQueue.Queue"/>'s <c>argument</c>. The method
    /// below is the only caller, so that change lands in one place.
    /// </para>
    /// </remarks>
    internal static double Queue(EffectDefinition effect, EffectOpContext context)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(context);

        if (!EffectOps.IsRunAndBoard(effect.Op))
        {
            throw new EffectContextException(
                effect.Id,
                $"{effect.Op} is not a 18 §2.5 run or board op",
                "Only the RUN_AND_BOARD family is queued rather than resolved. Routing anything else " +
                "through here would make a combat op silently do nothing during the battle.");
        }

        context.Seams.RunQueue.Queue(effect, context.Holder, 0.0);

        return 0.0;
    }
}
