using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Adapters.Api.Http;

/// <summary>
/// <see cref="IGameApiPort"/> over the game server's HTTP routes, hand-rolled over
/// <see cref="HttpClient"/> — the port boundary is the isolation, so no vendor package is pinned.
/// </summary>
/// <remarks>
/// <para>
/// The handler is injected rather than an <see cref="HttpClient"/>, so every route, header and
/// status this adapter decides on is observable without a server, a socket or a container. The
/// client is built here and the handler is left to its owner.
/// </para>
/// <para>
/// 🔒 <b>The three answers this adapter distinguishes.</b> HTTP 200 is an answer — accepted or
/// rejected in the envelope — and never an exception. A 429 or anything from 500 up is the
/// connection being unusable for now, which the caller retries. A 400, 403 or 404 is the server
/// refusing this request, which retrying unchanged cannot fix. Collapsing any two of those would
/// make the caller's backoff either useless or infinite.
/// </para>
/// <para>
/// 🔒 <b>Renewal is silent and happens at most once.</b> A 401 costs one refresh and one repeat of
/// the identical request — the same command id, so the server's idempotency record answers rather
/// than the command running twice. If the renewal itself does not land the caller is told the
/// connection is unavailable; a player is never shown anything about a token.
/// </para>
/// </remarks>
public sealed class HttpGameApi : IGameApiPort, IDisposable
{
    private const string DeviceRoute = "auth/device";
    private const string SessionRoute = "auth/session";
    private const string RefreshRoute = "auth/refresh";
    private const string PlayerCommandRoute = "player/command";
    private const string RunRoutePrefix = "run/";

    private readonly HttpClient _client;
    private readonly SemaphoreSlim _renewalGate = new(1, 1);

    private string? _accessToken;
    private string? _refreshToken;
    private WireCredentials? _credentials;

    /// <summary>Builds the adapter over its deployment configuration and the transport it talks through.</summary>
    /// <param name="options">Which server, and how long to wait.</param>
    /// <param name="handler">The transport. Owned by the caller.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public HttpGameApi(HttpGameApiOptions options, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(handler);

