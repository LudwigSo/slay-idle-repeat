using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// The inventory as a component of the <c>Player</c> aggregate: a live view, persisted with the
/// player. The component's own invariants are <c>InventoryTests</c>'/<c>InventoryPersistenceTests</c>'.
/// </summary>
public sealed class PlayerInventoryTests
{
    private static ContentSnapshot Content => TuningDocuments.Shipped;

    [Fact]
    public void A_player_carries_the_inventory_its_row_described()
    {
        var stored = Inventories.Fill(3).Select(Persist).ToArray();
        var held = new[] { Persist(Inventories.Item("waiting")) };

        var player = Rehydrated(PlayerSnapshots.With(inventory: new InventorySnapshot(2, stored, held)));

        player.Inventory.ExpansionsPurchased.ShouldBe(2);
        player.Inventory.Stored.Count.ShouldBe(3);
        player.Inventory.Held.Count.ShouldBe(1);
    }

    /// <summary>A copy would make every handler's mutation invisible to the very next read and persist.</summary>
    [Fact]
    public void The_inventory_property_is_a_live_view_of_the_component()
    {
        var player = Rehydrated(PlayerSnapshots.Valid);

        player.Inventory.Place(Inventories.Item("gi_0001"), Inventories.Tuning);

        player.Inventory.Stored.Count.ShouldBe(1);
        player.ToSnapshot().Inventory!.Stored.Count.ShouldBe(
            1,
            "the aggregate persists the component it handed out, not a snapshot taken before the " +
            "handler ran.");
    }

    /// <summary>
    /// 🔒 An absent inventory is a fault, not an empty one: read as empty it would delete a
    /// player's entire stock and look like a normal empty inventory.
    /// </summary>
    [Fact]
    public void A_null_inventory_is_refused_by_name()
    {
        var result = Core.Model.Player.Rehydrate(PlayerSnapshots.WithNull(inventory: true), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(
            nameof(PlayerSnapshot.Inventory),
            Case.Sensitive,
            customMessage: "several fields can fail this validation; the message must say WHICH one did.");

        // The asymmetry, stated where it can be checked: the sibling optional field IS read as
        // "nothing yet" when absent.
        Core.Model.Player.Rehydrate(PlayerSnapshots.WithNull(cleared: true), Content)
            .IsSuccess.ShouldBeTrue();
    }

    /// <summary>The aggregate delegates the component's invariants rather than taking the row on trust.</summary>
    [Fact]
    public void An_inventory_row_the_component_refuses_fails_the_players_rehydration()
    {
        var duplicated = Persist(Inventories.Item("gi_0001"));

        Core.Model.Player.Rehydrate(
                PlayerSnapshots.With(inventory: new InventorySnapshot(0, [duplicated, duplicated], [])),
                Content)
            .IsFailure.ShouldBeTrue();
    }

    private static GearInstanceSnapshot Persist(GearInstance item) => Inventories.Persist(item);

    private static Core.Model.Player Rehydrated(PlayerSnapshot snapshot)
    {
        var player = Core.Model.Player.Rehydrate(snapshot, Content);

        return player.IsSuccess
            ? player.Value
            : throw new InvalidOperationException(
                "The fixture PlayerSnapshot does not rehydrate: " + player.Error);
    }
}
