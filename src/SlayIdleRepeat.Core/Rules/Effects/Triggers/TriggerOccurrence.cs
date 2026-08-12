using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>
/// 🔒 One <b>moment</b> a trigger could fire on — what the tick loop hands
/// <see cref="TriggerRegistry.Evaluate"/> when it reaches a slot of `05` §3.1.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Keyed on <see cref="TriggerKind"/> rather than on a parallel "occasion" enum.</b> A second
/// vocabulary of 23 members would have to agree with the first with nothing making it — the reason
/// <c>EffectDefinition.StatusId</c> is a string rather than a thirteenth enum, and the reason
/// <c>IRunStateView.DieFaceCount</c> takes one. The moment an attack happens <em>is</em> the
/// <c>ON_ATTACK</c> moment.
/// </para>
/// <para>
/// 🔒 <b>Time is a <see cref="Tick"/>, not seconds.</b> `05` §3 is a fixed-tick simulation and every
/// timed trigger's arithmetic is integer — see <see cref="TriggerSchedule"/> for why that is a
/// determinism property rather than a convenience. The tick loop already holds the tick number.
/// </para>
/// <para>
/// 🔒 <b>Handed in, never derived</b>, exactly as <c>EffectEvaluationContext</c> is: there is no
/// clock here, no roster and no ambient state. A trigger predicate is a pure function of the
/// instance's own recorded state and this record, which is what lets M2-08 drive it from a loop this
/// task did not write, and lets this task's tests drive it from a hand-cranked one.
/// </para>
/// <para>
/// ⚠️ <b>Everything but <see cref="Kind"/> and <see cref="Tick"/> is occasion-specific and read only
/// by the kinds that need it.</b> A <c>Phase</c> on an <c>ON_HIT</c> moment is ignored rather than
/// refused: unlike an authored trigger parameter, this is a struct the caller fills in one place per
/// slot, and refusing a stale field would make M2-08's call sites carry a clearing step whose only
/// purpose is to satisfy this type.
/// </para>
/// </remarks>
internal readonly record struct TriggerOccurrence
{
    /// <summary>Which `18` §3 kind this moment can fire.</summary>
    internal required TriggerKind Kind { get; init; }

    /// <summary>
    /// The `05` §3 tick the moment falls on, <c>0..1799</c>. The battle-start pre-tick's moments
    /// carry tick <c>0</c> — `05` §3.1 runs it before tick 0 and there is no tick <c>-1</c>.
    /// </summary>
    internal required int Tick { get; init; }

    /// <summary>
    /// 🔒 `05` §3.3 — whether this is a Ghost Duel. <c>ON_KILL</c> triggers <b>never fire in
    /// duels</b>: <em>"The only death in a duel ends the fight."</em>
    /// </summary>
    /// <remarks>
    /// M2-14 builds the duel; what is settled here is that the predicate answers correctly when told
    /// it is one, and that a duel kill does not advance the run-scoped counter either — see
    /// <see cref="TriggerInstance"/>.
    /// </remarks>
    internal bool IsDuel { get; init; }

    /// <summary>
    /// <see cref="TriggerKind.ON_BATTLE_END"/> — whether the hero side won, read by
    /// <c>onlyIfWon</c>.
    /// </summary>
    internal bool HeroWon { get; init; }

    /// <summary>
    /// <see cref="TriggerKind.ON_PHASE_ENTER"/> — the phase just entered, <c>1..3</c> (`05` §6.3).
    /// </summary>
    /// <remarks>
    /// `05` §3.1's phase check may enter two phases inside one tick — <em>"a burst from 70% to 20%
    /// therefore fires phase 2's entry, then phase 3's"</em> — which is two occurrences at the same
    /// <see cref="Tick"/>, in that order, not one occurrence carrying two phases.
    /// </remarks>
    internal int Phase { get; init; }

    /// <summary>
    /// <see cref="TriggerKind.ON_LOW_HP"/> — the holder's HP fraction <b>after</b> the change that
    /// caused this moment, <c>0..1</c>.
    /// </summary>
    /// <remarks>
    /// 🔒 The <em>crossing</em> is not the caller's to detect. `18` §3 says <em>"self HP crosses a
    /// threshold downward"</em>, and a crossing needs the previous reading as well as this one;
    /// <see cref="TriggerInstance"/> holds it, so every instance watching a different threshold gets
    /// its own answer from one reading. A caller that had to compute the crossing would need one
    /// previous-HP field per threshold in the battle.
    /// </remarks>
    internal double HpFraction { get; init; }

    /// <summary><see cref="TriggerKind.ON_TILE_RESOLVED"/> — the <c>TILE_*</c> id that resolved.</summary>
    internal string? TileType { get; init; }

    /// <summary><see cref="TriggerKind.ON_ROLL"/> — the `04` §1 face kind the roll produced.</summary>
    internal string? FaceKind { get; init; }

    /// <summary><see cref="TriggerKind.ON_PERK_TAKEN"/> — the `06` §2 category of the perk drafted.</summary>
    internal string? Category { get; init; }
}