        // 🔒 Refused rather than passed on. HttpClient reads a negative TimeSpan as
        // Timeout.InfiniteTimeSpan, so the tightest-looking spelling of the setting — a negative
        // millisecond count from a mistyped binding — turns the documented budget into NO timeout
        // at all: a hung request never resolves, the ladder is never told, and the connection
        // indicator that this budget exists to feed sits on "connected" for ever.
        if (options.RequestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                "RequestTimeout is at or below zero. HttpClient reads that as no timeout at all, so " +
                "a request that hangs never reports the connection lost.",
                nameof(options));
        }

        if (options.BaseAddress is not { IsAbsoluteUri: true })
        {
            throw new ArgumentException(
                "BaseAddress must be an absolute URI; every route here is resolved relative to it.",
                nameof(options));
        }

        _client = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = options.BaseAddress,
            Timeout = options.RequestTimeout,
        };
    }

    /// <inheritdoc/>
    public async Task<WireDeviceRegistration> RegisterDeviceAsync(string? displayName, CancellationToken ct)
    {
        // An absent name and a supplied one are different requests to the server's name filter, so
        // the member is omitted rather than sent as null.
        var body = displayName is null ? "{}" : JsonSerializer.Serialize(new { displayName });

        var answer = await SendAsync(
                HttpMethod.Post, DeviceRoute, body, authenticated: false, renewable: false, ct)
            .ConfigureAwait(false);

        return Read(() => WireJson.ParseDeviceRegistration(answer));
    }

    /// <inheritdoc/>
    public async Task<WireSession> AuthenticateAsync(WireCredentials credentials, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        var answer = await SendAsync(
                HttpMethod.Post, SessionRoute, SessionBody(credentials),
                authenticated: false, renewable: false, ct)
            .ConfigureAwait(false);

        var session = Read(() => WireJson.ParseSession(answer));

        _credentials = credentials;
        Adopt(session);

        return session;
    }

    /// <inheritdoc/>
    public async Task<WireCommandResult> SendCommandAsync(
        RunId? run, CommandEnvelope envelope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var route = run is { } addressed
            ? RunRoutePrefix + Uri.EscapeDataString(addressed.Value) + "/command"
            : PlayerCommandRoute;

        // Built once, so the renewal repeat below sends the identical bytes rather than a second
        // rendering of the same envelope.
        var body = EnvelopeBody(envelope);

        var answer = await SendAsync(HttpMethod.Post, route, body, authenticated: true, renewable: true, ct)
            .ConfigureAwait(false);

        return Read(() => WireJson.ParseCommandResponse(answer));
    }

    /// <inheritdoc/>
    public async Task<WireRunState> FetchRunStateAsync(RunId run, long sinceSequence, CancellationToken ct)
    {
        var route = RunRoutePrefix + Uri.EscapeDataString(run.Value) + "/state?sinceSequence="
                    + sinceSequence.ToString(CultureInfo.InvariantCulture);

        var answer = await SendAsync(
                HttpMethod.Get, route, body: null, authenticated: true, renewable: true, ct)
            .ConfigureAwait(false);

        return Read(() => WireJson.ParseRunState(answer));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _client.Dispose();
        _renewalGate.Dispose();
    }

    /// <summary>
    /// Reads a 200 body through the wire dialect, keeping the port's two-failure contract.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>A body this build cannot read is the connection being unusable, not a third failure.</b>
    /// The port declares exactly two, and every caller catches exactly those two — so a
    /// <c>WireParseException</c> escaping here reaches the reconnect ladder as an unhandled fault
    /// with no backoff armed. The realistic producer is not a server defect: it is a captive portal
    /// or an intercepting proxy answering 200 with a login page, which is a handset's ordinary
    /// Tuesday and is precisely a connection worth retrying.
    /// </remarks>
    private static T Read<T>(Func<T> parse)
    {
        try
        {
            return parse();
        }
        catch (WireParseException fault)
        {
            throw new GameApiUnavailableException(
                "the server answered 200 with a body this build cannot read: " + fault.Message,
                retryAfter: null,
                fault);
        }
    }

    private async Task<string> SendAsync(
        HttpMethod method, string route, string? body, bool authenticated, bool renewable, CancellationToken ct)
    {
        // The token this exchange is about to present, captured BEFORE it goes out: it is what lets
        // a renewal tell "nobody has renewed yet" from "somebody already did while this caller was
        // waiting on the gate", and reading it afterwards would answer the second case as the first.
        var presented = _accessToken;
        var answer = await ExchangeAsync(method, route, body, authenticated, ct).ConfigureAwait(false);

        if (renewable && answer.Status == (int)HttpStatusCode.Unauthorized)
        {
            await RenewAsync(presented, ct).ConfigureAwait(false);

            answer = await ExchangeAsync(method, route, body, authenticated, ct).ConfigureAwait(false);

            if (answer.Status == (int)HttpStatusCode.Unauthorized)
            {
                throw new GameApiUnavailableException(
                    "the server refused a token this client had just renewed, so there is no request "
                    + "it can make that would succeed. Renewal is silent by contract, so this is "
                    + "reported as the connection being unusable rather than as anything a player "
                    + "is shown — and it is reported once, because a second renewal would be a loop.");
            }
        }

        return answer.Status switch
        {
            (int)HttpStatusCode.OK => answer.Body,

            (int)HttpStatusCode.TooManyRequests => throw new GameApiUnavailableException(
                "the server is rate limiting this client at the transport level. This is not the "
                + "in-envelope RATE_LIMITED rejection: nothing was decided about the command, so the "
                + "same command id is resent after the interval rather than surfaced to the player.",
                answer.RetryAfter),

            >= 500 => throw new GameApiUnavailableException(
                $"the server answered {answer.Status}. Nothing is known about whether the request was "
                + "processed, which is exactly the state the connection indicator exists for."),

            _ => throw new GameApiRefusedException(
                answer.Status,
                $"the server answered {answer.Status} and refused the request itself. Sending it "
                + "again unchanged cannot help — this is not a connection problem."),
        };
    }

    /// <summary>One silent renewal: rotate the family, or reopen it from the credential this instance holds.</summary>
    /// <remarks>
    /// The second arm runs only when <see cref="AuthenticateAsync"/> handed this instance the
    /// credential. Nothing here reads one from anywhere else — an adapter that went looking for a
    /// device secret would be deciding on its own which account to sign in as.
    /// </remarks>
    private async Task RenewAsync(string? staleToken, CancellationToken ct)
    {
        // 🔒 Single-flight. The server's rotation is single-use WITH reuse detection: two callers
        // presenting the same refresh token make the second read as a replay, and the response to a
        // replayed token is to close the whole family. So a pair of requests that happened to get
        // 401 together would sign the player out and force a re-authentication from the device
        // secret — the exact opposite of the silent renewal this method exists to perform.
        await _renewalGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            // Whoever held the gate may already have renewed. The token this caller was refused
            // with is the evidence: if it has moved, the repeat is owed a try before a rotation is.
            if (!string.Equals(_accessToken, staleToken, StringComparison.Ordinal))
            {
                return;
            }

            if (_refreshToken is { } token)
            {
                var rotated = await ExchangeAsync(
                        HttpMethod.Post, RefreshRoute, JsonSerializer.Serialize(new { refreshToken = token }),
                        authenticated: false, ct)
                    .ConfigureAwait(false);

                if (rotated.Status == (int)HttpStatusCode.OK)
                {
                    Adopt(Read(() => WireJson.ParseSession(rotated.Body)));
                    return;
                }
            }

            if (_credentials is { } credentials)
            {
                var reopened = await ExchangeAsync(
                        HttpMethod.Post, SessionRoute, SessionBody(credentials), authenticated: false, ct)
                    .ConfigureAwait(false);

                if (reopened.Status == (int)HttpStatusCode.OK)
                {
                    Adopt(Read(() => WireJson.ParseSession(reopened.Body)));
                    return;
                }
            }
        }
        finally
        {
            _renewalGate.Release();
        }

        throw new GameApiUnavailableException(
            "the session could not be renewed, and this client holds nothing else it could "
            + "authenticate with. Reported as an unavailable connection because 14 §16.5 is that "
            + "renewal is silent: a player is never shown a token problem.");
    }

    private async Task<(int Status, string Body, TimeSpan? RetryAfter)> ExchangeAsync(
        HttpMethod method, string route, string? body, bool authenticated, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, route);

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        if (authenticated && _accessToken is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        try
        {
            using var response = await _client.SendAsync(request, ct).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            return ((int)response.StatusCode, payload, response.Headers.RetryAfter?.Delta);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // The caller did not cancel, so a cancellation here is this client's own timeout —
            // which is a connection that did not answer, told apart from one that refused.
            throw new GameApiUnavailableException(
                "the server could not be reached: " + ex.Message, retryAfter: null, ex);
        }
    }

    private void Adopt(WireSession session)
    {
        _accessToken = session.AccessToken;
        _refreshToken = session.RefreshToken;
    }

    private static string SessionBody(WireCredentials credentials) =>
        JsonSerializer.Serialize(
            new { deviceId = credentials.DeviceId, deviceSecret = credentials.DeviceSecret });

    private static string EnvelopeBody(CommandEnvelope envelope)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("protocolVersion", envelope.ProtocolVersion);
            writer.WriteString("commandId", envelope.CommandId.Value);
            writer.WriteNumber("sequence", envelope.Sequence);
            writer.WriteString("type", envelope.Type);
            writer.WritePropertyName("payload");

            // The payload was encoded by the command codec in its own dialect and rides out
            // untouched; re-serialising it here would be a second opinion on what the command says.
            if (envelope.Payload.ValueKind == JsonValueKind.Undefined)
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
            }
            else
            {
                envelope.Payload.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
