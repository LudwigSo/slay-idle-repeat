using Shouldly;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.Tests.Events;
using SlayIdleRepeat.Application.Tests.Persistence;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.UseCases;

/// <summary>
/// The write seam: load, guard the addressing, apply, commit, dispatch — and the three places a
/// refusal has to leave the method before it reaches the last two.
/// </summary>
public sealed class ApplyCommandUseCaseTests
{
    // ═════════════════════════════════════════════════════ an accepted command

    [Fact]
    public async Task ExecuteAsync_commits_exactly_the_state_the_domain_returned()
    {
        var (game, player) = Worlds.InARun();
        var cache = Worlds.CacheHolding(game.State(player));
        var run = game.State(player).Run!.Id;
        var useCase = UseCase(cache, out _);

        // What the domain reaches, worked out on its own harness rather than taken from the outcome:
        // otherwise this case checks the store against the use case's own report of the domain's
        // answer, and one wrong slice committed and reported alike would satisfy it.
        var domain = Worlds.HashAfterApplying(new RollDiceCommand());

        var outcome = await useCase.ExecuteAsync(
            new ApplyCommandRequest(player, run, new RollDiceCommand()), Worlds.Context(game), Worlds.Cancel);

        outcome.Accepted.ShouldBeTrue("the roll was refused " + outcome.Rejection + ".");

        var committed = SnapshotCodec.DecodeSlice(
            (await cache.ReadAsync(SliceKeys.ForPlayer(player), Worlds.Cancel))!);

        Worlds.Hash(committed).ShouldBe(
            domain,
            "the bytes in the store are not the state GameRules.Apply produces for this command. " +
            "Compared canonically because these rows carry dictionaries, which record equality " +
            "compares by reference.");

        Worlds.Hash(outcome.State).ShouldBe(
            domain,
            "the outcome reports a state the domain never produced, so a caller animating from it " +
            "would show something the store does not hold.");
    }

    [Fact]
    public async Task ExecuteAsync_replaces_the_stored_state_rather_than_re_committing_the_loaded_one()
    {
        var (game, player) = Worlds.InARun();
        var loaded = Worlds.Hash(game.State(player));
        var cache = Worlds.CacheHolding(game.State(player));
        var run = game.State(player).Run!.Id;
        var useCase = UseCase(cache, out _);

        await useCase.ExecuteAsync(
            new ApplyCommandRequest(player, run, new RollDiceCommand()), Worlds.Context(game), Worlds.Cancel);

        var committed = SnapshotCodec.DecodeSlice(
            (await cache.ReadAsync(SliceKeys.ForPlayer(player), Worlds.Cancel))!);

        Worlds.Hash(committed).ShouldNotBe(
            loaded,
            "the store still holds the state that was loaded, so a use case that applied the command " +
            "and then committed its own input would pass every other case in this file.");
    }

    [Fact]
    public async Task ExecuteAsync_delivers_the_commands_events_once_to_every_sink_in_order()
    {
        var (game, player) = Worlds.InARun();
        var cache = Worlds.CacheHolding(game.State(player));
        var run = game.State(player).Run!.Id;

        var order = new List<string>();
        var first = new RecordingSink("first", order);
        var second = new RecordingSink("second", order);
        var useCase = new ApplyCommandUseCase(
            new WorldSliceStore(cache), new DomainEventDispatcher([first, second]));
        var command = new RollDiceCommand();

        var outcome = await useCase.ExecuteAsync(
            new ApplyCommandRequest(player, run, command), Worlds.Context(game), Worlds.Cancel);

        outcome.Events.ShouldContain(
            @event => @event is DiceRolled,
            "a roll announces the face it drew, and that event is what this command has to deliver. " +
            "An outcome carrying only the catch-up's own rows would satisfy a bare non-empty check.");

        first.Batches.Count.ShouldBe(1, "the first sink saw the batch " + first.Batches.Count + " times.");
        second.Batches.Count.ShouldBe(1, "the second sink saw the batch " + second.Batches.Count + " times.");
        first.Batches[0].Events.ShouldBe(outcome.Events, "the first sink was handed something other than this command's events.");
        second.Batches[0].Events.ShouldBe(outcome.Events, "the second sink was handed something other than this command's events.");
        first.Batches[0].Player.ShouldBe(player, "the batch attributes the events to the player who sent the command.");
        first.Batches[0].Command.ShouldBeSameAs(command, "the batch carries the command as accepted, not a reconstruction.");
        first.Batches[0].State.ShouldBeSameAs(outcome.State, "the batch carries the committed state the command produced.");
        order.ShouldBe(new[] { "first", "second" }, Case.Sensitive, "sinks are delivered to in registration order.");
    }

