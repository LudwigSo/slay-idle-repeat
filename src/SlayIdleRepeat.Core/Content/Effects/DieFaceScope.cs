namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// <see cref="EffectOp.MODIFY_DIE_FACE"/>'s own <c>scope</c> key — e.g.
/// <c>{"op":"MODIFY_DIE_FACE","scope":"NEXT_3_ROLLS","newFace":{"kind":"Star"}}</c>.
/// </summary>
/// <remarks>
/// This is not <see cref="DurationScope"/>, and that is a documented collision: one place expresses
/// the same idea as <c>"duration": {"scope": "RUN"}</c>, while this puts a <c>scope</c> key directly
/// on the effect with a value from a different vocabulary. Both are declared because both are
/// authored; which one survives is a design decision recorded as errata, not resolved here.
/// </remarks>
public enum DieFaceScope
{
    /// <summary>The replacement holds for the next three rolls.</summary>
    NEXT_3_ROLLS = 1,
}
