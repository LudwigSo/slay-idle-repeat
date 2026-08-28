using System.Globalization;
using System.Text.Json;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Server.Auth;

namespace SlayIdleRepeat.Server.Endpoints;

/// <summary>What an auth route writes back: the HTTP status and the exact body.</summary>
/// <param name="StatusCode">The status the route answers with.</param>
/// <param name="Body">The JSON body, or empty where the contract carries none.</param>
public sealed record AuthReply(int StatusCode, string Body);

/// <summary>The four auth endpoints as plain functions — collaborators in, status and body out.</summary>
/// <remarks>
/// No ASP.NET type appears in any signature, for the same reason the command handlers carry none:
/// this repository constructs no host in any test tier, so the whole HTTP surface has to be
/// callable directly or it is not covered at all. The route lambdas adapt <c>HttpContext</c> to
/// these calls and do nothing else.
/// </remarks>
public static class AuthRequestHandler
{
    /// <summary><c>POST /auth/device</c> — mints an anonymous account and its device credential.</summary>
    /// <param name="store">The auth rows.</param>
    /// <param name="names">The display-name filter.</param>
    /// <param name="nowUtc">The request instant — injected, never read ambiently.</param>
    /// <param name="body">The request body.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentNullException">A seam or the body is null.</exception>
    public static async Task<AuthReply> HandleDeviceRegistrationAsync(
        IAuthStore store,
        IDisplayNamePolicy names,
        DateTimeOffset nowUtc,
        string body,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(body);

        if (!TryReadObject(body, out var request))
        {
            return Malformed;
        }

        // An absent name and a supplied blank one are different requests: the first takes the
        // filter's own default, the second is a name the caller chose and is refused like any other.
        var decided = names.Decide(Text(request, "displayName"));

        if (decided.Refusal is { } refusal)
        {
            return new AuthReply(400, Json(new { refusal = refusal.ToString() }));
        }

        var credential = DeviceCredentialFactory.Mint(NewPlayerId(), nowUtc);

        await store.CreateAnonymousAccountAsync(credential.Device, decided.Name!, ct).ConfigureAwait(false);

        return new AuthReply(200, Json(new
        {
            deviceId = credential.Device.DeviceId,
            deviceSecret = credential.DeviceSecret,
            playerId = credential.Device.Player.Value,
            displayName = decided.Name,
        }));
    }

    /// <summary><c>POST /auth/session</c> — device-secret authentication opening a token family.</summary>
    /// <param name="store">The auth rows.</param>
    /// <param name="issuer">Mints the access token.</param>
    /// <param name="options">The lifetimes and the renewal fraction.</param>
    /// <param name="nowUtc">The request instant — injected, never read ambiently.</param>
    /// <param name="body">The request body.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentNullException">A seam or the body is null.</exception>
    public static async Task<AuthReply> HandleSessionAsync(
        IAuthStore store,
        AccessTokenIssuer issuer,
        AuthOptions options,
        DateTimeOffset nowUtc,
        string body,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(body);

        if (!TryReadObject(body, out var request) ||
            Text(request, "deviceId") is not { Length: > 0 } deviceId ||
            Text(request, "deviceSecret") is not { } presentedSecret)
        {
            return Malformed;
        }

        var device = await store.FindDeviceAsync(deviceId, ct).ConfigureAwait(false);

        // 🔒 An unknown device and a wrong secret take the same path to the same answer. Running
        // the check even when no device matched — against a digest nothing hashes to — keeps the
        // absent-device arm from being the cheap one to a caller timing the two.
        var authenticated = DeviceCredentialFactory.Verify(
                presentedSecret,
                device?.SecretDigest ?? UnmatchableDigest);

        if (device is null || !authenticated)
        {
            return Unauthenticated;
        }

        if (await store.IsAccountDeletedAsync(device.Player, ct).ConfigureAwait(false))
        {
            return AccountUnavailable;
        }

        var refreshToken = DeviceCredentialFactory.OpaqueToken(RefreshTokenBytes);

        var family = new AuthTokenFamily(
            FamilyIdPrefix + DeviceCredentialFactory.OpaqueToken(16),
            device.DeviceId,
            device.Player,
            nowUtc,
            RevokedAtUtc: null,
            RevokedReason: null);

        var stored = new AuthRefreshToken(
            DeviceCredentialFactory.DigestOf(refreshToken),
            family.FamilyId,
            device.DeviceId,
            device.Player,
            nowUtc,
            nowUtc.Add(options.RefreshTokenLifetime),
            RotatedAtUtc: null);

        await store.OpenTokenFamilyAsync(family, stored, nowUtc, ct).ConfigureAwait(false);

        return Session(issuer, options, device.Player, device.DeviceId, nowUtc, refreshToken, stored.ExpiresAtUtc);
    }

