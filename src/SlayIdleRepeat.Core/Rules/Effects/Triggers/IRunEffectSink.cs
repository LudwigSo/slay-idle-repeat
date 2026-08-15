using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>Where a combat trigger's run/board op goes instead of being resolved.</summary>
/// <remarks>
/// <para>
/// A combat trigger may emit a run/board op — the sanctioned case is a periodic effect firing
/// <c>MODIFY_DIE_FACE</c>. The simulator still never resolves it: it appends a queued event to the
/// combat log, and the run controller applies queued ops in log order when the battle resolves. In a
/// PvP duel the queue is discarded.
/// </para>
/// <para>
/// An interface rather than a direct call into the combat log: the intra-<c>Rules</c> layering keeps
/// <c>Rules.Effects</c> from naming <c>Rules.Combat</c> directly, so a direct call would put a cycle
/// between the two namespaces.
/// </para>
/// <para>Nothing consumes the queue yet — draining it, and discarding it in a duel, are both later concerns this seam leaves clean room for.</para>
/// </remarks>
internal interface IRunEffectSink
{
    /// <summary>Records that a combat trigger fired an effect whose op is a run/board one, for the run controller to apply when the battle resolves.</summary>
    /// <param name="tick">The tick the trigger fired on.</param>
    /// <param name="source">The actor whose effect fired.</param>
    /// <param name="effect">The effect. The implementer resolves it to whatever index the combat log carries.</param>
    /// <param name="argument">The op's one runtime-resolved argument, already rounded to 4 dp. <c>0</c> when every argument is authored.</param>
    void QueueRunEffect(int tick, IEffectActorView source, EffectDefinition effect, double argument);
}
