using Shouldly;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model.Gear;

/// <summary>
/// The stock a player carries: hold-never-lose at capacity, the four availability answers, locks,
/// and auto-reclaim.
/// </summary>
public sealed class InventoryTests
{
    [Fact]
    public void A_fresh_inventory_is_empty_at_the_base_capacity()
    {
        var inventory = Inventories.Empty();

        inventory.Stored.ShouldBeEmpty();
        inventory.Held.ShouldBeEmpty();
        inventory.ExpansionsPurchased.ShouldBe(0);
        inventory.CapacityWith(Inventories.Tuning).ShouldBe(
            1000,
            "the M4 retro of 2026-08-17 ruled capacity flat at 1000. It was 120 with a ladder to " +
            "320 until then; nothing buys a slot now, so the base IS the capacity.");
    }

    /// <remarks>Load-bearing: the "newest" sort key reads this order.</remarks>
    [Fact]
    public void Stored_order_is_grant_order_with_the_newest_last()
    {
        var inventory = Inventories.Holding(
            Inventories.Item("first"), Inventories.Item("second"), Inventories.Item("third"));

        inventory.Stored.Select(item => item.InstanceId).ShouldBe(
            new[]
            {
                new GearInstanceId("first"),
                new GearInstanceId("second"),
                new GearInstanceId("third"),
            });
    }

    /// <remarks>
    /// Asserted on the identity of the refusal rather than on "something threw": an
    /// <see cref="ArgumentException"/> out of the item's own constructor would satisfy a bare throw
    /// assertion while the duplicate went in.
    /// </remarks>
    [Fact]
    public void Placing_an_identity_the_stock_already_holds_is_refused()
    {
        var inventory = Inventories.Holding(Inventories.Item("gi_0001"));

        var refusal = Should.Throw<InvalidOperationException>(
            () => inventory.Place(Inventories.Item("gi_0001"), Inventories.Tuning));

        refusal.Message.ShouldContain("gi_0001", Case.Sensitive);
        refusal.Message.ShouldContain("already owned", Case.Sensitive);

        inventory.Stored.Count.ShouldBe(1, "…and the refusal did not half-apply");
    }

    /// <remarks>
    /// A duplicate check written over the stored list alone would pass the case above and let a
    /// second copy of a held item in.
    /// </remarks>
    [Fact]
    public void Placing_an_identity_that_is_only_held_is_refused_too()
    {
        var inventory = Full();

        inventory.Place(Inventories.Item("waiting"), Inventories.Tuning)
            .ShouldBe(InventoryPlacement.HELD, "the fixture has to actually overflow, or the " +
                "duplicate below is being placed against the stored list instead of the held one.");

        var refusal = Should.Throw<InvalidOperationException>(
            () => inventory.Place(Inventories.Item("waiting"), Inventories.Tuning));

        refusal.Message.ShouldContain("waiting", Case.Sensitive);
        refusal.Message.ShouldContain("already owned", Case.Sensitive);

        inventory.Held.Count.ShouldBe(1, "…and the refusal did not half-apply");
    }

    // ------------------------------------------------------------------------ hold, never lose

    /// <remarks>
    /// Overfilled by sixty, not by one: a holding list with a small cap, a decay or a "keep the
    /// newest N" rule would pass a one-item probe. The conservation assertion is over identities,
    /// so sixty copies of one item would still be caught.
    /// </remarks>
    [Fact]
    public void At_capacity_an_item_is_held_and_never_dropped()
    {
        const int Overflow = 60;

        var inventory = Inventories.Empty();
        var capacity = inventory.CapacityWith(Inventories.Tuning);
        var granted = Inventories.Fill(capacity + Overflow);

        var placements = granted
            .Select(item => inventory.Place(item, Inventories.Tuning))
            .ToArray();

        placements.Take(capacity).ShouldAllBe(placement => placement == InventoryPlacement.STORED);
        placements.Skip(capacity).ShouldAllBe(placement => placement == InventoryPlacement.HELD);

        inventory.Stored.Count.ShouldBe(capacity);
        inventory.Held.Count.ShouldBe(
            Overflow,
            "the holding list is unbounded: no document authors a cap, a decay or an expiry on it, " +
            "so every one of the sixty items that did not fit is still there.");

        inventory.Stored.Concat(inventory.Held).Select(item => item.InstanceId).ShouldBe(
            granted.Select(item => item.InstanceId),
            "every granted item is somewhere — stored or held — and none was dropped, duplicated or " +
            "reordered on the way in.");
    }

    // ---------------------------------------------------------------- the four unavailabilities

