using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Forge;

/// <summary>
/// Which of a player's stored items their auto-salvage filter sweeps.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Three things are never swept, and each is a different reason.</b> A <b>locked</b> item is
/// locked precisely to be excluded from this, which is the lock's stated purpose. An item waiting in
/// <b>overflow</b> is not acted on at all — every operation on a held item is refused, and a filter
/// that quietly destroyed one would be the single exception. An item of a band the filter carries
/// <b>no row for</b> is not swept, so an empty filter sweeps nothing: a quality-of-life feature that
/// deleted a player's stock the moment it was switched on would not be one.
/// </para>
/// <para>
/// <b>It answers identities, not items, and destroys nothing.</b> The sweep is a selection the salvage
/// command then acts on, so the same rule that prices a salvage prices these — there is no second,
/// cheaper path through the forge for an item the filter happened to pick.
/// </para>
/// <para>
/// <b>Stock order is kept.</b> The filter's answer is the order the stock is held in, so the same
/// stock and the same filter always produce the same list, and a payout summed over it is the same
/// number every time.
/// </para>
/// <para>
/// ⚠️ <b>Half-reachable, and worth saying plainly.</b> Until the M4 retro ruling of 2026-08-17
/// <c>Player.AutoSalvageRules</c> had no writer at all, so nothing a player could send could put a
/// row in front of this rule. <c>SET_AUTO_SALVAGE_RULES</c> is that writer, and the rows it stores
/// are driven through <see cref="Select"/> by <c>SetAutoSalvageRulesTests</c>. 🔴 <b>What is still
/// missing is the caller</b>: no run-end payout consults this filter, so <see cref="Select"/> has no
/// production caller even now. Applying the sweep at run end was deliberately out of that ruling's
/// scope, and the forge screen that edits the rows is M9-01's. Adding a caller here to make the
/// number look better would be wiring a feature nobody has ruled on.
/// </para>
/// </remarks>
internal static class AutoSalvageFilter
{
    /// <summary>The stored items this filter sweeps, in stock order.</summary>
    /// <param name="inventory">The player's stock.</param>
    /// <param name="rules">The filter rows the player configured. Empty sweeps nothing.</param>
    /// <returns>The identities to salvage. Empty when the filter matches nothing.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal static IReadOnlyList<GearInstanceId> Select(
        Model.Gear.Inventory inventory, IReadOnlyList<AutoSalvageRule> rules)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(rules);

        if (rules.Count == 0)
        {
            return Array.Empty<GearInstanceId>();
        }

        var swept = new List<GearInstanceId>();

        // The match is written out here rather than pulled into a helper, and that is the routing
        // rule rather than a preference: a helper taking the band or the item would carry a grant
        // outcome in its signature, and the only ways to answer that are an exemption on a type that
        // grants nothing or a call to a façade this rule has no business making.
        foreach (var item in inventory.Stored)
        {
            if (item.Locked)
            {
                continue;
            }

            foreach (var rule in rules)
            {
                // Any row, not the first: a filter carrying two rows for one band is a player who
                // set that band twice, and sweeping on either is the only reading under which
                // neither row is silently ignored.
                if (rule.Rarity == item.Rarity && item.EnhanceLevel < rule.BelowEnhanceLevel)
                {
                    swept.Add(item.InstanceId);
                    break;
                }
            }
        }

        return swept.AsReadOnly();
    }
}
