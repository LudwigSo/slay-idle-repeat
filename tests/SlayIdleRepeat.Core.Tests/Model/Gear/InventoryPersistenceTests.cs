using Shouldly;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model.Gear;

/// <summary>
/// The inventory's persisted shape: what a round trip has to preserve, and what has to move the
/// bytes.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Every comparison here is over canonical bytes, never over record equality.</b> An
/// <c>InventorySnapshot</c> carries two <c>IReadOnlyList</c>s and each element carries a third, and a
/// synthesized record <c>Equals</c> compares an <c>IReadOnlyList&lt;T&gt;</c> component <em>by
/// reference</em>. Two snapshots describing the same inventory would therefore be "unequal", and —
/// far worse — a test written on record equality would report a round trip as broken while the bytes
/// agreed, or pass a difference it never actually looked at.
/// </para>
/// <para>
/// The negative half is the load-bearing one: a round trip that dropped the lock flags, flattened the
/// held list into the stored one, or wrote the affixes as a count would still round-trip
/// <em>something</em>. Each case below moves exactly one thing and requires the bytes to move with it.
/// </para>
/// </remarks>
public sealed class InventoryPersistenceTests
{
    /// <summary>A non-empty inventory — stored and held both populated — survives a round trip byte for byte.</summary>
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

    /// <summary>The round trip preserves which list an item was in, not merely that it is owned.</summary>
    /// <remarks>
    /// Stated separately from the byte comparison above because it is the failure a reader would most
    /// want named: a rehydration that stocked everything it could would quietly hand a player items
    /// they had been waiting on, and the byte comparison alone reports only "different".
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

    /// <summary>One affix value apart is two different inventories, and the bytes say so.</summary>
    /// <remarks>
    /// The affixes are the deepest thing the encoding descends into — a snapshot that wrote a count
    /// instead of the rolls, or wrote the ids and not the values, would agree here.
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

    /// <summary>One lock flag apart is two different inventories.</summary>
    /// <remarks>
    /// A boolean is one byte in the encoding and the easiest field to leave out of a snapshot record
    /// entirely; if it were dropped, every other case in this file would still pass.
    /// </remarks>
    [Fact]
    public void Two_inventories_differing_in_one_lock_flag_hash_differently()
    {
        var unlocked = Inventories.Holding(Inventories.Item("gi_0001"), Inventories.Item("gi_0002"));
        var locked = Inventories.Holding(Inventories.Item("gi_0001"), Inventories.Item("gi_0002"));

        locked.SetLock(new GearInstanceId("gi_0002"), locked: true).ShouldBeTrue();

        Bytes(locked.ToSnapshot()).ShouldNotBe(Bytes(unlocked.ToSnapshot()));
    }

    /// <summary>
    /// The same items, one of them stored in the first inventory and held in the second, are two
    /// different states.
    /// </summary>
    /// <remarks>
    /// The case a snapshot that concatenated the two lists — or wrote a single list plus a count —
    /// would pass. Both inventories own exactly the same identities, so nothing but the placement can
    /// be moving the bytes.
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

    /// <summary>The purchase count is persisted, and moving it moves the bytes.</summary>
    /// <remarks>
    /// Without this the field would be state nothing reads back. ⚠️ It no longer buys anything —
    /// capacity is flat as of the 2026-08-17 ruling and nothing writes this column — but it is still
    /// carried, because the ladder is deferred rather than deleted and this is the counter it moves
    /// the day the owner deals with the limit. That it buys nothing is
    /// <c>InventoryTests.A_recorded_purchase_count_buys_nothing</c>'s claim; this one is only that
    /// the column survives.
    /// </remarks>
    [Fact]
    public void The_purchase_count_survives_the_round_trip()
    {
        var bought = Inventories.Rehydrated(new InventorySnapshot(2, [], []));

        var rehydrated = Inventories.Rehydrated(bought.ToSnapshot());

        rehydrated.ExpansionsPurchased.ShouldBe(2);

        Bytes(bought.ToSnapshot()).ShouldNotBe(Bytes(Inventories.Empty().ToSnapshot()));
    }

    /// <summary>A row whose stored list is longer than the flat ceiling is refused.</summary>
    /// <remarks>
    /// The stock cannot exceed the ceiling — a row that does is a save written by a build with
    /// different tuning, and reading it would hand a player slots the game says do not exist. One
    /// item over, so the bound is the ceiling itself rather than "roughly that many".
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

    /// <summary>
    /// 🔒 A row claiming more purchases than the ladder currently prices still loads, and the
    /// inventory still works afterwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Energy ceiling's rule, on the other tunable this aggregate carries: the count is a
    /// tunable's worth of history, a balance patch that shortened the ladder leaves real rows above
    /// the new cap, and refusing to load them would turn a data edit into an account outage.
    /// </para>
    /// <para>
    /// "Still loads" is only half of it, and the weaker half. A capacity read that derived anything
    /// from this count could still <em>throw</em> on the first grant — which is exactly what happened
    /// before the 2026-08-17 ruling, when every read went through a <c>CapacityAt</c> that refused an
    /// argument outside <c>0..MaxPurchases</c> — and a load-only assertion would report that outage
    /// as fixed. Hence the grant, the lock and the removal below.
    /// </para>
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

    /// <summary>A row naming one item twice is refused, whichever list the repeat is in.</summary>
    /// <remarks>
    /// Two lists means two places a duplicate can hide, and an inventory holding one identity twice
    /// makes every operation that names an id ambiguous.
    /// </remarks>
    [Fact]
    public void A_row_naming_one_item_twice_is_refused()
    {
        var item = Snapshot(Inventories.Item("gi_0001"));

        Inventory.Rehydrate(new InventorySnapshot(0, [item, item], []), Inventories.Tuning)
            .IsFailure.ShouldBeTrue("the same identity is stored twice");

        Inventory.Rehydrate(new InventorySnapshot(0, [item], [item]), Inventories.Tuning)
            .IsFailure.ShouldBeTrue("the same identity is both stored and held");
    }

    /// <summary>A null list is a fault, not an empty inventory.</summary>
    [Fact]
    public void A_null_list_is_a_fault_rather_than_an_empty_one()
    {
        Inventory.Rehydrate(new InventorySnapshot(0, null!, []), Inventories.Tuning)
            .IsFailure.ShouldBeTrue();

        Inventory.Rehydrate(new InventorySnapshot(0, [], null!), Inventories.Tuning)
            .IsFailure.ShouldBeTrue();
    }

    /// <summary>An inventory holding a stored item, a locked item, an affixed item and two held items.</summary>
    /// <remarks>
    /// The purchase count is written into the row rather than bought, because nothing buys an
    /// expansion since the 2026-08-17 ruling. It is still on the row on purpose: the byte comparison
    /// above is only about a column that carries a value.
    /// </remarks>
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

    /// <summary>The persisted form of one item, so a case can build a row without an inventory.</summary>
    private static GearInstanceSnapshot Snapshot(GearInstance item) => Inventories.Persist(item);

    /// <summary>The canonical bytes of a row — the one comparison this file makes.</summary>
    private static byte[] Bytes(InventorySnapshot snapshot) =>
        CanonicalStateWriter.CanonicalBytes(snapshot);
}
