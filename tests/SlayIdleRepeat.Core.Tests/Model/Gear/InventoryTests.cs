using Shouldly;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model.Gear;

/// <summary>
/// The stock a player carries: what fits, what happens to what does not, and the four different
/// reasons an item can be unavailable.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Hold, never lose.</b> No random source in this game may starve a player, and the opposite
/// failure — a grant that arrives while the stock is full and is silently dropped — is the same
/// wound from the other side. A full inventory therefore <em>holds</em> what it cannot store, and
/// the held item comes back the moment space exists. Nothing here caps, decays or expires the
/// holding list, because no document authorises any of the three.
/// </para>
/// <para>
/// 🔒 <b>Four unavailabilities, told apart.</b> "The item was not added" is equally true of a full
/// inventory, a locked item, an unknown id and a rejected command, and a suite that only asserted
/// that could not tell them apart. Every case below names which rule fired.
/// </para>
/// </remarks>
public sealed class InventoryTests
{
    /// <summary>A fresh inventory holds nothing, has bought nothing, and its capacity is the base.</summary>
    [Fact]
    public void A_fresh_inventory_is_empty_at_the_base_capacity()
    {
        var inventory = Inventories.Empty();

        inventory.Stored.ShouldBeEmpty();
        inventory.Held.ShouldBeEmpty();
        inventory.ExpansionsPurchased.ShouldBe(0);
        inventory.CapacityWith(Inventories.Tuning).ShouldBe(120);
    }

    /// <summary>Stored order is grant order: the newest item is last.</summary>
    /// <remarks>
    /// Load-bearing rather than incidental — the "newest" sort key reads it, and a container that
    /// normalised its order would leave that key with nothing to sort by.
    /// </remarks>
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

    /// <summary>An item placed into a stocked inventory is stored, and says so.</summary>
    [Fact]
    public void An_item_that_fits_is_stored()
    {
        var inventory = Inventories.Empty();

        inventory.Place(Inventories.Item("gi_0001"), Inventories.Tuning)
            .ShouldBe(InventoryPlacement.STORED);

        inventory.Stored.Count.ShouldBe(1);
        inventory.Held.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------------ hold, never lose

    /// <summary>
    /// 🔒 At capacity, an item is <b>held</b> rather than refused or dropped — and the holding list is
    /// unbounded.
    /// </summary>
    /// <remarks>
    /// Overfilled by half the base capacity again, not by one: a holding list with a small cap, a
    /// decay or a "keep the newest N" rule would pass a one-item probe and lose the sixtieth item
    /// silently. The conservation assertion is over the identities, not the count, so an
    /// implementation that held sixty <em>copies</em> of one item would still be caught.
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

    /// <summary>Held items arrive in order, and the order survives further grants.</summary>
    /// <remarks>The reclaim rule pulls them back in this order, so it has to be an order at all.</remarks>
    [Fact]
    public void Held_items_keep_their_arrival_order()
    {
        var inventory = Full();

        inventory.Place(Inventories.Item("held_a"), Inventories.Tuning);
        inventory.Place(Inventories.Item("held_b"), Inventories.Tuning);
        inventory.Place(Inventories.Item("held_c"), Inventories.Tuning);

        inventory.Held.Select(item => item.InstanceId.Value).ShouldBe(
            new[] { "held_a", "held_b", "held_c" });
    }

    // ---------------------------------------------------------------- the four unavailabilities

    /// <summary>
    /// 🔒 The four answers are four, and each names its own reason.
    /// </summary>
    /// <remarks>
    /// Written as one case rather than four, because the claim is that they are
    /// <em>distinguishable</em>: four separate cases each asserting "not AVAILABLE" would all pass
    /// against a container that answered <c>UNKNOWN_ITEM</c> to everything.
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

        // The floor under the four: an enum whose members all collapsed to one value would satisfy
        // every line above.
        new[]
        {
            ItemAvailability.AVAILABLE,
            ItemAvailability.LOCKED,
            ItemAvailability.HELD_IN_OVERFLOW,
            ItemAvailability.UNKNOWN_ITEM,
        }.Distinct().Count().ShouldBe(4);
    }

    /// <summary>A held item is not available even though it is owned — the two are different facts.</summary>
    /// <remarks>
    /// The discriminating half: an implementation that answered <c>UNKNOWN_ITEM</c> for a held item
    /// would be telling a caller the player does not own something they were just granted, and every
    /// "is it there" assertion would still pass.
    /// </remarks>
    [Fact]
    public void A_held_item_is_owned_and_unavailable_rather_than_unknown()
    {
        var inventory = Full();
        inventory.Place(Inventories.Item("waiting"), Inventories.Tuning);

        inventory.Held.Select(item => item.InstanceId.Value).ShouldContain("waiting");

        inventory.Availability(new GearInstanceId("waiting")).ShouldBe(
            ItemAvailability.HELD_IN_OVERFLOW,
            "the player owns it — it is in the holding list — and simply cannot act on it yet. " +
            "UNKNOWN_ITEM here would report the grant as never having happened.");
    }

    // ------------------------------------------------------------------------------ the lock

    /// <summary>Locking a stored item flips its own flag, and nothing else's.</summary>
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

