using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Server.Auth;

/// <summary>Everything one access token asserts, decoded.</summary>
/// <param name="Issuer">Fixed, this service.</param>
/// <param name="Audience">Fixed, this game's client.</param>
/// <param name="Player">The <c>sub</c> claim.</param>
/// <param name="DeviceId">Which device the session was opened from.</param>
/// <param name="TokenId">The <c>jti</c>: a fresh 128-bit id per token.</param>
/// <param name="IssuedAtUtc">The <c>iat</c> claim.</param>
/// <param name="ExpiresAtUtc">The <c>exp</c> claim.</param>
public sealed record AccessTokenClaims(
    string Issuer,
    string Audience,
    PlayerId Player,
    string DeviceId,
    string TokenId,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc);

/// <summary>Which validation rule refused a token. One member per rule, never a shared "invalid".</summary>
public enum AccessTokenRefusal
{
    /// <summary>Not three base64url segments carrying JSON — nothing to check further.</summary>
    MALFORMED = 1,

    /// <summary>The header names an algorithm this service does not verify, <c>none</c> above all.</summary>
    UNSUPPORTED_ALGORITHM = 2,

    /// <summary>The signature does not match the key, or the payload changed after signing.</summary>
    SIGNATURE_MISMATCH = 3,

    /// <summary><c>exp</c> is at or before the presented instant.</summary>
    EXPIRED = 4,

    /// <summary><c>iss</c> is not this service.</summary>
    WRONG_ISSUER = 5,

    /// <summary><c>aud</c> is not this game's client.</summary>
    WRONG_AUDIENCE = 6,
}

/// <summary>The outcome of validating one token: the claims, or which rule refused it.</summary>
public sealed record AccessTokenValidation
{
    private AccessTokenValidation(AccessTokenClaims? claims, AccessTokenRefusal? refusal)
    {
        Claims = claims;
        Refusal = refusal;
    }

    /// <summary>The decoded claims, or <c>null</c> on a refusal.</summary>
    public AccessTokenClaims? Claims { get; }

    /// <summary>Which rule fired, or <c>null</c> when the token is good.</summary>
    public AccessTokenRefusal? Refusal { get; }

    /// <summary>A token that passed every rule.</summary>
    public static AccessTokenValidation Valid(AccessTokenClaims claims) => new(claims, null);

    /// <summary>A token one named rule refused.</summary>
    public static AccessTokenValidation Refused(AccessTokenRefusal refusal) => new(null, refusal);
}

/// <summary>Mints and validates this service's access tokens. HS256, one key, both directions.</summary>
/// <remarks>
/// Symmetric on purpose: one service both signs and verifies, so a public key would have no second
/// reader and asymmetric signing would buy key-distribution machinery for nobody. Do not "upgrade"
/// this to RS256 without a second verifier to point at.
/// </remarks>
public sealed class AccessTokenIssuer
{
    /// <summary>The fixed <c>iss</c> claim.</summary>
    public const string Issuer = "slayidlerepeat-server";

    /// <summary>The fixed <c>aud</c> claim.</summary>
    public const string Audience = "slayidlerepeat-client";

    private readonly AuthOptions _options;

    /// <summary>Builds the issuer over the deployment's signing key and access lifetime.</summary>
    /// <param name="options">The bound auth options.</param>
    public AccessTokenIssuer(AuthOptions options) => _options = options;

    /// <summary>The options this issuer signs and times against.</summary>
    internal AuthOptions Options => _options;

