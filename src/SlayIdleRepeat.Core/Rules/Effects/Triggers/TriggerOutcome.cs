namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>
/// 🔒 What <see cref="TriggerRegistry.Evaluate"/> decided, and — when it decided not to fire —
/// <b>which rule decided it</b>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>A bool would be a defect, not a simplification.</b> Steering S2: when several rules can
/// produce the same answer, a test must pin <em>which one fired</em>, not merely that something did.
/// Eleven of the twelve refusals below are "did not fire", and a <c>PK_FLURRY</c> silently held back
/// by a spent <c>once</c> latch instead of by its <c>everyNth</c> counter is a bug whose test passes.
/// </para>
/// <para>
/// It is also what makes the guards testable at all: a rule that cannot be observed separately from
/// its neighbours cannot be made to fail on purpose (steering S1).
/// </para>
/// </remarks>
internal enum TriggerOutcome
{
    /// <summary>The trigger fires. Everything the effect does next is the op layer's (M2-03).</summary>
    FIRES = 1,

    /// <summary>
    /// The moment is not this trigger's kind — an <c>ON_HIT</c> instance asked about an
    /// <c>ON_CRIT</c> moment.
    /// </summary>
    WRONG_KIND = 2,

    /// <summary>
    /// The instance is not active: its `18` §6 <c>PHASE</c>-scoped effect ended when the boss left
    /// the phase (`05` §3.1), or it has not been activated yet.
    /// </summary>
    NOT_ACTIVE = 3,

    /// <summary>
    /// 🔒 `05` §3.3 — an <c>ON_KILL</c> in a Ghost Duel: <em>"The only death in a duel ends the
    /// fight."</em> It does not fire, and it does not count either — see
    /// <see cref="TriggerInstance"/>.
    /// </summary>
    NEVER_FIRES_IN_A_DUEL = 4,

    /// <summary>`18` §3's <c>once</c> has already been spent this battle (R2: a boolean).</summary>
    ONCE_SPENT = 5,

    /// <summary>`18` §3's internal <c>cooldown</c> has not elapsed.</summary>
    COOLDOWN_ACTIVE = 6,

    /// <summary>
    /// `18` §3's <c>everyNth</c> counter advanced, but this occurrence is not an Nth one.
    /// </summary>
    EVERY_NTH_PENDING = 7,

    /// <summary>`18` §3's <c>chance</c> was drawn and missed.</summary>
    CHANCE_MISSED = 8,

    /// <summary>`18` §3's <c>onlyIfWon</c> is set and the hero side lost.</summary>
    ONLY_IF_WON_AND_LOST = 9,

    /// <summary>The phase entered is not the one `18` §3's <c>phase</c> names.</summary>
    PHASE_MISMATCH = 10,

    /// <summary>
    /// `18` §3's <c>threshold</c> was not crossed <b>downward</b> by this HP change — the reading is
    /// still above it, or it was already at or below it before.
    /// </summary>
    THRESHOLD_NOT_CROSSED = 11,

    /// <summary>
    /// The run-layer filter did not match: the tile, die face or perk category is not the one
    /// `18` §3's <c>tileType</c>, <c>faceKind</c> or <c>category</c> names.
    /// </summary>
    FILTER_MISMATCH = 12,

    /// <summary>
    /// <see cref="Content.Effects.TriggerKind.PERIODIC"/> is not due on this tick. Reachable only
    /// through <see cref="TriggerRegistry.PeriodicDue"/>, which is the sole <c>PERIODIC</c> path.
    /// </summary>
    NOT_DUE = 13,
}
