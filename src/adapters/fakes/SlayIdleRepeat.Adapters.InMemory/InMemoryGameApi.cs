using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>
/// The scripted fake for <see cref="IGameApiPort"/>: one account, one run, and a transport a
/// scenario can take away.
/// </summary>
/// <remarks>
/// <para>
/// It is handed the two projections it serves rather than building them, and that is the honest
/// shape: a projection is what the <em>server</em> decided, and a fake that manufactured one would
/// be inventing state no client could ever have been sent. The idempotency record is real — a
/// repeated command id replays the stored outcome byte for byte, which is the guarantee the whole
/// resend-after-a-drop design rests on.
/// </para>
/// <para>
/// 🔒 <b>None of the port's clauses can be switched off.</b> A refusal is a returned outcome and
/// never an exception; an unreachable transport is
/// <see cref="GameApiUnavailableException"/> and never a rejection; a run this account does not own
/// is indistinguishable from one that does not exist. Same construction, and the same reason, as
/// <see cref="InMemoryPlatformInfo"/> refusing a locale spelled the host's way.
/// </para>
/// <para>
/// ⚠️ It runs no rules. An accepted command hands back the same projections it was built with, so a
/// scenario about what a command <em>does</em> belongs against the domain, not here.
/// </para>
/// </remarks>
public sealed class InMemoryGameApi : IGameApiPort
{
    /// <summary>The device a freshly constructed api answers a registration with.</summary>
    public const string StartDeviceId = "DEVICE_inmemoryapi";

    /// <summary>The secret that device is minted with — the only one this api authenticates.</summary>
    public const string StartDeviceSecret = "SECRET_inmemoryapi";

    /// <summary>The bearer token an authenticated session hands out.</summary>
    public const string StartAccessToken = "ACCESS_inmemoryapi";

    /// <summary>The rotation token that session hands out.</summary>
    public const string StartRefreshToken = "REFRESH_inmemoryapi";

    /// <summary>How long the access token is accepted for. An hour, as a plausible deployment would.</summary>
    public const long StartAccessLifetimeSeconds = 3600;

    /// <summary>
    /// When a client should renew — deliberately well inside <see cref="StartAccessLifetimeSeconds"/>,
    /// because a renewal horizon at or past expiry is a renewal that always arrives too late.
    /// </summary>
    public const long StartRenewAfterSeconds = 2700;

    /// <summary>How long the token family may be rotated within. Thirty days.</summary>
    public const long StartRefreshLifetimeSeconds = 2_592_000;

    /// <summary>The state hash every answer carries, shaped the way the canonical writer shapes one.</summary>
    public const string StartStateHash = "fnv1a:0123456789abcdef";

    private readonly PlayerWireProjection _profile;
    private readonly RunWireProjection _run;
    private readonly Dictionary<CommandId, WireCommandResult> _replays = new();
    private readonly List<WireCommandResult> _answered = [];

    private bool _authenticated;
    private bool _unavailable;
    private bool _resyncFull;
    private TimeSpan? _retryAfter;
    private RejectionReason? _refusal;
    private long _lastSequence;

    /// <summary>Builds an api over the two projections it serves.</summary>
    /// <param name="profile">The player projection every answer carries.</param>
    /// <param name="run">The run projection, and the one run this api knows.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public InMemoryGameApi(PlayerWireProjection profile, RunWireProjection run)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(run);

