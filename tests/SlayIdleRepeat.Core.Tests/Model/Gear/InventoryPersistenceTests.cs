using Shouldly;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model.Gear;

/// <summary>
/// The inventory's persisted shape, compared over canonical bytes throughout — never record
/// equality, which compares the snapshot's list components by reference.
/// </summary>
public sealed class InventoryPersistenceTests
{
    [Fact]
    public void A_stocked_inventory_round_trips_through_its_own_snapshot()
    {
        var original = Stocked();
        var snapshot = original.ToSnapshot();

        var rehydrated = Inventory.Rehydrate(snapshot, Inventories.Tuning);
        rehydrated.IsSuccess.ShouldBeTrue(rehydrated.IsFailure ? rehydrated.Error : string.Empty);

        Bytes(rehydrated.Value.ToSnapshot()).ShouldBe(Bytes(snapshot));

        // …and the round trip is over something, rather than over two empty lists.
        snapshot.Stored.Count.ShouldBeGreaterThan(1);
        snapshot.Held.Count.ShouldBeGreaterThan(1);
        snapshot.ExpansionsPurchased.ShouldBe(1);
        snapshot.Stored.ShouldContain(item => item.Locked);
        snapshot.Stored.ShouldContain(item => item.Affixes.Count > 0);
    }

    /// <remarks>
    /// Named separately from the byte comparison: a rehydration that stocked everything it could
    /// would quietly hand a player items they had been waiting on.
    /// </remarks>
    [Fact]
    public void A_round_trip_keeps_held_items_held()
    {
        var snapshot = Stocked().ToSnapshot();
        var rehydrated = Inventories.Rehydrated(snapshot);

        rehydrated.Held.Select(item => item.InstanceId).ShouldBe(
            snapshot.Held.Select(item => item.InstanceId));

        rehydrated.Stored.Select(item => item.InstanceId).ShouldBe(
            snapshot.Stored.Select(item => item.InstanceId));
    }

    /// <remarks>
    /// The affixes are the deepest thing the encoding descends into — a snapshot that wrote a count
    /// instead of the rolls, or the ids and not the values, would agree here.
    /// </remarks>
    [Fact]
    public void Two_inventories_differing_in_one_affix_value_hash_differently()
    {
        var left = Inventories.Holding(WithAffix(0.0642)).ToSnapshot();
        var right = Inventories.Holding(WithAffix(0.0643)).ToSnapshot();

        Bytes(right).ShouldNotBe(
            Bytes(left),
            "the two items differ by one ten-thousandth on one affix, which is the smallest " +
            "difference persisted state can carry at all.");
    }

    /// <remarks>
    /// A boolean is the easiest field to leave out of a snapshot record entirely; if it were
    /// dropped, every other case in this file would still pass.
    /// </remarks>
    [Fact]
    public void Two_inventories_differing_in_one_lock_flag_hash_differently()
    {
        var unlocked = Inventories.Holding(Inventories.Item("gi_0001"), Inventories.Item("gi_0002"));
        var locked = Inventories.Holding(Inventories.Item("gi_0001"), Inventories.Item("gi_0002"));

        locked.SetLock(new GearInstanceId("gi_0002"), locked: true).ShouldBeTrue();

        Bytes(locked.ToSnapshot()).ShouldNotBe(Bytes(unlocked.ToSnapshot()));
    }

    /// <remarks>
    /// The case a snapshot that concatenated the two lists — or wrote a single list plus a count —
    /// would pass.
    /// </remarks>
    [Fact]
    public void The_same_items_stored_and_held_are_two_different_states()
    {
        var items = Inventories.Fill(3);

        var allStored = new InventorySnapshot(
            0,
            items.Select(Snapshot).ToArray(),
            []);

        var oneHeld = new InventorySnapshot(
            0,
            items.Take(2).Select(Snapshot).ToArray(),
            items.Skip(2).Select(Snapshot).ToArray());

        allStored.Stored.Concat(allStored.Held).Select(item => item.InstanceId)
            .ShouldBe(
                oneHeld.Stored.Concat(oneHeld.Held).Select(item => item.InstanceId),
                "the two rows own exactly the same three items, so the placement is the only thing " +
                "that can move the bytes below.");

        Bytes(oneHeld).ShouldNotBe(Bytes(allStored));
    }

    /// <remarks>
    /// The count buys nothing since the 2026-08-17 flat-capacity ruling —
    /// <c>InventoryTests.A_recorded_purchase_count_buys_nothing</c> — but the column must survive,
    /// because the ladder is deferred rather than deleted.
    /// </remarks>
    [Fact]
    public void The_purchase_count_survives_the_round_trip()
    {
        var bought = Inventories.Rehydrated(new InventorySnapshot(2, [], []));

        var rehydrated = Inventories.Rehydrated(bought.ToSnapshot());

        rehydrated.ExpansionsPurchased.ShouldBe(2);

        Bytes(bought.ToSnapshot()).ShouldNotBe(Bytes(Inventories.Empty().ToSnapshot()));
    }

