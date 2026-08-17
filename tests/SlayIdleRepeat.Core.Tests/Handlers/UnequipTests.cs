using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// <c>UNEQUIP</c> — the three rules that refuse it, and what an accepted one leaves behind.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>All three refusals answer <c>ILLEGAL_STATE</c>, so a case that asserted only the code would
/// pass with two of the three rules deleted.</b> Each is therefore built as a PAIR of worlds
/// identical in every respect but the one fact its rule reads: the control has to be accepted and
/// the variant refused, and nothing else in the setup can account for the difference. The
/// undeclared-slot rule needs a third world on top of its pair, because the empty-slot rule refuses
/// its payload too — see that case.
/// </para>
/// <para>
/// Added by the M4 retro's product-owner ruling of 2026-08-17. Before it, <c>EQUIP</c> could
/// overwrite a slot and nothing could clear one.
/// </para>
/// </remarks>
public sealed class UnequipTests
{
    /// <summary>A blade, so the item's own slot is <see cref="GearSlot.WEAPON"/>.</summary>
    private const string Blade = "GI_BLADE";

    /// <summary>A helmet, so a second slot can be filled and shown to survive.</summary>
    private const string Helm = "GI_HELM";

    /// <summary>
    /// A slot value no hero wears. <c>GearSlot</c> has no zero member on purpose, so this is what an
    /// uninitialised wire column arrives as.
    /// </summary>
    private const GearSlot Undeclared = (GearSlot)0;

    // ═══════════════════════════════════════════════════════ 1 · a run is in progress

    /// <summary>
    /// 🔒 Unequipping is refused while a run is in progress, and the identical command outside one is
    /// accepted.
    /// </summary>
    /// <remarks>
    /// The pair is the assertion. Both slices carry the same player row wearing the same blade; the
    /// only difference is a live run, so no other rule can account for the refusal. Taking gear
    /// <em>off</em> mid-run had to be refused for <c>EQUIP</c>'s reason — the run's frozen
    /// <c>StartingLoadout</c> means the change could not reach the run it was aimed at, and would
    /// only leave the player's screen disagreeing with the run they are in.
    /// </remarks>
    [Fact]
    public void Unequipping_is_refused_while_a_run_is_in_progress_and_accepted_outside_one()
    {
        var wearer = Wearing(Blade, GearSlot.WEAPON);

        var outsideARun = Unequip(new WorldSlice(Worlds.Rehydrated(wearer), null), GearSlot.WEAPON);
        var duringARun = Unequip(
            new WorldSlice(Worlds.Rehydrated(wearer), Worlds.NewRun()), GearSlot.WEAPON);

        outsideARun.Accepted.ShouldBeTrue(
            "the control was refused " + outsideARun.Rejection + ". This is the same player wearing " +
            "the same blade in the same slot as the refused case below, minus the run — so with it " +
            "failing, the refusal below says nothing about runs at all.");

        duringARun.Accepted.ShouldBeFalse(
            "the loadout was changed during a run. Stripping the hero is as much a loadout change as " +
            "dressing them, and EQUIP is refused here for exactly the same reason.");

        duringARun.Rejection.ShouldBe(
            RejectionReason.ILLEGAL_STATE,
            "the in-run refusal answered some other reason, so it is no longer the rule this case " +
            "pins.");

        duringARun.NewState.Player.Loadout.EquippedCount.ShouldBe(
            1, "a refused UNEQUIP changes nothing — the blade is still worn.");
    }

    // ═══════════════════════════════════════════════════════ 2 · the slot is undeclared

