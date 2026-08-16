using Shouldly;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.Tests.Events;
using SlayIdleRepeat.Application.Tests.Persistence;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Hosting;

/// <summary>
/// The five ways a host call does not succeed, each pinned by which one it was. From outside they all
/// read as "that did not work", and every one of them has a different fix: a miswired host, a stale
/// screen, a rule saying no, a full disk, and a side channel being down.
/// </summary>
public sealed class InProcessGameHostFailureTests
{
    /// <summary>A player the host never issued. Inside the cache's key space, so nothing refuses it for its spelling.</summary>
    private static readonly PlayerId Stranger = new("PLAYER_00000404");

    private static BeginSessionCommand BeginSession => new("1.0.0", "content");

    private static StartRunCommand StartRun => new(Worlds.Chapter, DifficultyTier.NORMAL);

    // ═══════════════════════════════════════════ 1 · the player has no stored row

    [Fact]
    public async Task SubmitAsync_fails_loudly_and_names_the_player_when_nothing_is_stored_for_them()
    {
        var host = Hosts.Over(new InMemoryLocalCache());

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => host.SubmitAsync(Stranger, null, BeginSession, Worlds.Cancel));

        thrown.Message.ShouldContain(
            Stranger.ToString(),
            Case.Sensitive,
            "the failure has to name the player it could not load. A command arrives with an identity " +
            "the game already issued, so a miss is a miswired host — and a message that does not say " +
            "which identity leaves nothing to diagnose it with.");
    }

    [Fact]
    public async Task ReadOwnStateAsync_answers_NoSuchPlayer_when_nothing_is_stored_for_them()
    {
        var host = Hosts.Over(new InMemoryLocalCache());

        var read = await host.ReadOwnStateAsync(Stranger, null, Worlds.Cancel);

        read.Lookup.ShouldBe(
            OwnStateLookup.NoSuchPlayer,
            "an unknown player is an answer on the read side, not the loud failure the write side " +
            "gives — nothing is about to be overwritten by it.");
        read.View.ShouldBeNull("there is no state to hand back.");
    }

    // ═════════════════════════════════════ 2 · a run the player is not standing in

    [Fact]
    public async Task SubmitAsync_answers_RUN_NOT_FOUND_when_the_command_names_a_run_the_player_is_not_in()
    {
        var host = Hosts.Over(new InMemoryLocalCache());
        var player = await host.OpenProfileAsync(Worlds.Cancel);

        var opened = await host.SubmitAsync(player, null, StartRun, Worlds.Cancel);

        opened.Accepted.ShouldBeTrue("starting a run was refused " + opened.Rejection + ".");

        var outcome = await host.SubmitAsync(
            player, new RunId("RUN_SOMEONE_ELSES_1"), new AbandonRunCommand(), Worlds.Cancel);

        outcome.Rejection.ShouldBe(
            RejectionReason.RUN_NOT_FOUND,
            "addressing is decided above the domain, which would otherwise have been handed the run " +
            "the player is actually in and would have ended it. This value is unambiguous precisely " +
            "because the domain is forbidden to return it.");

        outcome.State.Run!.Phase.ShouldBe(
            RunPhase.InProgress,
            "the run the player is actually in was ended by a command addressed to a different one.");
    }

    [Fact]
    public async Task ReadOwnStateAsync_answers_NoSuchRun_when_the_request_names_a_run_the_player_is_not_in()
    {
        var host = Hosts.Over(new InMemoryLocalCache());
        var player = await host.OpenProfileAsync(Worlds.Cancel);

        var read = await host.ReadOwnStateAsync(player, new RunId("RUN_NEVER_HAPPENED"), Worlds.Cancel);

        read.Lookup.ShouldBe(
            OwnStateLookup.NoSuchRun,
            "the player is known and the run is not, which is a different answer from NoSuchPlayer — " +
            "one sends the client to account recovery and the other to its own stale run id.");
        read.View.ShouldBeNull("there is no run to hand back.");
    }

    // ══════════════════════════════════════════════════════ 3 · the domain refused

    [Fact]
    public async Task SubmitAsync_answers_with_the_domains_own_reason_when_a_rule_refuses_the_command()
    {
        var host = Hosts.Over(new InMemoryLocalCache());
        var player = await host.OpenProfileAsync(Worlds.Cancel);

        var opened = await host.SubmitAsync(player, null, StartRun, Worlds.Cancel);

        opened.Accepted.ShouldBeTrue(
            "the very same command has to be accepted for a player in no run, or the refusal below is " +
            "not the already-in-a-run rule firing and pins nothing about which rule answered.");

        var refused = await host.SubmitAsync(player, null, StartRun, Worlds.Cancel);

        refused.Rejection.ShouldBe(
            RejectionReason.ILLEGAL_STATE,
            "the reason has to be the one the rule gave. RUN_NOT_FOUND here would mean the host " +
            "refused it before the domain ever saw it, and RUN_ALREADY_ENDED would mean the live run " +
            "was treated as a finished one — three different defects that all read as 'refused'.");

        refused.Events.ShouldBeEmpty("a refused command changed nothing there could be anything to announce.");
    }

    // ═══════════════════════════════════════════════════════ 4 · the commit threw

    [Fact]
    public async Task SubmitAsync_lets_the_stores_own_failure_out_when_the_commit_write_fails()
    {
        var cache = new RecordingCache(new InMemoryLocalCache());
        var host = Hosts.Over(cache);
        var player = await host.OpenProfileAsync(Worlds.Cancel);

        // Refused only after the profile exists: the same key carries the profile's own creation write.
        cache.Refusing(SliceKeys.ForPlayer(player));

        var failure = await Should.ThrowAsync<IOException>(
            () => host.SubmitAsync(player, null, BeginSession, Worlds.Cancel));

        failure.Message.ShouldContain(
            RecordingCache.RefusalMessage,
            Case.Sensitive,
            "the store's own failure has to reach the caller unchanged. Wrapped or relabelled, a full " +
            "disk becomes indistinguishable from a corrupt row and from a handler defect.");

        failure.Message.ShouldContain(
            SliceKeys.ForPlayer(player),
            Case.Sensitive,
            "the write that failed has to be the commit itself — the player's own row.");
    }

    // ═════════════════════════════════════════════════════════ 5 · a sink threw

    [Fact]
    public async Task SubmitAsync_reports_a_failing_sink_without_turning_the_command_into_a_refusal()
    {
        var host = Hosts.Over(new InMemoryLocalCache(), sinks: [new ThrowingSink()]);
        var player = await host.OpenProfileAsync(Worlds.Cancel);

        var outcome = await host.SubmitAsync(player, null, BeginSession, Worlds.Cancel);

        outcome.Rejection.ShouldBeNull(
            "a sink failure is not a refusal. The command was committed before delivery was even " +
            "attempted, so telling the player no describes a state their own row contradicts.");

        outcome.DispatchFailures.ShouldHaveSingleItem()
            .Error.ShouldContain(
                ThrowingSink.Message,
                Case.Sensitive,
                "which channel is down, and why. A failure reported without its cause is a channel " +
                "nobody can fix.");
    }
}
