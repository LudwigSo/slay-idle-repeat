using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Client.Game.Net;

/// <summary>
/// The four duties <see cref="ConnectionPump.Advance"/> can start, named so a settled one is
/// observable rather than merely believed.
/// </summary>
/// <remarks>
/// 🔒 Without a name per duty the mirror's two duties are unobservable: <c>Advance</c> starts a task
/// and returns, so a pump that never touched the mirror or its cache would be indistinguishable
/// from one that did.
/// </remarks>
public enum ConnectionPumpWork
{
    /// <summary>Filling the mirror from the last thing the server said. Once per process.</summary>
    MirrorRestore = 1,

    /// <summary>Opening — or reopening — the account session.</summary>
    SessionOpen = 2,

    /// <summary>One turn of the reconnect ladder.</summary>
    ConnectionPoll = 3,

    /// <summary>Storing what the mirror now holds, so the next cold start has something to draw.</summary>
    MirrorPersist = 4,
}

/// <summary>
/// Drives the reconnect ladder one frame at a time: the thing that turns a composed connection into
/// a live one.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>No <c>async</c> method and no <c>async void</c>, and no continuation on the frame path.</b>
/// This is driven from the engine's per-frame callback, where an exception escaping a void async
/// method is unobservable and fatal. <see cref="Advance"/> starts at most one task, drains the
/// previous one's outcome on the next frame, and never awaits. <see cref="Stop"/> is the one place
/// that cannot wait for a next frame, and it is the one place with a continuation.
/// </para>
/// <para>
/// 🔴 <c>ReconnectManager.Follow</c> is deliberately never called from here. The only run id this
/// arm holds is the local host's, which the server has never minted — pointing the ladder at it
/// would turn every resync into a refusal. It acquires a caller when the presenters submit through
/// the wire.
/// </para>
/// </remarks>
public sealed class ConnectionPump
{
    private readonly ReconnectManager _connection;
    private readonly SessionOpener _session;
    private readonly StateMirror _mirror;
    private readonly MirrorCache _cache;
    private readonly IClockPort _clock;

    private Task? _inFlight;
    private ConnectionPumpWork _inFlightWork;

    private bool _mirrorRestoreStarted;
    private string? _persistedStateHash;

    /// <summary>Wires the pump over the ladder, the session, the mirror and its cache.</summary>
    /// <param name="connection">The ladder being climbed.</param>
    /// <param name="session">How a session is opened and reopened.</param>
    /// <param name="mirror">The local copy the pump persists.</param>
    /// <param name="cache">Where the mirror is stored between runs.</param>
    /// <param name="clock">The only sanctioned source of "now".</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public ConnectionPump(
        ReconnectManager connection,
        SessionOpener session,
        StateMirror mirror,
        MirrorCache cache,
        IClockPort clock)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(mirror);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(clock);

        _connection = connection;
        _session = session;
        _mirror = mirror;
        _cache = cache;
        _clock = clock;
    }

    /// <summary>Whether <see cref="Stop"/> has been called.</summary>
    public bool Stopped { get; private set; }

    /// <summary>The last fault a driven task produced, or null. Observed, never swallowed.</summary>
    public Exception? LastFault { get; private set; }

    /// <summary>How many driven tasks have faulted.</summary>
    public int FaultCount { get; private set; }

    /// <summary>How many driven tasks have finished, faulted or not.</summary>
    /// <remarks>
    /// 🔒 The pump's duties are started and never awaited, so this and
    /// <see cref="LastSettledWork"/> are the only way anything outside can tell a duty that ran
    /// from one that was skipped — which the mirror's restore and persist otherwise are not.
    /// </remarks>
    public int SettledCount { get; private set; }

    /// <summary>Which duty the most recently settled task was, or null before one settled.</summary>
    public ConnectionPumpWork? LastSettledWork { get; private set; }

    /// <summary>One frame. Never awaits, never throws.</summary>
    /// <remarks>
    /// The duties are tried in order and each is started at most once per frame, over one in-flight
    /// slot: a duty whose task is still running holds the slot and the frame ends there, and a duty
    /// that completed inside the frame settles at once rather than a frame later. That is what
    /// makes a cold start reach the server on its first frame instead of its third.
    /// </remarks>
    /// <param name="ct">The application's shutdown token.</param>
    public void Advance(CancellationToken ct)
    {
        if (Stopped || ct.IsCancellationRequested)
        {
            return;
        }

        if (!SlotIsFree())
        {
            return;
        }

        if (!_mirrorRestoreStarted)
        {
            _mirrorRestoreStarted = true;

            Start(ConnectionPumpWork.MirrorRestore, _cache.RestoreAsync(_mirror, ct));

            if (!SlotIsFree())
            {
                return;
            }
        }

        StartConnectionWork(ct);

        if (!SlotIsFree() || !PersistIsOwed())
        {
            return;
        }

        _persistedStateHash = _mirror.StateHash;

        Start(ConnectionPumpWork.MirrorPersist, _cache.PersistAsync(_mirror, ct));
    }

    /// <summary>Stops the pump for good.</summary>
    /// <remarks>
    /// 🔒 A task still in flight here belongs to a graph being torn down under it — the transport it
    /// is inside is disposed moments later — so its fault is a clean exit seen from this side and is
    /// not counted as an error. It is still <b>read</b>: nothing calls <see cref="Advance"/> again,
    /// so the frame drain will never reach it, and a fault left unread is a fault dropped in silence.
    /// </remarks>
    public void Stop()
    {
        Stopped = true;

        if (_inFlight is not { } running)
        {
            return;
        }

        _inFlight = null;

        running.ContinueWith(
            static settled => _ = settled.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>Reopening while shut, or one turn of the ladder while open.</summary>
    private void StartConnectionWork(CancellationToken ct)
    {
        if (!_session.IsOpen && _clock.UtcNow >= _connection.NextAttemptAtUtc)
        {
            Start(ConnectionPumpWork.SessionOpen, _session.OpenAsync(ct));

            return;
        }

        Start(ConnectionPumpWork.ConnectionPoll, _connection.PollAsync(ct));
    }

    /// <summary>Whether the mirror holds something the store does not.</summary>
    private bool PersistIsOwed() =>
        _mirror.LastChanged &&
        !string.Equals(_persistedStateHash, _mirror.StateHash, StringComparison.Ordinal);

    private void Start(ConnectionPumpWork work, Task started)
    {
        _inFlight = started;
        _inFlightWork = work;
    }

    /// <summary>Settles a finished task and answers whether the one slot is now free.</summary>
    private bool SlotIsFree()
    {
        if (_inFlight is not { } running)
        {
            return true;
        }

        if (!running.IsCompleted)
        {
            return false;
        }

        _inFlight = null;

        SettledCount++;
        LastSettledWork = _inFlightWork;

        if (running.IsFaulted)
        {
            FaultCount++;
            LastFault = running.Exception?.InnerException ?? running.Exception;
        }

        return true;
    }
}
