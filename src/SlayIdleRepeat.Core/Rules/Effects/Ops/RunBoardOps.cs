using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>The thirteen run and board ops — declared, validated, and deliberately not resolved.</summary>
/// <remarks>
/// <para>
/// These are resolved by the run controller, never by the combat simulator, which doesn't exist yet
/// on this branch — a declared op with no resolver is the correct end state for now, not an
/// omission. What this class implements is the one combat-context exception: a combat trigger may
/// emit a run/board op (e.g. a periodic effect firing <c>MODIFY_DIE_FACE</c>). The simulator still
/// never resolves it — it appends a queued event to the combat log, and the run controller applies
/// queued ops in log order when the battle resolves. In a PvP duel the queue is discarded.
/// </para>
/// <para>
/// Their parameters are genuinely under-specified, and none is invented here — which currency
/// <c>GRANT_CURRENCY</c> grants, which perk <c>GRANT_PERK</c> picks, and so on are keys for whoever
/// builds the real resolvers to author, since only a resolver knows what it needs.
/// </para>
/// <para>The count is 13, not 12 — <c>APPLY_CURSE</c> and <c>CLEANSE_CURSE</c> share one table row but are two ops with opposite meanings.</para>
/// </remarks>
internal static class RunBoardOps
{
    /// <summary>Queues one run/board op for the run controller, and does nothing else at all.</summary>
    /// <returns>The one runtime-resolved argument handed to the queue — <c>0</c> for every op whose arguments are all authored, which today is all thirteen.</returns>
    /// <remarks>
    /// Nothing today resolves a runtime argument for any of these ops, so 0 is always correct for
    /// now. Passing <c>effect.Value</c> instead would be wrong in a way that reads right: an authored
    /// value is already on the effect, so sending it twice would let the two disagree if the run
    /// controller ever reads both.
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
