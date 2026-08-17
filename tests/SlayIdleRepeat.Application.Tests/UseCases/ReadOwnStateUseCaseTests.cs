using Shouldly;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.Tests.Events;
using SlayIdleRepeat.Application.Tests.Persistence;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.UseCases;

/// <summary>
/// The read side: persisted rows out, nothing in, and never another player's run.
/// </summary>
public sealed class ReadOwnStateUseCaseTests
{
    [Fact]
    public void OwnStateView_declares_a_staleness_budget_of_zero()
    {
        OwnStateView.StalenessBudget.ShouldBe(
            TimeSpan.Zero,
            "own state is read-your-own-writes: the client has already animated the command it just " +
            "sent, so a budget above zero would let the very next read contradict the screen.");
    }

    /// <summary>
    /// The declared budget above is a number; this is the reason it can be believed. A read that
    /// cannot be handed a clock, an age or an as-of instant has no way to answer with anything but
    /// what the store holds now — so zero is a property of the shape rather than a promise.
    /// </summary>
    [Fact]
    public void ReadOwnStateUseCase_is_handed_nothing_it_could_serve_an_older_view_from()
    {
        var constructors = typeof(ReadOwnStateUseCase).GetConstructors();

        constructors.ShouldHaveSingleItem(
            "the read side is built exactly one way, which is what makes 'everything it can consult' " +
            "an answerable question at all.");

        constructors[0].GetParameters().Select(parameter => parameter.ParameterType).ShouldBe(
            [typeof(WorldSliceStore)],
            "the read side holds the store and nothing else. A clock, a cache handle or a freshness " +
            "budget in this list is the knob that turns a zero budget into a default.");

        var read = typeof(ReadOwnStateUseCase).GetMethod(nameof(ReadOwnStateUseCase.ReadAsync));

        read.ShouldNotBeNull("the method this rule is about is not there, so the rule has no subject.");
        read!.GetParameters().Select(parameter => parameter.ParameterType).ShouldBe(
            [typeof(ReadOwnStateRequest), typeof(CancellationToken)],
            "an extra parameter here is how 'give me the view as of a moment ago' becomes expressible.");

        // The constructor's parameters rather than the properties: reflection guarantees an order for
        // the one and not the other, and this list is asserted in order.
        typeof(ReadOwnStateRequest).GetConstructors().ShouldHaveSingleItem(
            "the request is built one way, so what it can carry is answerable.");

        typeof(ReadOwnStateRequest).GetConstructors()[0]
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ShouldBe(
                [typeof(PlayerId), typeof(RunId?)],
                "the request names who and which run, and carries no instant or age for a reader to honour.");
    }

    [Fact]
    public async Task ReadAsync_returns_the_state_the_last_accepted_command_committed()
    {
        var (game, player) = Worlds.InARun();
        var cache = Worlds.CacheHolding(game.State(player));
        var store = new WorldSliceStore(cache);
        var run = game.State(player).Run!.Id;

        var written = await new ApplyCommandUseCase(store, new DomainEventDispatcher([new RecordingSink()]))
            .ExecuteAsync(
                new ApplyCommandRequest(player, run, new RollDiceCommand()), Worlds.Context(game), Worlds.Cancel);

        written.Accepted.ShouldBeTrue("the roll was refused " + written.Rejection + ".");

        var read = await new ReadOwnStateUseCase(store)
            .ReadAsync(new ReadOwnStateRequest(player, null), Worlds.Cancel);

        read.Lookup.ShouldBe(OwnStateLookup.Found, "the player who just acted was not found.");
        Worlds.Hash(new StoredSlice(read.View!.Player, read.View.Run)).ShouldBe(
            Worlds.Hash(written.State),
            "the read answered with something other than the state the command committed.");
    }

    [Fact]
    public async Task ReadAsync_writes_nothing()
    {
        var (game, player) = Worlds.InARun();
        var cache = new RecordingCache(Worlds.CacheHolding(game.State(player)));

        await new ReadOwnStateUseCase(new WorldSliceStore(cache))
            .ReadAsync(new ReadOwnStateRequest(player, null), Worlds.Cancel);

        cache.Writes.ShouldBeEmpty(
            "a query wrote to the store. Reading is not an event: a write here would slide whatever the " +
            "store measures from the last one, so opening a screen would keep a run alive.");
    }

    [Fact]
    public async Task ReadAsync_answers_NoSuchPlayer_when_nothing_is_stored_for_the_player()
    {
        var read = await new ReadOwnStateUseCase(new WorldSliceStore(new InMemoryLocalCache()))
            .ReadAsync(new ReadOwnStateRequest(new PlayerId("PLAYER_00000404"), null), Worlds.Cancel);

        read.Lookup.ShouldBe(
            OwnStateLookup.NoSuchPlayer,
            "an unknown player is an answer on the read side, not the loud failure the write side gives " +
            "— nothing is about to be overwritten by it.");
        read.View.ShouldBeNull("there is no state to hand back.");
    }

