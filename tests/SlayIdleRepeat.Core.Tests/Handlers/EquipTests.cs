using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// <c>EQUIP</c> — the five rules that refuse it and the one shape that looks like a refusal and is
/// not.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Three of the five refusals answer <c>ILLEGAL_STATE</c>, so a case that asserted only the
/// code would pass with two of the three rules deleted.</b> Each of those three is therefore built as
/// a PAIR of worlds identical in every respect but the one fact the rule reads: the control has to be
/// accepted and the variant refused, and nothing else in the setup can account for the difference.
/// </para>
/// <para>
/// The two refusals with codes of their own — an unowned item and one parked in overflow — are still
/// built so that only their own rule can fire: no run is in the slice, the slot named is a declared
/// one, and it is the slot the item itself occupies.
/// </para>
/// </remarks>
public sealed class EquipTests
{
    /// <summary>Any well-formed <c>LogHash</c>, for the case that reaches overflow through a real drop.</summary>
    private const string BattleLog = "1";

    /// <summary>A blade, so the item's own slot is <see cref="GearSlot.WEAPON"/>.</summary>
    private const string Blade = "GI_BLADE";

    /// <summary>
    /// A slot value no hero wears. <c>GearSlot</c> has no zero member on purpose, so this is what an
    /// uninitialised wire column arrives as.
    /// </summary>
    private const GearSlot Undeclared = (GearSlot)0;

    // ═══════════════════════════════════════════════════════ 1 · a run is in progress

    /// <summary>
    /// 🔒 Equipping is refused while a run is in progress, and the identical command outside one is
    /// accepted.
    /// </summary>
    /// <remarks>
    /// The pair is the assertion. Both slices carry the same player row, the same owned and available
    /// blade and the same declared slot; the only difference is a live run, so no other rule can
    /// account for the refusal.
    /// </remarks>
    [Fact]
    public void Equipping_is_refused_while_a_run_is_in_progress_and_accepted_outside_one()
    {
        var wearer = Holding(Blade);

        var outsideARun = Equip(new WorldSlice(Worlds.Rehydrated(wearer), null), Blade, GearSlot.WEAPON);
        var duringARun = Equip(
            new WorldSlice(Worlds.Rehydrated(wearer), Worlds.NewRun()), Blade, GearSlot.WEAPON);

        outsideARun.Accepted.ShouldBeTrue(
            "the control was refused " + outsideARun.Rejection + ". This is the same player, the " +
            "same owned blade and the same declared slot as the refused case below, minus the run — " +
            "so with it failing, the refusal below says nothing about runs at all.");

        duringARun.Accepted.ShouldBeFalse(
            "the loadout was changed during a run. The run carries its own frozen StartingLoadout, so " +
            "the change could not reach the run it was aimed at and would only leave the player's " +
            "screen disagreeing with the run they are in.");

        duringARun.Rejection.ShouldBe(
            RejectionReason.ILLEGAL_STATE,
            "the in-run refusal answered some other reason, so it is no longer the rule this case " +
            "pins.");

        duringARun.NewState.Player.Loadout.EquippedCount.ShouldBe(
            0, "a refused EQUIP changes nothing.");
    }

    // ═══════════════════════════════════════════════════════ 2 · the slot is undeclared

    /// <summary>
    /// 🔒 An undeclared slot value is refused, where the identical command into a declared slot is
    /// accepted.
    /// </summary>
    /// <remarks>
    /// The pair again, and the reason this rule has to exist at all: the loadout throws on an
    /// undeclared slot, so a handler that did not check would answer a wire payload with an
    /// exception instead of a rejection.
    /// </remarks>
    [Fact]
    public void Equipping_into_an_undeclared_slot_is_refused_where_a_declared_one_is_accepted()
    {
        var world = new WorldSlice(Worlds.Rehydrated(Holding(Blade)), null);

        var declared = Equip(world, Blade, GearSlot.WEAPON);
        var undeclared = Equip(world, Blade, Undeclared);

        declared.Accepted.ShouldBeTrue(
            "the control was refused " + declared.Rejection + ". Same slice, same owned blade, no " +
            "run — the ONLY difference from the refused case below is the slot value, so with this " +
            "failing the refusal below is not about the slot.");

        undeclared.Accepted.ShouldBeFalse(
            "a slot value no hero wears was accepted. GearSlot has no zero member on purpose, so " +
            "this is exactly what an uninitialised column reads as.");

        undeclared.Rejection.ShouldBe(
            RejectionReason.ILLEGAL_STATE,
            "the undeclared-slot refusal answered some other reason, so it is no longer the rule " +
            "this case pins.");
    }

