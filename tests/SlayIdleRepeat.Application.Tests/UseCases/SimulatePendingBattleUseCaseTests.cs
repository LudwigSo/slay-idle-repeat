using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.UseCases;

/// <summary>
/// The read that turns a run standing in an open battle into the fight it is standing in.
/// </summary>
/// <remarks>
/// The one thing between a run entering <c>BattlePending</c> and leaving it: the confirming command
/// carries a <c>LogHash</c>, and until this use case existed nothing above the rules assembly could
/// produce one from a real fight.
/// </remarks>
public sealed class SimulatePendingBattleUseCaseTests
{
    /// <summary>An open battle simulates, and reports the fight, its seed and its hash.</summary>
    [Fact]
    public async Task An_open_battle_is_simulated_and_reported()
    {
        var (store, player, _) = await InAnOpenBattleAsync();

        var result = await Use(store).ExecuteAsync(new SimulatePendingBattleRequest(player), Worlds.Cancel);

        result.Lookup.ShouldBe(PendingBattleLookup.Found);
        result.View.ShouldNotBeNull();
        result.View!.Fight.Log.ShouldNotBeEmpty("a fight with no events is a fight that never composed");
    }

    /// <summary>
    /// 🔒 The reported hash is the fight's own, spelled the way the confirming command parses it.
    /// </summary>
    /// <remarks>
    /// The command shape-checks its hash with <c>NumberStyles.None</c> against the invariant culture,
    /// so a caller that formatted the same number with a group separator or in hex would be refused
    /// while holding a perfectly correct fight. The use case does the formatting once, here, rather
    /// than leaving every caller to rediscover the spelling.
    /// </remarks>
    [Fact]
    public async Task The_reported_hash_is_the_fights_own_in_the_spelling_the_command_parses()
    {
        var (store, player, _) = await InAnOpenBattleAsync();

        var view = (await Use(store).ExecuteAsync(new SimulatePendingBattleRequest(player), Worlds.Cancel)).View;

        view.ShouldNotBeNull();
        view!.LogHash.ShouldBe(view.Fight.LogHash.ToString(CultureInfo.InvariantCulture));

        ulong.TryParse(view.LogHash, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            .ShouldBeTrue("CONFIRM_BATTLE_RESULT parses the hash with exactly these two arguments");

        parsed.ShouldBe(view.Fight.LogHash);
    }

    /// <summary>The reported seed is the seed the run's own row derives, not merely a plausible one.</summary>
    /// <remarks>
    /// 🔴 Stated as an equality against the derivation. "Not zero and not the run seed" is true of
    /// almost every number a broken derivation could produce — an off-by-one on the battle index is a
    /// real seed for the wrong fight, passes both halves, and is exactly the substitution a reader
    /// could not spot. The two negatives are kept as controls beneath it.
    /// </remarks>
    [Fact]
    public async Task The_reported_seed_is_the_battles_and_not_the_runs()
    {
        var (store, player, run) = await InAnOpenBattleAsync();

        var view = (await Use(store).ExecuteAsync(new SimulatePendingBattleRequest(player), Worlds.Cancel)).View;

        view.ShouldNotBeNull();
        view!.BattleSeed.ShouldBe(
            SlayIdleRepeat.Core.Rules.Combat.RunBattle.SeedOf(run),
            "the reported seed is the one the run's committed row derives, and a server recomputing " +
            "the fight will derive that one");

        view.BattleSeed.ShouldNotBe(0UL);
        view.BattleSeed.ShouldNotBe(run.RunSeed, "the battle seed is derived FROM the run seed, not equal to it");
    }

    /// <summary>A run that is not in a battle has no fight to report, and says which.</summary>
    [Fact]
    public async Task A_run_that_is_not_in_a_battle_reports_no_open_battle()
    {
        var (game, player) = Worlds.InAPlayedRun();
        var store = Store(Worlds.CacheHolding(game.State(player)));

        var result = await Use(store).ExecuteAsync(new SimulatePendingBattleRequest(player), Worlds.Cancel);

        result.Lookup.ShouldBe(PendingBattleLookup.NoOpenBattle);
        result.View.ShouldBeNull();
    }

    /// <summary>A player with no run at all reports the same absence, not a crash.</summary>
    [Fact]
    public async Task A_player_outside_a_run_reports_no_open_battle()
    {
        var (game, player) = Worlds.AfterAnEndedRun();
        var store = Store(Worlds.CacheHolding(game.State(player)));

        (await Use(store).ExecuteAsync(new SimulatePendingBattleRequest(player), Worlds.Cancel))
            .Lookup.ShouldBe(PendingBattleLookup.NoOpenBattle);
    }

    /// <summary>A player nothing is stored for is a different answer from a player with no battle.</summary>
    /// <remarks>
    /// Named apart because the escapes differ: one is a bad address and the other is a screen that
    /// should not have asked yet. Collapsing them would have a client retry forever on a typo.
    /// </remarks>
    [Fact]
    public async Task An_unknown_player_is_reported_apart_from_a_player_with_no_battle()
    {
        var game = Worlds.Game();
        var store = Store(Worlds.CacheHolding(game.State(game.CreatePlayer())));

        (await Use(store).ExecuteAsync(new SimulatePendingBattleRequest(new PlayerId("PLAYER_NOBODY")), Worlds.Cancel))
            .Lookup.ShouldBe(PendingBattleLookup.NoSuchPlayer);
    }

    /// <summary>The read commits nothing: the stored bytes are byte-identical afterwards.</summary>
    /// <remarks>
    /// A simulation is an expensive read and nothing else. If it wrote — a slid expiry, a stamped
    /// activity instant — then opening the battle screen would change the run, and a player who
    /// looked at a fight twice would have a different run from one who looked once.
    /// </remarks>
    [Fact]
    public async Task Simulating_writes_nothing_back()
    {
        var (store, player, _) = await InAnOpenBattleAsync();

        var before = await store.ReadSnapshotsAsync(player, Worlds.Cancel);
        var beforeHash = Worlds.Hash(before!);

        await Use(store).ExecuteAsync(new SimulatePendingBattleRequest(player), Worlds.Cancel);

        var after = await store.ReadSnapshotsAsync(player, Worlds.Cancel);

        Worlds.Hash(after!).ShouldBe(beforeHash);
    }

    /// <summary>The request may not be null.</summary>
    [Fact]
    public async Task The_use_case_refuses_a_null_request()
    {
        var (store, _, _) = await InAnOpenBattleAsync();

        await Should.ThrowAsync<ArgumentNullException>(
            () => Use(store).ExecuteAsync(null!, Worlds.Cancel));
    }

    private static SimulatePendingBattleUseCase Use(WorldSliceStore store) => new(store, Worlds.Content);

    private static WorldSliceStore Store(Adapters.InMemory.InMemoryLocalCache cache) => new(cache.Reopen());

    /// <summary>
    /// A stored slice whose run is standing in an open battle against an ordinary enemy.
    /// </summary>
    /// <remarks>
    /// Built by playing a real run and then opening a battle through <c>GameRules.Apply</c>, so the
    /// combat counter and the phase are whatever the domain actually writes rather than whatever this
    /// fixture believes it writes. The pending tile is placed on the row first because the board a
    /// fresh run rolls onto is not guaranteed to be a fight tile, and a fixture that re-rolled until
    /// it was would make every case here depend on the board generator's draw order.
    /// </remarks>
    private static Task<(WorldSliceStore Store, PlayerId Player, RunSnapshot Run)> InAnOpenBattleAsync()
    {
        var (game, player) = Worlds.InAPlayedRun();
        var played = game.State(player);

        var standingOnAnEnemy = played.Run!.ToSnapshot() with
        {
            PendingTileKind = (int)TileKind.Enemy,
            PendingTileLinearIndex = 7,
            PendingTileStage = 1,
        };

        var run = Core.Model.Run.Rehydrate(standingOnAnEnemy);

        if (run.IsFailure)
        {
            throw new InvalidOperationException("The fixture run row does not rehydrate: " + run.Error);
        }

        var opened = GameRules.Apply(
            new WorldSlice(played.Player, run.Value), new Core.Commands.StartBattleCommand(), Worlds.Context(game));

        opened.Accepted.ShouldBeTrue("START_BATTLE was refused " + opened.Rejection + " by the fixture.");

        var store = Store(Worlds.CacheHolding(opened.NewState));

        return Task.FromResult((store, player, opened.NewState.Run!.ToSnapshot()));
    }
}
