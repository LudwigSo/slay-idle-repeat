using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SlayIdleRepeat.Server.Auth;

namespace SlayIdleRepeat.Server.Tests.Auth;

/// <summary>
/// The auth suite's shared fixture values and the hand-rolled JWT the token tests forge with.
/// </summary>
/// <remarks>
/// 🔒 Every secret-shaped literal here is obviously fake and is never written to stdout: a fixture
/// that prints a device secret or a raw refresh token is the same leak as production code doing it.
/// The JWT helpers are hand-rolled over the BCL rather than a library so a forged token — a swapped
/// payload, <c>alg: none</c>, a foreign key — is constructible at all.
/// </remarks>
internal static class AuthFixtures
{
    /// <summary>A signing key comfortably over the floor. Not a secret: it is in the repository.</summary>
    internal const string SigningKey = "fixture-signing-key-not-a-real-secret-0123456789";

    /// <summary>A second key, for the wrong-signature arm.</summary>
    internal const string OtherSigningKey = "fixture-other-signing-key-also-not-a-secret-98765";

    /// <summary>A fixed instant every time-dependent case is written against.</summary>
    internal static readonly DateTimeOffset Now = new(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The options the suite mints and validates against: the documented defaults.</summary>
    internal static AuthOptions Options(string signingKey = SigningKey) => new(
        signingKey,
        TimeSpan.FromMinutes(60),
        TimeSpan.FromDays(30),
        0.8);

    /// <summary>SHA-256 over a value's UTF-8 bytes — the stored form of every secret here.</summary>
    internal static byte[] Digest(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    /// <summary>base64url, no padding.</summary>
    internal static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>base64url back to bytes.</summary>
    internal static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');

        return Convert.FromBase64String(padded + new string('=', (4 - (padded.Length % 4)) % 4));
    }

    /// <summary>One segment of a token, decoded as JSON.</summary>
    internal static JsonElement SegmentOf(string token, int index) =>
        JsonDocument.Parse(FromBase64Url(token.Split('.')[index])).RootElement;

    /// <summary>The token's header, decoded.</summary>
    internal static JsonElement HeaderOf(string token) => SegmentOf(token, 0);

    /// <summary>The token's payload, decoded.</summary>
    internal static JsonElement PayloadOf(string token) => SegmentOf(token, 1);

    /// <summary>The property names one JSON object carries, ordinally sorted.</summary>
    internal static string[] NamesOf(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    /// <summary>Forges an HS256 token over the given header and payload JSON.</summary>
    internal static string Sign(string headerJson, string payloadJson, string key)
    {
        var signingInput = Base64Url(Encoding.UTF8.GetBytes(headerJson)) + "." +
                           Base64Url(Encoding.UTF8.GetBytes(payloadJson));

        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(signingInput));

        return signingInput + "." + Base64Url(signature);
    }

    /// <summary>The header every honestly signed token carries.</summary>
    internal const string Hs256Header = "{\"alg\":\"HS256\",\"typ\":\"JWT\"}";

    /// <summary>A well-formed payload with every claim the contract names, tweakable per case.</summary>
    internal static string PayloadJson(
        string issuer = AccessTokenIssuer.Issuer,
        string audience = AccessTokenIssuer.Audience,
        string subject = "PLAYER_forged",
        string deviceId = "DEVICE_forged",
        string tokenId = "0123456789abcdef0123456789abcdef",
        long? issuedAt = null,
        long? expiresAt = null) =>
        "{\"iss\":\"" + issuer + "\",\"aud\":\"" + audience + "\",\"sub\":\"" + subject +
        "\",\"deviceId\":\"" + deviceId + "\",\"jti\":\"" + tokenId +
        "\",\"iat\":" + (issuedAt ?? Now.ToUnixTimeSeconds()).ToString(System.Globalization.CultureInfo.InvariantCulture) +
        ",\"exp\":" + (expiresAt ?? Now.AddMinutes(60).ToUnixTimeSeconds()).ToString(System.Globalization.CultureInfo.InvariantCulture) +
        "}";

    /// <summary>Replaces a signed token's payload without re-signing — the tampering arm.</summary>
    internal static string WithSwappedPayload(string token, string payloadJson)
    {
        var segments = token.Split('.');

        return segments[0] + "." + Base64Url(Encoding.UTF8.GetBytes(payloadJson)) + "." + segments[2];
    }
}
