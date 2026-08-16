using Shouldly;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Inventory;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Inventory;

/// <summary>
/// The one place an inventory answer becomes a rejection: which of the four availabilities a caller
/// got, and which domain-tier reason the player is told.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>This is the seam that makes "the item was not added" tell its causes apart.</b> A handler
/// that mapped every unavailable item onto one catch-all would show the same toast to a player who
/// does not own the item, a player whose item is locked, and a player whose stock is full — three
/// different things to do about it. Every consumer of an owned item routes its precondition through
/// here rather than writing its own <c>if</c>.
/// </para>
/// <para>
/// It is also the only thing that makes <c>INVENTORY_FULL</c> reachable. The reason has existed,
/// documented, with no production code producing it — under the hold-not-lose rule a grant is never
/// refused, so the reason belongs to operations on a <em>held</em> item rather than to the grant.
/// </para>
/// </remarks>
public sealed class InventoryAccessTests
{
    /// <summary>The mapping, stated once as a table and checked against the whole vocabulary.</summary>
    /// <remarks>
    /// Transcribed here rather than read back off the production switch, which would say the mapping
    /// is right because it is what it is.
    /// </remarks>
    private static readonly (ItemAvailability Availability, RejectionReason? Reason)[] Mapping =
    {
        (ItemAvailability.AVAILABLE, null),
        (ItemAvailability.UNKNOWN_ITEM, RejectionReason.NOT_OWNED),
        (ItemAvailability.HELD_IN_OVERFLOW, RejectionReason.INVENTORY_FULL),
        (ItemAvailability.LOCKED, RejectionReason.ILLEGAL_STATE),
    };

    /// <summary>Each availability maps to its own reason, and an available item to none at all.</summary>
    [Fact]
    public void Each_availability_maps_to_its_own_rejection()
    {
        var checkedRows = 0;

        foreach (var (availability, reason) in Mapping)
        {
            InventoryAccess.RejectionFor(availability).ShouldBe(
                reason,
                $"{availability} is answered with {reason?.ToString() ?? "no rejection at all"}.");
            checkedRows++;
        }

        checkedRows.ShouldBe(4, "the assertion above lives inside a loop");
    }

    /// <summary>
    /// 🔒 S3 — the table covers the whole vocabulary, so a fifth availability goes red here rather
    /// than being quietly answered by a <c>default</c> arm.
    /// </summary>
    /// <remarks>
    /// Both directions. A member the table has forgotten is an unmapped state the game can be in; a
    /// table row for a member that no longer exists is a mapping covering nothing. Neither is visible
    /// from a case that only drives the four names it already knows.
    /// </remarks>
    [Fact]
    public void The_mapping_covers_every_declared_availability_and_nothing_else()
    {
        var declared = Enum.GetValues<ItemAvailability>();

        declared.Length.ShouldBe(
            4,
            "an emptied vocabulary would make the sweep below quantify over nothing. Four is the " +
            "count the mapping was written against: available, unknown, held, locked.");

        declared.Except(Mapping.Select(row => row.Availability)).ShouldBeEmpty(
            "an availability with no row in this table is a state a caller can observe and no rule " +
            "can turn into an answer. Add the row and decide the reason deliberately — do not let a " +
            "default arm pick one.");

        Mapping.Select(row => row.Availability).Except(declared).ShouldBeEmpty(
            "a table row for an availability nothing declares maps nothing.");

        Mapping.Select(row => row.Availability).Distinct().Count().ShouldBe(
            Mapping.Length, "the table names each availability once");
    }

    /// <summary>
    /// 🔒 S2 — the three refusals are three <b>different</b> reasons, and only one availability is
    /// not a refusal at all.
    /// </summary>
    /// <remarks>
    /// The whole point of the seam. A mapping that answered <c>ILLEGAL_STATE</c> to all three would
    /// satisfy every "is it refused" assertion above and tell the player nothing about which of three
    /// unrelated situations they are in.
    /// </remarks>
    [Fact]
    public void The_three_refusals_are_three_different_reasons()
    {
        var answers = Enum.GetValues<ItemAvailability>()
            .Select(InventoryAccess.RejectionFor)
            .ToArray();

        answers.Count(reason => reason is null).ShouldBe(
            1, "exactly one availability — AVAILABLE — is not a refusal.");

        answers.Where(reason => reason is not null).Distinct().Count().ShouldBe(
            3,
            "not owned, held in overflow and locked are three different things to tell a player and " +
            "three different things for a client to do about it.");
    }

    /// <summary>A value outside the vocabulary is a caller defect, not a silent <c>ILLEGAL_STATE</c>.</summary>
    /// <remarks>
    /// The complement of the coverage rule: that one catches a member added without a row at build
    /// time on the next test run, this catches a cast that never was a member.
    /// </remarks>
    [Fact]
    public void A_value_outside_the_vocabulary_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(
                () => InventoryAccess.RejectionFor((ItemAvailability)99))
            .ParamName.ShouldBe("availability");
    }

    // ------------------------------------------------------------------ against a real inventory

    /// <summary>
    /// 🔒 Driven off a real inventory rather than off cast enum values: the four states are reached
    /// the way a handler reaches them, and each one produces its own answer.
    /// </summary>
    /// <remarks>
    /// A lookup table can be correct about names it is handed and still be wired to a container that
    /// never produces three of them. This is the case that would fail if <c>Availability</c> answered
    /// <c>UNKNOWN_ITEM</c> for a held item — the state the hold rule exists to create.
    /// </remarks>
    [Fact]
    public void A_real_inventory_produces_all_four_answers()
    {
        var inventory = Inventories.Empty();
        var capacity = inventory.CapacityWith(Inventories.Tuning);

        foreach (var item in Inventories.Fill(capacity))
        {
            inventory.Place(item, Inventories.Tuning);
        }

        inventory.Place(Inventories.Item("waiting"), Inventories.Tuning)
            .ShouldBe(InventoryPlacement.HELD, "the fixture has to actually overflow");

        var locked = inventory.Stored[0].InstanceId;
        inventory.SetLock(locked, locked: true).ShouldBeTrue();

        Reject(inventory, inventory.Stored[1].InstanceId).ShouldBeNull(
            "a stored, unlocked item is available and there is nothing to refuse.");

        Reject(inventory, locked).ShouldBe(RejectionReason.ILLEGAL_STATE);

        Reject(inventory, new GearInstanceId("waiting")).ShouldBe(
            RejectionReason.INVENTORY_FULL,
            "the player owns it and cannot act on it until the stock has room — which is the only " +
            "situation this reason describes, now that a grant is never refused.");

        Reject(inventory, new GearInstanceId("nobody_owns_this")).ShouldBe(
            RejectionReason.NOT_OWNED,
            "…and an id nobody owns is a different answer from an item that is merely out of reach.");
    }

    private static RejectionReason? Reject(Core.Model.Gear.Inventory inventory, GearInstanceId id) =>
        InventoryAccess.RejectionFor(inventory.Availability(id));
}