    /// <summary><c>POST /auth/refresh</c> — single-use rotation within a family.</summary>
    /// <param name="store">The auth rows.</param>
    /// <param name="issuer">Mints the access token.</param>
    /// <param name="options">The lifetimes and the renewal fraction.</param>
    /// <param name="nowUtc">The request instant — injected, never read ambiently.</param>
    /// <param name="body">The request body.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentNullException">A seam or the body is null.</exception>
    public static async Task<AuthReply> HandleRefreshAsync(
        IAuthStore store,
        AccessTokenIssuer issuer,
        AuthOptions options,
        DateTimeOffset nowUtc,
        string body,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(body);

        if (!TryReadObject(body, out var request) ||
            Text(request, "refreshToken") is not { Length: > 0 } presentedToken)
        {
            return Malformed;
        }

        var presentedDigest = DeviceCredentialFactory.DigestOf(presentedToken);
        var state = await store.LoadTokenStateForDigestAsync(presentedDigest, ct).ConfigureAwait(false);

        var matched = state.Tokens.FirstOrDefault(
            token => DeviceCredentialFactory.DigestsMatch(token.TokenDigest, presentedDigest));

        // Ahead of the rotation decision, deliberately: a deleted account's families are already
        // revoked, so rotation alone would answer 401 where the account state is the real answer.
        if (matched is not null &&
            await store.IsAccountDeletedAsync(matched.Player, ct).ConfigureAwait(false))
        {
            return AccountUnavailable;
        }

        var replacementToken = DeviceCredentialFactory.OpaqueToken(RefreshTokenBytes);

        var decision = RefreshTokenRotation.Decide(
            state,
            presentedDigest,
            DeviceCredentialFactory.DigestOf(replacementToken),
            options.RefreshTokenLifetime,
            nowUtc);

        // A refusal that closes nothing writes nothing: applying every decision would let a typo
        // reach the store on behalf of a family the caller never held.
        if (decision.Accepted || decision.FamilyRevocation is not null)
        {
            await store.ApplyRotationAsync(decision, nowUtc, ct).ConfigureAwait(false);
        }

        if (!decision.Accepted)
        {
            return Unauthenticated;
        }

        var issued = decision.IssuedToken!;

        return Session(
            issuer, options, issued.Player, issued.DeviceId, nowUtc, replacementToken, issued.ExpiresAtUtc);
    }

    /// <summary><c>DELETE /account</c> — access token plus device-secret re-confirmation.</summary>
    /// <param name="store">The auth rows.</param>
    /// <param name="principals">The authenticated-player seam.</param>
    /// <param name="authorizationHeader">The request's <c>Authorization</c> header, or <c>null</c>.</param>
    /// <param name="nowUtc">The request instant — injected, never read ambiently.</param>
    /// <param name="body">The request body.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentNullException">A seam or the body is null.</exception>
    /// <remarks>
    /// 🔒 A failed secret re-confirmation is <c>403</c>, not <c>401</c>: a 401 is the status the
    /// client cures by refreshing and retrying the SAME request, which would resubmit the same wrong
    /// secret forever. It is an account-state answer, not a token answer.
    /// </remarks>
    public static async Task<AuthReply> HandleAccountDeletionAsync(
        IAuthStore store,
        IPrincipalResolver principals,
        string? authorizationHeader,
        DateTimeOffset nowUtc,
        string body,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(principals);
        ArgumentNullException.ThrowIfNull(body);

        var principal = principals.Resolve(authorizationHeader);
        if (principal.Player is not { } player)
        {
            return new AuthReply(principal.RefusalStatus!.Value, string.Empty);
        }

        if (!TryReadObject(body, out var request) ||
            Text(request, "deviceSecret") is not { } presentedSecret)
        {
            return Malformed;
        }

        var device = await store.FindDeviceForPlayerAsync(player, ct).ConfigureAwait(false);

        if (device is null || !DeviceCredentialFactory.Verify(presentedSecret, device.SecretDigest))
        {
            return AccountUnavailable;
        }

        var dueAtUtc = nowUtc.Add(HardDeleteWindow);

        await store.SoftDeleteAccountAsync(player, nowUtc, dueAtUtc, ct).ConfigureAwait(false);

        return new AuthReply(200, Json(new
        {
            playerId = player.Value,
            softDeletedAtUtc = Instant(nowUtc),
            hardDeleteDueAtUtc = Instant(dueAtUtc),
        }));
    }

    /// <summary>How long the erasure sweep has. A compliance deadline, not an operations dial.</summary>
    private static readonly TimeSpan HardDeleteWindow = TimeSpan.FromDays(30);

    /// <summary>The opaque refresh token's width, in bytes.</summary>
    private const int RefreshTokenBytes = 32;

    /// <summary>
    /// A digest of the right width that no secret hashes to, so the unknown-device arm still pays
    /// for a comparison rather than returning early and timing differently.
    /// </summary>
    private static readonly byte[] UnmatchableDigest = new byte[32];

    private const string FamilyIdPrefix = "FAMILY_";

    private const string PlayerIdPrefix = "PLAYER_";

    /// <summary>A body this handler could not read as the request it names.</summary>
    private static readonly AuthReply Malformed = new(400, string.Empty);

    /// <summary>The one answer an unknown device and a wrong secret share, byte for byte.</summary>
    private static readonly AuthReply Unauthenticated = new(401, string.Empty);

    /// <summary>Soft-deleted, locked, or a re-confirmation the account state refused.</summary>
    private static readonly AuthReply AccountUnavailable = new(403, string.Empty);

    /// <summary>The session body both issuing routes answer with.</summary>
    private static AuthReply Session(
        AccessTokenIssuer issuer,
        AuthOptions options,
        PlayerId player,
        string deviceId,
        DateTimeOffset nowUtc,
        string refreshToken,
        DateTimeOffset refreshExpiresAtUtc) =>
        new(200, Json(new
        {
            playerId = player.Value,
            accessToken = issuer.Mint(player, deviceId, nowUtc),
            accessExpiresInSeconds = (long)options.AccessTokenLifetime.TotalSeconds,
            renewAfterSeconds = options.RenewAfterSeconds,
            refreshToken,
            refreshExpiresInSeconds = (long)(refreshExpiresAtUtc - nowUtc).TotalSeconds,
        }));

    private static PlayerId NewPlayerId() =>
        new(PlayerIdPrefix + DeviceCredentialFactory.OpaqueToken(16));

    private static string Instant(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static string Json<T>(T value) => JsonSerializer.Serialize(value);

    private static bool TryReadObject(string body, out JsonElement element)
    {
        element = default;

        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            element = document.RootElement.Clone();

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
