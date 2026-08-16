using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Gear;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Gear;

namespace SlayIdleRepeat.Core.Rules.Inventory;

/// <summary>
/// Orders a list of owned items by one of the five keys a player can choose.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Every ordering here is total.</b> Each key ends in grant order — the arrival index of the
/// item in the list it was handed — so no two items are ever left tied. That is not tidiness: .NET's
/// introsort falls back to a stable insertion sort below a threshold in the mid-teens, so a comparer
/// with an unbroken tie behaves perfectly on a short list and scrambles on a long one, and a player
/// who sorted twice would watch their stock shuffle.
/// </para>
/// <para>
/// It reads a rarity to sort by it and never decides one, which is why it consumes the gear tables
/// rather than the luck façade. <see cref="InventorySortKey.POWER"/> is the item's real power
/// scalar, so the chapter it dropped in moves it — that is what makes it a different key from
/// <see cref="InventorySortKey.RARITY"/> rather than the same comparer wired twice.
/// </para>
/// <para>
/// ⚠️ <b><see cref="InventorySortKey.NEWEST"/> orders by identity, not by position, and that is
/// forced.</b> A position in the list a caller happened to hand over is a property of the
/// <em>list</em> rather than of the item, so a key built on it inverts on every pass: sorting an
/// already-newest-first stock would hand back the oldest first. A gear instance carries no
/// timestamp, so the identity is the only per-item thing that orders at all — and the stock is
/// filled in grant order, so the ids are minted in the same sequence the list is already in. That
/// coincidence is an assumption about how ids are minted, and it is the one thing in this file that
/// would stop being true if identities were ever issued out of order.
/// </para>
/// <para>
/// Stateless and pure: it answers a new list and never touches the container it came from. Which
/// ordering a screen offers, and how it presents them, is the forge screen's.
/// </para>
/// </remarks>
internal static class InventorySorting
{
    /// <summary>Orders <paramref name="items"/> by <paramref name="key"/>.</summary>
    /// <param name="items">The items to order, in grant order. Never null; may be empty.</param>
    /// <param name="key">Which ordering.</param>
    /// <param name="par">The par table, for the chapter half of an item's power.</param>
    /// <param name="drops">The gear tables, for the band half of it.</param>
    /// <param name="catalogue">The base-item grid, which declares the order the slots group in.</param>
    /// <returns>A new list in the chosen order. Every input item appears exactly once.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="key"/> is outside the vocabulary.</exception>
    internal static IReadOnlyList<GearInstance> Sort(
        IReadOnlyList<GearInstance> items,
        InventorySortKey key,
        ParPowerTuning par,
        DropsTuning drops,
        GearCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(par);
        ArgumentNullException.ThrowIfNull(drops);
        ArgumentNullException.ThrowIfNull(catalogue);

        // The arrival index is captured before anything moves, so "grant order" survives as the last
        // tie-break of every key rather than being whatever the sort happened to leave behind.
        var arrivals = items.Select((item, arrival) => (Item: item, Arrival: arrival)).ToArray();

        var ordered = key switch
        {
            InventorySortKey.SLOT =>
                arrivals.OrderBy(row => SlotOrder(catalogue, row.Item.Slot)),

            InventorySortKey.RARITY =>
                arrivals.OrderByDescending(row => row.Item.Rarity),

            InventorySortKey.POWER =>
                arrivals.OrderByDescending(row => PowerOf(par, drops, row.Item))
                        .ThenByDescending(row => row.Item.Quality),

            InventorySortKey.QUALITY =>
                arrivals.OrderByDescending(row => row.Item.Quality),

            InventorySortKey.NEWEST =>
                arrivals.OrderByDescending(row => row.Item.InstanceId.Value, StringComparer.Ordinal),

            _ => throw new ArgumentOutOfRangeException(
                nameof(key),
                key,
                "That is not one of the five orderings. A key outside the vocabulary is a miswired " +
                "caller, and answering it with grant order would silently show the player a list " +
                "they did not ask for."),
        };

        return ordered.ThenBy(row => row.Arrival).Select(row => row.Item).ToArray();
    }

    /// <summary>
    /// The power scalar an item carries: its band's multiplier against the par power of the chapter
    /// it dropped in.
    /// </summary>
    /// <remarks>
    /// Quality is deliberately not folded in here — it is the next tie-break instead, so the band
    /// dominates. At one chapter of origin the band's multiplier spans more than the quality range
    /// ever can, and a comparer that ranked quality first would put a perfect low-band roll above an
    /// unlucky high-band one.
    /// </remarks>
    private static double PowerOf(ParPowerTuning par, DropsTuning drops, GearInstance item) =>
        ItemPower.For(
            par.ChapterPowerTarget(item.ChapterOrigin),
            drops.ItemPowerCoefficient,
            drops.Band(item.Rarity).StatMultiplier);

    /// <summary>Where a slot sits in the catalogue's declared grid.</summary>
    /// <remarks>
    /// Read off the content the game already ships rather than transcribed here: a hand-written slot
    /// order would be a second answer to a question the base-item grid has already settled, and the
    /// two would eventually disagree about which group a player sees first.
    /// </remarks>
    private static int SlotOrder(GearCatalogue catalogue, GearSlot slot)
    {
        var definitions = catalogue.Definitions;

        for (var index = 0; index < definitions.Count; index++)
        {
            if (definitions[index].Slot == slot)
            {
                return index;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(slot),
            slot,
            "The base-item grid authors nothing in this slot, so the declared grouping has no place " +
            "to put the item. The catalogue refuses an incomplete grid, so reaching here means the " +
            "item was minted against different content than the list is being read against.");
    }
}