    [Fact]
    public async Task ExecuteAsync_reports_no_dispatch_failures_when_every_sink_takes_the_batch()
    {
        var (game, player) = Worlds.InARun();
        var cache = Worlds.CacheHolding(game.State(player));
        var run = game.State(player).Run!.Id;
        var useCase = UseCase(cache, out _);

        var outcome = await useCase.ExecuteAsync(
            new ApplyCommandRequest(player, run, new RollDiceCommand()), Worlds.Context(game), Worlds.Cancel);

        outcome.DispatchFailures.ShouldBeEmpty("nothing failed, so nothing should be reported as having failed.");
    }

    // ═════════════════════════════════════════════════════ a refused command

    [Fact]
    public async Task ExecuteAsync_answers_ILLEGAL_STATE_when_a_second_run_is_started_over_a_live_one()
    {
        var (game, player) = Worlds.InARun();
        var cache = Worlds.CacheHolding(game.State(player));
        var useCase = UseCase(cache, out _);

        var outcome = await useCase.ExecuteAsync(
            new ApplyCommandRequest(player, null, new StartRunCommand(Worlds.Chapter, DifficultyTier.NORMAL)),
            Worlds.Context(game),
            Worlds.Cancel);

        outcome.Accepted.ShouldBeFalse("a player already in a run cannot open a second one.");
        outcome.Rejection.ShouldBe(
            RejectionReason.ILLEGAL_STATE,
            "the reason has to be the one the rule gave. RUN_ALREADY_ENDED here would mean the live run " +
            "was being treated as a finished one, and RUN_NOT_FOUND would mean this layer refused it " +
            "before the domain ever saw it — three different defects that all read as 'refused'.");

        // The control that makes the refusal above an identity rather than a symptom: several rules
        // answer ILLEGAL_STATE, so the same command has to be accepted when the live run is the only
        // thing missing. Without this, a malformed chapter or an unregistered command would pass too.
        var runless = Worlds.Game();
        var newcomer = runless.CreatePlayer();
        var opened = await new ApplyCommandUseCase(
                new WorldSliceStore(Worlds.CacheHolding(runless.State(newcomer))),
                new DomainEventDispatcher([]))
            .ExecuteAsync(
                new ApplyCommandRequest(newcomer, null, new StartRunCommand(Worlds.Chapter, DifficultyTier.NORMAL)),
                Worlds.Context(runless),
                Worlds.Cancel);

        opened.Accepted.ShouldBeTrue(
            "the very same command was refused " + opened.Rejection + " for a player in no run, so the " +
            "refusal above is not the already-in-a-run rule firing — this command is simply never " +
            "accepted, and the case above proves nothing about which rule answered.");
    }

    [Fact]
    public async Task ExecuteAsync_answers_RUN_ALREADY_ENDED_when_a_run_command_arrives_after_the_run_ended()
    {
        var (game, player) = Worlds.AfterAnEndedRun();
        var cache = Worlds.CacheHolding(game.State(player));
        var run = game.State(player).Run!.Id;
        var useCase = UseCase(cache, out _);

        var outcome = await useCase.ExecuteAsync(
            new ApplyCommandRequest(player, run, new RollDiceCommand()), Worlds.Context(game), Worlds.Cancel);

        outcome.Accepted.ShouldBeFalse("the run is over.");
        outcome.Rejection.ShouldBe(
            RejectionReason.RUN_ALREADY_ENDED,
            "the run is the one the request names and it is present, so this refusal belongs to the " +
            "domain. RUN_NOT_FOUND here would mean the addressing guard cannot tell a finished run from " +
            "an absent one.");
    }

