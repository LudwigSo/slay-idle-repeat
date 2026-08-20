using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>What happens to an effect that has just fired — one boundary, decided in one place.</summary>
internal enum EffectRouting
{
    /// <summary>The simulator resolves it now. Every combat op, and every op fired by a run trigger.</summary>
    RESOLVE = 1,

    /// <summary>A combat trigger fired a run/board op. The simulator never resolves it — it's appended to the combat log and the run controller applies it when the battle resolves.</summary>
    QUEUE_FOR_RUN = 2,

    /// <summary>The same, in a Ghost Duel, where the queue is discarded. Nothing is emitted and nothing is resolved.</summary>
    DISCARDED_IN_A_DUEL = 3,
}

/// <summary>The combat-context exception, applied: a combat trigger carrying a run op emits, it never resolves.</summary>
/// <remarks>
/// <para>The rule has three arms, easy to collapse into two by accident:</para>
/// <list type="bullet">
///   <item>Fired by the combat loop and carrying a run/board op — it's queued. The simulator stays pure.</item>
///   <item>Fired by the run controller — an <c>ON_TILE_RESOLVED</c> effect firing <c>MOVE_NODES</c>,
///   or an <c>ALWAYS</c> perk carrying <c>MODIFY_SHOP</c> — it's resolved. It's already on the run
///   layer; queueing it would post a letter to the room it's standing in.</item>
///   <item>In a duel the queue is discarded — a duel has no run to apply anything to.</item>
/// </list>
/// <para>
/// Stated once, because getting it wrong is invisible: a combat trigger that resolved a run op would
/// mutate run state from inside the simulator, which is re-run by both client and server, so the
/// mutation would happen twice — or once on a machine that has no run at all.
/// </para>
/// </remarks>
internal static class TriggerRouting
{
    /// <summary>Where an effect that has just fired goes.</summary>
    /// <param name="effect">The effect that fired.</param>
    /// <param name="firedBy">Which loop fired it. See the remarks for why this is the caller's layer and not the trigger's.</param>
    /// <param name="isPvp">Whether this is a Ghost Duel.</param>
    /// <remarks>
    /// The layer is the caller's, because <c>ALWAYS</c> has no layer of its own —
    /// <see cref="TriggerLayer.PASSIVE"/> means both loops see it, so reading the layer off the
    /// trigger would misclassify a passive-carried run op (e.g. <c>MODIFY_SHOP</c> on an
    /// <c>ALWAYS</c> perk) as a combat emission when the run controller evaluates it off the board.
    /// </remarks>
    internal static EffectRouting RouteOf(EffectDefinition effect, TriggerLayer firedBy, bool isPvp)
    {
        ArgumentNullException.ThrowIfNull(effect);

        if (!EffectOps.IsRunAndBoard(effect.Op))
        {
            return EffectRouting.RESOLVE;
        }

        // The run controller resolves its own run ops — queueing one already on the run layer would
        // post a letter to the room it's standing in.
        if (firedBy == TriggerLayer.RUN)
        {
            return EffectRouting.RESOLVE;
        }

        // An effect with no trigger is fired by its wrapper, and the wrapper's layer is the one
        // above — firedBy answers for it rather than a trigger that isn't there.
        if (effect.Trigger is { } trigger && TriggerCatalogue.LayerOf(trigger.Kind) == TriggerLayer.RUN)
        {
            return EffectRouting.RESOLVE;
        }

        return isPvp ? EffectRouting.DISCARDED_IN_A_DUEL : EffectRouting.QUEUE_FOR_RUN;
    }

    /// <summary>Routes a fired effect: queues it through <paramref name="sink"/> when routing says to, discards it in a duel, and otherwise says "resolve it" without touching it.</summary>
    /// <param name="effect">The effect that fired.</param>
    /// <param name="occurrence">The moment it fired on — its tick and whether this is a duel.</param>
    /// <param name="firedBy">Which loop fired it. See <see cref="RouteOf"/>.</param>
    /// <param name="source">The actor whose effect fired.</param>
    /// <param name="sink">Where a queued op goes. Only reached for <see cref="EffectRouting.QUEUE_FOR_RUN"/>.</param>
    /// <param name="argument">The op's one runtime-resolved argument. Rounded to 4 dp here, since it crosses into the combat log, which is hashed for cross-platform replay.</param>
    /// <returns>What was done, so the caller resolves only what it should.</returns>
    /// <exception cref="EffectContextException">The effect must be queued and no sink was handed in.</exception>
    internal static EffectRouting Route(
        EffectDefinition effect,
        in TriggerOccurrence occurrence,
        TriggerLayer firedBy,
        IEffectActorView source,
        IRunEffectSink? sink,
        double argument = 0.0)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(source);

        var routing = RouteOf(effect, firedBy, occurrence.IsPvp);

        if (routing != EffectRouting.QUEUE_FOR_RUN)
        {
            return routing;
        }

        // A missing sink throws rather than silently dropping the op, which would look exactly like
        // the duel case and make a queued effect simply stop happening in a fight nobody replays.
        if (sink is null)
        {
            throw new EffectContextException(
                effect.Id,
                "it is a combat trigger carrying a `18` §2.5 run/board op and no run-effect sink was handed in",
                "`18` §2.5: the simulator never resolves such an op, it appends a RunEffectQueued " +
                "event and the run controller applies it when the battle resolves. With nowhere to " +
                "append, the op would be silently lost.");
        }

        sink.QueueRunEffect(occurrence.Tick, source, effect, DeterminismRounding.Round(argument));

        return routing;
    }
}
