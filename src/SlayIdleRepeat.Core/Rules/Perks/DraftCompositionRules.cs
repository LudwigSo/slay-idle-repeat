using SlayIdleRepeat.Core.Content.Perks;

namespace SlayIdleRepeat.Core.Rules.Perks;

/// <summary>
/// The three plain composition rules of a draft: no duplicate options, at least two categories, and
/// the per-option owned-upgrade bias.
/// </summary>
/// <remarks>
/// <para>
/// The other five rules that used to be stubbed here are the <c>DRAFT</c> source class and they are
/// not composition rules at all — each one is a guarantee with a counter or a weight behind it, and a
/// guarantee may fire in exactly one place. They live in <c>Rules.Luck</c> now, and the draft engine
/// reaches them through the luck façade rather than restating them.
/// </para>
/// <para>
/// These three carry no counter and no guarantee: they are shape constraints on a set of three
/// options, enforced <em>by construction</em> — by narrowing the pool a slot draws from — rather than
/// by drawing and repairing, so no rule here costs an extra draw index.
/// </para>
/// </remarks>
internal static class DraftCompositionRules
{
    /// <summary>At least this many distinct categories among a draft's options.</summary>
    internal const int MinimumDistinctCategories = 2;

    /// <summary>Whether a set of options holds no perk twice.</summary>
    /// <remarks>
    /// A question, not a repair: the engine narrows slot <i>k</i>'s pool by the perks slots
    /// <c>0..k-1</c> already took, and this is what makes that claim checkable from outside.
    /// </remarks>
    /// <param name="perkIds">The perk ids offered, one per slot.</param>
    /// <returns><see langword="true"/> when every perk id appears at most once.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="perkIds"/> is null.</exception>
    internal static bool NoDuplicateOptions(IReadOnlyList<string> perkIds)
    {
        ArgumentNullException.ThrowIfNull(perkIds);

        var seen = new HashSet<string>(perkIds.Count, StringComparer.Ordinal);

        foreach (var perkId in perkIds)
        {
            if (!seen.Add(perkId))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a set of options spans at least two categories.</summary>
    /// <param name="categories">The categories offered, one per slot.</param>
    /// <returns><see langword="true"/> when the options are not all of one category.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="categories"/> is null.</exception>
    internal static bool CategoryDiversity(IReadOnlyList<PerkCategory> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);

        var distinct = new HashSet<PerkCategory>(categories);

        return distinct.Count >= MinimumDistinctCategories;
    }

    /// <summary>
    /// Whether this slot's bias roll landed on the owned-but-not-maxed pool.
    /// </summary>
    /// <remarks>
    /// The roll is taken on <b>every</b> slot whether or not that pool is non-empty and whether or
    /// not a guarantee has already floored the slot, so the draw budget per slot is fixed and a
    /// resumed draft stream lands in the same place regardless of which rules fired.
    /// </remarks>
    /// <param name="roll">The slot's draw, in <c>[0,1)</c>.</param>
    /// <param name="bias">The authored per-option probability.</param>
    /// <returns><see langword="true"/> when the slot draws from the owned pool.</returns>
    internal static bool OwnedUpgradeBiasHits(double roll, double bias) => roll < bias;
}
