using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// A hand-written <see cref="IGameApiPort"/> for the reconnect machinery, recording what it was
/// asked rather than only what it answered.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written rather than the in-memory adapter, because this suite does not reference the
/// adapter projects and <c>ProjectFileTests</c> governs who may — the presenters and the client's
/// own collaborators are meant to be provable with nothing but the client assembly on the path.
/// </para>
/// <para>
/// 🔒 It records the <em>envelopes</em>, not just a call count. The two facts this fixture exists to
/// expose are both invisible in a return value: that a retry carried the same command id and the
/// same sequence as the attempt before it, and that a resync asked from the sequence the mirror
/// actually holds. Both are properties of the request.
/// </para>
/// <para>
/// The two failure budgets are separate and unavailability wins while both are set, mirroring what
/// the port's own contract says: a transport that cannot answer never gets far enough to be refused.
/// </para>
/// </remarks>
internal sealed class RecordingGameApi : IGameApiPort
{
    /// <summary>The device the scripted server mints, and the secret that reopens it.</summary>
    internal const string MintedDeviceId = "DEVICE_recording_2b7e";

    /// <summary>The secret the scripted registration issues — asserted against, never expected in output.</summary>
    internal const string MintedDeviceSecret = "SECRET_recording_5a13";

    private const string MintedDisplayName = "Recorded Hero";

    private readonly List<CommandEnvelope> _sent = [];
    private readonly List<RunId?> _sentTo = [];
    private readonly List<long> _fetchedFrom = [];
    private readonly List<WireCredentials> _presented = [];

    private WireRunState? _state;
    private WireCommandResult? _commandResult;

    private int _unavailableCallsLeft;
    private TimeSpan? _retryAfter;

    private int _refusedCallsLeft;
    private int _refusalStatusCode;

    private TaskCompletionSource? _gate;

    private RecordingGameApi()
    {
    }

    /// <summary>An api that answers every call.</summary>
    internal static RecordingGameApi Reachable() => new();

    /// <summary>Every envelope submitted, in the order it was submitted.</summary>
    internal IReadOnlyList<CommandEnvelope> Sent => _sent;

    /// <summary>The run each submission was addressed to, in order. Null for the player scope.</summary>
    internal IReadOnlyList<RunId?> SentTo => _sentTo;

    /// <summary>Every <c>sinceSequence</c> a state read asked from, in order.</summary>
    internal IReadOnlyList<long> FetchedFrom => _fetchedFrom;

    /// <summary>How many state reads reached this api, successful or not.</summary>
    internal int FetchAttempts { get; private set; }

    /// <summary>How many command submissions reached this api, successful or not.</summary>
    internal int SendAttempts { get; private set; }

    /// <summary>How many device registrations reached this api, successful or not.</summary>
    internal int RegisterAttempts { get; private set; }

    /// <summary>How many sign-ins reached this api, successful or not.</summary>
    internal int AuthenticateAttempts { get; private set; }

    /// <summary>Every credential a sign-in presented, in order.</summary>
    internal IReadOnlyList<WireCredentials> Presented => _presented;

    /// <summary>
    /// Makes the next <paramref name="calls"/> calls fail as an unreachable transport.
    /// </summary>
    /// <param name="calls">How many. <see cref="int.MaxValue"/> for a network that never comes back.</param>
    /// <param name="retryAfter">The interval the server asked for, when the case is about one.</param>
    internal RecordingGameApi UnreachableFor(int calls, TimeSpan? retryAfter = null)
    {
        _unavailableCallsLeft = calls;
        _retryAfter = retryAfter;

        return this;
    }

    /// <summary>
    /// Makes the next <paramref name="calls"/> calls come back refused — understood and said no.
    /// </summary>
    /// <remarks>
    /// Told apart from <see cref="UnreachableFor"/> on purpose, because that distinction is the one
    /// the whole backoff hangs off: a refusal is not a connection problem and must not start one.
    /// </remarks>
    /// <param name="calls">How many.</param>
    /// <param name="statusCode">The status the server refused with.</param>
    internal RecordingGameApi RefusingFor(int calls, int statusCode)
    {
        _refusedCallsLeft = calls;
        _refusalStatusCode = statusCode;

        return this;
    }

