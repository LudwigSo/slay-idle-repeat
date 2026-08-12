namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// A duration's early terminator — `18` §6's <c>"until"</c> field. <em>"<c>until</c> fields fire
/// whichever comes first, terminator or timer."</em>
/// </summary>
/// <remarks>
/// ⚠️ <b>One member, because `18` authorises exactly one.</b> <c>WARD_BROKEN</c> is the only
/// terminator the document names (Ossify's DR buff, `18` §7.10). A second terminator is a DSL
/// extension and takes `18` §10's route: new token, schema failure, then the op/enum/doc change in
/// one commit. Guessing at plausible companions (<c>PHASE_EXIT</c>, <c>STATUS_EXPIRED</c>) would
/// have manufactured vocabulary no design has asked for.
/// </remarks>
public enum DurationTerminator
{
    /// <summary>Ends the moment the owner's ward pool breaks (`05` §4.1).</summary>
    WARD_BROKEN = 1,
}
