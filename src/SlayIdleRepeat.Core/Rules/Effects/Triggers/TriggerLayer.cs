namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>Which loop fires a trigger kind — the one fact that decides whether the tick loop or the run controller is the caller.</summary>
/// <remarks>
/// Stating it once here is what lets <see cref="TriggerRegistry"/> refuse a run-layer occurrence
/// handed to the combat loop instead of silently answering "does not fire", which would be
/// indistinguishable from a wiring bug that never fires anything.
/// </remarks>
internal enum TriggerLayer
{
    /// <summary><see cref="Content.Effects.TriggerKind.ALWAYS"/> alone. Has no occasion at all — re-evaluated at every resolution pass rather than fired by an event — and both loops see it.</summary>
    PASSIVE = 1,

    /// <summary>Fired inside a battle.</summary>
    COMBAT = 2,

    /// <summary>Fired by the run controller — a tile resolving, a die roll, a perk draft, a stage boundary, a run beginning or ending.</summary>
    RUN = 3,
}
