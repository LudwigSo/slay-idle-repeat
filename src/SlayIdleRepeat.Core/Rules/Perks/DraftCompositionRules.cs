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
/// <para>
/// Each rule is stated here once, in the shape the engine needs it: the diversity rule as the
/// per-slot narrowing decision (<see cref="MustContributeNewCategory"/>) <em>and</em> as the question
/// over a finished draft (<see cref="CategoryDiversity"/>), both reading one threshold. The
/// duplicate rule is a narrowing with no threshold at all — the pool minus the ids already taken —
/// so it stays a question here and a pool filter in the engine; there is no number the two could
/// disagree about.
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
    /// Whether the slot about to be drawn has to contribute a category no earlier slot took.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 The <em>narrowing</em> half of the diversity rule, stated here beside the threshold it is
    /// narrowing towards. The engine used to restate the rule as "narrow the last slot when every
    /// earlier slot shares one category", which is the right narrowing for a threshold of exactly 2
    /// and silently the wrong one for any other value — a constant shaped like a dial that nothing
    /// actually turned. Reading <see cref="MinimumDistinctCategories"/> from the decision itself is
    /// what makes it one, and the arithmetic below reproduces the old narrowing exactly at the
    /// authored pair, so no seed's draft moves.
    /// </para>
    /// <para>
    /// The claim: a slot must widen the draft when the slots still to come could not reach the
    /// threshold otherwise. Narrowing earlier than that would be a stricter rule than the one stated
    /// — a draft is allowed to repeat a category as long as it still ends up spanning enough of them.
    /// </para>
    /// </remarks>
    /// <param name="distinctCategoriesSoFar">Distinct categories among the slots already drawn.</param>
    /// <param name="slotsRemaining">Slots left to draw, <b>including</b> the one being decided.</param>
    /// <returns><see langword="true"/> when this slot's pool must exclude the categories already taken.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either argument is negative.</exception>
    internal static bool MustContributeNewCategory(int distinctCategoriesSoFar, int slotsRemaining)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(distinctCategoriesSoFar);
        ArgumentOutOfRangeException.ThrowIfNegative(slotsRemaining);

        var missing = MinimumDistinctCategories - distinctCategoriesSoFar;

        return missing > 0 && missing >= slotsRemaining;
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