    /// <remarks>
    /// One case rather than four, because the claim is that the answers are distinguishable: four
    /// separate "not AVAILABLE" cases would all pass against a container answering
    /// <c>UNKNOWN_ITEM</c> to everything.
    /// </remarks>
    [Fact]
    public void The_four_reasons_an_item_is_or_is_not_available_are_told_apart()
    {
        var inventory = Full();

        inventory.Place(Inventories.Item("overflowed"), Inventories.Tuning)
            .ShouldBe(InventoryPlacement.HELD, "the fixture has to actually overflow, or the " +
                "HELD_IN_OVERFLOW arm below is asserted over an item that fitted.");

        var stored = inventory.Stored[0].InstanceId;
        var lockedItem = inventory.Stored[1].InstanceId;

        inventory.SetLock(lockedItem, locked: true).ShouldBeTrue();

        inventory.Availability(stored).ShouldBe(ItemAvailability.AVAILABLE);
        inventory.Availability(lockedItem).ShouldBe(ItemAvailability.LOCKED);
        inventory.Availability(new GearInstanceId("overflowed"))
            .ShouldBe(ItemAvailability.HELD_IN_OVERFLOW);
        inventory.Availability(new GearInstanceId("nobody_owns_this"))
            .ShouldBe(ItemAvailability.UNKNOWN_ITEM);

        // An enum whose members collapsed to one value would satisfy every line above.
        new[]
        {
            ItemAvailability.AVAILABLE,
            ItemAvailability.LOCKED,
            ItemAvailability.HELD_IN_OVERFLOW,
            ItemAvailability.UNKNOWN_ITEM,
        }.Distinct().Count().ShouldBe(4);
    }

    // ------------------------------------------------------------------------------ the lock

    [Fact]
    public void Locking_a_stored_item_flips_that_items_flag()
    {
        var inventory = Inventories.Holding(
            Inventories.Item("target"), Inventories.Item("bystander"));

        inventory.SetLock(new GearInstanceId("target"), locked: true).ShouldBeTrue();

        Stored(inventory, "target").Locked.ShouldBeTrue();
        Stored(inventory, "bystander").Locked.ShouldBeFalse(
            "a lock is per item; a container that stored one flag for all of them would pass every " +
            "assertion that only looked at the item it locked.");
    }

    /// <remarks>
    /// The return value is the discriminating part: without it, a caller cannot tell "already
    /// locked" from "refused".
    /// </remarks>
    [Fact]
    public void Locking_is_idempotent_and_reports_whether_anything_changed()
    {
        var inventory = Inventories.Holding(Inventories.Item("target"));
        var target = new GearInstanceId("target");

        inventory.SetLock(target, locked: true).ShouldBeTrue();
        inventory.SetLock(target, locked: true).ShouldBeFalse("it was already locked");
        Stored(inventory, "target").Locked.ShouldBeTrue("…and it is still locked");

        inventory.SetLock(target, locked: false).ShouldBeTrue();
        inventory.SetLock(target, locked: false).ShouldBeFalse("it was already unlocked");
        Stored(inventory, "target").Locked.ShouldBeFalse();
    }

    /// <remarks>
    /// A client naming an item the player does not have is a rejection, not a defect — the handler
    /// turns this <c>false</c> into <c>NOT_OWNED</c>, which it cannot do with an exception.
    /// </remarks>
    [Fact]
    public void An_unknown_id_cannot_be_locked()
    {
        var inventory = Inventories.Holding(Inventories.Item("target"));

        inventory.SetLock(new GearInstanceId("nobody_owns_this"), locked: true).ShouldBeFalse();

        inventory.Stored.ShouldAllBe(item => !item.Locked);
        inventory.Availability(new GearInstanceId("nobody_owns_this"))
            .ShouldBe(ItemAvailability.UNKNOWN_ITEM);
    }

    // -------------------------------------------------------------------------- auto-reclaim

    /// <remarks>
    /// "Exactly one" is the discriminating half: a reclaim that emptied the whole holding list
    /// would put the inventory over its own capacity, and one that pulled none would leave the
    /// player unable to reach an item they just made room for.
    /// </remarks>
    [Fact]
    public void Removing_a_stored_item_reclaims_the_oldest_held_item()
    {
        var inventory = Full();

        inventory.Place(Inventories.Item("held_a"), Inventories.Tuning);
        inventory.Place(Inventories.Item("held_b"), Inventories.Tuning);

        inventory.Remove(Inventories.Id("fill", 1), Inventories.Tuning).ShouldBeTrue();

        inventory.Stored.Count.ShouldBe(inventory.CapacityWith(Inventories.Tuning));
        inventory.Stored[^1].InstanceId.ShouldBe(
            new GearInstanceId("held_a"),
            "the oldest held item is the one that comes back, and it arrives at the end of the " +
            "stored list because that is where a newly stocked item goes.");

        inventory.Held.Select(item => item.InstanceId.Value).ShouldBe(new[] { "held_b" });
        inventory.Availability(new GearInstanceId("held_a")).ShouldBe(ItemAvailability.AVAILABLE);
        inventory.Availability(new GearInstanceId("held_b"))
            .ShouldBe(ItemAvailability.HELD_IN_OVERFLOW);
    }