    /// <summary>
    /// 🔒 An undeclared slot value is refused, where the identical command naming a declared, filled
    /// slot is accepted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>There is no separate slot rule, and this case exists to say so rather than to pin
    /// one.</b> The handler is written with the empty-slot read FIRST, and an undeclared slot is one
    /// nothing can be equipped in — so <c>Loadout.TryGet</c> answers false, the empty-slot rule
    /// refuses, and <c>Loadout.Without</c> (which <em>does</em> throw on an undeclared slot) is never
    /// reached. MEASURED: with an <c>Enum.IsDefined</c> guard added to the handler ahead of that
    /// read, neutering the guard left this whole file green, including the naked-hero world below
    /// that was written to discriminate. So the guard was removed rather than kept as a rule that
    /// could not fire (steering S1).
    /// </para>
    /// <para>
    /// What this case pins is therefore the CONTRACT — a malformed wire value is a rejection and
    /// never an exception — plus the ORDERING that delivers it. It fails the day somebody writes the
    /// loadout before reading it, or accepts an empty slot as a no-op, which are exactly the two
    /// edits that would put <c>Loadout.Without</c> back in reach of an uninitialised column.
    /// </para>
    /// </remarks>
    [Fact]
    public void Unequipping_an_undeclared_slot_is_refused_rather_than_thrown()
    {
        var world = new WorldSlice(Worlds.Rehydrated(Wearing(Blade, GearSlot.WEAPON)), null);

        var declared = Unequip(world, GearSlot.WEAPON);
        var undeclared = Unequip(world, Undeclared);

        declared.Accepted.ShouldBeTrue(
            "the control was refused " + declared.Rejection + ". Same slice, same worn blade, no " +
            "run — the ONLY difference from the refused case below is the slot value, so with this " +
            "failing the refusal below is not about the slot.");

        undeclared.Accepted.ShouldBeFalse(
            "a slot value no hero wears was accepted. GearSlot has no zero member on purpose, so " +
            "this is exactly what an uninitialised column reads as.");

        undeclared.Rejection.ShouldBe(
            RejectionReason.ILLEGAL_STATE,
            "the undeclared-slot refusal answered some other reason, so it is no longer the rule " +
            "this case pins.");

        // The same claim on a hero wearing NOTHING, where every slot is empty and the undeclared one
        // is not special in any way. Both worlds have to answer a rejection rather than an
        // exception, because between them they are every shape a loadout can be in when an
        // uninitialised column arrives.
        var naked = new WorldSlice(Worlds.Rehydrated(PlayerSnapshots.Valid), null);

        naked.Player.Loadout.EquippedCount.ShouldBe(
            0, "the premise: this hero wears nothing, so nothing about the slot is occupied.");

        Should.NotThrow(() => Unequip(naked, Undeclared))
            .Rejection.ShouldBe(
                RejectionReason.ILLEGAL_STATE,
                "an undeclared slot must be a REJECTION and never an exception out of Apply. It is " +
                "the empty-slot read that delivers that — Loadout.TryGet answers false where " +
                "Loadout.Without would throw — so this goes red the day the write is moved ahead of " +
                "the read.");
    }

    // ═══════════════════════════════════════════════════════ 3 · the slot is already empty

    /// <summary>
    /// 🔒 An already-empty slot is refused, where the same command on a filled slot is accepted.
    /// </summary>
    /// <remarks>
    /// The pair once more: the same player row, the same declared slot, no run in either slice — the
    /// only difference is whether anything is in the slot. Refusing rather than answering "done" is
    /// the ruling: a genuine retry is `14` §16.3's replay cache's business and never reaches this
    /// handler, so what arrives here with an empty slot is a client whose view of the hero disagrees
    /// with the server's.
    /// </remarks>
    [Fact]
    public void Unequipping_an_already_empty_slot_is_refused_where_a_filled_one_is_accepted()
    {
        var world = new WorldSlice(Worlds.Rehydrated(Wearing(Blade, GearSlot.WEAPON)), null);

        var filled = Unequip(world, GearSlot.WEAPON);
        var empty = Unequip(world, GearSlot.HELMET);

        filled.Accepted.ShouldBeTrue(
            "the control was refused " + filled.Rejection + ". Same slice, same absence of a run, a " +
            "declared slot in both — with this failing, the refusal below is not about emptiness.");

        empty.Accepted.ShouldBeFalse(
            "a slot the hero has nothing in was emptied. HELMET is a DECLARED slot, which is what " +
            "tells this rule apart from the undeclared-slot rule above.");

        empty.Rejection.ShouldBe(
            RejectionReason.ILLEGAL_STATE,
            "the empty-slot refusal answered some other reason, so it is no longer the rule this " +
            "case pins.");

        empty.NewState.Player.Loadout.EquippedCount.ShouldBe(1, "a refused UNEQUIP changes nothing.");
    }

    // ═══════════════════════════════════════════════════════ what an accepted one does