    /// <summary>Mints one access token for a player on a device.</summary>
    /// <param name="player">The <c>sub</c> claim.</param>
    /// <param name="deviceId">The device the session belongs to.</param>
    /// <param name="nowUtc">The instant to stamp <c>iat</c> from — injected, never read ambiently.</param>
    public string Mint(PlayerId player, string deviceId, DateTimeOffset nowUtc)
    {
        var expiresAt = nowUtc.Add(_options.AccessTokenLifetime);

        var header = "{\"" + AlgorithmClaim + "\":\"" + Hs256 + "\",\"typ\":\"JWT\"}";

        var payload =
            "{\"iss\":" + JsonText(Issuer) +
            ",\"aud\":" + JsonText(Audience) +
            ",\"sub\":" + JsonText(player.Value) +
            ",\"" + DeviceClaim + "\":" + JsonText(deviceId) +
            ",\"jti\":" + JsonText(NewTokenId()) +
            ",\"iat\":" + nowUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) +
            ",\"exp\":" + expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) +
            "}";

        var signingInput = Base64Url(Encoding.UTF8.GetBytes(header)) + "." +
                           Base64Url(Encoding.UTF8.GetBytes(payload));

        return signingInput + "." + Base64Url(SignatureOf(signingInput));
    }

    /// <summary>Validates one presented token against the key, the fixed iss/aud and the clock.</summary>
    /// <param name="token">The token as presented.</param>
    /// <param name="nowUtc">The instant <c>exp</c> is measured against — injected, never ambient.</param>
    public AccessTokenValidation Validate(string? token, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return AccessTokenValidation.Refused(AccessTokenRefusal.MALFORMED);
        }

        var segments = token.Split('.');
        if (segments.Length != 3 ||
            !TryReadObject(segments[0], out var header) ||
            !TryReadObject(segments[1], out var payload))
        {
            return AccessTokenValidation.Refused(AccessTokenRefusal.MALFORMED);
        }

        // An ALLOW-LIST of one, checked before anything reads the payload. Honouring the header's
        // own choice is what makes `alg: none` a forgery, but the rule is "an algorithm this
        // service does not verify" — HS512 and RS256 answer here too, not at the signature check.
        if (Text(header, AlgorithmClaim) != Hs256)
        {
            return AccessTokenValidation.Refused(AccessTokenRefusal.UNSUPPORTED_ALGORITHM);
        }

        if (!TryDecode(segments[2], out var presentedSignature) ||
            !CryptographicOperations.FixedTimeEquals(
                presentedSignature, SignatureOf(segments[0] + "." + segments[1])))
        {
            return AccessTokenValidation.Refused(AccessTokenRefusal.SIGNATURE_MISMATCH);
        }

        if (Text(payload, "iss") != Issuer)
        {
            return AccessTokenValidation.Refused(AccessTokenRefusal.WRONG_ISSUER);
        }

        if (Text(payload, "aud") != Audience)
        {
            return AccessTokenValidation.Refused(AccessTokenRefusal.WRONG_AUDIENCE);
        }

        if (Seconds(payload, "exp") is not { } expiresAt ||
            Seconds(payload, "iat") is not { } issuedAt ||
            Text(payload, "sub") is not { Length: > 0 } subject ||
            Text(payload, DeviceClaim) is not { Length: > 0 } deviceId ||
            Text(payload, "jti") is not { Length: > 0 } tokenId)
        {
            return AccessTokenValidation.Refused(AccessTokenRefusal.MALFORMED);
        }

        if (expiresAt <= nowUtc)
        {
            return AccessTokenValidation.Refused(AccessTokenRefusal.EXPIRED);
        }

        return AccessTokenValidation.Valid(new AccessTokenClaims(
            Issuer, Audience, new PlayerId(subject), deviceId, tokenId, issuedAt, expiresAt));
    }

    private const string Hs256 = "HS256";

    private const string AlgorithmClaim = "alg";

    private const string DeviceClaim = "deviceId";

    /// <summary>A fresh 128-bit token identity, so two tokens minted in one tick differ.</summary>
    private static string NewTokenId() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private byte[] SignatureOf(string signingInput) => HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(_options.JwtSigningKey), Encoding.UTF8.GetBytes(signingInput));

    private static string JsonText(string value) => JsonSerializer.Serialize(value);

    // One encoder for the area: this was a byte-identical second copy of the credential factory's,
    // two files apart in the same directory, from two agents that could not see each other.
    private static string Base64Url(byte[] bytes) => DeviceCredentialFactory.Base64Url(bytes);

    private static bool TryDecode(string segment, out byte[] bytes)
    {
        var padded = segment.Replace('-', '+').Replace('_', '/');
        bytes = Array.Empty<byte>();

        Span<byte> decoded = new byte[((padded.Length + 3) / 4) * 3];

        if (!Convert.TryFromBase64Chars(
                (padded + new string('=', (4 - (padded.Length % 4)) % 4)).AsSpan(),
                decoded,
                out var written))
        {
            return false;
        }

        bytes = decoded[..written].ToArray();

        return true;
    }

    private static bool TryReadObject(string segment, out JsonElement element)
    {
        element = default;

        if (!TryDecode(segment, out var bytes))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(bytes);

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

    /// <summary>One numeric-date claim, or <c>null</c> when it is absent or unreadable.</summary>
    /// <remarks>
    /// The range is checked, not assumed: <c>TryGetInt64</c> accepts values <c>FromUnixTimeSeconds</c>
    /// refuses, and an <see cref="ArgumentOutOfRangeException"/> out of a method whose entire
    /// contract is to answer with a refusal would be the validator failing instead of refusing. Not
    /// attacker-reachable today — the signature is checked before any claim is read — but this is
    /// the seam a second issuer would open.
    /// </remarks>
    private static DateTimeOffset? Seconds(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var seconds) &&
        seconds >= DateTimeOffset.MinValue.ToUnixTimeSeconds() &&
        seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds()
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
}
