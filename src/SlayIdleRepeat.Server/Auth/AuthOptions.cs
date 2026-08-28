using Microsoft.Extensions.Configuration;

namespace SlayIdleRepeat.Server.Auth;

/// <summary>
/// The auth deployment contract: the signing key and the three token-lifetime numbers, bound from
/// environment configuration rather than from the content set.
/// </summary>
/// <remarks>
/// <para>
/// These are server-operations numbers, not economy dials: they must never ride a content push, so
/// they are read from <c>IConfiguration</c> (the <c>Auth__*</c> variables the compose file authors)
/// and nowhere else.
/// </para>
/// <para>
/// 🔒 The signing key has NO default, generated or otherwise. A generated one would make every
/// restart invalidate every live token while looking like it worked; a baked-in one would ship a
/// public secret. Absence throws, naming the variable.
/// </para>
/// </remarks>
public sealed class AuthOptions
{
    /// <summary>The configuration section the four settings bind from.</summary>
    public const string SectionName = "Auth";

    /// <summary>Builds the options directly — the shape a test or the composition root already holds.</summary>
    /// <param name="jwtSigningKey">The HS256 key, as text; its UTF-8 bytes are the key.</param>
    /// <param name="accessTokenLifetime">How long a minted access token is valid for.</param>
    /// <param name="refreshTokenLifetime">How long a token family lives.</param>
    /// <param name="silentRenewalFraction">The point in the access lifetime the client renews at.</param>
    public AuthOptions(
        string jwtSigningKey,
        TimeSpan accessTokenLifetime,
        TimeSpan refreshTokenLifetime,
        double silentRenewalFraction)
    {
        JwtSigningKey = jwtSigningKey;
        AccessTokenLifetime = accessTokenLifetime;
        RefreshTokenLifetime = refreshTokenLifetime;
        SilentRenewalFraction = silentRenewalFraction;
    }

    /// <summary>The HS256 signing key. Never empty, never generated, never defaulted.</summary>
    public string JwtSigningKey { get; }

    /// <summary>The access token's validity window.</summary>
    public TimeSpan AccessTokenLifetime { get; }

    /// <summary>The refresh token family's validity window.</summary>
    public TimeSpan RefreshTokenLifetime { get; }

    /// <summary>The fraction of the access lifetime at which the client renews in the background.</summary>
    public double SilentRenewalFraction { get; }

    /// <summary>
    /// What every session body tells the client: renew after this many seconds. Derived here so the
    /// fraction stays server-side configuration instead of being duplicated in client code.
    /// </summary>
    public long RenewAfterSeconds => throw new NotImplementedException(
        "M5-06 Phase 3: floor(AccessTokenLifetime.TotalSeconds * SilentRenewalFraction).");

    /// <summary>Binds the four settings, applying the documented defaults and refusing the rest.</summary>
    /// <param name="configuration">The host's configuration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The signing key is absent, blank or too short, or one of the three numbers is out of range.
    /// The message names the environment variable, so a failed boot is greppable.
    /// </exception>
    public static AuthOptions Bind(IConfiguration configuration) => throw new NotImplementedException(
        "M5-06 Phase 3: read Auth:JwtSigningKey (required, no default), " +
        "Auth:AccessTokenLifetimeMinutes (60), Auth:RefreshTokenLifetimeDays (30) and " +
        "Auth:SilentRenewalFraction (0.8), refusing an absent key, a key under 32 UTF-8 bytes, a " +
        "non-positive lifetime and a fraction outside (0, 1).");
}