    /// <remarks>
    /// A row over the ceiling is a save written by a build with different tuning, and reading it
    /// would hand a player slots the game says do not exist. One item over, so the bound is the
    /// ceiling itself.
    /// </remarks>
    [Fact]
    public void A_row_holding_more_than_the_ceiling_is_refused()
    {
        var overfull = new InventorySnapshot(
            0,
            Inventories.Fill(Inventories.Tuning.MaxCapacity + 1).Select(Snapshot).ToArray(),
            []);

        var result = Inventory.Rehydrate(overfull, Inventories.Tuning);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(InventorySnapshot.Stored), Case.Sensitive);
    }

    /// <remarks>
    /// The purchase count is a tunable's worth of history: a balance patch that shortened the
    /// ladder leaves real rows above the new cap, and refusing them would turn a data edit into an
    /// account outage. "Still loads" is the weaker half — before the 2026-08-17 ruling a capacity
    /// read derived from this count threw on the first grant — hence the grant, lock and removal.
    /// </remarks>
    [Fact]
    public void A_row_past_the_purchase_cap_still_loads_and_still_grants()
    {
        var beyond = Inventories.Tuning.MaxPurchases + 3;

        var loaded = Inventory.Rehydrate(
            new InventorySnapshot(beyond, [], []), Inventories.Tuning);

        loaded.IsSuccess.ShouldBeTrue(loaded.IsFailure ? loaded.Error : string.Empty);
        loaded.Value.ExpansionsPurchased.ShouldBe(
            beyond, "the row is read as written — a load that clamped the stored count would edit a " +
            "player's state on the way in, invisibly.");

        loaded.Value.CapacityWith(Inventories.Tuning).ShouldBe(
            Inventories.Tuning.MaxCapacity,
            "the capacity is the flat ceiling, not an exception and not something the count moved.");

        loaded.Value.Place(Inventories.Item("gi_0001"), Inventories.Tuning)
            .ShouldBe(InventoryPlacement.STORED);
        loaded.Value.SetLock(new GearInstanceId("gi_0001"), locked: true).ShouldBeTrue();
        loaded.Value.Remove(new GearInstanceId("gi_0001"), Inventories.Tuning).ShouldBeTrue();
    }

    /// <remarks>An inventory holding one identity twice makes every operation that names an id ambiguous.</remarks>
    [Fact]
    public void A_row_naming_one_item_twice_is_refused()
    {
        var item = Snapshot(Inventories.Item("gi_0001"));

        Inventory.Rehydrate(new InventorySnapshot(0, [item, item], []), Inventories.Tuning)
            .IsFailure.ShouldBeTrue("the same identity is stored twice");

        Inventory.Rehydrate(new InventorySnapshot(0, [item], [item]), Inventories.Tuning)
            .IsFailure.ShouldBeTrue("the same identity is both stored and held");
    }

    [Fact]
    public void A_null_list_is_a_fault_rather_than_an_empty_one()
    {
        Inventory.Rehydrate(new InventorySnapshot(0, null!, []), Inventories.Tuning)
            .IsFailure.ShouldBeTrue();

        Inventory.Rehydrate(new InventorySnapshot(0, [], null!), Inventories.Tuning)
            .IsFailure.ShouldBeTrue();
    }

    /// <summary>An inventory holding a stored item, a locked item, an affixed item and two held items.</summary>
    private static Inventory Stocked()
    {
        var inventory = Inventories.Rehydrated(new InventorySnapshot(1, [], []));

        foreach (var item in Inventories.Fill(inventory.CapacityWith(Inventories.Tuning) - 1))
        {
            inventory.Place(item, Inventories.Tuning);
        }

        inventory.Place(WithAffix(0.0642), Inventories.Tuning);
        inventory.Place(Inventories.Item("held_a"), Inventories.Tuning);
        inventory.Place(Inventories.Item("held_b"), Inventories.Tuning);

        inventory.SetLock(Inventories.Id("fill", 1), locked: true);

        return inventory;
    }

    /// <summary>One affixed item, whose single affix value is the only thing a caller varies.</summary>
    private static GearInstance WithAffix(double value) => Inventories.Item(
        "gi_affixed",
        rarity: Rarity.B,
        affixes: [new GearAffixRoll("AFX_CRIT_CHANCE", value)]);

    private static GearInstanceSnapshot Snapshot(GearInstance item) => Inventories.Persist(item);

    private static byte[] Bytes(InventorySnapshot snapshot) =>
        CanonicalStateWriter.CanonicalBytes(snapshot);
}
