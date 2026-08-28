using System.Security.Cryptography;
using System.Text;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Server.Auth;

/// <summary>Mints and checks the device secret — the anonymous account's root credential.</summary>
/// <remarks>
/// 🔒 <b>SHA-256, deliberately, and not bcrypt/argon2/PBKDF2.</b> A slow KDF exists to make a
/// LOW-ENTROPY secret expensive to guess. This secret is 256 bits straight out of a CSPRNG: it is
/// not guessable at any speed, and a work factor would buy nothing but latency on every session
/// request. Do not "fix" this into a password hash.
/// </remarks>
public static class DeviceCredentialFactory
{
    /// <summary>Mints a device id and a 256-bit secret, returning the secret once and the row to store.</summary>
    /// <param name="player">The account the device belongs to.</param>
    /// <param name="nowUtc">The issue instant — injected, never read ambiently.</param>
    public static MintedDeviceCredential Mint(PlayerId player, DateTimeOffset nowUtc)
    {
        var secret = OpaqueToken(SecretBytes);

        return new MintedDeviceCredential(
            secret,
            new AuthDevice(DeviceIdPrefix + OpaqueToken(16), player, DigestOf(secret), nowUtc, nowUtc));
    }

    /// <summary>Base64url over the requested count of CSPRNG bytes — the shape every opaque credential here takes.</summary>
    /// <param name="byteCount">How many random bytes the value carries.</param>
    public static string OpaqueToken(int byteCount) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteCount))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>The stored form of a secret: SHA-256 over its UTF-8 bytes.</summary>
    /// <param name="secret">The secret as presented or as minted.</param>
    public static byte[] DigestOf(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    /// <summary>Whether a presented secret is the one behind a stored digest.</summary>
    /// <param name="presentedSecret">What the caller sent. Null and empty are refusals, not faults.</param>
    /// <param name="storedDigest">The stored digest.</param>
    public static bool Verify(string? presentedSecret, byte[] storedDigest) =>
        presentedSecret is not null && DigestsMatch(DigestOf(presentedSecret), storedDigest);

    /// <summary>Compares two digests through a fixed-time primitive.</summary>
    /// <param name="left">One digest.</param>
    /// <param name="right">The other.</param>
    /// <remarks>
    /// The comparison must not return early on the first differing byte — an early-out leaks how much
    /// of a guess was right. <c>CryptographicOperations.FixedTimeEquals</c> is the primitive; a
    /// <c>SequenceEqual</c> here would be a real regression that no unit test can observe, which is
    /// why it is spelled out rather than left to a reviewer's memory.
    /// </remarks>
    public static bool DigestsMatch(byte[] left, byte[] right) =>
        CryptographicOperations.FixedTimeEquals(left, right);

    /// <summary>The secret's width, in bytes: the 256 bits that make a slow KDF pointless here.</summary>
    private const int SecretBytes = 32;

    /// <summary>The device id's prefix, so a value read out of a log or a row names its own kind.</summary>
    private const string DeviceIdPrefix = "DEVICE_";
}
