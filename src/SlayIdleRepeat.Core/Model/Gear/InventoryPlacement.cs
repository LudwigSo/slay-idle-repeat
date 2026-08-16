namespace SlayIdleRepeat.Core.Model.Gear;

/// <summary>Where an item granted to a player actually landed.</summary>
/// <remarks>
/// <para>
/// 🔒 There is no third member, and its absence is the rule: a grant is never refused and never
/// dropped. No random source in this game may be able to starve a player, and a grant that arrives
/// while the stock is full and is silently discarded is that same wound from the other side. A full
/// inventory therefore <em>holds</em> what it cannot store.
/// </para>
/// <para>
/// A top-level enum rather than a member of <c>Inventory</c>, for the reason
/// <see cref="ItemAvailability"/> records.
/// </para>
/// </remarks>
internal enum InventoryPlacement
{
    /// <summary>It fitted, and is in stock.</summary>
    STORED,

    /// <summary>The stock was full, so it is waiting in the holding list. It is owned either way.</summary>
    HELD,
}
