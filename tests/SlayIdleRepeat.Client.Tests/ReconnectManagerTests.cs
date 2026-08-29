using Shouldly;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Client.Game.Net;
using SlayIdleRepeat.Core.Commands;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The backoff ladder, the pill threshold, the one distinction the whole thing hangs off — a
/// transport that could not answer is not a server that said no — and the drain that follows a
/// reconnect.
/// </summary>
/// <remarks>
/// 🔴 Two failures here are invisible until a player is on a bad connection. A ladder that keeps
/// doubling leaves somebody waiting most of a minute after their network came back; a refusal
/// mistaken for a connection loss dims every server-backed control on the screen because the server
/// said "you cannot afford that". Both are pinned below by name.
/// </remarks>
public sealed class ReconnectManagerTests
{
    /// <summary>Anchors every case's clock, so a failure message reads an instant rather than "now".</summary>
    private static readonly DateTimeOffset FixtureInstant = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The authored pill threshold, written out rather than read back off the class under test.
    /// </summary>
    /// <remarks>
    /// 🔒 It is a LITERAL on purpose, and the first draft of this file got that wrong: a case that
    /// advances the clock by <c>ReconnectManager.PillThreshold</c> agrees with every value that
    /// constant could ever hold, so retuning a spec-locked presentation number would leave the whole
    /// file green. Two seconds is the design's, not this class's, and the two are pinned against each
    /// other here.
    /// </remarks>
    private static readonly TimeSpan AuthoredPillThreshold = TimeSpan.FromSeconds(2);

    // ---- the ladder, verbatim --------------------------------------------------------------------

    /// <summary>
    /// 🔒 The authored schedule: 0.5 s, 1 s, 2 s, 4 s, 8 s, then every 10 s indefinitely.
    /// </summary>
    [Theory]
    [InlineData(1, 500)]
    [InlineData(2, 1_000)]
    [InlineData(3, 2_000)]
    [InlineData(4, 4_000)]
    [InlineData(5, 8_000)]
    [InlineData(6, 10_000)]
    [InlineData(7, 10_000)]
    [InlineData(40, 10_000)]
    public void The_delay_after_a_failure_follows_the_authored_ladder(
        int consecutiveFailures, int expectedMilliseconds)
    {
        ReconnectManager.DelayAfterFailure(consecutiveFailures).ShouldBe(
            TimeSpan.FromMilliseconds(expectedMilliseconds),
            $"attempt {consecutiveFailures + 1} is scheduled {expectedMilliseconds} ms after failure " +
            $"{consecutiveFailures}. The ladder rises 0.5 → 1 → 2 → 4 → 8 and then STOPS at 10 s " +
            "forever; a red on the last three rows means it kept doubling, and a player who put the " +
            "phone down for a minute would then wait most of another minute after the network " +
            "returned, with every server-backed control dimmed the whole time.");
    }

