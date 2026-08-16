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
/// player, and absent from every one of the aggregate's signatures.
/// </summary>
/// <remarks>
/// 🔒 <b>No <c>GearInstance</c> appears anywhere on <c>Player</c>.</b> The aggregate exposes the
/// component and the component owns the item operations; a convenience member on <c>Player</c> that
/// took or returned an item would put a grant outcome in the aggregate's signature, which is the
/// shape the luck-routing rule was narrowed to see. Handlers reach <c>player.Inventory</c>.
/// </remarks>
public sealed class PlayerInventoryTests
{
    private static ContentSnapshot Content => TuningDocuments.Shipped;

    /// <summary>A rehydrated player carries the inventory its row described.</summary>
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

    /// <summary>The property is a live view of the component, not a copy taken at rehydration.</summary>
    /// <remarks>
    /// A copy would make every handler's mutation invisible to the very next read, and the aggregate
    /// would persist the state it started with.
    /// </remarks>
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

    /// <summary><c>ToSnapshot</c> carries the inventory, stocked and held alike.</summary>
    [Fact]
    public void ToSnapshot_carries_the_inventory()
    {
        var player = Rehydrated(PlayerSnapshots.Valid);

        player.Inventory.Place(Inventories.Item("gi_0001", rarity: Rarity.B), Inventories.Tuning);
        player.Inventory.SetLock(new GearInstanceId("gi_0001"), locked: true);

        var carried = player.ToSnapshot().Inventory;

        carried.ShouldNotBeNull();
        carried.Stored.Count.ShouldBe(1);
        carried.Stored[0].InstanceId.ShouldBe(new GearInstanceId("gi_0001"));
        carried.Stored[0].Locked.ShouldBeTrue();
    }

    /// <summary>
    /// 🔒 An absent inventory is a fault, not an empty one — <c>FeatCounters</c>' precedent, and the
    /// opposite of <c>ClearedChapterTiers</c>.
    /// </summary>
    /// <remarks>
    /// Reading it as empty would delete a player's entire stock on the first load of a row that
    /// simply failed to write it, and the deletion would look like a normal empty inventory.
    /// </remarks>
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
        // "nothing yet" when absent, so "a null is a fault" is a claim about THIS field.
        Core.Model.Player.Rehydrate(PlayerSnapshots.WithNull(cleared: true), Content)
            .IsSuccess.ShouldBeTrue();
    }

    /// <summary>An inventory row the component itself refuses fails the player's rehydration too.</summary>
    /// <remarks>
    /// The aggregate does not re-validate the component's invariants — it delegates — and this is
    /// what says the delegation actually happened rather than the row being taken on trust.
    /// </remarks>
    [Fact]
    public void An_inventory_row_the_component_refuses_fails_the_players_rehydration()
    {
        var duplicated = Persist(Inventories.Item("gi_0001"));

        Core.Model.Player.Rehydrate(
                PlayerSnapshots.With(inventory: new InventorySnapshot(0, [duplicated, duplicated], [])),
                Content)
            .IsFailure.ShouldBeTrue();
    }

    /// <summary>🔒 No member of <c>Player</c> names a <c>GearInstance</c> in its signature.</summary>
    /// <remarks>
    /// A reflection rule with a floor under it: the aggregate has plenty of members, and an empty
    /// subject set would make "none of them names an item" hold over nothing.
    /// </remarks>
    [Fact]
    public void No_member_of_the_player_aggregate_names_a_gear_instance()
    {
        var members = typeof(Core.Model.Player).GetMembers(
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.DeclaredOnly);

        members.Length.ShouldBeGreaterThan(
            20,
            "the aggregate declares dozens of members; a filter reaching none of them would make the " +
            "sweep below silent rather than red.");

        var offenders = members
            .OfType<System.Reflection.MethodBase>()
            .Where(method => method.GetParameters().Any(p => p.ParameterType == typeof(GearInstance)))
            .Select(method => $"Player.{method.Name} takes a GearInstance")
            .Concat(members
                .OfType<System.Reflection.PropertyInfo>()
                .Where(property => property.PropertyType == typeof(GearInstance))
                .Select(property => $"Player.{property.Name} is a GearInstance"))
            .ToArray();

        offenders.ShouldBeEmpty(
            "the item operations live on the Inventory component, which handlers reach through " +
            "player.Inventory. A member here that took or returned an item would put a grant outcome " +
            "in the aggregate's signature and pull Player onto the luck-routing exemption list.");
    }

    /// <summary>The persisted form of one item, for the rows these cases build by hand.</summary>
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
