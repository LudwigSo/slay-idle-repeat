namespace SlayIdleRepeat.Core.Rules.Inventory;

/// <summary>
/// The five orderings a player can ask the gear stock to be listed in.
/// </summary>
/// <remarks>
/// <para>
/// Public because the Inventory screen offers the choice and <c>InventoryView.Project</c> takes it;
/// the ordering itself stays <c>InventorySorting</c>'s, which is internal, so a caller can name an
/// order and cannot re-implement one. Every ordering is total — each ends in arrival order — so the
/// same stock in the same order always draws the same grid.
/// </para>
/// <para>
/// No <c>0</c> member, matching every other vocabulary in <c>Primitives/</c>: a default-initialised
/// key is not an order the player asked for, and the sorter refuses it rather than guessing.
/// </para>
/// </remarks>
public enum InventorySortKey
{
    /// <summary>Grouped by slot, in the order the base-item grid authors the slots.</summary>
    SLOT = 1,

    /// <summary>Highest band first.</summary>
    RARITY = 2,

    /// <summary>Highest item power first — band, chapter and enhancement together — then the better roll.</summary>
    POWER = 3,

    /// <summary>Best roll first.</summary>
    QUALITY = 4,

    /// <summary>Most recently granted first.</summary>
    NEWEST = 5,
}