    [Fact]
    public async Task ExecuteAsync_writes_nothing_when_the_domain_refuses_the_command()
    {
        var (game, player) = Worlds.InARun();
        var cache = new RecordingCache(Worlds.CacheHolding(game.State(player)));
        var useCase = UseCase(cache, out _);

        await useCase.ExecuteAsync(
            new ApplyCommandRequest(player, null, new StartRunCommand(Worlds.Chapter, DifficultyTier.NORMAL)),
            Worlds.Context(game),
            Worlds.Cancel);

        cache.Writes.ShouldBeEmpty(
            "a refused command reached the store at all. It changed nothing, so there is nothing to " +
            "commit — and a write of the unchanged state would still slide whatever the store measures " +
            "from the last write.");
    }

    [Fact]
    public async Task ExecuteAsync_leaves_the_stored_bytes_untouched_when_the_domain_refuses_the_command()
    {
        var (game, player) = Worlds.InARun();
        var cache = Worlds.CacheHolding(game.State(player));
        var before = (await cache.ReadAsync(SliceKeys.ForPlayer(player), Worlds.Cancel))!;
        var useCase = UseCase(cache, out _);

        await useCase.ExecuteAsync(
            new ApplyCommandRequest(player, null, new StartRunCommand(Worlds.Chapter, DifficultyTier.NORMAL)),
            Worlds.Context(game),
            Worlds.Cancel);

        (await cache.ReadAsync(SliceKeys.ForPlayer(player), Worlds.Cancel))!.ShouldBe(
            before, "the stored row moved under a command the domain said no to.");
    }

    [Fact]
    public async Task ExecuteAsync_dispatches_nothing_when_the_domain_refuses_the_command()
    {
        var (game, player) = Worlds.InARun();
        var cache = Worlds.CacheHolding(game.State(player));
        var useCase = UseCase(cache, out var sink);

        var outcome = await useCase.ExecuteAsync(
            new ApplyCommandRequest(player, null, new StartRunCommand(Worlds.Chapter, DifficultyTier.NORMAL)),
            Worlds.Context(game),
            Worlds.Cancel);

        sink.Batches.ShouldBeEmpty(
            "a refused command told a sink something happened. Nothing did — an analytics row or an " +
            "economy-log append for a command that changed no state is a fabricated event.");
        outcome.Events.ShouldBeEmpty("a refused outcome carries no events either.");
    }

    // ═════════════════════════════════════════════════ addressing, before Apply

    [Fact]
    public async Task ExecuteAsync_answers_RUN_NOT_FOUND_when_the_request_names_a_run_the_player_is_not_in()
    {
        var (game, player) = Worlds.InARun();
        var cache = Worlds.CacheHolding(game.State(player));
        var useCase = UseCase(cache, out _);

        var outcome = await useCase.ExecuteAsync(
            new ApplyCommandRequest(player, new RunId("RUN_SOMEONE_ELSES_1"), new AbandonRunCommand()),
            Worlds.Context(game),
            Worlds.Cancel);

        outcome.Accepted.ShouldBeFalse("the command was addressed to a run that is not the one loaded.");
        outcome.Rejection.ShouldBe(
            RejectionReason.RUN_NOT_FOUND,
            "addressing is this layer's decision, not the domain's — the domain would have been handed " +
            "the player's actual run and would have acted on it.");
    }

