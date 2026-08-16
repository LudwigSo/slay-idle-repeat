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
/// <para>
/// ⚠️ <b>The standing cost that buys, stated rather than left to be rediscovered.</b> Hoisting the
/// enums out only fixes <em>these</em> names. No type under <c>SlayIdleRepeat.Core.Rules.Inventory</c>
/// can ever write the bare identifier <c>Inventory</c> and mean the container: the enclosing
/// namespace's own member wins over any <c>using</c>, so the name resolves to the namespace and the
/// compiler answers CS0118 rather than a "did you mean" — a caller there has to spell
/// <c>Model.Gear.Inventory</c> in full. That cost is permanent and was accepted deliberately, because
/// the model type's name is <b>forced</b>: <c>GapRegister</c>'s <c>30</c> §4 Player-contents row
/// requires a type called exactly <c>Inventory</c> under <c>Core.Model</c>, so between the two names
/// the rules namespace is the one that can give way. Today nothing under those rules needs the
/// container at all — the sorting and comparison rules take an
/// <c>IReadOnlyList&lt;GearInstance&gt;</c>, and <c>InventoryAccess</c> takes this enum — which is
/// the arrangement that keeps the cost theoretical. If it ever stops being, rename the <em>rules
/// namespace</em>, never the model type.
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
