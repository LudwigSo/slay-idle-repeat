using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Hero;
using SlayIdleRepeat.Core.Rules.Inventory;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>EQUIP</c> handler: puts one owned item into the slot it occupies.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Refused during a run</b>, on <c>APPLY_PRESET</c>'s reason and through the same predicate:
/// the run carries its own frozen starting loadout, so a mid-run equip could not reach the run it
/// was aimed at and would only leave the player's screen disagreeing with the run they are in.
/// </para>
/// <para>
/// 🔒 <b>A LOCKED item is equippable, and that is the ruling rather than an oversight.</b> The lock
/// protects an item from a destructive operation, which is the opposite of taking it out of use —
/// so <c>InventoryAccess.RejectionFor</c>'s <c>LOCKED</c> arm is unreachable from here: the
/// equippability rule accepts a locked item before the explanation is ever asked for.
/// </para>
/// <para>
/// It emits no event. Wearing gear grants nothing and moves no currency, so there is nothing to
/// report; what changed is state, and the state hash carries it.
/// </para>
/// </remarks>
internal static class Equip
{
    /// <summary>Applies <c>EQUIP</c>.</summary>
    /// <param name="command">The item and the slot to wear it in.</param>
    /// <param name="input">The cloned, already-caught-up slice.</param>
    /// <returns>Accepted with no events, or the reason the equip was refused.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal static HandlerResult Handle(EquipCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        if (!LoadoutRules.MayChangeLoadout(input.State.Run))
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        // Ahead of the ownership read on purpose: the loadout THROWS on a slot no hero wears, so a
        // handler that looked the item up first would answer an uninitialised wire column with an
        // exception on the items it happens to own and a rejection on the ones it does not.
        if (!Enum.IsDefined(command.GearSlot))
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var stock = input.Player.Inventory;

        if (!LoadoutRules.IsEquippable(command.ItemId, stock))
        {
            return HandlerResult.Reject(RefusalFor(stock.Availability(command.ItemId)));
        }

        // Equippable means stored — AVAILABLE or LOCKED — so Find cannot answer null here.
        if (stock.Find(command.ItemId)!.Slot != command.GearSlot)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        input.Player.Equip(command.GearSlot, command.ItemId);

        return HandlerResult.Accept();
    }

    /// <summary>Why an item that is not equippable may not be worn.</summary>
    /// <remarks>
    /// The two states left once <c>AVAILABLE</c> and <c>LOCKED</c> are accepted both carry a
    /// rejection of their own, so the <c>null</c> arm is a container disagreeing with the rule that
    /// just read it rather than an item the player may wear.
    /// </remarks>
    private static RejectionReason RefusalFor(ItemAvailability availability) =>
        InventoryAccess.RejectionFor(availability) ?? throw new InvalidOperationException(
            "An item the equippability rule refused is reported as available by the container that " +
            "answered it. One of the two read a different stock from the other, and accepting the " +
            "equip would put an identity in the loadout that nothing guarantees is in the stock.");
}
