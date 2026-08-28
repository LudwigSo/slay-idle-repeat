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
    public string Mint(PlayerId player, string deviceId, DateTimeOffset nowUtc) =>
        throw new NotImplementedException(
            "M5-06 Phase 3: HS256 over the fixed claim set (iss, aud, sub, deviceId, jti, iat, exp) " +
            "and nothing else, with exp = iat + AccessTokenLifetime and a fresh 128-bit jti.");

    /// <summary>Validates one presented token against the key, the fixed iss/aud and the clock.</summary>
    /// <param name="token">The token as presented.</param>
    /// <param name="nowUtc">The instant <c>exp</c> is measured against — injected, never ambient.</param>
    public AccessTokenValidation Validate(string? token, DateTimeOffset nowUtc) =>
        throw new NotImplementedException(
            "M5-06 Phase 3: check shape, then alg, then signature, then iss, then aud, then exp — " +
            "and answer with the rule that fired, never a shared 'invalid'.");
}