        _profile = profile;
        _run = run;
    }

    /// <summary>The one credential this api authenticates.</summary>
    public WireCredentials Credentials => new(StartDeviceId, StartDeviceSecret);

    /// <summary>The one run this api knows.</summary>
    public RunId Run => _run.Id;

    /// <summary>Takes the transport away: every call throws until <see cref="MakeAvailable"/>.</summary>
    /// <param name="retryAfter">What the server asks to be left alone for, or <c>null</c> when it names no interval.</param>
    /// <returns>This api, so a scenario reads as one statement.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="retryAfter"/> is negative. A <c>Retry-After</c> in the past is not something
    /// any transport reports, and a caller sleeping for it would not sleep at all.
    /// </exception>
    public InMemoryGameApi MakeUnavailable(TimeSpan? retryAfter = null)
    {
        if (retryAfter is { } interval && interval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryAfter),
                interval,
                "a negative Retry-After is an interval no server sends and no backoff can honour. A " +
                "fake that accepted it would let a reconnect loop be written against a wait that " +
                "cannot happen.");
        }

        _unavailable = true;
        _retryAfter = retryAfter;
        return this;
    }

    /// <summary>Gives the transport back — the state a freshly constructed api is in.</summary>
    /// <returns>This api, so a scenario reads as one statement.</returns>
    public InMemoryGameApi MakeAvailable()
    {
        _unavailable = false;
        _retryAfter = null;
        return this;
    }

    /// <summary>Refuses every command from now on with <paramref name="reason"/>, on HTTP 200 as the server does.</summary>
    /// <param name="reason">Why. Any reason either tier produces.</param>
    /// <returns>This api, so a scenario reads as one statement.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="reason"/> is not a declared reason.</exception>
    public InMemoryGameApi RefuseEveryCommand(RejectionReason reason)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "RejectionReason's numeric values are wire values and its member set is the whole " +
                "catalogue, so a value outside it is one no server can send. A fake that accepted it " +
                "would let a caller switch on a rejection that cannot arrive.");
        }

        _refusal = reason;
        return this;
    }

    /// <summary>Accepts every command from now on — the state a freshly constructed api is in.</summary>
    /// <returns>This api, so a scenario reads as one statement.</returns>
    public InMemoryGameApi AcceptEveryCommand()
    {
        _refusal = null;
        return this;
    }

    /// <summary>Marks every later state read as one the client cannot resume incrementally.</summary>
    /// <returns>This api, so a scenario reads as one statement.</returns>
    public InMemoryGameApi RequireFullResync()
    {
        _resyncFull = true;
        return this;
    }

    /// <inheritdoc/>
    public Task<WireDeviceRegistration> RegisterDeviceAsync(string? displayName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        RequireReachable();

        // An absent name takes the server's default; a supplied blank one is a name the caller chose
        // and is refused like any other the filter will not issue.
        if (displayName is not null && string.IsNullOrWhiteSpace(displayName))
        {
            throw new GameApiRefusedException(
                400,
                "a blank display name is not a name. The server's own filter decides this one, and " +
                "retrying the same request cannot change its mind.");
        }

        return Task.FromResult(
            new WireDeviceRegistration(
                StartDeviceId, StartDeviceSecret, _profile.Id, displayName ?? _profile.DisplayName));
    }

    /// <inheritdoc/>
    public Task<WireSession> AuthenticateAsync(WireCredentials credentials, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ct.ThrowIfCancellationRequested();
        RequireReachable();

        if (credentials != Credentials)
        {
            throw new GameApiRefusedException(
                401,
                "an unknown device and a wrong secret are the same answer here, as they are on the " +
                "server: telling them apart is telling a caller which device ids exist.");
        }

        _authenticated = true;

        return Task.FromResult(
            new WireSession(
                _profile.Id,
                StartAccessToken,
                StartAccessLifetimeSeconds,
                StartRenewAfterSeconds,
                StartRefreshToken,
                StartRefreshLifetimeSeconds));
    }

    /// <inheritdoc/>
    public Task<WireCommandResult> SendCommandAsync(
        RunId? run, CommandEnvelope envelope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ct.ThrowIfCancellationRequested();
        RequireReachable();
        RequireSession();

        if (_replays.TryGetValue(envelope.CommandId, out var stored))
        {
            return Task.FromResult(stored);
        }

        var answer = Answer(run, envelope);

        _replays[envelope.CommandId] = answer;
        _answered.Add(answer);
        _lastSequence = Math.Max(_lastSequence, envelope.Sequence);

        return Task.FromResult(answer);
    }

    /// <inheritdoc/>
    public Task<WireRunState> FetchRunStateAsync(RunId run, long sinceSequence, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        RequireReachable();
        RequireSession();

        if (sinceSequence < 0)
        {
            throw new GameApiRefusedException(
                400, "sinceSequence is a count of answered commands, and there is no negative one.");
        }

        if (run != _run.Id)
        {
            throw new GameApiRefusedException(
                404,
                "a run this account does not own answers exactly as one that does not exist, or the " +
                "answer confirms somebody else's run id to whoever guessed it.");
        }

        return Task.FromResult(
            new WireRunState(
                _run.Id,
                _answered.Count == 0 ? null : _lastSequence,
                sinceSequence,
                _profile,
                _run,
                StartStateHash,
                _answered.Where(a => a.Sequence > sinceSequence).ToArray(),
                _resyncFull));
    }

    private WireCommandResult Answer(RunId? run, CommandEnvelope envelope)
    {
        if (run is { } addressed && addressed != _run.Id)
        {
            // In the envelope on HTTP 200, never a 404: the server understood the request and
            // answered it, which is what the reason exists to say.
            return Rejected(envelope.Sequence, RejectionReason.RUN_NOT_FOUND);
        }

        if (_refusal is { } reason)
        {
            return Rejected(envelope.Sequence, reason);
        }

        return new WireCommandResult(
            envelope.Sequence,
            Accepted: true,
            Rejection: null,
            run,
            _profile,
            run is null ? null : _run,
            _run.RngStreamPositions,
            BattleSeed: null,
            StartStateHash);
    }

    private static WireCommandResult Rejected(long sequence, RejectionReason reason) =>
        new(sequence, Accepted: false, reason, RunId: null, Profile: null, Run: null,
            RngStreamStates: null, BattleSeed: null, StartStateHash);

    private void RequireReachable()
    {
        if (_unavailable)
        {
            throw new GameApiUnavailableException(
                "the transport cannot answer at all, which is the state this fake was put in.",
                _retryAfter);
        }
    }

    private void RequireSession()
    {
        if (!_authenticated)
        {
            throw new GameApiRefusedException(
                401,
                "every command and state route needs a bearer token, so an api nothing has " +
                "authenticated cannot answer one. A fake that served them anyway would let a caller " +
                "be written against a session it never opened.");
        }
    }
}
