namespace SlayIdleRepeat.Core.Model.Gear;

/// <summary>Why a caller can or cannot act on an item it named by identity.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>Four answers, not a boolean.</b> "The item was not added" is equally true of a player who
/// does not own it, one whose item is locked and one whose stock is full — three different things
/// to tell a player and three different things for a client to do about it. Every consumer of an
/// owned item asks this question and routes the answer through
/// <c>Rules.Inventory.InventoryAccess</c> rather than writing its own <c>if</c>.
/// </para>
/// <para>
/// 🔴 A <b>top-level</b> enum rather than a member of <c>Inventory</c>, and that is forced: the
/// rules namespace that reads it is <c>Core.Rules.Inventory</c>, and a child namespace named
/// <c>Inventory</c> shadows the type <c>Inventory</c> for everything inside it. Nested here, the
/// enum could not be named from the very rules that consume it. The same hazard <c>Player</c>
/// documents for itself, one namespace over.
/// </para>
/// </remarks>
internal enum ItemAvailability
{
    /// <summary>In stock, unlocked, and ready to be merged, salvaged, equipped or enhanced.</summary>
    AVAILABLE,

    /// <summary>No item of that identity is owned at all — neither in stock nor waiting.</summary>
    UNKNOWN_ITEM,

    /// <summary>
    /// Owned, but waiting in the holding list because the stock was full when it arrived. It comes
    /// back the moment space exists; until then nothing can be done to it.
    /// </summary>
    HELD_IN_OVERFLOW,

    /// <summary>In stock, and locked by the player against destructive operations.</summary>
    LOCKED,
}