    // ═══════════════════════════════════════════════════════ 3 · the item is not owned

    /// <summary>An item the player does not own is <c>NOT_OWNED</c>, and only that rule can fire.</summary>
    /// <remarks>
    /// No run is in the slice, the slot is a declared one, and the stock is empty — so neither the
    /// run rule, the slot rule, the overflow rule nor the wrong-slot rule has anything to read.
    /// </remarks>
    [Fact]
    public void Equipping_an_item_the_player_does_not_own_is_NOT_OWNED()
    {
        var world = new WorldSlice(Worlds.Rehydrated(PlayerSnapshots.Valid), null);

        world.Player.Inventory.Held.ShouldBeEmpty(
            "the premise: nothing is in overflow, so the overflow rule cannot be what refuses this.");

        var result = Equip(world, "GI_GHOST", GearSlot.WEAPON);

        result.Rejection.ShouldBe(
            RejectionReason.NOT_OWNED,
            "a hero was dressed in an item nobody holds. NOT_OWNED is what tells the player their " +
            "client is naming an item that no longer exists, and it is the one refusal here that " +
            "cannot be confused with a stock that is merely full.");

        result.NewState.Player.Loadout.EquippedCount.ShouldBe(0, "a refused EQUIP changes nothing.");
    }

    // ═══════════════════════════════════════════════════════ 4 · the item is in overflow

    /// <summary>
    /// 🔒 An item parked in overflow is <c>INVENTORY_FULL</c>, driven through a real overflowing drop.
    /// </summary>
    /// <remarks>
    /// The overflow is produced by a kill arriving at a full stock rather than hand-written, because
    /// that is the only way an item gets there in the real game — and it is what makes
    /// <c>INVENTORY_FULL</c> reachable at all. The run is ended before the EQUIP, so the run rule
    /// cannot be what refuses; the slot named is the item's own declared slot, so neither the slot
    /// rule nor the wrong-slot rule can be; and the item IS owned, so it is not <c>NOT_OWNED</c>.
    /// </remarks>
    [Fact]
    public void Equipping_an_item_held_in_overflow_is_INVENTORY_FULL()
    {
        var kill = SlayIdleRepeat.Core.GameRules.Apply(
            GearGrantWorlds.OnKill(TileKind.Elite, inventory: GearGrantWorlds.FullStock()),
            new ConfirmBattleResultCommand(BattleLog, Won: true),
            GearGrantWorlds.Context);

        kill.Accepted.ShouldBeTrue("the kill was refused " + kill.Rejection + ".");

        var held = kill.NewState.Player.Inventory.Held;

        held.ShouldNotBeEmpty(
            "the premise: the drop did not overflow, so this case is not about an item in overflow " +
            "at all. A grant is never refused for want of space, so a full stock has to park it.");

        var world = GearGrantWorlds.WithEndedRun(kill.NewState);
        var overflowed = held[0];

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            world,
            new EquipCommand(overflowed.InstanceId, overflowed.Slot),
            GearGrantWorlds.Context);

        result.Rejection.ShouldBe(
            RejectionReason.INVENTORY_FULL,
            "an item waiting in overflow was equipped. Equipping from there would let a full " +
            "inventory be emptied through the hero screen, which is the reclaim rule's job and " +
            "nothing else's — and the player needs to be told their bag is full, not that they do " +
            "not own the item.");