    [Fact]
    public async Task ReadAsync_returns_the_current_run_when_the_request_names_it()
    {
        var (game, player) = Worlds.InAPlayedRun();
        var slice = game.State(player);
        var store = new WorldSliceStore(Worlds.CacheHolding(slice));

        var read = await new ReadOwnStateUseCase(store)
            .ReadAsync(new ReadOwnStateRequest(player, slice.Run!.Id), Worlds.Cancel);

        read.Lookup.ShouldBe(OwnStateLookup.Found, "the run the player is standing in was not found.");
        Worlds.RunHash(slice.Player.ToSnapshot(), read.View!.Run!).ShouldBe(
            Worlds.RunHash(slice.Player.ToSnapshot(), slice.Run.ToSnapshot()),
            "the run came back differing from the one committed; the same player row is hashed on both " +
            "sides, so the run is the only thing that can have moved.");
    }

    [Fact]
    public async Task ReadAsync_returns_a_run_the_player_has_already_finished()
    {
        var (game, player) = Worlds.InARun();
        var cache = Worlds.CacheHolding(game.State(player));
        var store = new WorldSliceStore(cache);
        var useCase = new ApplyCommandUseCase(store, new DomainEventDispatcher([]));
        var first = game.State(player).Run!.Id;

        var ended = await useCase.ExecuteAsync(
            new ApplyCommandRequest(player, first, new AbandonRunCommand()), Worlds.Context(game), Worlds.Cancel);

        ended.Accepted.ShouldBeTrue("abandoning the run was refused " + ended.Rejection + ".");

        var started = await useCase.ExecuteAsync(
            new ApplyCommandRequest(player, null, new StartRunCommand(Worlds.Chapter, DifficultyTier.NORMAL)),
            Worlds.Context(game),
            Worlds.Cancel);

        started.Accepted.ShouldBeTrue("starting the next run was refused " + started.Rejection + ".");
        started.State.Run!.Id.ShouldNotBe(
            first, "the second run reuses the first one's identity, so nothing here is archived over.");

        var read = await new ReadOwnStateUseCase(store)
            .ReadAsync(new ReadOwnStateRequest(player, first), Worlds.Cancel);

        read.Lookup.ShouldBe(
            OwnStateLookup.Found,
            "the results screen for a finished run is opened after the next run has already started, so " +
            "the archive is what gives that read something to answer with.");
        read.View!.Run!.Id.ShouldBe(first, "some other run was handed back for the id asked about.");
        read.View.Run.Phase.ShouldBe(RunPhase.Ended, "the run asked about is over.");
    }

    [Fact]
    public async Task ReadAsync_answers_NoSuchRun_when_nothing_is_stored_under_that_run()
    {
        var (game, player) = Worlds.InARun();
        var store = new WorldSliceStore(Worlds.CacheHolding(game.State(player)));

        var read = await new ReadOwnStateUseCase(store)
            .ReadAsync(new ReadOwnStateRequest(player, new RunId("RUN_NEVER_HAPPENED")), Worlds.Cancel);

        read.Lookup.ShouldBe(OwnStateLookup.NoSuchRun, "no run was ever stored under that identity.");
        read.View.ShouldBeNull("there is no run to hand back.");
    }

    /// <summary>
    /// 🔒 The archive is keyed by run id alone, so ownership is checked here or not at all.
    /// </summary>
    [Fact]
    public async Task ReadAsync_answers_NoSuchRun_for_a_run_archived_under_a_different_player()
    {
        var (game, asking, other) = Worlds.TwoPlayersOneEndedRun();

        var strangersRun = game.State(other).Run!.ToSnapshot();
        var cache = Worlds.CacheHolding(game.State(asking));

        cache.Seed(SliceKeys.ForRun(strangersRun.Id), SnapshotCodec.EncodeRun(strangersRun));

        var read = await new ReadOwnStateUseCase(new WorldSliceStore(cache))
            .ReadAsync(new ReadOwnStateRequest(asking, strangersRun.Id), Worlds.Cancel);

        read.Lookup.ShouldBe(
            OwnStateLookup.NoSuchRun,
            "one player read another player's finished run. The archive key carries no owner, so a read " +
            "that does not check the run's own player id hands out anybody's run to anybody who guesses " +
            "an id — and answering anything other than NoSuchRun would confirm the id exists.");
        read.View.ShouldBeNull("nothing of the other player's may be handed back, not even their player row.");
    }
}
