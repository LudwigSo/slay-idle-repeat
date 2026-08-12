using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>
/// What happens to an effect that has just fired — `18` §2.5's one boundary, decided in one place.
/// </summary>
internal enum EffectRouting
{
    /// <summary>The simulator resolves it now. Every combat op, and every op fired by a run trigger.</summary>
    RESOLVE = 1,

    /// <summary>
    /// A <b>combat</b> trigger fired a `18` §2.5 <b>run/board</b> op. The simulator never resolves
    /// it: it is appended to the combat log as <c>RunEffectQueued</c> and M3 applies it when the
    /// battle resolves.
    /// </summary>
    QUEUE_FOR_RUN = 2,

    /// <summary>
    /// The same, in a Ghost Duel — where `18` §2.5 says <em>"the queue is discarded"</em>. Nothing
    /// is emitted and nothing is resolved.
    /// </summary>
    DISCARDED_IN_A_DUEL = 3,
}

/// <summary>
/// 🔒 `18` §2.5's combat-context exception, applied: <b>a combat trigger carrying a run op emits,
/// it never resolves.</b>
/// </summary>
/// <remarks>
/// <para>
/// The rule has three arms and they are easy to collapse into two by accident:
/// </para>
/// <list type="bullet">
///   <item>
///     A <b>combat</b> trigger (`18` §3's <see cref="TriggerLayer.COMBAT"/> kinds, and
///     <c>ALWAYS</c>) carrying a `18` §2.5 op — the Dicelord's Scramble, a
///     <c>PERIODIC</c> firing <c>MODIFY_DIE_FACE</c> — is <b>queued</b>. The simulator stays pure:
///     <em>"these are resolved by the run controller, never by the combat simulator"</em>.
///   </item>
///   <item>
///     A <b>run</b> trigger carrying the same op — `18` §7.9's <c>TILE_DICE_FORGE</c>, an
///     <c>ON_TILE_RESOLVED</c> firing <c>MODIFY_DIE_FACE</c> — is <b>resolved</b>. It is already on
///     the run layer; queueing it would post a letter to the room it is standing in.
///   </item>
///   <item>
///     In a duel the queue is <b>discarded</b> (`18` §2.5, consistent with §9.3's <c>IS_PVP</c>
///     skipping). A duel has no run to apply anything to.
///   </item>
/// </list>
/// <para>
/// 🔒 <b>Stated once, because getting it wrong is invisible.</b> A combat trigger that resolved a run
/// op would mutate run state from inside the simulator — which `14` §2.4 has the client re-run and
/// `11` §6 has the server re-run, so the mutation would happen twice, or once on a machine that has
/// no run at all.
/// </para>
/// </remarks>
internal static class TriggerRouting
{
    /// <summary>
    /// Where an effect that has just fired goes.
    /// </summary>
    /// <param name="effect">The effect that fired. Its trigger decides which layer fired it.</param>
    /// <param name="isDuel">`05` §3.3 — whether this is a Ghost Duel.</param>
    internal static EffectRouting RouteOf(EffectDefinition effect, bool isDuel)
    {
        ArgumentNullException.ThrowIfNull(effect);

        if (!EffectOps.IsRunAndBoard(effect.Op))
        {
            return EffectRouting.RESOLVE;
        }

        // 🔒 An effect with no trigger is not a combat trigger, so it cannot be the 18 §2.5
        // exception. 18 §9.1's CP_GLASS_HEART and §7.7's pet actives are the authored triggerless
        // effects and neither carries a run op; if one ever does, its wrapper decides when it fires
        // and its wrapper is the run layer's or the battle's — not this function's to guess.
        if (effect.Trigger is not { } trigger ||
            TriggerCatalogue.LayerOf(trigger.Kind) == TriggerLayer.RUN)
        {
            return EffectRouting.RESOLVE;
        }

        return isDuel ? EffectRouting.DISCARDED_IN_A_DUEL : EffectRouting.QUEUE_FOR_RUN;
    }

    /// <summary>
    /// 🔒 Routes a fired effect: queues it through <paramref name="sink"/> when `18` §2.5 says to,
    /// discards it in a duel, and otherwise says "resolve it" without touching it.
    /// </summary>
    /// <param name="effect">The effect that fired.</param>
    /// <param name="occurrence">The moment it fired on — its tick and whether this is a duel.</param>
    /// <param name="source">The actor whose effect fired.</param>
    /// <param name="sink">Where a queued op goes. Only reached for <see cref="EffectRouting.QUEUE_FOR_RUN"/>.</param>
    /// <param name="argument">The op's one runtime-resolved argument, rounded to 4 dp.</param>
    /// <returns>What was done, so the caller resolves only what it should.</returns>
    /// <exception cref="EffectContextException">
    /// The effect must be queued and no sink was handed in.
    /// </exception>
    internal static EffectRouting Route(
        EffectDefinition effect,
        in TriggerOccurrence occurrence,
        IEffectActorView source,
        IRunEffectSink? sink,
        double argument = 0.0)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(source);

        var routing = RouteOf(effect, occurrence.IsDuel);

        if (routing != EffectRouting.QUEUE_FOR_RUN)
        {
            return routing;
        }

        // 🔒 A missing sink is a THROW, not a silent drop. Dropping it would look exactly like the
        // duel case and the Dicelord's Scramble — which "persists into the remainder of the run if
        // the player survives" (`17` §9) — would simply stop happening, in a fight nobody replays.
        if (sink is null)
        {
            throw new EffectContextException(
                effect.Id,
                "it is a combat trigger carrying a `18` §2.5 run/board op and no run-effect sink was handed in",
                "`18` §2.5: the simulator never resolves such an op, it appends a RunEffectQueued " +
                "event and the run controller applies it when the battle resolves. With nowhere to " +
                "append, the op would be silently lost.");
        }

        sink.QueueRunEffect(occurrence.Tick, source, effect, argument);

        return routing;
    }
}
