namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// <see cref="EffectOp.MODIFY_DIE_FACE"/>'s own <c>scope</c> key — `18` §9.2's
/// <c>{"op":"MODIFY_DIE_FACE","scope":"NEXT_3_ROLLS","newFace":{"kind":"Star"}}</c>.
/// </summary>
/// <remarks>
/// ⚠️ <b>This is not <see cref="DurationScope"/>, and that is a documented collision.</b> `18` §7.9
/// expresses the same idea as <c>"duration": {"scope": "RUN"}</c>, while §9.2 puts a <c>scope</c>
/// key directly on the effect with a value from a different vocabulary. M2-01 declares both because
/// both are authored; which one survives is a design decision, recorded as errata for the milestone
/// conductor rather than resolved here by picking one and deleting the other.
/// <para>
/// One member, because §9.2 authorises exactly one token.
/// </para>
/// </remarks>
public enum DieFaceScope
{
    /// <summary>The replacement holds for the next three rolls (`18` §9.2, <c>PET_DICEBEAST</c>).</summary>
    NEXT_3_ROLLS = 1,
}
