using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Forge;

/// <summary>
/// What makes two items "the same item" to a fusion: the definition, the band and the enhancement
/// level — and nothing else.
/// </summary>
/// <remarks>
/// 🔒 <b>One type, read by the rule and by the screen.</b> <c>GearMerge.Refusal</c> refuses a pair that
/// differs in any of these three, and the Gear screen groups the bag by them to say which copies a
/// fusion could take. Stated twice — once as pairwise compares, once as a joined string — the two
/// would drift the first time a fourth field joined the rule, with the screen offering a merge the
/// handler refuses. Quality, chapter of origin, affixes and the lock are deliberately absent: two items
/// differing only in those fuse.
/// </remarks>
/// <param name="DefId">The definition the item was rolled from.</param>
/// <param name="Rarity">Its band.</param>
/// <param name="EnhanceLevel">Its <c>+N</c>.</param>
public readonly record struct MergeIdentity(string DefId, Rarity Rarity, int EnhanceLevel)
{
    /// <summary>The identity of one item.</summary>
    internal static MergeIdentity Of(GearInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new MergeIdentity(item.DefId, item.Rarity, item.EnhanceLevel);
    }
}