    /// <summary>
    /// Makes every sign-in hang for the rest of the case, answering nothing.
    /// </summary>
    /// <remarks>
    /// 🔒 The only way to observe "one attempt is already in flight". Every other answer here
    /// completes before the call returns, and against a synchronous api a second attempt is not a
    /// missing guard — it is a previous attempt that finished.
    /// </remarks>
    internal RecordingGameApi Gated()
    {
        _gate = new TaskCompletionSource();

        return this;
    }

    /// <summary>Makes every state read answer with the given state.</summary>
    internal RecordingGameApi Answering(WireRunState state)
    {
        _state = state;

        return this;
    }

    /// <summary>Makes every accepted submission answer with the given outcome.</summary>
    internal RecordingGameApi AnsweringCommandsWith(WireCommandResult result)
    {
        _commandResult = result;

        return this;
    }

    /// <inheritdoc/>
    public Task<WireDeviceRegistration> RegisterDeviceAsync(string? displayName, CancellationToken ct)
    {
        RegisterAttempts++;

        return NextFailure() is { } failure
            ? Task.FromException<WireDeviceRegistration>(failure)
            : Gate(
                new WireDeviceRegistration(
                    MintedDeviceId, MintedDeviceSecret, NetWorlds.Player, MintedDisplayName));
    }

    /// <inheritdoc/>
    public Task<WireSession> AuthenticateAsync(WireCredentials credentials, CancellationToken ct)
    {
        AuthenticateAttempts++;
        _presented.Add(credentials);

        return NextFailure() is { } failure
            ? Task.FromException<WireSession>(failure)
            : Gate(
                new WireSession(
                    NetWorlds.Player,
                    AccessToken: "ACCESS_recording",
                    AccessExpiresInSeconds: 3600,
                    RenewAfterSeconds: 2700,
                    RefreshToken: "REFRESH_recording",
                    RefreshExpiresInSeconds: 2_592_000));
    }

    /// <inheritdoc/>
    public Task<WireCommandResult> SendCommandAsync(
        RunId? run, CommandEnvelope envelope, CancellationToken ct)
    {
        SendAttempts++;
        _sent.Add(envelope);
        _sentTo.Add(run);

        if (NextFailure() is { } failure)
        {
            return Task.FromException<WireCommandResult>(failure);
        }

        return Task.FromResult(
            _commandResult ?? throw new NotSupportedException(
                "This api was not told what an accepted command answers with. Call " +
                nameof(AnsweringCommandsWith) + " in the arrangement."));
    }

    /// <inheritdoc/>
    public Task<WireRunState> FetchRunStateAsync(RunId run, long sinceSequence, CancellationToken ct)
    {
        FetchAttempts++;
        _fetchedFrom.Add(sinceSequence);

        if (NextFailure() is { } failure)
        {
            return Task.FromException<WireRunState>(failure);
        }

        return Task.FromResult(
            _state ?? throw new NotSupportedException(
                "This api was not told what a state read answers with. Call " +
                nameof(Answering) + " in the arrangement."));
    }

    /// <summary>Answers now, or once the gate opens.</summary>
    private Task<T> Gate<T>(T answer) =>
        _gate is { } gate
            ? gate.Task.ContinueWith(
                _ => answer,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default)
            : Task.FromResult(answer);

    private Exception? NextFailure()
    {
        if (_unavailableCallsLeft > 0)
        {
            _unavailableCallsLeft--;

            return new GameApiUnavailableException("The scripted transport could not answer.", _retryAfter);
        }

        if (_refusedCallsLeft > 0)
        {
            _refusedCallsLeft--;

            return new GameApiRefusedException(_refusalStatusCode, "The scripted server said no.");
        }

        return null;
    }
}
