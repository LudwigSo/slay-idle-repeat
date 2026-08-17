using Shouldly;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Hero;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Hero;

/// <summary>
/// <see cref="LoadoutRules"/> — which of a player's items may be worn, and what applying a preset
/// actually produces.
/// </summary>
public sealed class LoadoutRulesTests
{
    /// <summary>An item in stock is equippable, and so is a locked one.</summary>
    /// <remarks>
    /// The lock protects an item from a destructive operation, which is the opposite of taking it
    /// out of use — a rule that read LOCKED as "unavailable" would make protecting an item unequip it.
    /// </remarks>
    [Fact]
    public void A_stored_item_is_equippable_and_so_is_a_locked_one()
    {
        var stock = Stock(Inventories.Item("GI_1"), Inventories.Item("GI_LOCKED", locked: true));

        LoadoutRules.IsEquippable(new GearInstanceId("GI_1"), stock).ShouldBeTrue();
        LoadoutRules.IsEquippable(new GearInstanceId("GI_LOCKED"), stock).ShouldBeTrue();
    }

    /// <summary>An item the player does not own is not equippable.</summary>
    [Fact]
    public void An_unowned_item_is_not_equippable()
    {
        LoadoutRules.IsEquippable(new GearInstanceId("GI_GONE"), Stock()).ShouldBeFalse();
    }

    /// <summary>
    /// 🔒 An item waiting in the overflow holding list is owned but not equippable.
    /// </summary>
    /// <remarks>
    /// The two states are told apart on purpose. Equipping out of overflow would let a full
    /// inventory be emptied through the hero screen, which is the reclaim rule's job — and the
    /// player-aggregate's own dangling-reference check deliberately treats a held item as OWNED, so
    /// the two rules disagree by design and both have to be pinned.
    /// </remarks>
    [Fact]
    public void An_item_waiting_in_overflow_is_owned_but_not_equippable()
    {
        var held = Inventories.Item("GI_HELD");
        var stock = Inventories.Rehydrated(new InventorySnapshot(0, [], [Inventories.Persist(held)]));

        stock.Availability(held.InstanceId).ShouldBe(ItemAvailability.HELD_IN_OVERFLOW);
        LoadoutRules.IsEquippable(held.InstanceId, stock).ShouldBeFalse();
    }

    /// <summary>Applying restores every slot whose item is still owned.</summary>
    [Fact]
    public void Applying_restores_the_slots_whose_items_survive()
    {
        var stock = Stock(Inventories.Item("GI_1"), Inventories.Item("GI_2"));

        var preset = Loadout.Empty
            .With(GearSlot.WEAPON, new GearInstanceId("GI_1"))
            .With(GearSlot.RING, new GearInstanceId("GI_2"));

        LoadoutRules.Applied(preset, stock).EquippedCount.ShouldBe(2);
    }

    /// <summary>A slot whose item is gone is left empty rather than refusing the whole preset.</summary>
    [Fact]
    public void A_slot_whose_item_is_gone_is_left_empty()
    {
        var stock = Stock(Inventories.Item("GI_1"));

        var preset = Loadout.Empty
            .With(GearSlot.WEAPON, new GearInstanceId("GI_1"))
            .With(GearSlot.RING, new GearInstanceId("GI_SALVAGED"));

        var applied = LoadoutRules.Applied(preset, stock);

        applied.EquippedCount.ShouldBe(1);
        applied.TryGet(GearSlot.WEAPON, out _).ShouldBeTrue();
        applied.TryGet(GearSlot.RING, out _).ShouldBeFalse();
    }

    /// <summary>A preset of which nothing survives applies as an empty loadout, not as a refusal.</summary>
    [Fact]
    public void A_preset_of_which_nothing_survives_applies_as_empty()
    {
        var preset = Loadout.Empty.With(GearSlot.WEAPON, new GearInstanceId("GI_GONE"));

        LoadoutRules.Applied(preset, Stock()).EquippedCount.ShouldBe(0);
    }

    /// <summary>An inventory holding the named items, with room for them.</summary>
    private static Core.Model.Gear.Inventory Stock(params GearInstance[] items) =>
        Inventories.Rehydrated(
            new InventorySnapshot(0, items.Select(Inventories.Persist).ToArray(), []));
}
