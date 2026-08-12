namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>The five op families of `18` §2.</summary>
/// <remarks>
/// The families are not decoration. `18` §2.5 draws the one boundary the simulator has to honour:
/// run and board ops are <em>"resolved by the run controller, never by the combat simulator"</em>.
/// <see cref="EffectOps.IsRunAndBoard"/> is that predicate, stated once so sixteen call sites cannot
/// each get it slightly wrong.
/// </remarks>
public enum EffectOpFamily
{
    /// <summary>`18` §2.1 — six stat operations.</summary>
    STAT = 1,

    /// <summary>`18` §2.2 — seven damage and healing operations.</summary>
    DAMAGE_AND_HEALING = 2,

    /// <summary>`18` §2.3 — six status operations.</summary>
    STATUS = 3,

    /// <summary>`18` §2.4 — eleven combat-flow operations.</summary>
    COMBAT_FLOW = 4,

    /// <summary>`18` §2.5 — thirteen run and board operations, resolved by the run controller.</summary>
    RUN_AND_BOARD = 5,
}
