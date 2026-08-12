namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>
/// 🔒 Which loop fires a `18` §3 trigger kind — the one fact that decides whether M2-08's tick loop
/// or M3's run controller is the caller.
/// </summary>
/// <remarks>
/// <para>
/// `18` §3 does not tabulate this, but it is implicit in every row and `05` §3.1 depends on it: the
/// tick order names <c>ON_BATTLE_START</c>, <c>PERIODIC</c>, the on-hit family and <c>ON_DEATH</c>
/// in its slots and never mentions <c>ON_ROLL</c> or <c>ON_TILE_RESOLVED</c>, because a die roll does
/// not happen inside a battle. Stating it once here is what lets
/// <see cref="TriggerRegistry"/> refuse a run-layer occurrence handed to the combat loop instead of
/// silently answering "does not fire" — which is indistinguishable from a wiring bug that never
/// fires anything.
/// </para>
/// <para>
/// ⚠️ The six <see cref="RUN"/> kinds are declared and unit-tested here and <b>not wired</b>: the run
/// controller that fires them is M3, over a <c>Run</c> aggregate that is M1-05, and neither exists.
/// That is the finished state for M2, not a gap — see <see cref="TriggerCatalogue"/>.
/// </para>
/// </remarks>
internal enum TriggerLayer
{
    /// <summary>
    /// <see cref="Content.Effects.TriggerKind.ALWAYS"/> alone — `18` §3: <em>"Passive, always
    /// active"</em>. It has no occasion at all; it is re-evaluated at every resolution pass
    /// (`18` §1.1) rather than fired by an event, and both loops see it.
    /// </summary>
    PASSIVE = 1,

    /// <summary>Fired inside a battle, from one of `05` §3.1's slots.</summary>
    COMBAT = 2,

    /// <summary>
    /// Fired by the run controller (M3) — a tile resolving, a die roll, a perk draft, a stage
    /// boundary, a run beginning or ending. Never reached from `05` §3.1.
    /// </summary>
    RUN = 3,
}
