namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>The run-scoped half of <c>everyNth</c> counters — read and write.</summary>
/// <remarks>
/// <para>
/// <c>ON_ATTACK</c> counters reset at battle start and need no seam — a fresh battle gets a fresh
/// <see cref="TriggerRegistry"/>. <c>ON_KILL</c> counters persist across battles for the run, which
/// needs state that outlives the battle, living on the <c>Run</c> aggregate under a run controller.
/// </para>
/// <para>
/// Shaped after <see cref="IRunStateView"/>, with one difference: that view is read-only since
/// conditions must be pure functions of state, but a run-scoped counter is state the act of killing
/// advances, so this seam has a write path.
/// </para>
/// <para>
/// The key is an <see cref="EffectInstanceId"/> — a stable name for one holding, not an effect id and
/// not an actor id, since two copies of one effect count separately and a battle-local actor doesn't
/// survive to the next fight. An unknown key reads <c>0</c>. Persistence is whatever the run's own
/// snapshot does with the pairs.
/// </para>
/// <para>A duel never writes here — <c>ON_KILL</c> triggers never fire in a duel, and a duel has no run.</para>
/// </remarks>
internal interface IRunTriggerCounters
{
    /// <summary>Every instance the run holds a count for, in ascending ordinal id order — what the run persists.</summary>
    /// <remarks>
    /// On the interface, not the implementation, so a run's snapshot can enumerate the pairs without
    /// depending on the concrete type. Ordered deterministically rather than by dictionary
    /// enumeration order, which is a property of the runtime and would make the same run hash
    /// differently across platforms.
    /// </remarks>
    IReadOnlyList<KeyValuePair<EffectInstanceId, int>> Entries { get; }

    /// <summary>
    /// How many qualifying occurrences this instance has accumulated over the run so far.
    /// </summary>
    /// <param name="instance">The effect instance's stable run-scoped id.</param>
    /// <returns><c>0</c> for an instance the run has no count for.</returns>
    int Read(EffectInstanceId instance);

    /// <summary>Records this instance's new count.</summary>
    /// <param name="instance">The effect instance's stable run-scoped id.</param>
    /// <param name="count">The new count. Never negative — a counter only advances.</param>
    void Write(EffectInstanceId instance, int count);
}
