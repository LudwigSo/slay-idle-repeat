using Shouldly;
using SlayIdleRepeat.Adapters.Cache.LocalFile;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Client.Game.Net;
using Xunit;

// S2925 (no Thread.Sleep in a test) is off for this file. Every pause here is an INPUT, not a
// race being waited out: the subject under test is a pump whose behaviour depends on the wall-clock
// elapsed between Advance calls, so a fake clock would remove the thing being measured. Scoped to
// this file rather than the suite, because anywhere else a sleep in a test is exactly the smell
// S2925 says it is.
#pragma warning disable S2925

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The thing that turns a composed connection into a live one: one frame, one attempt at a time,
/// and nothing swallowed.
/// </summary>
/// <remarks>
/// 🔴 M7-02 shipped the ladder with no caller at all — a poll nobody called, an overlay nobody
/// instantiated. This is the seam that closes that, and it runs on the engine's per-frame callback,
/// where an escaping exception is fatal and an unobserved one is invisible.
/// </remarks>
public sealed class ConnectionPumpTests : IDisposable
{
    private static readonly DateTimeOffset StartedAt = new(2026, 5, 2, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The restore, the sign-in, one turn of the ladder, and the write back.</summary>
    private const int SettledAfterAPersist = 4;

    /// <summary>The one duty a gated pump can finish: the restore, before it reaches the server.</summary>
    private const int SettledAfterTheRestore = 1;

    /// <summary>Enough frames for a file read or write to land, and few enough that a stall fails.</summary>
    private const int FrameBudget = 500;

    /// <summary>How long a frame waits, so the budget is a real interval rather than a spin count.</summary>
    private static readonly TimeSpan FramePause = TimeSpan.FromMilliseconds(1);

    /// <summary>Frames driven after the store is moved, so a re-read would have had every chance.</summary>
    private const int FramesAfterTheStoreMoves = 5;

    /// <summary>Frames driven against a server that never stops refusing. Enough that a per-frame retry is unmistakable.</summary>
    private const int FramesUnderARefusal = 30;

    private readonly string _cacheRoot = RepoPaths.ScratchCacheRoot();

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_cacheRoot))
        {
            Directory.Delete(_cacheRoot, recursive: true);
        }
    }

    [Fact]
    public void Advance_makes_no_second_attempt_while_one_is_in_flight()
    {
        var api = RecordingGameApi.Reachable().Gated();
        var (pump, _, clock) = Pump(api);

        pump.Advance(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(30));
        pump.Advance(CancellationToken.None);

        api.RegisterAttempts.ShouldBe(
            1,
            "the pump started a second sign-in while the first had not answered. This runs once per " +
            "frame, so a missing in-flight guard is not one extra request — it is one per frame for " +
            "as long as the server is slow, which is precisely when it can least afford them, and " +
            "each one mints a fresh anonymous account.");
    }

    [Fact]
    public void Advance_reports_a_fault_rather_than_swallowing_it()
    {
        var api = RecordingGameApi.Reachable().RefusingFor(1, statusCode: 403);
        var (pump, _, _) = Pump(api);

        pump.Advance(CancellationToken.None);
        pump.Advance(CancellationToken.None);

        pump.FaultCount.ShouldBe(
            1,
            "a refusal escapes the session opener on purpose — retrying it unchanged cannot help — " +
            "so something has to notice. A pump that starts a task and never looks at it turns every " +
            "such failure into an unobserved exception nobody will ever see.");
        pump.LastFault.ShouldBeOfType<GameApiRefusedException>(
            "and it has to be the fault that actually happened. A pump recording only that something " +
            "went wrong tells whoever is holding the handset exactly as much as recording nothing.");
    }

    [Fact]
    public void Advance_does_nothing_after_Stop()
    {
        var api = RecordingGameApi.Reachable();
        var (pump, _, _) = Pump(api);

        pump.Stop();
        pump.Advance(CancellationToken.None);

        pump.Stopped.ShouldBeTrue("the pump has to say it stopped, or nothing can tell shutdown from idle");
        api.RegisterAttempts.ShouldBe(
            0,
            "the root node stops the pump as the window closes, and a request started after that " +
            "runs against a graph being torn down underneath it — which is how a clean exit becomes " +
            "an ObjectDisposedException in a log nobody is reading any more.");
    }

    /// <summary>
    /// 🔒 The ladder is climbed and then descended: a failure schedules the next attempt, and the
    /// attempt that lands puts the connection back.
    /// </summary>
    [Fact]
    public void Advance_climbs_the_ladder_back_down_when_the_session_reopens()
    {
        var api = RecordingGameApi.Reachable().UnreachableFor(1);
        var (pump, connection, clock) = Pump(api);

        pump.Advance(CancellationToken.None);
        connection.State.ShouldBe(
            ConnectionState.Waiting,
            "a failed sign-in has to reach the ladder. Without it nothing is ever due, the ladder " +
            "has no input at all on this arm, and the overlay reports a connection that was never " +
            "attempted.");

        clock.Advance(ReconnectManager.DelayAfterFailure(1));
        pump.Advance(CancellationToken.None);

        api.RegisterAttempts.ShouldBe(
            2,
            "the retry never happened. The whole ladder exists to make one, and the first delay is " +
            "half a second — a player who walked through a tunnel should be back before they notice.");
        connection.State.ShouldBe(
            ConnectionState.Connected,
            "and an attempt that reached the server has to clear the failure. A ladder that only " +
            "ever climbs leaves the pill up over a connection that came back.");
    }

    /// <summary>
    /// 🔒 The control: on the not-due path the pump costs a clock read and nothing else.
    /// </summary>
    /// <remarks>
    /// Without it, an implementation that attempted on every single frame would satisfy every case
    /// above — and would turn a dropped connection into sixty sign-ins a second.
    /// </remarks>
    [Fact]
    public void Advance_issues_no_request_while_nothing_is_due()
    {
        var api = RecordingGameApi.Reachable().UnreachableFor(int.MaxValue);
        var (pump, _, clock) = Pump(api);

        pump.Advance(CancellationToken.None);
        clock.Advance(ReconnectManager.DelayAfterFailure(1) - TimeSpan.FromMilliseconds(1));
        pump.Advance(CancellationToken.None);

        api.RegisterAttempts.ShouldBe(
            1,
            "the pump attempted again before the ladder said it was due. The delay is the whole " +
            "backoff: ignoring it turns a server that is refusing connections into a client that " +
            "hammers it once per frame.");
    }

    /// <summary>
    /// 🔒 <b>A server that goes on refusing the sign-in is asked again on the ladder, not on the frame.</b>
    /// </summary>
    /// <remarks>
    /// The refusal budget is unbounded here, which is what the other refusal case above is not: with
    /// a budget of one, the second frame finds the server answering and nothing hammers anything. A
    /// refusal escapes the opener by design and leaves the connection reporting Connected — so
    /// nothing else in this suite can see that the pump then reopens on every single frame, minting
    /// a fresh anonymous account each time, with no pill drawn and no fault a player could act on.
    /// </remarks>
    [Fact]
    public void Advance_holds_off_a_sign_in_the_server_keeps_refusing()
    {
        var api = RecordingGameApi.Reachable().RefusingFor(int.MaxValue, statusCode: 403);
        var (pump, connection, clock) = Pump(api);

        for (var frame = 0; frame < FramesUnderARefusal; frame++)
        {
            pump.Advance(CancellationToken.None);
            Thread.Sleep(FramePause);
        }

        api.RegisterAttempts.ShouldBe(
            1,
            "the pump asked a refusing server again on the very next frame, and on every frame after " +
            "it. At sixty frames a second that is sixty registrations a second for as long as the " +
            "application is open — each one an attempt to mint another anonymous account — over an " +
            "answer that is identical every time.");
        connection.State.ShouldBe(
            ConnectionState.Connected,
            "and the hold must not be bought by turning a refusal into a connection loss. The " +
            "network is demonstrably there: the server answered. Drawing a reconnect pill over it " +
            "would tell the player the one thing that is not wrong.");
        connection.ConsecutiveFailures.ShouldBe(
            0,
            "for the same reason, and this is the value the opener's own suite pins: a refusal that " +
            "climbed the ladder would back off from a server that is up and answering.");

        clock.Advance(ReconnectManager.SteadyRetryInterval);

        for (var frame = 0; frame < FramesUnderARefusal; frame++)
        {
            pump.Advance(CancellationToken.None);
            Thread.Sleep(FramePause);
        }

        api.RegisterAttempts.ShouldBe(
            2,
            "and the control: a hold is not a stop. A refusal can stop being one — an account " +
            "service that was down comes back — so the sign-in is retried once the interval has " +
            "passed, and exactly once, not once per frame from then on.");
    }

    // ------------------------------------ the mirror's two duties, which nothing else can observe

    /// <summary>
    /// 🔒 <b>The mirror is filled from the store before the pump ever reaches the server.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other case here is about the ladder, and every one of them passes against a pump that
    /// ignores the mirror and its cache outright — <c>Advance</c> starts a task and returns, so a
    /// duty that never ran leaves no trace in a return value. This is the case that says the cold
    /// start has something to draw with, which is the entire reason the store exists.
    /// </para>
    /// <para>
    /// The api is gated so the sign-in never answers, which is what makes the settled-work signal a
    /// fact rather than a race: the restore is then the only duty that can have finished.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Advance_fills_the_mirror_from_the_store_on_the_first_tick()
    {
        var cache = new LocalFileCache(_cacheRoot);
        await Store(cache, NetWorlds.SomeHash, sequence: 4);

        var (pump, mirror) = PumpOver(RecordingGameApi.Reachable().Gated(), cache);
        RunUntilSettled(pump, SettledAfterTheRestore);

        pump.LastSettledWork.ShouldBe(
            ConnectionPumpWork.MirrorRestore,
            "the restore is the first thing a frame does and nothing else here had answered yet, so " +
            "a pump that skipped it settled a different duty — or none at all, which is what a pump " +
            "that never touched the cache would report.");
        mirror.StateHash.ShouldBe(
            NetWorlds.SomeHash,
            "and the restore has to reach the MIRROR, not merely read the file. The hash is what the " +
            "first server answer is compared against: a mirror left empty makes that answer look like " +
            "a change and announces a resync for a state the player was already looking at.");
        mirror.Sequence.ShouldBe(
            4,
            "the sequence comes back with it, or the first slow answer to an old command walks the " +
            "screen backwards.");
        mirror.Run.ShouldNotBeNull(
            "and the projections, since a mirror with a hash and nothing to draw is the one thing it " +
            "does not exist for.");
    }

    /// <summary>
    /// 🔒 …and only on the first tick. The store is a cold-start convenience, never a second opinion.
    /// </summary>
    /// <remarks>
    /// The store is moved underneath the pump after it has finished with it. A pump that re-read it
    /// every frame would stamp whatever is on disk over live state sixty times a second — which is
    /// the cache becoming authoritative, the one thing it may never be — and would do a file read
    /// per frame on a handset to do it.
    /// </remarks>
    [Fact]
    public async Task Advance_fills_the_mirror_from_the_store_only_once()
    {
        var cache = new LocalFileCache(_cacheRoot);
        await Store(cache, NetWorlds.SomeHash, sequence: 4);

        var (pump, mirror) = PumpOver(RecordingGameApi.Reachable(), cache);

        RunUntilSettled(pump, SettledAfterAPersist);

        await Store(cache, NetWorlds.AnotherHash, sequence: 9);

        for (var frame = 0; frame < FramesAfterTheStoreMoves; frame++)
        {
            pump.Advance(CancellationToken.None);
        }

        mirror.StateHash.ShouldBe(
            NetWorlds.SomeHash,
            "the mirror took the store's newer contents, so the restore is running on more than the " +
            "first tick. Nothing on disk is news: the ladder owes a resync and the server's answer is " +
            "what may move this.");
    }

    /// <summary>
    /// 🔒 <b>A mirror that moved is written back, so the next cold start opens on it.</b>
    /// </summary>
    [Fact]
    public async Task Advance_persists_the_mirror_once_the_state_moves()
    {
        var cache = new LocalFileCache(_cacheRoot);
        var (pump, mirror) = PumpOver(RecordingGameApi.Reachable(), cache);

        pump.Advance(CancellationToken.None);
        mirror.Apply(NetWorlds.State(NetWorlds.SomeHash, sequence: 6));

        RunUntilSettled(pump, SettledAfterAPersist);

        var stored = new StateMirror();

        (await new MirrorCache(cache).RestoreAsync(stored, CancellationToken.None)).ShouldBeTrue(
            "nothing was written, so every restart after this one opens on a blank screen until the " +
            "network answers — which is the whole of what the mirror buys and it is bought by this " +
            "one duty.");
        stored.StateHash.ShouldBe(
            NetWorlds.SomeHash,
            "and it has to be what the mirror actually holds. A pump that wrote a stale copy would " +
            "hand the next cold start a screen the server had already moved past.");
        stored.Sequence.ShouldBe(
            6,
            "the sequence with it, for the reason the restore case gives: it is what tells a stale " +
            "answer from news.");
    }

    /// <summary>
    /// 🔒 The control: the mirror holds a state and has not moved, so nothing is written.
    /// </summary>
    /// <remarks>
    /// The mirror is deliberately filled first and then given an answer that changes nothing, so
    /// there IS something a careless pump could write: a case over an empty mirror would pass
    /// against a pump that persisted on every single frame, because the cache declines to store an
    /// empty one anyway. What that pump would actually cost is a compress-and-write of two whole
    /// projections sixty times a second on a battery.
    /// </remarks>
    [Fact]
    public async Task Advance_persists_nothing_while_the_mirror_has_not_moved()
    {
        var cache = new LocalFileCache(_cacheRoot);
        var (pump, mirror) = PumpOver(RecordingGameApi.Reachable(), cache);

        mirror.Apply(NetWorlds.State(NetWorlds.SomeHash, sequence: 3));
        mirror.Apply(NetWorlds.State(NetWorlds.SomeHash, sequence: 4));

        RunUntilSettled(pump, SettledAfterAPersist);

        (await new MirrorCache(cache).RestoreAsync(new StateMirror(), CancellationToken.None))
            .ShouldBeFalse(
                "the mirror was written back although the last answer moved nothing. A reconnect " +
                "that finds the server exactly where the client left it is the ordinary case, and " +
                "paying a compress-and-write for it every frame is what turns an idle screen into a " +
                "flat battery.");
    }

    /// <summary>Stores a mirror carrying the given answer, the way the pump would have.</summary>
    private static async Task Store(LocalFileCache cache, string stateHash, long sequence)
    {
        var mirror = new StateMirror();
        mirror.Apply(NetWorlds.State(stateHash, sequence));

        await new MirrorCache(cache).PersistAsync(mirror, CancellationToken.None);
    }

    /// <summary>
    /// Drives frames until the pump has settled <paramref name="count"/> duties.
    /// </summary>
    /// <remarks>
    /// The settled count is the only thing that can say a started task has finished: the pump never
    /// awaits and hands out no task. Spinning on a wall clock instead would make every case here a
    /// timing bet on a build agent.
    /// </remarks>
    private static void RunUntilSettled(ConnectionPump pump, int count)
    {
        for (var frame = 0; frame < FrameBudget && pump.SettledCount < count; frame++)
        {
            pump.Advance(CancellationToken.None);
            Thread.Sleep(FramePause);
        }

        pump.SettledCount.ShouldBeGreaterThanOrEqualTo(
            count,
            $"the pump settled {pump.SettledCount} duties in {FrameBudget} frames and needed {count}, " +
            "so one it started never finished — and everything below would be asserting about work " +
            "that did not happen.");
    }

    /// <summary>A pump and the mirror it drives, over a given store.</summary>
    private (ConnectionPump Pump, StateMirror Mirror) PumpOver(IGameApiPort api, LocalFileCache cache)
    {
        var clock = new ManualClock(StartedAt);
        var mirror = new StateMirror();
        var connection = new ReconnectManager(
            api, mirror, new CommandQueue(CountingIdGenerator.Counting()), clock);
        var session = new SessionOpener(api, new EphemeralDeviceCredentials(), connection);

        return (new ConnectionPump(connection, session, mirror, new MirrorCache(cache), clock), mirror);
    }

    private (ConnectionPump Pump, ReconnectManager Connection, ManualClock Clock) Pump(IGameApiPort api)
    {
        var clock = new ManualClock(StartedAt);
        var mirror = new StateMirror();
        var connection = new ReconnectManager(
            api, mirror, new CommandQueue(CountingIdGenerator.Counting()), clock);
        var session = new SessionOpener(api, new EphemeralDeviceCredentials(), connection);

        return (
            new ConnectionPump(connection, session, mirror, new MirrorCache(new LocalFileCache(_cacheRoot)), clock),
            connection,
            clock);
    }
}