    [Fact]
    public void The_ladder_has_no_delay_for_a_zeroth_failure()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => ReconnectManager.DelayAfterFailure(0),
            "the ladder describes the wait AFTER a failure, so asking for the zeroth one is asking a " +
            "question with no answer. Returning the first rung instead would let a caller that had " +
            "not failed at all schedule a retry, and a connected client would start polling on a " +
            "backoff for nothing.");
    }

    // ---- the pill threshold ----------------------------------------------------------------------

    /// <summary>
    /// 🔒 A blink of failure that recovers inside the threshold shows a player nothing at all.
    /// </summary>
    [Fact]
    public async Task A_failure_just_under_the_pill_threshold_is_Waiting_and_draws_nothing()
    {
        var clock = new ManualClock(FixtureInstant);
        var manager = Build(RecordingGameApi.Reachable().UnreachableFor(int.MaxValue), clock, out _);
        manager.Follow(NetWorlds.Run);

        await manager.PollAsync(CancellationToken.None);

        clock.Advance(AuthoredPillThreshold - TimeSpan.FromMilliseconds(1));
        await manager.PollAsync(CancellationToken.None);

        manager.State.ShouldBe(
            ConnectionState.Waiting,
            "less than the threshold has elapsed since the first failure, so the connection is failing " +
            "but nothing is drawn. Reporting Reconnecting here is what would flash a status pill at " +
            "everybody whose handset drops one packet — the threshold exists for exactly that, and a " +
            "state machine without a Waiting member has no way to be failing and silent at once.");
    }

    [Fact]
    public async Task A_failure_at_the_pill_threshold_becomes_Reconnecting()
    {
        var clock = new ManualClock(FixtureInstant);
        var manager = Build(RecordingGameApi.Reachable().UnreachableFor(int.MaxValue), clock, out _);
        manager.Follow(NetWorlds.Run);

        await manager.PollAsync(CancellationToken.None);

        clock.Advance(AuthoredPillThreshold);
        await manager.PollAsync(CancellationToken.None);

        manager.State.ShouldBe(
            ConnectionState.Reconnecting,
            "the threshold has been reached, so the pill goes up. A red here means a connection that " +
            "has been down for two full seconds is still telling the player nothing, which is the " +
            "failure a player reads as the game having frozen.");
    }

    [Fact]
    public async Task A_reachable_server_leaves_the_connection_Connected()
    {
        var clock = new ManualClock(FixtureInstant);
        var api = RecordingGameApi.Reachable().Answering(NetWorlds.State(NetWorlds.SomeHash));
        var manager = Build(api, clock, out _);
        manager.Follow(NetWorlds.Run);

        await manager.PollAsync(CancellationToken.None);

        manager.State.ShouldBe(
            ConnectionState.Connected,
            "nothing failed, so nothing is wrong. This is the negative control for the two cases " +
            "above: a manager that reported Waiting or Reconnecting on a healthy connection would " +
            "dim every server-backed control in the build while the server was answering fine.");
        manager.ConsecutiveFailures.ShouldBe(0);
    }

    // ---- 🔒 a refusal is not a connection problem ------------------------------------------------

    /// <summary>
    /// 🔒 The server understood and said no. Retrying it unchanged cannot help, so no backoff starts.
    /// </summary>
    [Fact]
    public async Task A_refusal_does_not_start_a_backoff()
    {
        var clock = new ManualClock(FixtureInstant);
        var api = RecordingGameApi.Reachable().RefusingFor(int.MaxValue, statusCode: 404);
        var manager = Build(api, clock, out _);
        manager.Follow(NetWorlds.Run);

        await manager.PollAsync(CancellationToken.None);

        manager.State.ShouldBe(
            ConnectionState.Connected,
            "a refusal arrived, which means the request reached the server and came back — the " +
            "connection is demonstrably fine. Treating it as a connection loss would dim every " +
            "server-backed control on the screen because the server declined ONE request, and the " +
            "ladder would then wait ten seconds between retries of something that can never succeed.");
        manager.ConsecutiveFailures.ShouldBe(
            0,
            "no failure was recorded, so no rung of the ladder was taken. This is the counterpart of " +
            "the case below: the two exceptions the port raises must land in different places.");
        manager.LastRefusalStatusCode.ShouldBe(
            404,
            "the status is kept because the repair depends on which refusal it was, and because a " +
            "refusal that vanished without trace would be indistinguishable from a call that never " +
            "happened.");
    }

    [Fact]
    public async Task An_unreachable_transport_does_start_a_backoff()
    {
        var clock = new ManualClock(FixtureInstant);
        var api = RecordingGameApi.Reachable().UnreachableFor(int.MaxValue);
        var manager = Build(api, clock, out _);
        manager.Follow(NetWorlds.Run);

        await manager.PollAsync(CancellationToken.None);

        manager.ConsecutiveFailures.ShouldBe(
            1,
            "the transport could not answer, which IS what 'the connection is lost' means here. If " +
            "this is red the manager is swallowing the one failure the whole ladder is driven by, and " +
            "the case above becomes vacuous — it would be proving that neither exception starts a " +
            "backoff, rather than that only one does.");
    }

    /// <summary>🔒 A connection loss is a recorded fact, never an exception out of the poll.</summary>
    [Fact]
    public async Task PollAsync_does_not_throw_when_the_transport_cannot_answer()
    {
        var clock = new ManualClock(FixtureInstant);
        var manager = Build(RecordingGameApi.Reachable().UnreachableFor(int.MaxValue), clock, out _);
        manager.Follow(NetWorlds.Run);

        await Should.NotThrowAsync(
            () => manager.PollAsync(CancellationToken.None),
            "this is driven from the engine's per-frame callback, where nothing catches anything. An " +
            "escaping transport failure turns a dropped packet into a crashed game, which is the " +
            "opposite of the specification's one hard rule that a run is never blocked on the network.");
    }

    // ---- the retry is the same request ------------------------------------------------------------

    /// <summary>
    /// 🔒 A retry carries the same command id and the same sequence, so the server replays rather
    /// than reapplies.
    /// </summary>
    [Fact]
    public async Task A_retried_command_is_resent_with_its_original_id_and_sequence()
    {
        var clock = new ManualClock(FixtureInstant);
        var api = RecordingGameApi.Reachable()
                                  .UnreachableFor(1)
                                  .AnsweringCommandsWith(NetWorlds.Accepted(1, NetWorlds.SomeHash));
        var manager = Build(api, clock, out var queue);
        var queued = queue.Enqueue(new RollDiceCommand(), NetWorlds.Run);

        await manager.PollAsync(CancellationToken.None);

        clock.Advance(ReconnectManager.DelayAfterFailure(1));
        await manager.PollAsync(CancellationToken.None);

        api.Sent.Count.ShouldBe(2, "the first attempt failed and the second went through.");
        api.Sent[1].CommandId.ShouldBe(
            queued.Id,
            "the retry has to be recognisable to the server as the SAME command. A fresh id makes it a " +
            "second command, and the roll is charged twice — the failure this whole protocol exists to " +
            "prevent, and one that leaves no trace in any log, because both requests are well-formed.");
        api.Sent.Select(envelope => envelope.Sequence).ShouldBe(
            [queued.Sequence, queued.Sequence],
            "the sequence is the other half of the same claim, and BOTH attempts are held against the " +
            "number the QUEUE assigned rather than against each other. Comparing the two attempts " +
            "alone passes on a manager that shifts every envelope by the same amount — a deliberate " +
            "'+1' in the envelope builder went unnoticed until this assertion named the queued value. " +
            "A renumbered send lands at a position the scope never reached, and the server answers " +
            "SEQUENCE_GAP for the number that was skipped.");
        queue.PendingCount.ShouldBe(
            0,
            "the second attempt was answered, so the command left the queue. A drain that acknowledged " +
            "nothing would resend the same command every poll forever.");
    }

    [Fact]
    public async Task The_queue_is_drained_in_order_once_the_connection_returns()
    {
        var clock = new ManualClock(FixtureInstant);
        var api = RecordingGameApi.Reachable()
                                  .UnreachableFor(1)
                                  .Answering(NetWorlds.State(NetWorlds.SomeHash))
                                  .AnsweringCommandsWith(NetWorlds.Accepted(1, NetWorlds.SomeHash));
        var manager = Build(api, clock, out var queue);
        manager.Follow(NetWorlds.Run);
        var first = queue.Enqueue(new RollDiceCommand(), NetWorlds.Run);
        var second = queue.Enqueue(new RollDiceCommand(), NetWorlds.Run);

        await manager.PollAsync(CancellationToken.None);

        clock.Advance(ReconnectManager.DelayAfterFailure(1));
        await manager.PollAsync(CancellationToken.None);

        api.Sent.Select(envelope => envelope.CommandId).ShouldBe(
            [first.Id, second.Id],
            "the queue's order is the order the player issued the commands in, and the server applies " +
            "what it receives in the order it receives it. Draining out of order would apply a " +
            "purchase before the roll that paid for it, and the second command would be refused for a " +
            "state the first was about to create.");
    }

    // ---- the resync -------------------------------------------------------------------------------

    [Fact]
    public async Task A_reconnect_reads_the_run_from_the_sequence_the_mirror_holds()
    {
        var clock = new ManualClock(FixtureInstant);
        var api = RecordingGameApi.Reachable().Answering(NetWorlds.State(NetWorlds.AnotherHash, sequence: 9));
        var manager = Build(api, clock, out _, out var mirror);
        mirror.Apply(NetWorlds.Accepted(sequence: 4, NetWorlds.SomeHash));
        manager.Follow(NetWorlds.Run);

        await manager.PollAsync(CancellationToken.None);

        api.FetchedFrom.ShouldBe(
            [4],
            "the read asks from what the client already holds, so the server sends back only what was " +
            "missed. Asking from zero would re-send the whole run's outcomes on every reconnect, which " +
            "is the cost this parameter exists to avoid; asking from a number the mirror does not hold " +
            "would silently skip the outcomes in between.");
        mirror.StateHash.ShouldBe(
            NetWorlds.AnotherHash,
            "the answer is applied to the mirror, which is the only reason to make the call.");
    }

    /// <summary>
    /// 🔒 Whether the mirror MOVED is what decides whether a player is told anything at all.
    /// </summary>
    [Fact]
    public async Task LastResyncChangedState_is_false_when_the_read_found_nothing_new()
    {
        var clock = new ManualClock(FixtureInstant);
        var api = RecordingGameApi.Reachable().Answering(NetWorlds.State(NetWorlds.SomeHash, sequence: 9));
        var manager = Build(api, clock, out _, out var mirror);
        mirror.Apply(NetWorlds.Accepted(sequence: 4, NetWorlds.SomeHash));
        manager.Follow(NetWorlds.Run);

        await manager.PollAsync(CancellationToken.None);

        manager.LastResyncChangedState.ShouldBeFalse(
            "the server was exactly where the client left it, so nothing a player could see changed. " +
            "Reporting true here would announce the network rather than the game, and a 'Caught up.' " +
            "toast that fires on every reconnect is one a player stops reading before the reconnect " +
            "that actually moved something.");
    }

    [Fact]
    public async Task LastResyncChangedState_is_true_when_the_read_moved_the_mirror()
    {
        var clock = new ManualClock(FixtureInstant);
        var api = RecordingGameApi.Reachable().Answering(NetWorlds.State(NetWorlds.AnotherHash, sequence: 9));
        var manager = Build(api, clock, out _, out var mirror);
        mirror.Apply(NetWorlds.Accepted(sequence: 4, NetWorlds.SomeHash));
        manager.Follow(NetWorlds.Run);

        await manager.PollAsync(CancellationToken.None);

        manager.LastResyncChangedState.ShouldBeTrue(
            "the server had moved on while the client was away, so there IS something to announce. " +
            "This is the discriminating half of the pair: without it the flag could be hardwired false " +
            "and the case above would still pass.");
    }

    // ---- the not-due path -------------------------------------------------------------------------

    [Fact]
    public async Task No_attempt_is_made_before_the_ladder_says_it_is_due()
    {
        var clock = new ManualClock(FixtureInstant);
        var api = RecordingGameApi.Reachable().UnreachableFor(int.MaxValue);
        var manager = Build(api, clock, out _);
        manager.Follow(NetWorlds.Run);

        await manager.PollAsync(CancellationToken.None);

        clock.Advance(ReconnectManager.DelayAfterFailure(1) - TimeSpan.FromMilliseconds(1));
        await manager.PollAsync(CancellationToken.None);

        api.FetchAttempts.ShouldBe(
            1,
            "the first rung of the ladder has not elapsed, so no second attempt was made. This is " +
            "called every frame: a manager that ignored its own schedule would hammer an unreachable " +
            "server sixty times a second and hold the radio awake while it did.");
    }

    [Fact]
    public async Task An_idle_connected_client_sends_nothing_at_all()
    {
        var clock = new ManualClock(FixtureInstant);
        var api = RecordingGameApi.Reachable();
        var manager = Build(api, clock, out _);

        await manager.PollAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(1));
        await manager.PollAsync(CancellationToken.None);

        api.FetchAttempts.ShouldBe(0);
        api.SendAttempts.ShouldBe(
            0,
            "nothing is queued, nothing has failed and no run is being followed, so there is nothing " +
            "to say to the server. A poll that called out anyway would be a background heartbeat " +
            "nobody asked for, on a handset, every frame.");
    }

    // ---- the server's own Retry-After wins ---------------------------------------------------------

    [Fact]
    public async Task A_server_that_named_a_Retry_After_is_waited_for_instead_of_the_ladder()
    {
        var clock = new ManualClock(FixtureInstant);
        var askedFor = TimeSpan.FromSeconds(30);
        var api = RecordingGameApi.Reachable().UnreachableFor(int.MaxValue, askedFor);
        var manager = Build(api, clock, out _);
        manager.Follow(NetWorlds.Run);

        await manager.PollAsync(CancellationToken.None);

        clock.Advance(ReconnectManager.DelayAfterFailure(1));
        await manager.PollAsync(CancellationToken.None);

        api.FetchAttempts.ShouldBe(
            1,
            "the ladder's first rung has elapsed but the server asked for thirty seconds, and the " +
            "server's own answer wins over this client's guess. Retrying at 0.5 s after a 429 is what " +
            "gets a client rate-limited harder, which the ladder cannot see and the header states.");
    }

    private static ReconnectManager Build(RecordingGameApi api, IClockPort clock, out CommandQueue queue) =>
        Build(api, clock, out queue, out _);

    private static ReconnectManager Build(
        RecordingGameApi api, IClockPort clock, out CommandQueue queue, out StateMirror mirror)
    {
        mirror = new StateMirror();
        queue = new CommandQueue(CountingIdGenerator.Counting());

        return new ReconnectManager(api, mirror, queue, clock);
    }
}
