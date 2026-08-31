using System.Globalization;
using System.Text;
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
    public long RenewAfterSeconds =>
        (long)Math.Floor(AccessTokenLifetime.TotalSeconds * SilentRenewalFraction);

    /// <summary>Binds the four settings, applying the documented defaults and refusing the rest.</summary>
    /// <param name="configuration">The host's configuration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The signing key is absent, blank or too short, or one of the three numbers is out of range.
    /// The message names the environment variable, so a failed boot is greppable.
    /// </exception>
    public static AuthOptions Bind(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);

        var signingKey = section["JwtSigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw Misconfigured(
                "JwtSigningKey",
                "no value. It has no default: a generated one would invalidate every live token on " +
                "each restart while looking like it worked, and a baked-in one would ship a public " +
                "secret.");
        }

        // HS256's own digest is 256 bits; a key shorter than that is the weakest link in a chain
        // whose whole point is that it has none.
        if (Encoding.UTF8.GetByteCount(signingKey) < MinimumSigningKeyBytes)
        {
            throw Misconfigured(
                "JwtSigningKey",
                "a key of fewer than " + MinimumSigningKeyBytes + " UTF-8 bytes.");
        }

        var minutes = Number(section, "AccessTokenLifetimeMinutes", 60d);
        if (minutes <= 0)
        {
            throw Misconfigured(
                "AccessTokenLifetimeMinutes",
                "a value at or below zero, which expires every token at or before it is issued.");
        }

        var days = Number(section, "RefreshTokenLifetimeDays", 30d);
        if (days <= 0)
        {
            throw Misconfigured(
                "RefreshTokenLifetimeDays",
                "a value at or below zero, which expires every token family at birth.");
        }

        var fraction = Number(section, "SilentRenewalFraction", 0.8d);
        if (fraction is <= 0 or >= 1)
        {
            throw Misconfigured(
                "SilentRenewalFraction",
                "a value outside the open interval (0, 1). At or below zero the client renews " +
                "continuously; at or above one it renews only once the token it was renewing has " +
                "already expired.");
        }

        return new AuthOptions(
            signingKey, TimeSpan.FromMinutes(minutes), TimeSpan.FromDays(days), fraction);
    }

    /// <summary>The signing-key floor, in UTF-8 bytes: HS256's own digest width.</summary>
    private const int MinimumSigningKeyBytes = 32;

    private static double Number(IConfiguration section, string name, double fallback)
    {
        var configured = section[name];

        if (string.IsNullOrWhiteSpace(configured))
        {
            return fallback;
        }

        if (!double.TryParse(configured, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw Misconfigured(name, "a value that is not a number.");
        }

        // 🔒 Refused here rather than by each range check below, because NaN passes every one of
        // them: every comparison against NaN is false, so `fraction is <= 0 or >= 1` lets it
        // through and `(long)Math.Floor(NaN)` is an unspecified conversion — long.MinValue on x64,
        // zero on ARM64 — either of which tells the client to renew on every single request. The
        // strictest-looking setting would become no renewal policy at all.
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw Misconfigured(name, "a value that is not a finite number.");
        }

        return value;
    }

    /// <summary>
    /// The failure a deployment reads. It spells the variable the way the compose file does — with
    /// the double underscore — so a failed boot is greppable straight into the environment.
    /// </summary>
    private static InvalidOperationException Misconfigured(string setting, string problem) =>
        new(SectionName + "__" + setting + " carries " + problem);
}
