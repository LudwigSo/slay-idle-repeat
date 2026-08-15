namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>What <see cref="TriggerRegistry.Evaluate"/> decided, and — when it decided not to fire — which rule decided it.</summary>
/// <remarks>
/// A bool would be a defect, not a simplification: when several rules can produce the same "did not
/// fire" answer, collapsing them would let a trigger silently held back by the wrong rule (e.g. a
/// spent <c>once</c> latch instead of its <c>everyNth</c> counter) pass a test that only checks
/// whether it fired.
/// </remarks>
internal enum TriggerOutcome
{
    /// <summary>The trigger fires. Everything the effect does next is the op layer's.</summary>
    FIRES = 1,

    /// <summary>The moment is not this trigger's kind — an <c>ON_HIT</c> instance asked about an <c>ON_CRIT</c> moment.</summary>
    WRONG_KIND = 2,

    /// <summary>The instance is not active: its scoped effect has ended, or it hasn't been activated yet.</summary>
    NOT_ACTIVE = 3,

    /// <summary>An <c>ON_KILL</c> in a Ghost Duel — the only death in a duel ends the fight. Does not fire, and does not count either.</summary>
    NEVER_FIRES_IN_A_DUEL = 4,

    /// <summary><c>once</c> has already been spent this battle.</summary>
    ONCE_SPENT = 5,

    /// <summary>The internal <c>cooldown</c> has not elapsed.</summary>
    COOLDOWN_ACTIVE = 6,

    /// <summary>The <c>everyNth</c> counter advanced, but this occurrence is not an Nth one.</summary>
    EVERY_NTH_PENDING = 7,

    /// <summary><c>chance</c> was drawn and missed.</summary>
    CHANCE_MISSED = 8,

    /// <summary><c>onlyIfWon</c> is set and the hero side lost.</summary>
    ONLY_IF_WON_AND_LOST = 9,

    /// <summary>The phase entered is not the one <c>phase</c> names.</summary>
    PHASE_MISMATCH = 10,

    /// <summary><c>threshold</c> was not crossed downward by this HP change.</summary>
    THRESHOLD_NOT_CROSSED = 11,

    /// <summary>The run-layer filter did not match — the tile, die face or perk category isn't the one this trigger names.</summary>
    FILTER_MISMATCH = 12,

    /// <summary><see cref="Content.Effects.TriggerKind.PERIODIC"/> is not due on this tick. Reachable only through <see cref="TriggerRegistry.PeriodicDue"/>.</summary>
    NOT_DUE = 13,
}
