using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Server.Auth;

/// <summary>One stored device row — the anonymous account's root credential, hashed.</summary>
/// <param name="DeviceId">The server-minted opaque device id.</param>
/// <param name="Player">The account this device is the credential for.</param>
/// <param name="SecretDigest">SHA-256 of the device secret's UTF-8 bytes. The secret itself is returned once and stored nowhere.</param>
/// <param name="CreatedAtUtc">When the device was issued.</param>
/// <param name="LastSeenAtUtc">The authoritative last-seen instant, restamped on each session.</param>
public sealed record AuthDevice(
    string DeviceId,
    PlayerId Player,
    byte[] SecretDigest,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenAtUtc);

/// <summary>A freshly minted credential: the once-returned secret, and the row that outlives it.</summary>
/// <param name="DeviceSecret">256 bits of CSPRNG output, base64url. Returned to the caller once and never persisted.</param>
/// <param name="Device">What is actually stored — a digest and timestamps.</param>
public sealed record MintedDeviceCredential(string DeviceSecret, AuthDevice Device);

/// <summary>Why a token family was closed.</summary>
public enum TokenFamilyRevocation
{
    /// <summary>An already-rotated refresh token was presented — the family is burned.</summary>
    REFRESH_REUSE = 1,

    /// <summary>The account was soft-deleted; every family goes with it.</summary>
    ACCOUNT_DELETED = 2,
}

/// <summary>One refresh-token family: per device, opened by a successful device-secret authentication.</summary>
/// <param name="FamilyId">The family's opaque id.</param>
/// <param name="DeviceId">The device the family belongs to. Families are per device; several may be live at once.</param>
/// <param name="Player">The account.</param>
/// <param name="CreatedAtUtc">When the family opened — the horizon rotation may not extend past.</param>
/// <param name="RevokedAtUtc"><c>null</c> means live.</param>
/// <param name="RevokedReason">Set with <paramref name="RevokedAtUtc"/>.</param>
public sealed record AuthTokenFamily(
    string FamilyId,
    string DeviceId,
    PlayerId Player,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    TokenFamilyRevocation? RevokedReason);

/// <summary>One refresh token, as stored: a digest, never the token.</summary>
/// <param name="TokenDigest">SHA-256 of the opaque token's UTF-8 bytes.</param>
/// <param name="FamilyId">The family it belongs to.</param>
/// <param name="DeviceId">The device.</param>
/// <param name="Player">The account.</param>
/// <param name="IssuedAtUtc">When it was issued.</param>
/// <param name="ExpiresAtUtc">Never past its family's own horizon.</param>
/// <param name="RotatedAtUtc"><c>null</c> means live; set makes a second presentation a reuse.</param>
public sealed record AuthRefreshToken(
    byte[] TokenDigest,
    string FamilyId,
    string DeviceId,
    PlayerId Player,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? RotatedAtUtc);

/// <summary>The family/token rows a rotation decides over. Pure data — no store, no connection.</summary>
/// <param name="Families">Every family in scope.</param>
/// <param name="Tokens">Every token in scope, across those families.</param>
public sealed record AuthTokenState(
    IReadOnlyList<AuthTokenFamily> Families,
    IReadOnlyList<AuthRefreshToken> Tokens)
{
    /// <summary>Nothing matched — the shape a lookup for an unknown digest returns.</summary>
    public static AuthTokenState Empty { get; } = new(Array.Empty<AuthTokenFamily>(), Array.Empty<AuthRefreshToken>());
}
