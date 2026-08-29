using System.Net;
using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Adapters.Api.Http;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Contracts;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Client;

/// <summary>
/// The HTTP adapter's own wire behaviour — the half no shared contract suite can state, because it
/// is about statuses, headers and retries that only exist over a transport.
/// </summary>
/// <remarks>
/// Observed through a scripted handler and never a network. Everything the two implementations owe
/// each other is in <see cref="IGameApiPortContractTests"/>; what is here is what makes THIS one an
/// HTTP client rather than an object that answers questions.
/// </remarks>
public sealed class HttpGameApiTransportTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly JsonElement EmptyPayload = JsonDocument.Parse("{}").RootElement.Clone();

    private static readonly WireCredentials Credentials =
        new(ScriptedGameServer.DeviceId, ScriptedGameServer.DeviceSecret);

    private readonly List<HttpGameApi> _built = [];

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var api in _built)
        {
            api.Dispose();
        }
    }

    /// <summary>
    /// 🔒 <c>14</c> §16.5: a 401 costs one refresh and one repeat of the identical request — the same
    /// command id, so the server answers from its idempotency record rather than running the command
    /// twice.
    /// </summary>
    /// <remarks>
    /// The command id is the whole point of the case. A retry that minted a fresh one would look
    /// identical from the caller's side and would spend the player's currency twice the first time a
    /// token expired mid-purchase — which is the defect this clause exists to prevent, and it is
    /// invisible without reading what the transport actually sent.
    /// </remarks>
    [Fact]
    public async Task A_401_costs_one_silent_refresh_and_one_retry_carrying_the_same_command_id()
    {
        var server = new ScriptedGameServer();
        var api = Over(server);

        await api.AuthenticateAsync(Credentials, Cancel);
        server.UnauthorizedAnswersLeft = 1;

        var result = await api.SendCommandAsync(run: null, Envelope("CMD_renewal_probe", 11), Cancel);

        result.Accepted.ShouldBeTrue(
            "the renewal is silent: after it the original request succeeds and nothing about a token "
            + "reaches the caller, let alone a player.");

        server.RefreshCalls.ShouldBe(
            1, "exactly one refresh. Zero is no renewal at all; two is the loop this must never be.");

        server.CommandIds.ShouldBe(
            new[] { "CMD_renewal_probe", "CMD_renewal_probe" },
            Case.Sensitive,
            "the retry has to be the SAME command, or the server's idempotency record cannot "
            + "recognise it and the command runs a second time.");

        server.Requests
            .Where(r => r.Path == "/player/command")
            .Select(r => r.BearerToken)
            .ShouldBe(
                new[] { ScriptedGameServer.AccessToken, ScriptedGameServer.RenewedAccessToken },
                "the first attempt carried the expired token and the retry carried the renewed one. "
                + "A retry that resent the old token would burn the refresh for nothing.");
    }

    /// <summary>A 401 that survives the renewal is reported once, not retried forever.</summary>
    /// <remarks>
    /// The other half of "exactly one": a second 401 after a freshly minted token means no request
    /// this client can make would succeed, and the honest answer is that the connection is unusable.
    /// A loop here would be an infinite one, hidden behind a silent renewal nobody can see.
    /// </remarks>
    [Fact]
    public async Task A_401_that_survives_the_renewal_is_reported_once_rather_than_retried_again()
    {
        var server = new ScriptedGameServer();
        var api = Over(server);

        await api.AuthenticateAsync(Credentials, Cancel);
        server.UnauthorizedAnswersLeft = 2;

        await Should.ThrowAsync<GameApiUnavailableException>(
            () => api.SendCommandAsync(run: null, Envelope("CMD_still_401", 12), Cancel),
            "a token the server refuses immediately after issuing it is a connection this client "
            + "cannot use, and 14 §16.5 forbids surfacing it as anything a player reads.");

        server.RefreshCalls.ShouldBe(1, "one renewal attempt, never a second.");
        server.CommandIds.Count.ShouldBe(2, "the original attempt and one retry. Nothing more.");
    }

    /// <summary>
    /// A refresh the server will not honour falls back once to the device credential this adapter is
    /// holding.
    /// </summary>
    /// <remarks>
    /// The fallback is legal only because <c>AuthenticateAsync</c> handed the credential over — an
    /// adapter that went looking for a device secret would be choosing which account to sign in as.
    /// It happens once, and the original request is still repeated with its own command id.
    /// </remarks>
    [Fact]
    public async Task A_refused_refresh_reopens_the_family_from_the_held_credential_and_retries_once()
    {
        var server = new ScriptedGameServer { RefreshStatus = HttpStatusCode.Unauthorized };
        var api = Over(server);

        await api.AuthenticateAsync(Credentials, Cancel);
        server.UnauthorizedAnswersLeft = 1;

        var result = await api.SendCommandAsync(run: null, Envelope("CMD_reauth_probe", 13), Cancel);

        result.Accepted.ShouldBeTrue("the reopened session carries the request through.");
        server.RefreshCalls.ShouldBe(1, "the rotation is tried first, and only once.");
        server.Requests.Count(r => r.Path == "/auth/session").ShouldBe(
            2,
            "the original sign-in, and one reopen after the rotation was refused. A third would mean "
            + "the fallback is a loop of its own.");
        server.CommandIds.ShouldBe(
            new[] { "CMD_reauth_probe", "CMD_reauth_probe" },
            Case.Sensitive,
            "the retry is still the same command, whichever route restored the session.");
    }

    /// <summary>A renewal that fails both ways is an unavailable connection, never a player-visible error.</summary>
    [Fact]
    public async Task A_renewal_that_fails_both_ways_surfaces_as_an_unavailable_connection()
    {
        var server = new ScriptedGameServer
        {
            RefreshStatus = HttpStatusCode.Unauthorized,
            LaterSessionStatus = HttpStatusCode.Unauthorized,
        };

        var api = Over(server);

        await api.AuthenticateAsync(Credentials, Cancel);
        server.UnauthorizedAnswersLeft = 1;

        await Should.ThrowAsync<GameApiUnavailableException>(
            () => api.SendCommandAsync(run: null, Envelope("CMD_no_renewal", 14), Cancel),
            "neither route restored the session. Reported as a refusal this would reach a player as "
            + "a message about their account, which is exactly what silent renewal forbids.");
    }

    /// <summary>
    /// 🔒 A 429 is the connection being unusable for a stated interval — not the in-envelope
    /// <c>RATE_LIMITED</c> rejection.
    /// </summary>
    /// <remarks>
    /// The two are deliberately different answers to different questions. A 429 decided nothing about
    /// the command, so the same command id is resent after the interval; the in-envelope rejection
    /// IS a decision and must never be blind-retried. The interval has to survive the mapping or the
    /// caller's backoff ignores what the server asked for.
    /// </remarks>
    [Fact]
    public async Task A_429_is_an_unavailable_connection_carrying_the_servers_own_Retry_After()
    {
        var server = new ScriptedGameServer
        {
            ForcedStatus = HttpStatusCode.TooManyRequests,
            RetryAfterSeconds = 7,
        };

        var api = Over(server);
        await api.AuthenticateAsync(Credentials, Cancel);

        var failure = await Should.ThrowAsync<GameApiUnavailableException>(
            () => api.SendCommandAsync(run: null, Envelope("CMD_throttled", 15), Cancel));

        failure.RetryAfter.ShouldBe(
            TimeSpan.FromSeconds(7),
            $"the server asked for 7 seconds and the failure carries {failure.RetryAfter}. A caller "
            + "that cannot read the interval falls back to its own backoff and hammers a server that "
            + "already said how long it needed.");
    }

    /// <summary>A 404 is the server refusing this request, and it says which refusal it was.</summary>
    /// <remarks>
    /// Told apart from the unavailable family because retrying it unchanged cannot help: the run is
    /// unknown, or it belongs to somebody else, and no amount of backoff changes either.
    /// </remarks>
    [Fact]
    public async Task A_404_on_a_state_read_is_a_refusal_naming_its_status()
    {
        var server = new ScriptedGameServer { ForcedStatus = HttpStatusCode.NotFound };
        var api = Over(server);

        await api.AuthenticateAsync(Credentials, Cancel);

        var failure = await Should.ThrowAsync<GameApiRefusedException>(
            () => api.FetchRunStateAsync(WireWorlds.Run.Id, sinceSequence: 0, Cancel));

        failure.StatusCode.ShouldBe(
            404,
            "the status is what tells the caller whether to re-authenticate, resync or give up on "
            + "this run. Without it every refusal looks the same and gets the same wrong repair.");
    }

    /// <summary>
    /// A rejection envelope on HTTP 200 is parsed into a result — reason and untouched state hash
    /// intact.
    /// </summary>
    /// <remarks>
    /// The shared suite asserts that a refusal is not an exception; what only this fixture can add is
    /// that the hash the adapter hands back is the one the SERVER rendered for the untouched state.
    /// That value is how the mirror knows it is still in step after a refusal, and an adapter that
    /// dropped it would make every rejection look like a state it had to resync from.
    /// </remarks>
    [Fact]
    public async Task A_rejection_on_200_keeps_the_reason_and_the_untouched_state_hash()
    {
        var server = new ScriptedGameServer { Refusal = RejectionReason.INSUFFICIENT_FUNDS };
        var api = Over(server);

        await api.AuthenticateAsync(Credentials, Cancel);

        var result = await api.SendCommandAsync(run: null, Envelope("CMD_rejected", 16), Cancel);

        result.Accepted.ShouldBeFalse("the server said no.");
        result.Rejection.ShouldBe(RejectionReason.INSUFFICIENT_FUNDS, "and said why.");
        result.StateHash.ShouldBe(
            WireWorlds.StateHash,
            "a refusal carries the hash of the state it did NOT change, which is what lets the mirror "
            + "stay put instead of resyncing after every affordable-looking mistake.");
        result.Profile.ShouldBeNull("nothing changed, so there is no new projection to carry.");
    }

    /// <summary>
    /// 🔒 An unknown <c>reason</c> is a generic rejection, never a crash: the server may add reasons
    /// this build has never heard of.
    /// </summary>
    /// <remarks>
    /// Written as a hand-built body rather than through <c>WireJson.Render</c>, because the reason
    /// this case is about is one the enum does not have — so it cannot be rendered from the server's
    /// own vocabulary, which is precisely the situation an older client is in.
    /// </remarks>
    [Fact]
    public async Task An_unknown_rejection_reason_is_a_refusal_with_no_reason_rather_than_a_crash()
    {
        var server = new UnknownReasonServer();
        var api = Over(server);

        var result = await api.SendCommandAsync(run: null, Envelope("CMD_future_reason", 17), Cancel);

        result.Accepted.ShouldBeFalse("the server still refused it.");
        result.Rejection.ShouldBeNull(
            "the reason is one this build does not know, and RejectionReason's own contract is that "
            + "a client treats an unknown value as a generic rejection and resyncs. A client that "
            + "threw here would be a client the server could never add a reason to.");
    }

    /// <summary>A state read spells its <c>sinceSequence</c> as plain invariant digits on the query string.</summary>
    /// <remarks>
    /// The server parses that value strictly, so a thousands separator or a culture's own digits
    /// would be a 400 on every machine whose locale is not the developer's. The bearer is asserted in
    /// the same case because both are properties of one request, and the interesting failure — a read
    /// that reaches the right route unauthenticated — is one an assertion on either half alone
    /// misses.
    /// </remarks>
    [Fact]
    public async Task A_state_read_sends_plain_digits_and_the_session_bearer()
    {
        var server = new ScriptedGameServer();
        var api = Over(server);

        await api.AuthenticateAsync(Credentials, Cancel);
        await api.FetchRunStateAsync(WireWorlds.Run.Id, sinceSequence: 1_234_567, Cancel);

        var read = server.Requests.Last();

        read.Query.ShouldBe(
            "?sinceSequence=1234567",
            "the server reads this with strict digits and answers 400 on anything else — a grouped "
            + "or localised number would fail only on the machines that use one.");
        read.Path.ShouldBe(
            "/run/" + WireWorlds.Run.Id.Value + "/state", "the run's own state route.");
        read.BearerToken.ShouldBe(
            ScriptedGameServer.AccessToken, "the state route needs the session, like the command route.");
    }

    private static CommandEnvelope Envelope(string commandId, long sequence) =>
        new(
            WireProtocol.PROTOCOL_VERSION,
            new CommandId(commandId),
            sequence,
            WireCommandCodec.WireNameOf(new SkipDraftCommand()),
            EmptyPayload);

    private HttpGameApi Over(HttpMessageHandler handler)
    {
        var api = new HttpGameApi(new HttpGameApiOptions(), handler);
        _built.Add(api);

        return api;
    }

    /// <summary>A server one protocol revision ahead: it refuses with a reason this build has no member for.</summary>
    private sealed class UnknownReasonServer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent(
                        """
                        {"protocolVersion":1,"sequence":17,"rejected":true,
                         "reason":"SOMETHING_THIS_BUILD_HAS_NEVER_HEARD_OF",
                         "stateHash":"fnv1a:0123456789abcdef"}
                        """,
                        System.Text.Encoding.UTF8,
                        "application/json"),
                });
    }
}
