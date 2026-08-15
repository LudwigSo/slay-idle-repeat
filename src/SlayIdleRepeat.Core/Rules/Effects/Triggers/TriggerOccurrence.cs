using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>One moment a trigger could fire on — what the tick loop hands <see cref="TriggerRegistry.Evaluate"/>.</summary>
/// <remarks>
/// <para>Keyed on <see cref="TriggerKind"/> rather than a parallel "occasion" enum, so the two vocabularies can't drift — the moment an attack happens is the <c>ON_ATTACK</c> moment.</para>
/// <para>Time is a <see cref="Tick"/>, not seconds — the simulation is fixed-tick and every timed trigger's arithmetic is integer.</para>
/// <para>Handed in, never derived — no clock, no roster, no ambient state. A trigger predicate is a pure function of the instance's own recorded state and this record.</para>
/// <para>Everything but <see cref="Kind"/> and <see cref="Tick"/> is occasion-specific and read only by the kinds that need it; a stray field on the wrong occasion is ignored rather than refused.</para>
/// </remarks>
internal readonly record struct TriggerOccurrence
{
    /// <summary>Which kind this moment can fire.</summary>
    internal required TriggerKind Kind { get; init; }

    /// <summary>The tick the moment falls on. The battle-start pre-tick's moments carry tick <c>0</c>.</summary>
    internal required int Tick { get; init; }

    /// <summary>Whether this is a Ghost Duel. <c>ON_KILL</c> triggers never fire in duels.</summary>
    /// <remarks>
    /// Named <c>IsPvp</c> to match <c>EffectEvaluationContext.IsPvp</c> — one fact, one spelling.
    /// Derive it from that context's value rather than setting it independently, or <c>ON_KILL</c>
    /// could fire in a duel while also silently advancing the run-scoped counter.
    /// </remarks>
    internal bool IsPvp { get; init; }

    /// <summary><see cref="TriggerKind.ON_BATTLE_END"/> — whether the hero side won, read by <c>onlyIfWon</c>.</summary>
    internal bool HeroWon { get; init; }

    /// <summary><see cref="TriggerKind.ON_PHASE_ENTER"/> — the phase just entered.</summary>
    /// <remarks>A boss can enter two phases inside one tick on a big HP burst — that's two occurrences at the same tick, in order, not one occurrence carrying two phases.</remarks>
    internal int Phase { get; init; }

    /// <summary><see cref="TriggerKind.ON_LOW_HP"/> — the holder's HP fraction after the change that caused this moment.</summary>
    /// <remarks>The crossing itself is not the caller's to detect — that needs the previous reading too, which <see cref="TriggerInstance"/> holds per threshold.</remarks>
    internal double HpFraction { get; init; }

    /// <summary><see cref="TriggerKind.ON_TILE_RESOLVED"/> — the tile id that resolved.</summary>
    internal string? TileType { get; init; }

    /// <summary><see cref="TriggerKind.ON_ROLL"/> — the die face kind the roll produced.</summary>
    internal string? FaceKind { get; init; }

    /// <summary><see cref="TriggerKind.ON_PERK_TAKEN"/> — the category of the perk drafted.</summary>
    internal string? Category { get; init; }
}
