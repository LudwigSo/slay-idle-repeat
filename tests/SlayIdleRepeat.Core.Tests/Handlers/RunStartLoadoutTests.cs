using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// `07` §4's snapshot-at-run-start: the loadout the run was begun with, frozen for the run's whole
/// life.
/// </summary>
public sealed class RunStartLoadoutTests
{
    [Fact]
    public void A_started_run_carries_the_loadout_it_began_with()
    {
        var run = Started(Wearing("GI_1", GearSlot.WEAPON));

        run.StartingLoadout.TryGet(GearSlot.WEAPON, out var item).ShouldBeTrue();
        item.Value.ShouldBe("GI_1");
    }

    /// <summary>A hero wearing nothing starts a run with an empty snapshot, not an absent one.</summary>
    /// <remarks>
    /// The negative control: without it, the case above would also pass over a field that simply
    /// copied whatever the player happened to have at any later moment.
    /// </remarks>
    [Fact]
    public void A_run_started_by_a_bare_hero_carries_an_empty_loadout()
    {
        Started(PlayerSnapshots.Valid).StartingLoadout.EquippedCount.ShouldBe(0);
    }

    /// <summary>
    /// 🔒 Equipping after the run started does not move what the run is fighting with — the whole
    /// reason the loadout is a field on the run rather than a rule stated over the player.
    /// </summary>
    [Fact]
    public void Changing_the_players_loadout_after_the_start_does_not_move_the_runs()
    {
        var world = Outside(Wearing("GI_1", GearSlot.WEAPON));
        var started = SlayIdleRepeat.Core.GameRules.Apply(
            world, new StartRunCommand(1, DifficultyTier.NORMAL), Worlds.Context).NewState;

        started.Player.Equip(GearSlot.RING, new GearInstanceId("GI_1"));

        started.Player.Loadout.TryGet(GearSlot.RING, out _).ShouldBeTrue(
            "the fixture only discriminates while the player's own loadout really did move.");

        started.Run!.StartingLoadout.TryGet(GearSlot.WEAPON, out _).ShouldBeTrue();
        started.Run.StartingLoadout.TryGet(GearSlot.RING, out _).ShouldBeFalse();
    }

    /// <summary>The starting loadout survives the run's own round trip.</summary>
    [Fact]
    public void The_starting_loadout_round_trips_with_the_run()
    {
        var run = Started(Wearing("GI_1", GearSlot.WEAPON));

        var round = Core.Model.Run.Rehydrate(run.ToSnapshot());

        round.IsSuccess.ShouldBeTrue();
        CanonicalStateWriter.CanonicalBytes(round.Value.ToSnapshot())
            .ShouldBe(CanonicalStateWriter.CanonicalBytes(run.ToSnapshot()));
    }

    /// <summary>An absent starting loadout is a corrupt row, not a run fought naked.</summary>
    [Fact]
    public void An_absent_starting_loadout_is_a_fault()
    {
        var result = Core.Model.Run.Rehydrate(RunSnapshots.WithNull(startingLoadout: true));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(nameof(RunSnapshot.StartingLoadout));
    }

    private static Core.Model.Run Started(PlayerSnapshot player)
    {
        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Outside(player), new StartRunCommand(1, DifficultyTier.NORMAL), Worlds.Context);

        result.Accepted.ShouldBeTrue();

        return result.NewState.Run!;
    }

    /// <summary>The given row, funded for the one run every case here starts.</summary>
    /// <remarks>
    /// The Energy is the fixture's, not the case's subject: START_RUN charges a run's price, so a
    /// row carrying the empty banks <c>PlayerSnapshots.Valid</c> holds is refused before it ever
    /// reaches the loadout snapshot these cases are about.
    /// </remarks>
    private static WorldSlice Outside(PlayerSnapshot player) =>
        new(Worlds.Rehydrated(player with { Energy = PlayerSnapshots.OneRunsWorth }), null);

    private static PlayerSnapshot Wearing(string itemId, GearSlot slot) =>
        PlayerSnapshots.With(
            inventory: new InventorySnapshot(0, [Inventories.Persist(Inventories.Item(itemId))], []),
            loadout: new LoadoutSnapshot(PlayerSnapshots.Gear((slot, itemId))));
}