    /// <summary>
    /// The proof that the guard runs <b>before</b> the domain: the command it carries would be
    /// accepted, so a use case that applied it first would have ended the run and stamped the player.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_does_not_apply_the_command_when_the_request_names_a_run_the_player_is_not_in()
    {
        var (game, player) = Worlds.InARun();
        var loaded = game.State(player);
        var cache = Worlds.CacheHolding(loaded);
        var useCase = UseCase(cache, out _);

        // A later instant, so an Apply that ran would move the player's stamp visibly.
        game.Clock.Advance(TimeSpan.FromHours(3));

        // The control for the stamp assertion below: the same command, at the same later instant,
        // against the run the player is actually in. If this did not move the stamp, "the stamp did
        // not move" would be true whether Apply ran or not and would pin nothing at all.
        var (control, controlPlayer) = Worlds.InARun();
        control.Clock.Advance(TimeSpan.FromHours(3));
        control.Send(controlPlayer, new AbandonRunCommand());

        control.State(controlPlayer).Player.LastAppliedAtUtc.ShouldNotBe(
            loaded.Player.LastAppliedAtUtc,
            "an accepted ABANDON_RUN at a later instant left the applied-at stamp where it was, so the " +
            "assertion below cannot tell an Apply that ran from one that did not.");

        var outcome = await useCase.ExecuteAsync(
            new ApplyCommandRequest(player, new RunId("RUN_SOMEONE_ELSES_1"), new AbandonRunCommand()),
            Worlds.Context(game),
            Worlds.Cancel);

        outcome.State.Player.LastAppliedAtUtc.ShouldBe(
            loaded.Player.LastAppliedAtUtc,
            "the player's applied-at stamp moved, which only an accepted command does — so the command " +
            "was applied to the player's own run and the addressing guard ran too late, or not at all.");

        outcome.State.Run!.Phase.ShouldBe(
            RunPhase.InProgress,
            "the run the player is actually in was ended by a command addressed to a different run.");
    }

    /// <summary>
    /// 🔒 The proof that the guard runs before <c>Apply</c> rather than merely instead of reporting
    /// its answer. The other addressing cases all compare the state that came back, and the loaded
    /// slice is what comes back either way — a guard moved to <em>after</em> the domain ran would
    /// satisfy every one of them, because <c>Apply</c> works on a clone and its result is discarded.
    /// A run-less player is the one shape where the difference is observable: the domain refuses to
    /// answer at all, loudly, and says in its own words that this refusal is the caller's to make
    /// before it is invoked.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_answers_RUN_NOT_FOUND_without_invoking_the_domain_for_a_player_in_no_run()
    {
        var game = Worlds.Game();
        var player = game.CreatePlayer();
        var slice = game.State(player);
        var useCase = UseCase(Worlds.CacheHolding(slice), out var sink);

        slice.Run.ShouldBeNull("this case is about a player who is in no run at all.");

        // The control that makes the assertion below a discriminator: without it, "no exception
        // escaped" would be true of a use case that never had a domain to invoke in the first place.
        Should.Throw<InvalidOperationException>(
            () => GameRules.Apply(slice, new RollDiceCommand(), Worlds.Context(game)),
            "the domain accepted a run command over a slice carrying no run, so reaching it is no " +
            "longer distinguishable from refusing before it and this case pins nothing.");

        var outcome = await useCase.ExecuteAsync(
            new ApplyCommandRequest(player, new RunId("RUN_SOMEONE_ELSES_1"), new RollDiceCommand()),
            Worlds.Context(game),
            Worlds.Cancel);

        outcome.Rejection.ShouldBe(
            RejectionReason.RUN_NOT_FOUND,
            "the addressing refusal has to be decided before the domain is handed the command. The " +
            "domain refuses to answer this one at all, so a guard placed after it turns a refusal the " +
            "player should be told into a failure the host has to explain.");

        sink.Batches.ShouldBeEmpty("nothing was applied, so there is nothing to deliver.");
    }

    [Fact]
    public async Task ExecuteAsync_writes_nothing_when_the_request_names_a_run_the_player_is_not_in()
    {
        var (game, player) = Worlds.InARun();
        var cache = new RecordingCache(Worlds.CacheHolding(game.State(player)));
        var useCase = UseCase(cache, out _);

        await useCase.ExecuteAsync(
            new ApplyCommandRequest(player, new RunId("RUN_SOMEONE_ELSES_1"), new AbandonRunCommand()),
            Worlds.Context(game),
            Worlds.Cancel);

        cache.Writes.ShouldBeEmpty("a command refused before the domain ran committed something.");
    }

