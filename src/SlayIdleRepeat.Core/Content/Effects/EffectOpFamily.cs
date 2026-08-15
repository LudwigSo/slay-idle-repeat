namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>The five op families.</summary>
/// <remarks>
/// The families are not decoration: they draw the one boundary the simulator has to honour — run
/// and board ops are resolved by the run controller, never by the combat simulator.
/// <see cref="EffectOps.IsRunAndBoard"/> is that predicate, stated once so many call sites cannot
/// each get it slightly wrong.
/// </remarks>
public enum EffectOpFamily
{
    /// <summary>Six stat operations.</summary>
    STAT = 1,

    /// <summary>Seven damage and healing operations.</summary>
    DAMAGE_AND_HEALING = 2,

    /// <summary>Six status operations.</summary>
    STATUS = 3,

    /// <summary>Twelve combat-flow operations, the twelfth being <see cref="EffectOp.RANDOM_OUTCOME"/>.</summary>
    COMBAT_FLOW = 4,

    /// <summary>Thirteen run and board operations, resolved by the run controller.</summary>
    RUN_AND_BOARD = 5,
}