    /// <summary>
    /// 🔒 An accepted <c>UNEQUIP</c> empties the named slot, leaves every other slot alone, and takes
    /// nothing out of the stock.
    /// </summary>
    /// <remarks>
    /// The positive control that keeps the three refusals honest — without it a handler that refused
    /// everything would satisfy all of them. Two slots are filled, because "the named slot is empty
    /// afterwards" is also true of a handler that emptied the whole loadout.
    /// </remarks>
    [Fact]
    public void An_accepted_unequip_empties_only_the_named_slot_and_keeps_the_item()
    {
        var world = new WorldSlice(
            Worlds.Rehydrated(
                PlayerSnapshots.With(
                    inventory: new InventorySnapshot(
                        0,
                        [
                            Inventories.Persist(Inventories.Item(Blade)),
                            Inventories.Persist(Inventories.Item(Helm, GearFamily.HELM)),
                        ],
                        []),
                    loadout: new LoadoutSnapshot(
                        PlayerSnapshots.Gear((GearSlot.WEAPON, Blade), (GearSlot.HELMET, Helm))))),
            null);

        world.Player.Loadout.EquippedCount.ShouldBe(2, "the premise: two slots are filled.");

        var result = Unequip(world, GearSlot.WEAPON);

        result.Accepted.ShouldBeTrue("the unequip was refused " + result.Rejection + ".");

        var loadout = result.NewState.Player.Loadout;

        loadout.TryGet(GearSlot.WEAPON, out _).ShouldBeFalse("the named slot is empty.");

        loadout.TryGet(GearSlot.HELMET, out var stillWorn).ShouldBeTrue(
            "…and ONLY the named slot. A handler that replaced the loadout with an empty one would " +
            "satisfy every other assertion in this case.");

        stillWorn.Value.ShouldBe(Helm);

        // 🔴 Unequipping is not discarding. Player.DiscardItem clears a slot AND removes the item;
        // this must do only the first, or a player would lose the gear they took off.
        result.NewState.Player.Inventory.Stored.Count.ShouldBe(
            2, "the blade is off the hero and still in the stock. UNEQUIP takes gear off; it does " +
            "not destroy it.");

        result.NewState.Player.Inventory
            .Availability(new GearInstanceId(Blade))
            .ShouldBe(ItemAvailability.AVAILABLE, "…and it is available to be worn again.");

        result.Events.ShouldBeEmpty(
            "taking gear off grants nothing and moves no currency, so it has nothing to report.");
    }

    /// <summary>
    /// The slot can be filled again immediately, which is the round trip <c>EQUIP</c> could not
    /// complete on its own.
    /// </summary>
    [Fact]
    public void A_slot_emptied_by_unequip_can_be_filled_again()
    {
        var world = new WorldSlice(Worlds.Rehydrated(Wearing(Blade, GearSlot.WEAPON)), null);

        var emptied = Unequip(world, GearSlot.WEAPON);
        emptied.Accepted.ShouldBeTrue("the unequip was refused " + emptied.Rejection + ".");

        var refilled = SlayIdleRepeat.Core.GameRules.Apply(
            emptied.NewState,
            new EquipCommand(new GearInstanceId(Blade), GearSlot.WEAPON),
            Worlds.Context);

        refilled.Accepted.ShouldBeTrue(
            "re-equipping the item that was just taken off was refused " + refilled.Rejection + ". " +
            "UNEQUIP has to leave the stock in a state EQUIP accepts, or the pair is a one-way door.");

        refilled.NewState.Player.Loadout.TryGet(GearSlot.WEAPON, out var worn).ShouldBeTrue();
        worn.Value.ShouldBe(Blade);
    }

    // ═══════════════════════════════════════════════════════ the worlds

    private static CommandResult Unequip(WorldSlice world, GearSlot slot) =>
        SlayIdleRepeat.Core.GameRules.Apply(world, new UnequipCommand(slot), Worlds.Context);

    /// <summary>A player row holding one item in stock and wearing it in the given slot.</summary>
    private static PlayerSnapshot Wearing(string itemId, GearSlot slot) =>
        PlayerSnapshots.With(
            inventory: new InventorySnapshot(0, [Inventories.Persist(Inventories.Item(itemId))], []),
            loadout: new LoadoutSnapshot(PlayerSnapshots.Gear((slot, itemId))));
}
