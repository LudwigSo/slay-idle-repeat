namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>Which cap a <see cref="EffectOp.STAT_CAP_OVERRIDE"/> raises or redirects.</summary>
/// <remarks>
/// ⚠️ <b>One member, because `18` authorises exactly one.</b> <c>HEAL_CEILING</c> is the only
/// <c>capKind</c> the document writes (§7.6's <c>Avatar of War</c>). §2.1 says the op may also
/// <em>raise</em> a stat cap, but no token for that is authored anywhere, and inventing one
/// (<c>STAT_MAX</c>, <c>CRIT_CEILING</c>, …) would be a value nobody has agreed. A design that
/// needs one takes `18` §10's route.
/// </remarks>
public enum StatCapKind
{
    /// <summary>The ceiling on healing, expressed as a fraction of Max HP (`18` §7.6).</summary>
    HEAL_CEILING = 1,
}