    [Fact]
    public async Task ExecuteAsync_dispatches_nothing_when_the_request_names_a_run_the_player_is_not_in()
    {
        var (game, player) = Worlds.InARun();
        var cache = Worlds.CacheHolding(game.State(player));
        var useCase = UseCase(cache, out var sink);

        await useCase.ExecuteAsync(
            new ApplyCommandRequest(player, new RunId("RUN_SOMEONE_ELSES_1"), new AbandonRunCommand()),
            Worlds.Context(game),
            Worlds.Cancel);

        sink.Batches.ShouldBeEmpty("a command that was never applied has no events to deliver.");
    }

    // ═════════════════════════════════════════════════ commit and delivery order

    [Fact]
    public async Task ExecuteAsync_dispatches_nothing_when_the_commit_write_fails()
    {
        var (game, player) = Worlds.InARun();
        var cache = new RecordingCache(Worlds.CacheHolding(game.State(player)))
            .Refusing(SliceKeys.ForPlayer(player));
        var run = game.State(player).Run!.Id;
        var useCase = UseCase(cache, out var sink);

        var failure = await Should.ThrowAsync<IOException>(
            () => useCase.ExecuteAsync(
                new ApplyCommandRequest(player, run, new RollDiceCommand()), Worlds.Context(game), Worlds.Cancel));

        // Which write failed, not merely that something threw: an IOException out of the load, the
        // codec or an archive write would leave this case green while the commit never even ran.
        failure.Message.ShouldContain(
            RecordingCache.RefusalMessage, Case.Sensitive, "the failure the store was told to raise");
        failure.Message.ShouldContain(
            SliceKeys.ForPlayer(player),
            Case.Sensitive,
            "the write that failed has to be the commit itself — the player's own row.");

        sink.Batches.ShouldBeEmpty(
            "the commit failed, so the command did not happen — delivering its events would announce a " +
            "state change that was rolled back with the write.");
    }

    [Fact]
    public async Task ExecuteAsync_keeps_the_commit_and_accepts_the_command_when_a_sink_throws()
    {
        var (game, player) = Worlds.InARun();
        var cache = Worlds.CacheHolding(game.State(player));
        var run = game.State(player).Run!.Id;
        var useCase = new ApplyCommandUseCase(
            new WorldSliceStore(cache), new DomainEventDispatcher([new ThrowingSink()]));

        var outcome = await useCase.ExecuteAsync(
            new ApplyCommandRequest(player, run, new RollDiceCommand()), Worlds.Context(game), Worlds.Cancel);

        outcome.Accepted.ShouldBeTrue(
            "a side channel being down turned a command that provably happened into a refusal, so the " +
            "player is told no about a roll their state already records.");

        var committed = SnapshotCodec.DecodeSlice(
            (await cache.ReadAsync(SliceKeys.ForPlayer(player), Worlds.Cancel))!);

        Worlds.Hash(committed).ShouldBe(Worlds.Hash(outcome.State), "the commit was undone by a failing sink.");
    }

    [Fact]
    public async Task ExecuteAsync_reports_the_sink_that_failed_on_the_outcome()
    {
        var (game, player) = Worlds.InARun();
        var cache = Worlds.CacheHolding(game.State(player));
        var run = game.State(player).Run!.Id;
        var survivor = new RecordingSink();
        var useCase = new ApplyCommandUseCase(
            new WorldSliceStore(cache), new DomainEventDispatcher([new ThrowingSink(), survivor]));

        var outcome = await useCase.ExecuteAsync(
            new ApplyCommandRequest(player, run, new RollDiceCommand()), Worlds.Context(game), Worlds.Cancel);

        outcome.DispatchFailures.Count.ShouldBe(
            1, "exactly one sink failed, and a failure swallowed silently is a side channel nobody knows is down.");
        outcome.DispatchFailures[0].Error.ShouldContain(
            ThrowingSink.Message, Case.Sensitive, "what the sink actually threw");
        survivor.Batches.Count.ShouldBe(
            1, "the sink registered after the failing one was skipped, so one broken transport silences the rest.");
    }

