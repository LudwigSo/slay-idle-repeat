namespace SlayIdleRepeat.Core.Rules.Inventory;

/// <summary>The five orderings a player can read their stock in.</summary>
/// <remarks>
/// Rarity, power and quality sort <em>best first</em> and <see cref="NEWEST"/> sorts newest first:
/// these exist so a player looking for the item worth acting on finds it at the top.
/// <see cref="SLOT"/> is the exception, and deliberately — it is a grouping rather than a ranking, so
/// it follows the declared order of the slots themselves. Every tie falls back to grant order, which
/// is the order the stock is already in, so each ordering is total and a second pass never reshuffles
/// the first.
/// </remarks>
internal enum InventorySortKey
{
    /// <summary>Grouped by slot, in the order the gear catalogue declares its grid.</summary>
    SLOT,

    /// <summary>Best band first.</summary>
    RARITY,

    /// <summary>Strongest first: the item's own power scalar, which the chapter it dropped in moves.</summary>
    POWER,

    /// <summary>Best roll first.</summary>
    QUALITY,

    /// <summary>Newest first — the exact reverse of grant order.</summary>
    NEWEST,
}