    /// <summary>Locking is idempotent, and the return value says whether anything moved.</summary>
    /// <remarks>
    /// The return value is the discriminating part: without it, a caller cannot tell "already locked"
    /// from "refused", and both look like "nothing happened".
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

    /// <summary>An unknown id cannot be locked, and the refusal is a <c>false</c> rather than a throw.</summary>
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

    /// <summary>A locked item stays locked across a place that does not touch it.</summary>
    [Fact]
    public void A_lock_survives_later_grants()
    {
        var inventory = Inventories.Holding(Inventories.Item("target"));
        inventory.SetLock(new GearInstanceId("target"), locked: true);

        inventory.Place(Inventories.Item("later"), Inventories.Tuning);

        Stored(inventory, "target").Locked.ShouldBeTrue();
        inventory.Availability(new GearInstanceId("target")).ShouldBe(ItemAvailability.LOCKED);
    }

    // -------------------------------------------------------------------------- auto-reclaim

    /// <summary>Removing a stored item pulls exactly one held item back, the oldest first.</summary>
    /// <remarks>
    /// "Exactly one" is the discriminating half: a reclaim that emptied the whole holding list would
    /// put the inventory over its own capacity, and one that pulled none would leave the player
    /// unable to reach an item they just made room for.
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

    /// <summary>Buying an expansion reclaims as many held items as the new slots hold, and no more.</summary>
    /// <remarks>
    /// The fixture holds thirty and buys twenty slots, so the arithmetic can be wrong in both
    /// directions and be seen: a reclaim that emptied the list would store thirty into twenty slots,
    /// and one that took a fixed one would leave nineteen slots idle.
    /// </remarks>
    [Fact]
    public void Buying_an_expansion_reclaims_only_as_many_held_items_as_now_fit()
    {
        var inventory = Full();

        foreach (var item in Inventories.Fill(30, "held"))
        {
            inventory.Place(item, Inventories.Tuning).ShouldBe(InventoryPlacement.HELD);
        }

        inventory.PurchaseExpansion(Inventories.Tuning);

        inventory.ExpansionsPurchased.ShouldBe(1);
        inventory.CapacityWith(Inventories.Tuning).ShouldBe(140);
        inventory.Stored.Count.ShouldBe(140);
        inventory.Held.Count.ShouldBe(10);

        inventory.Stored.TakeLast(20).Select(item => item.InstanceId).ShouldBe(
            Enumerable.Range(1, 20).Select(n => Inventories.Id("held", n)),
            "arrival order, oldest first — a reclaim that took the newest twenty would leave the ten " +
            "the player has been waiting longest for still waiting.");

        inventory.Held.Select(item => item.InstanceId).ShouldBe(
            Enumerable.Range(21, 10).Select(n => Inventories.Id("held", n)));
    }

    /// <summary>Removing a held item removes that item, and does not reclaim in its place.</summary>
    /// <remarks>
    /// The stock did not change, so there is nothing to reclaim into — an implementation that
    /// reclaimed unconditionally after a remove would pull an item into a slot that never opened.
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

    /// <summary>Removing an id nobody owns changes nothing and says so.</summary>
    [Fact]
    public void Removing_an_unknown_id_changes_nothing()
    {
        var inventory = Inventories.Holding(Inventories.Item("target"));

        inventory.Remove(new GearInstanceId("nobody_owns_this"), Inventories.Tuning).ShouldBeFalse();

        inventory.Stored.Count.ShouldBe(1);
    }

    // ------------------------------------------------------------------------- the purchase cap

    /// <summary>Ten expansions can be bought, and the eleventh is a defect rather than a no-op.</summary>
    /// <remarks>
    /// A throw rather than a <c>false</c>, and deliberately unlike <see cref="An_unknown_id_cannot_be_locked"/>:
    /// the handler checks the cap before it charges anybody, so reaching this method past the cap is
    /// a miswired caller rather than a player asking for something they cannot have.
    /// </remarks>
    [Fact]
    public void The_eleventh_expansion_is_refused()
    {
        var inventory = Inventories.Empty();

        for (var purchase = 0; purchase < 10; purchase++)
        {
            inventory.PurchaseExpansion(Inventories.Tuning);
        }

        inventory.ExpansionsPurchased.ShouldBe(10);
        inventory.CapacityWith(Inventories.Tuning).ShouldBe(320);

        Should.Throw<InvalidOperationException>(
            () => inventory.PurchaseExpansion(Inventories.Tuning));

        inventory.ExpansionsPurchased.ShouldBe(10, "…and the refusal did not half-apply");
    }

    /// <summary>Capacity grows by the authored step with each purchase.</summary>
    [Fact]
    public void Capacity_grows_by_the_authored_step_with_each_purchase()
    {
        var inventory = Inventories.Empty();
        var capacities = new List<int> { inventory.CapacityWith(Inventories.Tuning) };

        for (var purchase = 0; purchase < 10; purchase++)
        {
            inventory.PurchaseExpansion(Inventories.Tuning);
            capacities.Add(inventory.CapacityWith(Inventories.Tuning));
        }

        capacities.ShouldBe(new[] { 120, 140, 160, 180, 200, 220, 240, 260, 280, 300, 320 });
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
