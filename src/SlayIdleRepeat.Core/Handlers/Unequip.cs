using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Hero;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>UNEQUIP</c> handler: empties one gear slot.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Refused during a run</b>, on <c>EQUIP</c>'s reason and through the same predicate: the run
/// carries its own frozen starting loadout, so a mid-run change could not reach the run it was aimed
/// at and would only leave the player's screen disagreeing with the run they are in. Taking gear
/// <em>off</em> is as much a loadout change as putting it on, and a command that could strip the
/// hero mid-run while <c>EQUIP</c> could not dress them would be the same rule stated twice with
/// only one of the two applied.
/// </para>
/// <para>
/// 🔴 <b>It reads the stock for nothing at all, and that is the design.</b> An equipped slot names an
/// identity the stock already holds — <c>Player.RequireLoadoutResolves</c> is the invariant that says
/// so and it runs after every accepted command — so emptying a slot cannot make the loadout name
/// something unowned, and there is no ownership question to ask. That is what makes this the one
/// gear command with no <c>NOT_OWNED</c> and no <c>INVENTORY_FULL</c> arm: an item parked in overflow
/// cannot be worn in the first place.
/// </para>
/// <para>
/// 🔴 <b>There is no <c>Enum.IsDefined</c> slot guard here, unlike <c>EQUIP</c>, and that is measured
/// rather than assumed.</b> An undeclared slot is one nothing can be equipped in, so the empty-slot
/// rule below refuses it first and with the same code — a separate guard could never fire. See the
/// comment on that rule for what was measured and for the ordering obligation it leaves behind.
/// </para>
/// <para>
/// 🔒 <b>It writes through <c>Player.Unequip</c>, the seam <c>Player.DiscardItem</c> already uses to
/// clear a slot when an item is destroyed.</b> A second way to write the loadout is how the two
/// eventually disagree — <c>Loadout.Without</c> is reached through the aggregate, never around it,
/// so every path that empties a slot empties it the same way.
/// </para>
/// <para>
/// It emits no event. Taking gear off grants nothing and moves no currency, so there is nothing to
/// report; what changed is state, and the state hash carries it.
/// </para>
/// </remarks>
internal static class Unequip
{
    /// <summary>Applies <c>UNEQUIP</c>.</summary>
    /// <param name="command">The slot to empty.</param>
    /// <param name="input">The cloned, already-caught-up slice.</param>
    /// <returns>Accepted with no events, or the reason the unequip was refused.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal static HandlerResult Handle(UnequipCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        if (!LoadoutRules.MayChangeLoadout(input.State.Run))
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        // 🔒 An already-empty slot is REFUSED rather than accepted as a no-op, and it is the one
        // decision here worth arguing. The alternative reading — "unequip is idempotent, so an empty
        // slot is success" — is the one 14 §16.3's replay rule would want, but that rule already
        // covers a genuine retry: the same commandId returns the first outcome without re-running
        // this. What reaches here with an empty slot is a client whose view of the hero disagrees
        // with the server's, and answering "done" would leave it disagreeing silently.
        //
        // 🔴 THIS IS ALSO THE UNDECLARED-SLOT GUARD, and there is deliberately no Enum.IsDefined
        // check beside it. EQUIP needs one — it reaches Loadout.With, which THROWS on a slot no hero
        // wears, before anything has filtered the value — but here the read comes first and
        // Loadout.TryGet does not throw: an undeclared slot is one nothing can be equipped in, so it
        // answers false and this rule refuses the payload with the same code the slot check would
        // have used. A guard beside this one could never fire, and a guard that cannot fire reads as
        // a live rule in every summary (steering S1). MEASURED: with an Enum.IsDefined check added
        // here, neutering it left the whole UNEQUIP suite green — including a case built on a hero
        // wearing nothing, which was written to discriminate and could not.
        //
        // ⚠️ What that leaves is an ORDERING obligation rather than a missing check: read the slot
        // before writing it. A future edit that called Player.Unequip first, or that accepted an
        // empty slot as a no-op, would put Loadout.Without back on a path an undeclared value can
        // reach — and would turn a malformed wire payload into an exception out of Apply.
        if (!input.Player.Loadout.TryGet(command.GearSlot, out _))
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        input.Player.Unequip(command.GearSlot);

        return HandlerResult.Accept();
    }
}
