using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Inventory;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>LOCK_ITEM</c> handler: sets or clears the flag that protects one stored item from being
/// destroyed.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>This is the command that makes <c>LOCKED</c> a state production can actually be in.</b>
/// <c>Inventory.SetLock</c> shipped with M4-05 and had <b>zero</b> production callers until now, so
/// <c>GearInstance.Locked</c> was false for every real player and everything downstream of it was
/// unreachable code that read as live: <c>InventoryAccess.RejectionFor</c>'s <c>LOCKED</c> arm — and
/// with it <c>SALVAGE</c>'s and <c>MERGE</c>'s refusal to destroy a protected item — <c>Enhance</c>'s
/// deliberate lock-passthrough, <c>Equip</c>'s "a locked item is equippable" ruling, and
/// <c>AutoSalvageFilter</c>'s first exclusion. Each of those was written, tested against a
/// hand-written fixture, and reachable by nothing.
/// </para>
/// <para>
/// 🔒 <b>A LOCKED item is accepted here, and no other command accepts one through this door.</b>
/// The admitted set is <c>AVAILABLE</c> and <c>LOCKED</c> — the same pair
/// <c>LoadoutRules.IsEquippable</c> admits — because refusing a locked item would make the flag
/// impossible to clear: the only command that can unlock an item would refuse every item that is
/// locked. So <c>InventoryAccess.RejectionFor</c> is asked only about the two states that are
/// genuinely refusals, and its <c>LOCKED</c> arm is deliberately not reached from here.
/// </para>
/// <para>
/// <b>Not refused mid-run</b>, on <c>SAVE_PRESET</c>'s reason: a lock is protection against a
/// destructive <em>meta</em> operation, none of which can be sent while a run is being played
/// anyway, and it changes nothing the run is fighting with. `07` §4's in-run freeze is about the
/// loadout, and this is not one.
/// </para>
/// <para>
/// It emits no event. A lock grants nothing and moves no currency; what changed is state, and the
/// state hash carries it.
/// </para>
/// </remarks>
internal static class LockItem
{
    /// <summary>Applies <c>LOCK_ITEM</c>.</summary>
    /// <param name="command">The item and the state to leave its flag in.</param>
    /// <param name="input">The cloned, already-caught-up slice.</param>
    /// <returns>
    /// Accepted with no events — including when the flag was already where the command asked for it —
    /// or <c>NOT_OWNED</c> for an item nobody holds and <c>INVENTORY_FULL</c> for one waiting in
    /// overflow.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal static HandlerResult Handle(LockItemCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var stock = input.Player.Inventory;
        var availability = stock.Availability(command.ItemId);

        // Stated as the two states that are legal rather than as "anything but a refusal", on
        // Player.RequireLoadoutResolves' argument: written as a single exclusion this would admit
        // whatever the fifth member of the vocabulary turns out to mean.
        if (availability is not (ItemAvailability.AVAILABLE or ItemAvailability.LOCKED))
        {
            return HandlerResult.Reject(RefusalFor(availability));
        }

        // 🔒 The return value is DISCARDED, and that is the idempotence. SetLock answers false both
        // when the flag was already there and when the stock does not hold the item — two facts one
        // bool cannot separate — so the second is settled above, by identity, and the first is not a
        // refusal at all: asking for a state the item is already in is a command that got what it
        // asked for.
        stock.SetLock(command.ItemId, command.Locked);

        return HandlerResult.Accept();
    }

    /// <summary>Why an item that is neither stored nor locked may not have its flag set.</summary>
    /// <remarks>
    /// The two states left once <c>AVAILABLE</c> and <c>LOCKED</c> are accepted both carry a
    /// rejection of their own, so the <c>null</c> arm is the container disagreeing with the check
    /// that just read it — the same shape <c>Equip.RefusalFor</c> guards against.
    /// </remarks>
    private static RejectionReason RefusalFor(ItemAvailability availability) =>
        InventoryAccess.RejectionFor(availability) ?? throw new InvalidOperationException(
            "An item this handler refused is reported as available by the container that answered " +
            "it. One of the two read a different stock from the other, and accepting the lock would " +
            "set a flag on an item nothing guarantees is in the stock.");
}
