using Shouldly;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// <see cref="Loadout"/>'s one-item-one-slot invariant and its persistence. Equip/unequip
/// behaviour is covered at the <c>Apply</c> seam by <c>EquipTests</c>/<c>UnequipTests</c>.
/// </summary>
public sealed class LoadoutTests
{
    private static readonly GearInstanceId Blade = new("GI_BLADE");
    private static readonly GearInstanceId Ring = new("GI_RING");

    /// <summary>The immutability presets rest on: a stored loadout cannot change under a later equip.</summary>
    [Fact]
    public void Equipping_answers_a_new_loadout_and_leaves_the_old_one_alone()
    {
        var before = Loadout.Empty.With(GearSlot.WEAPON, Blade);
        var after = before.With(GearSlot.RING, Ring);

        before.EquippedCount.ShouldBe(1);
        after.EquippedCount.ShouldBe(2);
        ReferenceEquals(before, after).ShouldBeFalse();
    }

    /// <summary>
    /// 🔒 One item is in at most one slot: moving it clears the slot it came from. Not reachable
    /// through Apply — EQUIP refuses an item outside its own slot, so only the component can move one.
    /// </summary>
    [Fact]
    public void Moving_an_item_between_slots_leaves_it_in_only_one()
    {
        var moved = Loadout.Empty
            .With(GearSlot.RING, Ring)
            .With(GearSlot.AMULET, Ring);

        moved.EquippedCount.ShouldBe(1);
        moved.TryGet(GearSlot.RING, out _).ShouldBeFalse();
        moved.TryGet(GearSlot.AMULET, out var item).ShouldBeTrue();
        item.ShouldBe(Ring);
    }

    [Fact]
    public void A_loadout_round_trips_through_its_snapshot()
    {
        var worn = Loadout.Empty.With(GearSlot.WEAPON, Blade).With(GearSlot.RING, Ring);

        var round = Loadout.Rehydrate(worn.ToSnapshot());

        round.IsSuccess.ShouldBeTrue();
        CanonicalStateWriter.CanonicalBytes(round.Value.ToSnapshot())
            .ShouldBe(
                CanonicalStateWriter.CanonicalBytes(worn.ToSnapshot()),
                "compared as canonical bytes, not by record equality: a record compares its " +
                "IReadOnlyDictionary member by REFERENCE, so equality would hold for two loadouts " +
                "that shared a map and fail for two that merely agreed.");
    }

    [Fact]
    public void An_absent_gear_map_is_a_fault()
    {
        var result = Loadout.Rehydrate(new LoadoutSnapshot(null!));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("not a naked hero");
    }

    [Fact]
    public void A_persisted_row_naming_an_undeclared_slot_is_a_fault()
    {
        var result = Loadout.Rehydrate(new LoadoutSnapshot(
            new Dictionary<GearSlot, GearInstanceId> { [0] = Blade }));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("not one of the six a hero wears");
    }

    [Fact]
    public void A_persisted_slot_with_no_identity_is_a_fault()
    {
        var result = Loadout.Rehydrate(new LoadoutSnapshot(
            new Dictionary<GearSlot, GearInstanceId> { [GearSlot.WEAPON] = default }));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("blank instance id");
    }

    /// <summary><see cref="Loadout.With"/> keeps the invariant by construction, so only a foreign row can carry a duplicate — hence the load-path check.</summary>
    [Fact]
    public void A_persisted_row_wearing_one_item_twice_is_a_fault()
    {
        var result = Loadout.Rehydrate(new LoadoutSnapshot(
            new Dictionary<GearSlot, GearInstanceId>
            {
                [GearSlot.RING] = Ring,
                [GearSlot.AMULET] = Ring,
            }));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("more than one slot");
    }

    [Fact]
    public void The_order_a_loadout_was_assembled_in_is_not_state()
    {
        var forwards = Loadout.Empty.With(GearSlot.WEAPON, Blade).With(GearSlot.RING, Ring);
        var backwards = Loadout.Empty.With(GearSlot.RING, Ring).With(GearSlot.WEAPON, Blade);

        CanonicalStateWriter.CanonicalBytes(forwards.ToSnapshot())
            .ShouldBe(CanonicalStateWriter.CanonicalBytes(backwards.ToSnapshot()));

        // Negative control: two materially different loadouts must not share bytes.
        CanonicalStateWriter.CanonicalBytes(forwards.ToSnapshot())
            .ShouldNotBe(CanonicalStateWriter.CanonicalBytes(
                Loadout.Empty.With(GearSlot.WEAPON, Blade).ToSnapshot()));
    }
}