        result.NewState.Player.Loadout.EquippedCount.ShouldBe(0, "a refused EQUIP changes nothing.");
    }

    // ═══════════════════════════════════════════════════════ 5 · the slot is not the item's

    /// <summary>
    /// 🔒 An item put in a slot it does not occupy is refused, where the same item in its own slot is
    /// accepted.
    /// </summary>
    /// <remarks>
    /// The pair once more: the same owned, available blade and no run in either slice, so the slot
    /// the item itself declares is the only thing that can account for the difference. A helmet slot
    /// is a DECLARED one, which is what tells this rule apart from the undeclared-slot rule above.
    /// </remarks>
    [Fact]
    public void Equipping_an_item_into_a_slot_it_does_not_occupy_is_refused_where_its_own_slot_is_accepted()
    {
        var world = new WorldSlice(Worlds.Rehydrated(Holding(Blade)), null);

        world.Player.Inventory.Stored[0].Slot.ShouldBe(
            GearSlot.WEAPON, "the premise: a blade is worn in the weapon slot and nowhere else.");

        var ownSlot = Equip(world, Blade, GearSlot.WEAPON);
        var otherSlot = Equip(world, Blade, GearSlot.HELMET);

        ownSlot.Accepted.ShouldBeTrue(
            "the control was refused " + ownSlot.Rejection + ". Same slice, same item, same absence " +
            "of a run — with this failing, the refusal below is not about the slot the item occupies.");

        otherSlot.Accepted.ShouldBeFalse(
            "a blade was worn on the head. A slot holds an item's identity and the stats it " +
            "contributes are the slot's, so a mismatched pair would score a weapon's attack as " +
            "helmet defence for the rest of the run.");

        otherSlot.Rejection.ShouldBe(
            RejectionReason.ILLEGAL_STATE,
            "the wrong-slot refusal answered some other reason, so it is no longer the rule this " +
            "case pins.");
    }

    // ═══════════════════════════════════════════════════════ the negative control

    /// <summary>
    /// 🔒 A LOCKED item is equippable, and that is the ruling rather than an oversight.
    /// </summary>
    /// <remarks>
    /// The lock protects an item from a destructive operation, which is the opposite of taking it out
    /// of use. This is the control that keeps the five refusals above honest: without it, a handler
    /// that refused everything would satisfy every one of them.
    /// </remarks>
    [Fact]
    public void A_LOCKED_item_is_equippable()
    {
        var world = new WorldSlice(Worlds.Rehydrated(Holding(Blade, locked: true)), null);

        world.Player.Inventory.Stored[0].Locked.ShouldBeTrue(
            "the premise: the item is actually locked, or this case is the plain accept case twice.");

        var result = Equip(world, Blade, GearSlot.WEAPON);

        result.Accepted.ShouldBeTrue(
            "a locked item was refused " + result.Rejection + ". The lock guards against salvaging " +
            "and merging; refusing to WEAR the item a player marked as their best is the opposite of " +
            "what they asked for.");

        result.NewState.Player.Loadout.TryGet(GearSlot.WEAPON, out var worn).ShouldBeTrue(
            "EQUIP was accepted and the slot is still empty, so nothing was actually equipped.");

        worn.Value.ShouldBe(Blade, "and the slot names the item the command asked for.");

        result.Events.ShouldBeEmpty(
            "equipping grants nothing and moves no currency, so it has nothing to report.");
    }

    // ═══════════════════════════════════════════════════════ the worlds

    private static CommandResult Equip(WorldSlice world, string itemId, GearSlot slot) =>
        SlayIdleRepeat.Core.GameRules.Apply(
            world, new EquipCommand(new GearInstanceId(itemId), slot), Worlds.Context);

    /// <summary>A player row holding one blade in stock and wearing nothing.</summary>
    private static PlayerSnapshot Holding(string itemId, bool locked = false) =>
        PlayerSnapshots.With(inventory: new InventorySnapshot(
            0, [Inventories.Persist(Inventories.Item(itemId, locked: locked))], []));
}
