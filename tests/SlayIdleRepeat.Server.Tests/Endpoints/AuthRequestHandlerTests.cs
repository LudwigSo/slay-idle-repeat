using System.Globalization;
using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Server.Auth;
using SlayIdleRepeat.Server.Endpoints;
using SlayIdleRepeat.Server.Tests.Auth;
using Xunit;

namespace SlayIdleRepeat.Server.Tests.Endpoints;

/// <summary>
/// The four auth endpoints as the plain functions they are: every status and every body shape the
/// contract names, success and refusal alike. No ASP.NET host, no database, no network.
/// </summary>
public sealed class AuthRequestHandlerTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly PlayerId Player = new("PLAYER_session_owner");

    private const string DeviceId = "DEVICE_session_owner";

    private const string DeviceSecret = "fixture-device-secret-alpha";

    private const string WrongSecret = "fixture-device-secret-not-the-one";

    private static readonly AuthOptions Options = AuthFixtures.Options();

    private static AccessTokenIssuer Issuer() => new(Options);

    private static AuthDevice RegisteredDevice() => new(
        DeviceId, Player, AuthFixtures.Digest(DeviceSecret), AuthFixtures.Now, AuthFixtures.Now);

    private static JsonElement BodyOf(AuthReply reply) => JsonDocument.Parse(reply.Body).RootElement;

    private static string[] NamesOf(AuthReply reply) => AuthFixtures.NamesOf(BodyOf(reply));

    private static readonly string[] SessionBodyNames =
    {
        "accessExpiresInSeconds", "accessToken", "playerId",
        "refreshExpiresInSeconds", "refreshToken", "renewAfterSeconds",
    };

    // ---- POST /auth/device -------------------------------------------------

    [Fact]
    public async Task Device_registration_mints_an_account_and_a_credential_whose_digest_is_what_is_stored()
    {
        var store = new FakeAuthStore();
        var names = new RecordingDisplayNamePolicy(DisplayNameDecision.Accepted("Wanderer"));

        var reply = await AuthRequestHandler.HandleDeviceRegistrationAsync(
            store, names, AuthFixtures.Now, "{}", Cancel);

        reply.StatusCode.ShouldBe(200);
        NamesOf(reply).ShouldBe(new[] { "deviceId", "deviceSecret", "displayName", "playerId" });

        store.Created.Count.ShouldBe(1, "one call, one account.");
        store.Created[0].Device.SecretDigest.ShouldBe(
            AuthFixtures.Digest(BodyOf(reply).GetProperty("deviceSecret").GetString()!),
            "the secret is returned once and the row keeps only its digest — the two halves must " +
            "be the same credential or the very next session request fails.");
        store.Created[0].Device.DeviceId.ShouldBe(BodyOf(reply).GetProperty("deviceId").GetString());
        store.Created[0].Device.Player.Value.ShouldBe(BodyOf(reply).GetProperty("playerId").GetString());
    }

    [Fact]
    public async Task Device_registration_asks_the_filter_for_the_default_when_no_name_was_sent()
    {
        var store = new FakeAuthStore();
        var names = new RecordingDisplayNamePolicy(DisplayNameDecision.Accepted("FILTER_DEFAULT"));

        var reply = await AuthRequestHandler.HandleDeviceRegistrationAsync(
            store, names, AuthFixtures.Now, "{}", Cancel);

        names.Candidates.Count.ShouldBe(1, "the filter is consulted exactly once.");
        names.Candidates[0].ShouldBeNull(
            "an absent name is passed through as absent, so the filter decides the default rather " +
            "than the endpoint spelling one of its own.");
        BodyOf(reply).GetProperty("displayName").GetString().ShouldBe("FILTER_DEFAULT");
        store.Created[0].DisplayName.ShouldBe("FILTER_DEFAULT");
    }

    [Fact]
    public async Task Device_registration_passes_a_supplied_name_through_the_filter_unchanged()
    {
        var names = new RecordingDisplayNamePolicy(DisplayNameDecision.Accepted("Rogue"));

        var reply = await AuthRequestHandler.HandleDeviceRegistrationAsync(
            new FakeAuthStore(), names, AuthFixtures.Now, "{\"displayName\": \"Rogue\"}", Cancel);

        names.Candidates.Count.ShouldBe(1);
        names.Candidates[0].ShouldBe(
            "Rogue",
            "a trimmed, lower-cased or truncated candidate would be a second name rule beside the " +
            "one that exists.");
        BodyOf(reply).GetProperty("displayName").GetString().ShouldBe("Rogue");
    }

    [Fact]
    public async Task Device_registration_refuses_a_filtered_name_with_the_refusal_value_alone()
    {
        var store = new FakeAuthStore();
        var names = new RecordingDisplayNamePolicy(
            DisplayNameDecision.Refused(HeroNameRefusal.PROFANE_EN));

        var reply = await AuthRequestHandler.HandleDeviceRegistrationAsync(
            store, names, AuthFixtures.Now, "{\"displayName\": \"fixture-refused-candidate\"}", Cancel);

        reply.StatusCode.ShouldBe(400);
        NamesOf(reply).ShouldBe(new[] { "refusal" });
        BodyOf(reply).GetProperty("refusal").GetString().ShouldBe(
            "PROFANE_EN",
            "the caller is told WHICH check fired — a bare 400 leaves a player unable to tell a " +
            "too-long name from a refused one.");
        store.Created.ShouldBeEmpty("a refused name creates no account.");
    }

    /// <summary>
    /// 🔒 The refusal must not carry the term back. Echoing the candidate turns the endpoint into a
    /// word-list oracle that anybody can enumerate one request at a time.
    /// </summary>
    [Fact]
    public async Task Device_registration_never_echoes_the_refused_candidate()
    {
        const string candidate = "fixture-refused-candidate";

        var reply = await AuthRequestHandler.HandleDeviceRegistrationAsync(
            new FakeAuthStore(),
            new RecordingDisplayNamePolicy(DisplayNameDecision.Refused(HeroNameRefusal.PROFANE_DE)),
            AuthFixtures.Now,
            "{\"displayName\": \"" + candidate + "\"}",
            Cancel);

        reply.Body.Contains(candidate, StringComparison.Ordinal).ShouldBeFalse(
            "the refusal vocabulary exists precisely so a rejected name can be explained without " +
            "quoting it back.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    public async Task Device_registration_refuses_a_malformed_body(string body)
    {
        var reply = await AuthRequestHandler.HandleDeviceRegistrationAsync(
            new FakeAuthStore(),
            new RecordingDisplayNamePolicy(DisplayNameDecision.Accepted("Wanderer")),
            AuthFixtures.Now,
            body,
            Cancel);

        reply.StatusCode.ShouldBe(400);
    }

    // ---- POST /auth/session ------------------------------------------------

    [Fact]
    public async Task Session_issues_the_session_body_for_correct_credentials()
    {
        var store = new FakeAuthStore().WithDevice(RegisteredDevice());

        var reply = await AuthRequestHandler.HandleSessionAsync(
            store, Issuer(), Options, AuthFixtures.Now, SessionRequest(DeviceId, DeviceSecret), Cancel);

        reply.StatusCode.ShouldBe(200);
        NamesOf(reply).ShouldBe(SessionBodyNames);

        var body = BodyOf(reply);
        body.GetProperty("playerId").GetString().ShouldBe(Player.Value);
        body.GetProperty("accessExpiresInSeconds").GetInt64().ShouldBe(3600);
        body.GetProperty("refreshExpiresInSeconds").GetInt64().ShouldBe(2592000, "30 days, in seconds.");
    }

    [Fact]
    public async Task Session_opens_a_family_holding_only_the_refresh_tokens_digest()
    {
        var store = new FakeAuthStore().WithDevice(RegisteredDevice());

        var reply = await AuthRequestHandler.HandleSessionAsync(
            store, Issuer(), Options, AuthFixtures.Now, SessionRequest(DeviceId, DeviceSecret), Cancel);

        store.Opened.Count.ShouldBe(1, "one authentication opens exactly one family.");
        store.Opened[0].Token.TokenDigest.ShouldBe(
            AuthFixtures.Digest(BodyOf(reply).GetProperty("refreshToken").GetString()!),
            "what the client gets and what the database keeps are the token and its digest — a " +
            "stored raw token is a database dump that logs everybody in.");
        store.Opened[0].Token.FamilyId.ShouldBe(store.Opened[0].Family.FamilyId);
        store.Opened[0].Family.RevokedAtUtc.ShouldBeNull("a fresh family is live.");
        store.Opened[0].LastSeenAtUtc.ShouldBe(
            AuthFixtures.Now, "the device's authoritative last-seen instant is restamped here.");
    }

    /// <summary>
    /// 🔒 An unknown device and a wrong secret must be INDISTINGUISHABLE. Any difference — a status,
    /// a body, a header — turns the endpoint into a device-id enumeration oracle.
    /// </summary>
    [Fact]
    public async Task Session_answers_an_unknown_device_and_a_wrong_secret_byte_identically()
    {
        var store = new FakeAuthStore().WithDevice(RegisteredDevice());

        var unknownDevice = await AuthRequestHandler.HandleSessionAsync(
            store, Issuer(), Options, AuthFixtures.Now,
            SessionRequest("DEVICE_never_registered", DeviceSecret), Cancel);

        var wrongSecret = await AuthRequestHandler.HandleSessionAsync(
            store, Issuer(), Options, AuthFixtures.Now,
            SessionRequest(DeviceId, WrongSecret), Cancel);

        unknownDevice.StatusCode.ShouldBe(401);
        wrongSecret.StatusCode.ShouldBe(401);
        wrongSecret.Body.ShouldBe(
            unknownDevice.Body,
            "the two refusals differ in nothing an attacker can read, so a probe learns whether a " +
            "device id exists only by guessing its 256-bit secret too.");
    }

    [Fact]
    public async Task Session_answers_a_soft_deleted_account_with_the_account_state_status()
    {
        var store = new FakeAuthStore().WithDevice(RegisteredDevice()).WithDeletedAccount(Player);

        var reply = await AuthRequestHandler.HandleSessionAsync(
            store, Issuer(), Options, AuthFixtures.Now, SessionRequest(DeviceId, DeviceSecret), Cancel);

        reply.StatusCode.ShouldBe(
            403,
            "the credentials are correct, so refreshing cures nothing — a deleted account is an " +
            "account-state answer.");
        store.Opened.ShouldBeEmpty("a deleted account opens no family.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"deviceId\": \"DEVICE_session_owner\"}")]
    public async Task Session_refuses_a_malformed_body(string body)
    {
        var reply = await AuthRequestHandler.HandleSessionAsync(
            new FakeAuthStore().WithDevice(RegisteredDevice()),
            Issuer(), Options, AuthFixtures.Now, body, Cancel);

        reply.StatusCode.ShouldBe(
            400, "a body missing a required field is malformed, not a failed authentication.");
    }

    [Fact]
    public async Task Session_tells_the_client_to_renew_at_the_configured_fraction_of_the_lifetime()
    {
        var store = new FakeAuthStore().WithDevice(RegisteredDevice());

        var reply = await AuthRequestHandler.HandleSessionAsync(
            store, Issuer(), Options, AuthFixtures.Now, SessionRequest(DeviceId, DeviceSecret), Cancel);

        BodyOf(reply).GetProperty("renewAfterSeconds").GetInt64().ShouldBe(
            2880,
            "floor(3600 x 0.8). The server carries the fraction so the client never hard-codes it " +
            "and a retuned deployment does not need a client release.");
    }

    // ---- POST /auth/refresh ------------------------------------------------

    [Fact]
    public async Task Refresh_rotates_a_live_token_into_a_new_session_body()
    {
        const string presented = "fixture-refresh-token-live";
        var store = LiveFamilyStore(presented);

        var reply = await AuthRequestHandler.HandleRefreshAsync(
            store, Issuer(), Options, AuthFixtures.Now, RefreshRequest(presented), Cancel);

        reply.StatusCode.ShouldBe(200);
        NamesOf(reply).ShouldBe(SessionBodyNames);
        BodyOf(reply).GetProperty("playerId").GetString().ShouldBe(Player.Value);
        BodyOf(reply).GetProperty("refreshToken").GetString().ShouldNotBe(
            presented, "single-use means the client leaves with a different token than it brought.");

        store.Rotations.Count.ShouldBe(1);
        store.Rotations[0].Accepted.ShouldBeTrue();
    }

    [Fact]
    public async Task Refresh_answers_a_reused_token_with_the_refreshable_status()
    {
        const string presented = "fixture-refresh-token-spent";
        var store = ReusedFamilyStore(presented);

        var reply = await AuthRequestHandler.HandleRefreshAsync(
            store, Issuer(), Options, AuthFixtures.Now, RefreshRequest(presented), Cancel);

        reply.StatusCode.ShouldBe(
            401, "the client's cure is a device-secret re-authentication, which opens a fresh family.");
    }

    [Fact]
    public async Task Refresh_revokes_the_whole_family_when_a_reused_token_is_presented()
    {
        const string presented = "fixture-refresh-token-spent";
        var store = ReusedFamilyStore(presented);

        await AuthRequestHandler.HandleRefreshAsync(
            store, Issuer(), Options, AuthFixtures.Now, RefreshRequest(presented), Cancel);

        store.Rotations.Count.ShouldBe(
            1, "the revocation is APPLIED, not merely decided — a decision nobody writes down " +
               "leaves the stolen token working.");
        store.Rotations[0].FamilyRevocation.ShouldBe(TokenFamilyRevocation.REFRESH_REUSE);
        store.Rotations[0].RevokedTokenDigests.Count.ShouldBe(
            2, "the replayed token and the live sibling a thief would be holding.");
    }

    [Fact]
    public async Task Refresh_answers_a_soft_deleted_account_with_the_account_state_status()
    {
        const string presented = "fixture-refresh-token-live";
        var store = LiveFamilyStore(presented).WithDeletedAccount(Player);

        var reply = await AuthRequestHandler.HandleRefreshAsync(
            store, Issuer(), Options, AuthFixtures.Now, RefreshRequest(presented), Cancel);

        reply.StatusCode.ShouldBe(403);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    public async Task Refresh_refuses_a_malformed_body(string body)
    {
        var reply = await AuthRequestHandler.HandleRefreshAsync(
            new FakeAuthStore(), Issuer(), Options, AuthFixtures.Now, body, Cancel);

        reply.StatusCode.ShouldBe(400);
    }

    // ---- DELETE /account ---------------------------------------------------

    [Fact]
    public async Task Account_deletion_soft_deletes_and_dates_the_hard_delete_thirty_days_out()
    {
        var store = new FakeAuthStore().WithDevice(RegisteredDevice());

        var reply = await AuthRequestHandler.HandleAccountDeletionAsync(
            store,
            new FixedPrincipalResolver(PrincipalResolution.Resolved(Player)),
            "Bearer fixture-access-token",
            AuthFixtures.Now,
            "{\"deviceSecret\": \"" + DeviceSecret + "\"}",
            Cancel);

        reply.StatusCode.ShouldBe(200);
        NamesOf(reply).ShouldBe(new[] { "hardDeleteDueAtUtc", "playerId", "softDeletedAtUtc" });

        Instant(BodyOf(reply), "softDeletedAtUtc").ShouldBe(
            AuthFixtures.Now, "the soft delete is immediate: logins are refused from this instant.");
        Instant(BodyOf(reply), "hardDeleteDueAtUtc").ShouldBe(
            AuthFixtures.Now.AddDays(30),
            "the erasure deadline the player is promised, and the date the hosted sweep works to.");

        store.SoftDeletes.Count.ShouldBe(1);
        store.SoftDeletes[0].Player.ShouldBe(Player);
        store.SoftDeletes[0].DueAtUtc.ShouldBe(AuthFixtures.Now.AddDays(30));
        store.IsLive(Player).ShouldBeFalse();
    }

    [Fact]
    public async Task Account_deletion_refuses_a_wrong_device_secret_and_changes_nothing()
    {
        var store = new FakeAuthStore().WithDevice(RegisteredDevice());

        var reply = await AuthRequestHandler.HandleAccountDeletionAsync(
            store,
            new FixedPrincipalResolver(PrincipalResolution.Resolved(Player)),
            "Bearer fixture-access-token",
            AuthFixtures.Now,
            "{\"deviceSecret\": \"" + WrongSecret + "\"}",
            Cancel);

        reply.StatusCode.ShouldBe(
            403,
            "401 is the status a client cures by refreshing and retrying the SAME request, which " +
            "would resubmit the same wrong secret forever. A failed re-confirmation is an " +
            "account-state answer, not a token answer.");
        store.SoftDeletes.ShouldBeEmpty();
        store.IsLive(Player).ShouldBeTrue("the account a wrong secret failed to delete is still live.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer expired-or-forged")]
    public async Task Account_deletion_refuses_a_missing_or_invalid_access_token_and_changes_nothing(
        string? header)
    {
        var store = new FakeAuthStore().WithDevice(RegisteredDevice());

        var reply = await AuthRequestHandler.HandleAccountDeletionAsync(
            store,
            new FixedPrincipalResolver(PrincipalResolution.Unauthorized()),
            header,
            AuthFixtures.Now,
            "{\"deviceSecret\": \"" + DeviceSecret + "\"}",
            Cancel);

        reply.StatusCode.ShouldBe(401);
        store.SoftDeletes.ShouldBeEmpty();
        store.IsLive(Player).ShouldBeTrue(
            "the right secret with no token deletes nothing — both halves are required, and the " +
            "token half is checked first.");
    }

    [Fact]
    public async Task Account_deletion_refuses_a_malformed_body()
    {
        var store = new FakeAuthStore().WithDevice(RegisteredDevice());

        var reply = await AuthRequestHandler.HandleAccountDeletionAsync(
            store,
            new FixedPrincipalResolver(PrincipalResolution.Resolved(Player)),
            "Bearer fixture-access-token",
            AuthFixtures.Now,
            "{}",
            Cancel);

        reply.StatusCode.ShouldBe(400);
        store.SoftDeletes.ShouldBeEmpty();
    }

    // ---- the cross-cutting leak sweep --------------------------------------

    /// <summary>
    /// 🔒 No success body anywhere carries a stored digest. A response that returned one would hand
    /// out the exact value a database dump holds, which is the only thing standing between a leaked
    /// backup and every account in it.
    /// </summary>
    [Fact]
    public async Task No_response_body_carries_a_stored_digest()
    {
        var registrationStore = new FakeAuthStore();
        var registration = await AuthRequestHandler.HandleDeviceRegistrationAsync(
            registrationStore,
            new RecordingDisplayNamePolicy(DisplayNameDecision.Accepted("Wanderer")),
            AuthFixtures.Now, "{}", Cancel);

        var sessionStore = new FakeAuthStore().WithDevice(RegisteredDevice());
        var session = await AuthRequestHandler.HandleSessionAsync(
            sessionStore, Issuer(), Options, AuthFixtures.Now,
            SessionRequest(DeviceId, DeviceSecret), Cancel);

        var refreshStore = LiveFamilyStore("fixture-refresh-token-live");
        var refresh = await AuthRequestHandler.HandleRefreshAsync(
            refreshStore, Issuer(), Options, AuthFixtures.Now,
            RefreshRequest("fixture-refresh-token-live"), Cancel);

        var deletion = await AuthRequestHandler.HandleAccountDeletionAsync(
            new FakeAuthStore().WithDevice(RegisteredDevice()),
            new FixedPrincipalResolver(PrincipalResolution.Resolved(Player)),
            "Bearer fixture-access-token", AuthFixtures.Now,
            "{\"deviceSecret\": \"" + DeviceSecret + "\"}", Cancel);

        var digests = new[]
        {
            registrationStore.Created[0].Device.SecretDigest,
            sessionStore.Opened[0].Token.TokenDigest,
            AuthFixtures.Digest(DeviceSecret),
        };

        var bodies = new[] { registration.Body, session.Body, refresh.Body, deletion.Body };

        bodies.SelectMany(body => digests.SelectMany(digest => Spellings(digest)
                .Where(spelling => body.Contains(spelling, StringComparison.OrdinalIgnoreCase))))
            .ShouldBeEmpty(
                "a digest reaching a response body — hex, base64 or base64url — is the stored " +
                "credential leaving the database, whichever spelling it travels in.");
    }

    [Fact]
    public async Task The_account_deletion_body_returns_no_token_of_any_kind()
    {
        var reply = await AuthRequestHandler.HandleAccountDeletionAsync(
            new FakeAuthStore().WithDevice(RegisteredDevice()),
            new FixedPrincipalResolver(PrincipalResolution.Resolved(Player)),
            "Bearer fixture-access-token", AuthFixtures.Now,
            "{\"deviceSecret\": \"" + DeviceSecret + "\"}", Cancel);

        NamesOf(reply).ShouldBe(
            new[] { "hardDeleteDueAtUtc", "playerId", "softDeletedAtUtc" },
            "the account is going away; a token in this body is a credential for something that " +
            "must no longer be reachable.");
        reply.Body.Contains(DeviceSecret, StringComparison.Ordinal).ShouldBeFalse(
            "the secret the caller re-confirmed with is never echoed.");
    }

    // ---- fixtures ----------------------------------------------------------

    private static string SessionRequest(string deviceId, string deviceSecret) =>
        "{\"deviceId\": \"" + deviceId + "\", \"deviceSecret\": \"" + deviceSecret + "\"}";

    private static string RefreshRequest(string refreshToken) =>
        "{\"refreshToken\": \"" + refreshToken + "\"}";

    private static DateTimeOffset Instant(JsonElement body, string property) =>
        DateTimeOffset.Parse(
            body.GetProperty(property).GetString()!,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private static FakeAuthStore LiveFamilyStore(string presentedToken)
    {
        var digest = AuthFixtures.Digest(presentedToken);

        var family = new AuthTokenFamily(
            "FAMILY_alpha", DeviceId, Player, AuthFixtures.Now.AddDays(-1), null, null);

        var token = new AuthRefreshToken(
            digest, family.FamilyId, DeviceId, Player,
            AuthFixtures.Now.AddDays(-1), AuthFixtures.Now.AddDays(29), null);

        return new FakeAuthStore()
            .WithDevice(RegisteredDevice())
            .WithTokenState(digest, new AuthTokenState(new[] { family }, new[] { token }));
    }

    private static FakeAuthStore ReusedFamilyStore(string presentedToken)
    {
        var digest = AuthFixtures.Digest(presentedToken);
        var siblingDigest = AuthFixtures.Digest("fixture-refresh-token-sibling");

        var family = new AuthTokenFamily(
            "FAMILY_alpha", DeviceId, Player, AuthFixtures.Now.AddDays(-1), null, null);

        var spent = new AuthRefreshToken(
            digest, family.FamilyId, DeviceId, Player,
            AuthFixtures.Now.AddDays(-1), AuthFixtures.Now.AddDays(29), AuthFixtures.Now.AddHours(-2));

        var sibling = new AuthRefreshToken(
            siblingDigest, family.FamilyId, DeviceId, Player,
            AuthFixtures.Now.AddHours(-2), AuthFixtures.Now.AddDays(29), null);

        return new FakeAuthStore()
            .WithDevice(RegisteredDevice())
            .WithTokenState(digest, new AuthTokenState(new[] { family }, new[] { spent, sibling }));
    }

    /// <summary>Every way one digest could be spelled into a JSON body.</summary>
    private static string[] Spellings(byte[] digest) => new[]
    {
        Convert.ToHexString(digest),
        Convert.ToBase64String(digest),
        AuthFixtures.Base64Url(digest),
    };
}
