namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>
/// 🔒 The <b>run-scoped</b> half of `18` §3's <c>everyNth</c> counters — read <b>and</b> write.
/// </summary>
/// <remarks>
/// <para>
/// `18` §3: <em>"<c>ON_ATTACK</c> counters reset at battle start; <c>ON_KILL</c> counters
/// <b>persist across battles for the run</b> (<c>PK_MIDAS</c>'s 'every 6th enemy killed')."</em> The
/// first half needs no seam at all: a battle gets a fresh <see cref="TriggerRegistry"/> and the
/// counters go with it, so the reset is structural rather than a step somebody has to remember. The
/// second half needs state that outlives the battle, and that state lives on the <c>Run</c> aggregate
/// (M1-05) under a run controller (M3) — neither of which exists.
/// </para>
/// <para>
/// 🔒 <b>This is a cross-milestone contract, shaped after <see cref="IRunStateView"/>.</b> Same
/// reasoning, one difference: <c>IRunStateView</c> is deliberately read-only, because `18` §4's
/// conditions are <em>"pure functions of current state"</em> and a condition that wrote would be a
/// determinism defect. A <c>PK_MIDAS</c> counter is not a condition — it is state the act of killing
/// <b>advances</b>, and `18` §3 says so — so this seam has a write path and
/// <c>IRunStateView</c> must never grow one.
/// </para>
/// <para>
/// 🔒 <b>Narrow, and to stay narrow.</b> Three members over one key type. The <c>Run</c> aggregate
/// cannot implement it directly for the reason <see cref="IRunStateView"/> records — `30` §11.4's
/// <em>"<c>Model</c> never references <c>Rules</c>"</em>, enforced by
/// <c>AccessibilityBoundaryTests.Core_internal_layering_holds</c> — so M3 honours it with a
/// projection on this side of the seam, exactly as <see cref="RunStateReading"/> is for the read-only
/// view. <see cref="RunTriggerCounters"/> is that shape, and it ships here with the shared contract
/// suite every later implementation is run through (steering S7).
/// </para>
/// <para>
/// ⚠️ <b>What M3 and M1-05 must honour, stated plainly.</b> The key is an
/// <see cref="EffectInstanceId"/>: a stable, ordinally-compared name for one <em>holding</em> — a
/// perk in a draft slot, an affix on a gear item — <b>not</b> an effect id and <b>not</b> an actor id,
/// because two copies of one effect count separately and a battle-local actor does not survive to the
/// next fight. An unknown key reads <c>0</c>: a run that has never killed anything with
/// <c>PK_MIDAS</c> has a count of zero, which is a reading and not an error. Persistence is whatever
/// the run's own snapshot does with the pairs; nothing here writes to disk, and there is deliberately
/// no placeholder run controller in M2.
/// </para>
/// <para>
/// ⚠️ <b>A duel never writes here.</b> `05` §3.3 rules that <c>ON_KILL</c> triggers never fire in a
/// duel, and a duel is not part of a run — <c>EffectEvaluationContext.Run</c> is <c>null</c> in one.
/// <see cref="TriggerInstance"/> refuses the advance, so the seam is never reached; an implementation
/// does not need to defend against it and must not silently absorb it if it happens.
/// </para>
/// </remarks>
internal interface IRunTriggerCounters
{
    /// <summary>
    /// 🔒 Every instance the run holds a count for, <b>in ascending ordinal id order</b> — what M3
    /// persists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>On the interface, not on the implementation, because otherwise the seam does not close.</b>
    /// The obligation this contract creates on M3 is that a run's snapshot carries these pairs; a
    /// seam that could only be <em>written</em> and <em>read one key at a time</em> would force M3 to
    /// abandon it and depend on the concrete type — at which point the interface buys nothing at all.
    /// </para>
    /// <para>
    /// 🔒 <b>Ordered, and ordered by the implementation.</b> A dictionary's enumeration order is a
    /// property of the runtime, and `14` §8.2 hashes the run snapshot on x64 and on ARM64 and
    /// compares — so a set of pairs serialised in insertion order would make the same run hash
    /// differently depending on which enemy the hero happened to kill first. Ordinal, for
    /// <c>EffectOrder</c>'s reason.
    /// </para>
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