    /// <remarks>
    /// The stock did not change, so there is nothing to reclaim into — an unconditional reclaim
    /// after a remove would pull an item into a slot that never opened.
    /// </remarks>
    [Fact]
    public void Removing_a_held_item_takes_it_out_of_the_holding_list()
    {
        var inventory = Full();
        inventory.Place(Inventories.Item("held_a"), Inventories.Tuning);
        inventory.Place(Inventories.Item("held_b"), Inventories.Tuning);

        inventory.Remove(new GearInstanceId("held_a"), Inventories.Tuning).ShouldBeTrue();

        inventory.Held.Select(item => item.InstanceId.Value).ShouldBe(new[] { "held_b" });
        inventory.Stored.Count.ShouldBe(inventory.CapacityWith(Inventories.Tuning));
        inventory.Availability(new GearInstanceId("held_a")).ShouldBe(ItemAvailability.UNKNOWN_ITEM);
    }

    [Fact]
    public void Removing_an_unknown_id_changes_nothing()
    {
        var inventory = Inventories.Holding(Inventories.Item("target"));

        inventory.Remove(new GearInstanceId("nobody_owns_this"), Inventories.Tuning).ShouldBeFalse();

        inventory.Stored.Count.ShouldBe(1);
    }

    // ---------------------------------------------------------------- the deferred expansion

    /// <remarks>
    /// The M4 retro of 2026-08-17 ruled capacity flat and authored no <c>EXPAND_INVENTORY</c>
    /// command; the seam survives as a member that exists to throw. A throw rather than a
    /// <c>false</c>: no command carries this request, so a caller reaching it is miswired rather
    /// than a player asking for something they cannot have.
    /// </remarks>
    [Fact]
    public void No_expansion_can_be_bought_and_capacity_does_not_move()
    {
        var inventory = Inventories.Empty();
        var before = inventory.CapacityWith(Inventories.Tuning);

        var refusal = Should.Throw<InvalidOperationException>(
            () => Inventory.PurchaseExpansion(Inventories.Tuning));

        refusal.Message.ShouldContain(
            "flat",
            Case.Sensitive,
            "the refusal has to say WHY — a flat ceiling — rather than read as a purchase cap the " +
            "player could still be under. A caller told 'all 10 expansions have been bought' would " +
            "conclude the ladder works and this account has finished it.");

        inventory.ExpansionsPurchased.ShouldBe(0, "…and the refusal did not half-apply.");
        inventory.CapacityWith(Inventories.Tuning).ShouldBe(
            before, "capacity is flat: nothing here can move it.");
    }

    /// <remarks>
    /// The purchase counter is persisted for the day the owner deals with the limit, so it must
    /// not be read as capacity in the meantime.
    /// </remarks>
    [Fact]
    public void A_recorded_purchase_count_buys_nothing()
    {
        var bought = Inventories.Rehydrated(
            new InventorySnapshot(Inventories.Tuning.MaxPurchases, [], []));

        bought.ExpansionsPurchased.ShouldBe(
            Inventories.Tuning.MaxPurchases, "the premise: the row really does record purchases.");

        bought.CapacityWith(Inventories.Tuning).ShouldBe(
            Inventories.Empty().CapacityWith(Inventories.Tuning),
            "a player whose row records every purchase the deferred ladder prices holds exactly what " +
            "a brand-new player holds. If this ever differs, capacity has started reading the count " +
            "again and the flat ruling is gone.");
    }

    /// <summary>An inventory at capacity, built by placing exactly as many items as fit.</summary>
    private static Inventory Full()
    {
        var inventory = Inventories.Empty();

        foreach (var item in Inventories.Fill(inventory.CapacityWith(Inventories.Tuning)))
        {
            inventory.Place(item, Inventories.Tuning);
        }

        return inventory;
    }

    /// <summary>The stored item with an identity, failing with the id rather than an index error.</summary>
    private static GearInstance Stored(Inventory inventory, string id) =>
        inventory.Stored.SingleOrDefault(item => item.InstanceId.Value.Equals(id, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"'{id}' is not in the stored list.");
}
