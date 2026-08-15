namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// A duration's early terminator — the <c>"until"</c> field. Fires whichever comes first,
/// terminator or timer.
/// </summary>
/// <remarks>
/// One member, because only one is currently authored. A second terminator is a DSL extension:
/// new token, schema failure, then the op/enum/doc change in one commit — guessing at plausible
/// companions would manufacture vocabulary no design has asked for.
/// </remarks>
public enum DurationTerminator
{
    /// <summary>Ends the moment the owner's ward pool breaks.</summary>
    WARD_BROKEN = 1,
}
