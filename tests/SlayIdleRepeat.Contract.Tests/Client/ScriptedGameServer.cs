using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Contracts;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Contract.Tests.Client;

/// <summary>
/// The game server's routes, answered offline: a handler that records what it was sent and replies
/// with bodies the server's own <see cref="WireJson"/> rendered.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The bodies are rendered, not written by hand.</b> That is what makes the HTTP fixture worth
/// more than a mock: the adapter is parsing exactly what
/// <c>WireJson.Render(CommandResponse)</c> and <c>WireJson.Render(RunStateResponse)</c> produce, so a
/// dialect change on the server turns this red rather than shipping a client that cannot read its
/// own protocol. The two auth bodies are the exception and are hand-written on purpose — the real
/// handler serialises anonymous objects at DEFAULT options, so their member names are literal
/// spellings rather than the camelCase policy's output, and a fixture that rendered them through the
/// policy would be testing a dialect the server does not speak.
/// </para>
/// <para>
/// No host, no socket, no container: everything below is a function from a request to a response.
/// </para>
/// </remarks>
internal sealed class ScriptedGameServer : HttpMessageHandler
{
    internal const string DeviceId = "DEVICE_scripted";
    internal const string DeviceSecret = "SECRET_scripted";
    internal const string DisplayName = "Scripted Hero";
    internal const string AccessToken = "ACCESS_scripted";
    internal const string RenewedAccessToken = "ACCESS_scripted_renewed";
    internal const string RefreshToken = "REFRESH_scripted";

    private const long AccessLifetimeSeconds = 3600;
    private const long RenewAfterSeconds = 2700;
    private const long RefreshLifetimeSeconds = 2_592_000;

    /// <summary>Every request this server was sent, in order.</summary>
    internal List<RecordedRequest> Requests { get; } = [];

    /// <summary>Why every command is refused, or <c>null</c> to accept them.</summary>
    internal RejectionReason? Refusal { get; set; }

    /// <summary>How many more command or state calls answer 401 before the token is honoured.</summary>
    internal int UnauthorizedAnswersLeft { get; set; }

    /// <summary>A status forced on every command and state call, or <c>null</c> to answer normally.</summary>
    internal HttpStatusCode? ForcedStatus { get; set; }

    /// <summary>The <c>Retry-After</c> a forced 429 carries, in seconds.</summary>
    internal int? RetryAfterSeconds { get; set; }

    /// <summary>A status forced on <c>POST /auth/refresh</c>, or <c>null</c> to rotate normally.</summary>
    internal HttpStatusCode? RefreshStatus { get; set; }

    /// <summary>A status forced on <c>POST /auth/session</c> AFTER the first one has succeeded.</summary>
    /// <remarks>
    /// The renewal fallback reopens the family from the stored credential, and this is how that arm
    /// is made to fail without breaking the sign-in the case needs first.
    /// </remarks>
    internal HttpStatusCode? LaterSessionStatus { get; set; }

    /// <summary>How many times the refresh route was called.</summary>
    internal int RefreshCalls { get; private set; }

    /// <summary>Every command id this server was sent, in order, including repeats.</summary>
    internal List<string> CommandIds { get; } = [];

    private int _sessionCalls;

    /// <summary>One request, flattened to what a case needs to assert on.</summary>
    /// <param name="Method">The verb.</param>
    /// <param name="Path">The path, without the query.</param>
    /// <param name="Query">The query, including its leading question mark, or empty.</param>
    /// <param name="Body">The request body, or empty for a body-less request.</param>
    /// <param name="BearerToken">The bearer token the request carried, or <c>null</c>.</param>
    internal sealed record RecordedRequest(
        HttpMethod Method, string Path, string Query, string Body, string? BearerToken);

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(
            new RecordedRequest(
                request.Method, uri.AbsolutePath, uri.Query, body,
                request.Headers.Authorization?.Parameter));

