using System.Text.Json;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Contracts;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Game.Net;

/// <summary>
/// Keeps the connection fact, retries on the authored ladder, and drains what the player queued
/// while the server was out of reach.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>A connection loss is a recorded fact, never an exception out of <see cref="PollAsync"/>.</b>
/// This is driven from a per-frame engine callback, where nothing catches anything: a manager that
/// let a transport failure escape would turn a dropped packet into a crashed game. Both failures the
/// port can raise are caught, turned into state, and left for the presenter to read.
/// </para>
/// <para>
/// 🔒 <b>Time comes from the injected clock only.</b> The ladder is meaningless if "how long since
/// the last attempt" is measured with a different clock from the one a test can move, and the
/// ambient clocks are banned outright.
/// </para>
/// <para>
/// ⚠️ It talks to the server only when there is a reason to: while connected with an empty queue it
/// sends nothing at all. A connection loss is discovered on the next command a player issues, which
/// is what makes the whole arrangement free when a player is idle.
/// </para>
/// </remarks>
public sealed class ReconnectManager
{
    /// <summary>
    /// How long a failing connection must go on failing before the pill is drawn — a spec-locked
    /// presentation constant, not a balance tunable.
    /// </summary>
    /// <remarks>
    /// 🔒 The threshold is the whole reason <see cref="ConnectionState.Waiting"/> exists. A blink of
    /// failure that recovers inside it must leave a player's screen untouched; without it every
    /// dropped packet flashes a status pill at somebody on a handset with ordinary reception.
    /// </remarks>
    public static readonly TimeSpan PillThreshold = TimeSpan.FromSeconds(2);

    /// <summary>The interval the ladder settles at, and stays at, indefinitely.</summary>
    public static readonly TimeSpan SteadyRetryInterval = TimeSpan.FromSeconds(10);

    /// <summary>The authored ladder, verbatim: 0.5 s, 1 s, 2 s, 4 s, 8 s, then <see cref="SteadyRetryInterval"/>.</summary>
    /// <remarks>
    /// 🔒 It stops doubling rather than backing off forever. A ladder that kept doubling would have a
    /// player who put the phone down for a minute waiting most of another minute after the network
    /// came back, with the screen dimmed the whole time — the ceiling is what bounds that wait.
    /// </remarks>
    private static readonly TimeSpan[] RisingDelays =
    [
        TimeSpan.FromSeconds(0.5),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
    ];

    private readonly IGameApiPort _api;
    private readonly StateMirror _mirror;
    private readonly CommandQueue _queue;
    private readonly IClockPort _clock;

    private int _consecutiveFailures;
    private DateTimeOffset _firstFailureAtUtc;
    private DateTimeOffset _nextAttemptAtUtc;
    private bool _resyncOwed = true;

    /// <summary>Builds the manager over the wire seam, the mirror it fills and the queue it drains.</summary>
    /// <param name="api">The wire seam. The only thing here that touches a network.</param>
    /// <param name="mirror">The local copy a resync writes into.</param>
    /// <param name="queue">The commands waiting to be sent.</param>
    /// <param name="clock">The only sanctioned source of "now".</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public ReconnectManager(IGameApiPort api, StateMirror mirror, CommandQueue queue, IClockPort clock)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(mirror);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(clock);

