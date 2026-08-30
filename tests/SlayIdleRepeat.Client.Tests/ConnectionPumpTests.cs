using Shouldly;
using SlayIdleRepeat.Adapters.Cache.LocalFile;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Client.Game.Net;
using Xunit;

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
