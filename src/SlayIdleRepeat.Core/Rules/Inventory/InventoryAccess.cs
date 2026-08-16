using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Inventory;

/// <summary>
/// The one place an inventory's answer about an item becomes a rejection the player is told.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>This is the seam that keeps "the item was not added" from meaning four different things at
/// once.</b> A handler that mapped every unavailable item onto one catch-all would show the same
/// toast to a player who does not own the item, one whose item is locked, and one whose stock is
/// full — three different situations with three different things to do about them. Every consumer of
/// an owned item routes its precondition through here rather than writing its own <c>if</c>.
/// </para>
/// <para>
/// ⚠️ It is the seam through which <see cref="RejectionReason.INVENTORY_FULL"/> is <em>meant</em> to
/// become reachable, and it has not yet: nothing calls this method, so the reason still has no
/// production emitter. That is a statement of what is left rather than of what was done. The reason
/// has existed, documented, since the vocabulary was written, and M4-05 settled only what it means —
/// under the hold-not-lose rule a grant is never refused, so <c>INVENTORY_FULL</c> belongs to an
/// operation on a <em>held</em> item rather than to the grant that filled the stock. <b>M4-04</b>
/// (merge, enhance, salvage) and <b>M4-10</b> (equip) are the callers that will make it reachable;
/// each routes its owned-item precondition through here rather than writing its own <c>if</c>.
/// </para>
/// </remarks>
internal static class InventoryAccess
{
    /// <summary>Why a caller may not act on an item, or <c>null</c> when it may.</summary>
    /// <param name="availability">What the container answered about the identity.</param>
    /// <returns>The reason to refuse the command, or <c>null</c> for an available item.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A value outside the vocabulary. Refused rather than answered with a catch-all: a silent
    /// default arm would give a state nobody decided on a reason nobody chose.
    /// </exception>
    internal static RejectionReason? RejectionFor(ItemAvailability availability) => availability switch
    {
        ItemAvailability.AVAILABLE => null,
        ItemAvailability.UNKNOWN_ITEM => RejectionReason.NOT_OWNED,
        ItemAvailability.HELD_IN_OVERFLOW => RejectionReason.INVENTORY_FULL,
        ItemAvailability.LOCKED => RejectionReason.ILLEGAL_STATE,
        _ => throw new ArgumentOutOfRangeException(
            nameof(availability),
            availability,
            "That is not one of the four states an item can be in. Every member of the vocabulary " +
            "has a reason chosen for it deliberately; a default arm here would answer a state " +
            "nobody has decided about with a rejection nobody picked."),
    };
}
