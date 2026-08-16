using Shouldly;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// <see cref="Loadout"/> — the slot → gear-instance map the hero wears, its one-item-one-slot
/// invariant, and its persistence.
/// </summary>
public sealed class LoadoutTests
{
    private static readonly GearInstanceId Blade = new("GI_BLADE");
    private static readonly GearInstanceId Ring = new("GI_RING");

    /// <summary>A new hero wears nothing, and an empty slot is an absent key rather than a blank id.</summary>
    [Fact]
    public void An_empty_loadout_holds_no_slots()
    {
        Loadout.Empty.Gear.ShouldBeEmpty();
        Loadout.Empty.EquippedCount.ShouldBe(0);
        Loadout.Empty.TryGet(GearSlot.WEAPON, out _).ShouldBeFalse();
    }

    /// <summary>Equipping fills the slot and leaves every other one alone.</summary>
    [Fact]
    public void Equipping_fills_one_slot()
    {
        var worn = Loadout.Empty.With(GearSlot.WEAPON, Blade);

        worn.TryGet(GearSlot.WEAPON, out var item).ShouldBeTrue();
        item.ShouldBe(Blade);
        worn.EquippedCount.ShouldBe(1);
    }

    /// <summary>The value is replaced, never mutated: the loadout a preset stored cannot change under it.</summary>
    /// <remarks>
    /// The property presets rest on. A mutable loadout would mean saving a preset and then equipping
    /// a helmet silently rewrote the preset the player had just saved.
    /// </remarks>
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
    /// 🔒 One item is in at most one slot: moving it clears the slot it came from.
    /// </summary>
    /// <remarks>
    /// A ring dragged from one hand to the other is the gesture this exists for. The second
    /// assertion is the one that matters — without the clear, the item would contribute its stats
    /// twice and be salvaged from under itself.
    /// </remarks>
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

    /// <summary>Equipping the same item into the slot it already occupies changes nothing.</summary>
    [Fact]
    public void Re_equipping_the_same_item_into_the_same_slot_is_a_no_op()
    {
        var worn = Loadout.Empty.With(GearSlot.WEAPON, Blade);

        ReferenceEquals(worn, worn.With(GearSlot.WEAPON, Blade)).ShouldBeTrue();
    }

    /// <summary>Unequipping empties one slot; unequipping an empty one changes nothing.</summary>
    [Fact]
    public void Unequipping_empties_the_slot_and_an_empty_slot_is_a_no_op()
    {
        var worn = Loadout.Empty.With(GearSlot.WEAPON, Blade);

        worn.Without(GearSlot.WEAPON).EquippedCount.ShouldBe(0);
        ReferenceEquals(worn, worn.Without(GearSlot.RING)).ShouldBeTrue();
    }

    /// <summary>An identity can be taken off without knowing where it was worn.</summary>
    /// <remarks>The seam a destructive item operation needs — salvage knows the id, not the slot.</remarks>
    [Fact]
    public void An_item_can_be_taken_off_by_identity()
    {
        var worn = Loadout.Empty.With(GearSlot.AMULET, Ring);

        worn.WithoutItem(Ring).EquippedCount.ShouldBe(0);
        ReferenceEquals(worn, worn.WithoutItem(Blade)).ShouldBeTrue();
    }

    /// <summary>An undeclared slot and a blank identity are caller defects, told apart.</summary>
    [Fact]
    public void An_undeclared_slot_and_a_blank_identity_are_refused_separately()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Loadout.Empty.With(0, Blade))
            .ParamName.ShouldBe("slot");

        Should.Throw<ArgumentOutOfRangeException>(() => Loadout.Empty.With(GearSlot.WEAPON, default))
            .ParamName.ShouldBe("item");
    }

    /// <summary>A loadout round-trips through its persisted shape.</summary>
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

    /// <summary>An absent map is a fault, never a hero who happens to wear nothing.</summary>
    [Fact]
    public void An_absent_gear_map_is_a_fault()
    {
        var result = Loadout.Rehydrate(new LoadoutSnapshot(null!));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("not a naked hero");
    }

    /// <summary>An undeclared slot in a persisted row is a fault, told apart from a blank id.</summary>
    [Fact]
    public void A_persisted_row_naming_an_undeclared_slot_is_a_fault()
    {
        var result = Loadout.Rehydrate(new LoadoutSnapshot(
            new Dictionary<GearSlot, GearInstanceId> { [0] = Blade }));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("not one of the six a hero wears");
    }

    /// <summary>A slot present with no identity is a fault, distinct from an absent slot.</summary>
    [Fact]
    public void A_persisted_slot_with_no_identity_is_a_fault()
    {
        var result = Loadout.Rehydrate(new LoadoutSnapshot(
            new Dictionary<GearSlot, GearInstanceId> { [GearSlot.WEAPON] = default }));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("blank instance id");
    }

    /// <summary>One identity in two slots is a fault the persisted row cannot smuggle past the mutator.</summary>
    /// <remarks>
    /// <see cref="Loadout.With"/> keeps the invariant by construction, so the only way a duplicate
    /// can exist is a row written by something else — which is exactly why the check lives on the
    /// load path as well.
    /// </remarks>
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

    /// <summary>Two loadouts assembled in different orders encode identically.</summary>
    /// <remarks>
    /// The canonical writer orders a map by key, so the order the player equipped in is not state.
    /// The negative control is the second assertion: two materially different loadouts must not
    /// share bytes, or the first would be observing that everything encodes the same.
    /// </remarks>
    [Fact]
    public void The_order_a_loadout_was_assembled_in_is_not_state()
    {
        var forwards = Loadout.Empty.With(GearSlot.WEAPON, Blade).With(GearSlot.RING, Ring);
        var backwards = Loadout.Empty.With(GearSlot.RING, Ring).With(GearSlot.WEAPON, Blade);

        CanonicalStateWriter.CanonicalBytes(forwards.ToSnapshot())
            .ShouldBe(CanonicalStateWriter.CanonicalBytes(backwards.ToSnapshot()));

        CanonicalStateWriter.CanonicalBytes(forwards.ToSnapshot())
            .ShouldNotBe(CanonicalStateWriter.CanonicalBytes(
                Loadout.Empty.With(GearSlot.WEAPON, Blade).ToSnapshot()));
    }
}