    // ═════════════════════════════════════════════════ one seam, both command kinds

    [Fact]
    public async Task ExecuteAsync_leaves_the_run_untouched_when_a_command_outside_the_run_is_applied()
    {
        var (game, player) = Worlds.InARun();
        var loaded = game.State(player);
        var pin = loaded.Player.ToSnapshot();
        var before = Worlds.RunHash(pin, loaded.Run!.ToSnapshot());
        var cache = Worlds.CacheHolding(loaded);
        var useCase = UseCase(cache, out _);

        var outcome = await useCase.ExecuteAsync(
            new ApplyCommandRequest(player, null, new BeginSessionCommand("1.0.0", "content")),
            Worlds.Context(game, Worlds.MetaSeed),
            Worlds.Cancel);

        outcome.Accepted.ShouldBeTrue("beginning a session was refused " + outcome.Rejection + ".");

        var committed = SnapshotCodec.DecodeSlice(
            (await cache.ReadAsync(SliceKeys.ForPlayer(player), Worlds.Cancel))!);

        committed.Run.ShouldNotBeNull(
            "the run was dropped by a command that acts outside it — a player can open a shop without " +
            "leaving their run, and the loaded run travels with them.");

        Worlds.RunHash(pin, committed.Run).ShouldBe(
            before,
            "the run changed under a command that acts outside it. The same player row is hashed on " +
            "both sides, so the run is the only thing that can have moved.");
    }

    [Fact]
    public async Task ExecuteAsync_advances_the_run_when_a_command_inside_the_run_is_applied()
    {
        var (game, player) = Worlds.InARun();
        var loaded = game.State(player);
        var pin = loaded.Player.ToSnapshot();
        var before = Worlds.RunHash(pin, loaded.Run!.ToSnapshot());
        var cache = Worlds.CacheHolding(loaded);
        var useCase = UseCase(cache, out _);

        await useCase.ExecuteAsync(
            new ApplyCommandRequest(player, loaded.Run.Id, new RollDiceCommand()),
            Worlds.Context(game),
            Worlds.Cancel);

        var committed = SnapshotCodec.DecodeSlice(
            (await cache.ReadAsync(SliceKeys.ForPlayer(player), Worlds.Cancel))!);

        Worlds.RunHash(pin, committed.Run!).ShouldNotBe(
            before,
            "a roll left the run exactly as it was, so the same seam that must not write the run for " +
            "one kind of command is not writing it for the other either.");
    }

    // ═════════════════════════════════════════════════ the finished-run archive

    [Fact]
    public async Task ExecuteAsync_keeps_an_ended_runs_row_readable_after_the_next_run_has_started()
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
        started.State.Run!.Id.ShouldNotBe(first, "the second run carries the first one's identity.");

        var archived = await store.ReadArchivedRunAsync(first, Worlds.Cancel);

        archived.ShouldNotBeNull(
            "the finished run is gone. It is keyed by its own identity precisely so the next run cannot " +
            "displace it, and the results screen is read after the next run has already started.");
        archived.Id.ShouldBe(
            first,
            "the row under the first run's key holds some other run, so the second run displaced the " +
            "first one's finished record instead of being archived under its own identity.");
        archived.Phase.ShouldBe(RunPhase.Ended, "the archived copy is not the finished state of that run.");
    }

    // ═════════════════════════════════════════════════════════════ fixtures

    private static ApplyCommandUseCase UseCase(ILocalCachePort cache, out RecordingSink sink)
    {
        sink = new RecordingSink();

        return new ApplyCommandUseCase(new WorldSliceStore(cache), new DomainEventDispatcher([sink]));
    }
}
