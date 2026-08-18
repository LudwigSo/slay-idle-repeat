using Shouldly;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Hosting;

/// <summary>
/// The third verb of the loop, end to end through the host: a run enters a battle, the battle is
/// fought for real, and the run leaves the battle on the fight's own hash.
/// </summary>
/// <remarks>
/// 🔒 <b>The milestone's exit criterion in one file.</b> Until this branch a run could enter
/// <c>BattlePending</c> and nothing anywhere could produce the fight it was standing in, so
/// <em>"roll → move → fight → draft → results"</em> stopped at the third verb — not because the
/// confirming handler was missing, but because nothing could hand it a hash that came from a fight.
/// Every case here goes through <see cref="Application.Hosting.InProcessGameHost"/> rather than
/// through <c>GameRules.Apply</c>, because "the run leaves the battle" is a claim about the seam a
/// player actually plays through.
/// </remarks>
public sealed class PendingBattlePathTests
{
    /// <summary>A run enters the battle phase, fights, and comes back out of it.</summary>
    [Fact]
    public async Task A_run_enters_a_battle_fights_it_and_leaves_it()
    {
        var world = await OnAFightTileAsync();

        var opened = await world.Host.SubmitAsync(
            world.Player, world.Run, new StartBattleCommand(), Worlds.Cancel);

        opened.Accepted.ShouldBeTrue("START_BATTLE was refused " + opened.Rejection + ".");
        (await PhaseAsync(world)).ShouldBe(RunPhase.BattlePending, "the run has to be IN the battle first");

        var fight = await world.Battles.ExecuteAsync(
            new SimulatePendingBattleRequest(world.Player), Worlds.Cancel);

        fight.Lookup.ShouldBe(PendingBattleLookup.Found);

        var confirmed = await world.Host.SubmitAsync(
            world.Player,
            world.Run,
            new ConfirmBattleResultCommand(fight.View!.LogHash, fight.View.Fight.HeroWon),
            Worlds.Cancel);

        confirmed.Accepted.ShouldBeTrue("CONFIRM_BATTLE_RESULT was refused " + confirmed.Rejection + ".");
        (await PhaseAsync(world)).ShouldBe(
            RunPhase.InProgress,
            "a run that enters a battle and never leaves it is the hole this whole task exists to close");
    }

    /// <summary>The hash the run leaves on is the one the fight produced, not a shape that parses.</summary>
    /// <remarks>
    /// 🔒 The distinction the confirming handler cannot yet make and M7-06c will: today any
    /// well-formed number is accepted, so "the command was accepted" proves the run left the battle
    /// and nothing about which fight it left on. This case asserts the value itself — that what the
    /// path hands the command is the hash of the fight the run was actually standing in — so the
    /// claim survives the day the handler starts checking.
    /// </remarks>
    [Fact]
    public async Task The_hash_the_run_leaves_on_is_the_hash_of_the_fight_it_was_standing_in()
    {
        var world = await OnAFightTileAsync();

        await world.Host.SubmitAsync(world.Player, world.Run, new StartBattleCommand(), Worlds.Cancel);

        var rows = await world.Store.ReadSnapshotsAsync(world.Player, Worlds.Cancel);
        var independent = RunBattle.Simulate(rows!.Player, rows.Run!, Worlds.Content);

        var reported = await world.Battles.ExecuteAsync(
            new SimulatePendingBattleRequest(world.Player), Worlds.Cancel);

        reported.View!.LogHash.ShouldBe(
            independent.LogHash.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "the path's hash has to be the hash of the fight the run's own row composes");
    }

    /// <summary>Winning the fight leaves a draft pending; the loop's fourth verb has something to do.</summary>
    /// <remarks>
    /// The whole point of leaving the battle rather than merely leaving the phase. A confirmation
    /// that cleared the phase and set nothing else would satisfy the first case in this file and
    /// still strand the run.
    /// </remarks>
    [Fact]
    public async Task Winning_the_fight_leaves_the_run_with_a_draft_to_take()
    {
        var world = await OnAFightTileAsync();

        await world.Host.SubmitAsync(world.Player, world.Run, new StartBattleCommand(), Worlds.Cancel);

        var fight = await world.Battles.ExecuteAsync(
            new SimulatePendingBattleRequest(world.Player), Worlds.Cancel);

        // The fixture's hero is a fresh, ungeared level-1 build against a chapter-1 node, so the win
        // is asserted rather than assumed: a lost fight takes the other branch and this case would be
        // measuring the death path under the victory path's name.
        fight.View!.Fight.HeroWon.ShouldBeTrue(
            "the fixture is built to be winnable; a loss here means the fixture moved, not the rule");

        await world.Host.SubmitAsync(
            world.Player,
            world.Run,
            new ConfirmBattleResultCommand(fight.View.LogHash, Won: true),
            Worlds.Cancel);

        var after = await world.Store.ReadSnapshotsAsync(world.Player, Worlds.Cancel);

        after!.Run!.DraftPending.ShouldBeTrue();
        after.Run.PendingTileKind.ShouldBe(-1, "a resolved fight clears the tile it was fought over");
    }