        _api = api;
        _mirror = mirror;
        _queue = queue;
        _clock = clock;
    }

    /// <summary>The connection fact, as of the last <see cref="PollAsync"/>.</summary>
    public ConnectionState State { get; private set; } = ConnectionState.Connected;

    /// <summary>The run a resync reads, or null when no run is open.</summary>
    public RunId? Run { get; private set; }

    /// <summary>Whether the most recent resync actually moved the mirror.</summary>
    /// <remarks>
    /// 🔒 This — not "a resync happened" — is what decides whether a player is told anything. A
    /// reconnect that finds the server exactly where the client left it changed nothing a player
    /// could see, and announcing it would be announcing the network rather than the game.
    /// </remarks>
    public bool LastResyncChangedState { get; private set; }

    /// <summary>How many attempts in a row have failed. Zero while connected.</summary>
    public int ConsecutiveFailures => _consecutiveFailures;

    /// <summary>The status the server last refused with, or null when it has refused nothing.</summary>
    /// <remarks>
    /// Recorded rather than thrown, for <see cref="PollAsync"/>'s reason, and kept apart from the
    /// connection fact for the port's: a refusal means the server understood and said no, so
    /// retrying it unchanged cannot help and starting a backoff over it would be waiting for a
    /// network that is already there.
    /// </remarks>
    public int? LastRefusalStatusCode { get; private set; }

    /// <summary>The next instant an attempt is due, the server's own Retry-After included.</summary>
    /// <remarks>
    /// Read by whatever drives this per frame, so the ladder is stated once here rather than
    /// recomputed beside it — two schedules would be two answers to "may I try again yet".
    /// </remarks>
    public DateTimeOffset NextAttemptAtUtc => _nextAttemptAtUtc;

    /// <summary>Records an exchange made outside this manager that reached the server.</summary>
    /// <remarks>
    /// 🔒 Without this the ladder has no input at all on an arm whose presenters do not yet submit
    /// through the wire: an attempt is only due for a queued command, a followed run or a prior
    /// failure, and the first two never happen there.
    /// </remarks>
    public void RecordReached() => MarkReached();

    /// <summary>…and one that did not.</summary>
    /// <param name="failure">The transport failure, whose Retry-After wins over the ladder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="failure"/> is null.</exception>
    public void RecordLost(GameApiUnavailableException failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        RecordConnectionLoss(failure);
    }

    /// <summary>The delay before attempt <paramref name="consecutiveFailures"/> + 1, off the authored ladder.</summary>
    /// <param name="consecutiveFailures">How many attempts have failed in a row. One or more.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="consecutiveFailures"/> is below one.</exception>
    public static TimeSpan DelayAfterFailure(int consecutiveFailures)
    {
        if (consecutiveFailures < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(consecutiveFailures),
                consecutiveFailures,
                "The ladder describes the wait AFTER a failure, so there is no delay for a zeroth one.");
        }

        return consecutiveFailures <= RisingDelays.Length
            ? RisingDelays[consecutiveFailures - 1]
            : SteadyRetryInterval;
    }

    /// <summary>Points the resync at a run, or at none.</summary>
    /// <param name="run">The run now open, or null once it is over.</param>
    /// <remarks>
    /// A resync is owed after this, even while connected: a run that has just been opened has never
    /// been read, and the mirror holds whatever the previous one left.
    /// <para>
    /// 🔴 <b>Nothing in production calls this yet, and that is a stated absence rather than an
    /// oversight.</b> The only run id the composed client holds is the in-process host's, which no
    /// server ever minted, so pointing the ladder at it would turn every resync into a refusal.
    /// It acquires a caller when the presenters submit through the wire and the server mints the run.
    /// </para>
    /// </remarks>
    public void Follow(RunId? run)
    {
        Run = run;
        _resyncOwed = true;
    }

    /// <summary>
    /// Advances the manager: takes the connection's temperature, and makes one attempt if one is due.
    /// </summary>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// 🔒 Not an <c>async</c> method, and that is the point rather than a style choice: this is
    /// called from the engine's per-frame callback, and the not-due path has to cost a clock read
    /// and a comparison. Returning the cached completed task keeps that path free of an awaiter, a
    /// state machine box and a task allocation.
    /// </remarks>
    public Task PollAsync(CancellationToken ct)
    {
        var now = _clock.UtcNow;

        RefreshState(now);

        return IsAttemptDue(now) ? AttemptAsync(ct) : Task.CompletedTask;
    }

    private bool IsAttemptDue(DateTimeOffset now)
    {
        // A resync owed on no run is not something to do — it is a debt that will be discharged the
        // moment a run is followed, and treating it as work would make every idle frame allocate.
        var thereIsSomethingToDo =
            _queue.PendingCount > 0 || _consecutiveFailures > 0 || (_resyncOwed && Run is not null);

        return thereIsSomethingToDo && now >= _nextAttemptAtUtc;
    }

    private void RefreshState(DateTimeOffset now)
    {
        if (_consecutiveFailures == 0)
        {
            State = ConnectionState.Connected;

            return;
        }

        State = now - _firstFailureAtUtc >= PillThreshold
            ? ConnectionState.Reconnecting
            : ConnectionState.Waiting;
    }

    private async Task AttemptAsync(CancellationToken ct)
    {
        if (_resyncOwed && Run is { } run)
        {
            try
            {
                // Awaited inside the guard rather than merely called inside it: the port's calls are
                // async, so a transport failure arrives as a faulted task and a try around the call
                // alone would never catch it.
                var state = await _api.FetchRunStateAsync(run, _mirror.Sequence, ct).ConfigureAwait(false);

                LastResyncChangedState = _mirror.Apply(state);
                _resyncOwed = false;

                MarkReached();
            }
            catch (GameApiUnavailableException failure)
            {
                RecordConnectionLoss(failure);

                return;
            }
            catch (GameApiRefusedException refusal)
            {
                // The run is unknown or belongs to someone else, and the two are indistinguishable by
                // design. Nothing about repeating the read can change either, so the debt is cleared
                // rather than retried forever, and the connection itself is fine.
                _resyncOwed = false;
                LastRefusalStatusCode = refusal.StatusCode;

                MarkReached();
            }
        }

        while (_queue.Head is { } head)
        {
            WireCommandResult result;

            try
            {
                result = await _api.SendCommandAsync(head.Run, EnvelopeFor(head), ct).ConfigureAwait(false);
            }
            catch (GameApiUnavailableException failure)
            {
                // The command stays queued with its id and sequence untouched, which is what makes
                // the next attempt a retry the server can recognise rather than a second command.
                RecordConnectionLoss(failure);

                return;
            }
            catch (GameApiRefusedException refusal)
            {
                // The server could not read the request at all. Sending it again unchanged would be
                // refused again forever, so it leaves the queue — and the queue's own order rule is
                // why it is dropped from the head rather than skipped over.
                LastRefusalStatusCode = refusal.StatusCode;
                _queue.Acknowledge(head.Id);

                MarkReached();

                continue;
            }

            _mirror.Apply(result);
            _queue.Acknowledge(head.Id);

            MarkReached();
        }
    }

    private static CommandEnvelope EnvelopeFor(PendingCommand pending)
    {
        // Cloned off the document because a JsonElement is a window onto a buffer the document owns,
        // and the envelope outlives this method.
        using var payload = JsonDocument.Parse(WireCommandCodec.EncodePayload(pending.Command));

        return new CommandEnvelope(
            WireProtocol.PROTOCOL_VERSION,
            pending.Id,
            pending.Sequence,
            WireCommandCodec.WireNameOf(pending.Command),
            payload.RootElement.Clone());
    }

    private void MarkReached()
    {
        _consecutiveFailures = 0;
        _nextAttemptAtUtc = default;
        State = ConnectionState.Connected;
    }

    private void RecordConnectionLoss(GameApiUnavailableException failure)
    {
        var now = _clock.UtcNow;

        if (_consecutiveFailures == 0)
        {
            _firstFailureAtUtc = now;
        }

        _consecutiveFailures++;

        // The server's own Retry-After wins over the ladder when it named one: the ladder is this
        // client guessing, and a 429 is the server saying.
        _nextAttemptAtUtc = now + (failure.RetryAfter ?? DelayAfterFailure(_consecutiveFailures));

        // A connection that dropped has to be re-read before it can be trusted again, whatever it
        // was showing before it dropped.
        _resyncOwed = true;

        RefreshState(now);
    }
}