        return uri.AbsolutePath switch
        {
            "/auth/device" => Json(HttpStatusCode.OK, DeviceBody()),
            "/auth/session" => SessionAnswer(body),
            "/auth/refresh" => RefreshAnswer(),
            _ when uri.AbsolutePath.EndsWith("/command", StringComparison.Ordinal) => CommandAnswer(uri, body),
            _ when uri.AbsolutePath.EndsWith("/state", StringComparison.Ordinal) => StateAnswer(uri),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private HttpResponseMessage SessionAnswer(string body)
    {
        _sessionCalls++;

        if (_sessionCalls > 1 && LaterSessionStatus is { } forced)
        {
            return new HttpResponseMessage(forced);
        }

        using var request = JsonDocument.Parse(body);

        var presented = Text(request.RootElement, "deviceId");
        var secret = Text(request.RootElement, "deviceSecret");

        // An unknown device and a wrong secret take the same path to the same answer, as they do on
        // the server: telling them apart tells a caller which device ids exist.
        return presented == DeviceId && secret == DeviceSecret
            ? Json(HttpStatusCode.OK, SessionBody(AccessToken))
            : new HttpResponseMessage(HttpStatusCode.Unauthorized);
    }

    private HttpResponseMessage RefreshAnswer()
    {
        RefreshCalls++;

        return RefreshStatus is { } forced
            ? new HttpResponseMessage(forced)
            : Json(HttpStatusCode.OK, SessionBody(RenewedAccessToken));
    }

    private HttpResponseMessage CommandAnswer(Uri uri, string body)
    {
        using var envelope = JsonDocument.Parse(body);

        CommandIds.Add(Text(envelope.RootElement, "commandId") ?? string.Empty);

        if (Forced() is { } forced)
        {
            return forced;
        }

        var sequence = envelope.RootElement.GetProperty("sequence").GetInt64();
        var addressed = uri.AbsolutePath.StartsWith("/run/", StringComparison.Ordinal);

        if (Refusal is { } reason)
        {
            return Json(
                HttpStatusCode.OK,
                WireJson.Render(CommandResponse.RejectedWith(sequence, reason, WireWorlds.StateHash)));
        }

        var outcome = new AcceptedCommandOutcome(
            addressed ? WireWorlds.Run.Id : null,
            addressed ? WireWorlds.Run : null,
            addressed ? WireWorlds.Run.RngStreamPositions : null,
            BattleSeed: null,
            Array.Empty<DomainEvent>());

        return Json(
            HttpStatusCode.OK,
            WireJson.Render(
                CommandResponse.Accepted(sequence, outcome, WireWorlds.Profile, WireWorlds.StateHash)));
    }

    private HttpResponseMessage StateAnswer(Uri uri)
    {
        if (Forced() is { } forced)
        {
            return forced;
        }

        var since = long.Parse(
            uri.Query.Split("sinceSequence=", StringSplitOptions.None)[1],
            CultureInfo.InvariantCulture);

        var answer = new RunStateResponse(
            WireProtocol.PROTOCOL_VERSION,
            WireWorlds.Run.Id,
            Sequence: since,
            since,
            WireWorlds.Profile,
            WireWorlds.Run,
            WireWorlds.StateHash,
            Array.Empty<JsonElement>(),
            ResyncFull: null);

        return Json(HttpStatusCode.OK, WireJson.Render(answer));
    }

    private HttpResponseMessage? Forced()
    {
        if (UnauthorizedAnswersLeft > 0)
        {
            UnauthorizedAnswersLeft--;
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        if (ForcedStatus is not { } status)
        {
            return null;
        }

        var response = new HttpResponseMessage(status);

        if (RetryAfterSeconds is { } seconds)
        {
            response.Headers.Add(
                "Retry-After", seconds.ToString(CultureInfo.InvariantCulture));
        }

        return response;
    }

    private static string DeviceBody() =>
        JsonSerializer.Serialize(
            new
            {
                deviceId = DeviceId,
                deviceSecret = DeviceSecret,
                playerId = WireWorlds.Profile.Id.Value,
                displayName = DisplayName,
            });

    private static string SessionBody(string accessToken) =>
        JsonSerializer.Serialize(
            new
            {
                playerId = WireWorlds.Profile.Id.Value,
                accessToken,
                accessExpiresInSeconds = AccessLifetimeSeconds,
                renewAfterSeconds = RenewAfterSeconds,
                refreshToken = RefreshToken,
                refreshExpiresInSeconds = RefreshLifetimeSeconds,
            });

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var member) && member.ValueKind == JsonValueKind.String
            ? member.GetString()
            : null;

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

/// <summary>A transport with nothing on the other end of it.</summary>
internal sealed class UnreachableServer : HttpMessageHandler
{
    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new HttpRequestException("there is no server at that address.");
}