    /// <summary>The same run, opened and confirmed twice, fights two different battles.</summary>
    /// <remarks>
    /// The battle index advances with the combat stream, so the second fight of a run must not be a
    /// replay of the first. A seam that derived the seed from the run seed alone would hand back the
    /// same fight for every battle of a run, and every case above would still pass.
    /// </remarks>
    [Fact]
    public async Task The_second_battle_of_a_run_is_a_different_fight_from_the_first()
    {
        var world = await OnAFightTileAsync();

        await world.Host.SubmitAsync(world.Player, world.Run, new StartBattleCommand(), Worlds.Cancel);

        var first = await world.Battles.ExecuteAsync(
            new SimulatePendingBattleRequest(world.Player), Worlds.Cancel);

        await world.Host.SubmitAsync(
            world.Player,
            world.Run,
            new ConfirmBattleResultCommand(first.View!.LogHash, first.View.Fight.HeroWon),
            Worlds.Cancel);

        // Back onto a fight tile for the second battle: confirming a win clears the tile.
        await ReturnToAFightTileAsync(world);

        await world.Host.SubmitAsync(world.Player, world.Run, new StartBattleCommand(), Worlds.Cancel);

        var second = await world.Battles.ExecuteAsync(
            new SimulatePendingBattleRequest(world.Player), Worlds.Cancel);

        second.View!.BattleSeed.ShouldNotBe(first.View.BattleSeed);
    }

    private static async Task<RunPhase> PhaseAsync(BattleWorld world) =>
        (await world.Store.ReadSnapshotsAsync(world.Player, Worlds.Cancel))!.Run!.Phase;

    /// <summary>Everything one of these cases drives: the host, the store beside it, and the ids.</summary>
    private sealed record BattleWorld(
        Application.Hosting.InProcessGameHost Host,
        WorldSliceStore Store,
        Adapters.InMemory.InMemoryLocalCache Cache,
        PlayerId Player,
        RunId Run,
        SimulatePendingBattleUseCase Battles);

    /// <summary>A played run parked on an unresolved Enemy tile, committed to a host's cache.</summary>
    /// <remarks>
    /// The tile is written onto the row rather than rolled onto: the board a run generates is a
    /// function of its seed, so waiting for a fight tile would make this fixture depend on the board
    /// generator's draw order and would silently stop producing a battle the day a tile weight moved.
    /// Everything after the tile — opening the battle, the combat counter, the phase — is the real
    /// domain, driven through the host.
    /// </remarks>
    private static Task<BattleWorld> OnAFightTileAsync()
    {
        var (game, player) = Worlds.InAPlayedRun();
        var played = game.State(player);

        var run = Core.Model.Run.Rehydrate(played.Run!.ToSnapshot() with
        {
            PendingTileKind = (int)TileKind.Enemy,
            PendingTileLinearIndex = 3,
            PendingTileStage = 1,
        });

        run.IsSuccess.ShouldBeTrue("the fixture run row must rehydrate: " + run.Error);

        var cache = Worlds.CacheHolding(new WorldSlice(played.Player, run.Value));
        var store = new WorldSliceStore(cache.Reopen());

        return Task.FromResult(new BattleWorld(
            Hosts.Over(cache.Reopen()),
            store,
            cache,
            player,
            run.Value.Id,
            new SimulatePendingBattleUseCase(new WorldSliceStore(cache.Reopen()), Worlds.Content)));
    }

    /// <summary>Puts the run back on an unresolved Enemy tile after a fight cleared the last one.</summary>
    private static async Task ReturnToAFightTileAsync(BattleWorld world)
    {
        var rows = await world.Store.ReadSnapshotsAsync(world.Player, Worlds.Cancel);

        var run = Core.Model.Run.Rehydrate(rows!.Run! with
        {
            PendingTileKind = (int)TileKind.Enemy,
            PendingTileLinearIndex = 4,
            PendingTileStage = 1,
            DraftPending = false,
            DraftBattleKind = -1,
            DraftBattleStage = 0,
        });

        run.IsSuccess.ShouldBeTrue("the second-battle row must rehydrate: " + run.Error);

        var player = Core.Model.Player.Rehydrate(rows.Player, Worlds.Content);

        player.IsSuccess.ShouldBeTrue("the second-battle player row must rehydrate: " + player.Error);

        await world.Store.SaveAsync(new WorldSlice(player.Value, run.Value), Worlds.Cancel);
    }
}
