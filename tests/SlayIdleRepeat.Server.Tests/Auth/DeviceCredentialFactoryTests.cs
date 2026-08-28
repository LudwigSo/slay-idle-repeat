using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Server.Auth;
using Xunit;

namespace SlayIdleRepeat.Server.Tests.Auth;

/// <summary>
/// The device secret: how much entropy it carries, what is stored instead of it, and what
/// verification accepts.
/// </summary>
/// <remarks>
/// No case writes a minted secret to stdout. Where a leak has to be asserted, the assertion answers
/// a boolean rather than echoing the string it is checking.
/// </remarks>
public sealed class DeviceCredentialFactoryTests
{
    private static readonly PlayerId Player = new("PLAYER_device_owner");

    [Fact]
    public void Mint_produces_a_secret_of_exactly_256_bits()
    {
        var minted = DeviceCredentialFactory.Mint(Player, AuthFixtures.Now);

        AuthFixtures.FromBase64Url(minted.DeviceSecret).Length.ShouldBe(
            32,
            "the decoded byte count is the entropy; the base64url text is a third longer than that " +
            "and asserting its length would pass on a 24-byte secret.");
    }

    [Fact]
    public void Mint_produces_a_different_secret_every_time()
    {
        var first = DeviceCredentialFactory.Mint(Player, AuthFixtures.Now);
        var second = DeviceCredentialFactory.Mint(Player, AuthFixtures.Now);

        second.DeviceSecret.ShouldNotBe(
            first.DeviceSecret,
            "same player, same instant: a secret derived from either would be the same secret for " +
            "every device that account ever registers.");
        second.Device.DeviceId.ShouldNotBe(first.Device.DeviceId);
    }

    [Fact]
    public void Mint_stores_the_sha256_of_the_secret_and_not_the_secret()
    {
        var minted = DeviceCredentialFactory.Mint(Player, AuthFixtures.Now);

        minted.Device.SecretDigest.ShouldBe(
            AuthFixtures.Digest(minted.DeviceSecret),
            "the stored form is SHA-256 over the secret's UTF-8 bytes — canonical bytes, so a " +
            "different encoding or a salt would show up here rather than at the first live login.");
    }

    [Fact]
    public void Mint_leaves_the_raw_secret_out_of_the_stored_row_entirely()
    {
        var minted = DeviceCredentialFactory.Mint(Player, AuthFixtures.Now);

        // Answered as a boolean on purpose: a failure must not print the value it found.
        minted.Device.ToString().Contains(minted.DeviceSecret, StringComparison.Ordinal).ShouldBeFalse(
            "the stored row is what a database dump contains. The secret is returned to the caller " +
            "once and lives only in the platform keystore afterwards.");
    }

    [Fact]
    public void Verify_accepts_the_secret_that_produced_the_stored_digest()
    {
        var minted = DeviceCredentialFactory.Mint(Player, AuthFixtures.Now);

        DeviceCredentialFactory.Verify(minted.DeviceSecret, minted.Device.SecretDigest).ShouldBeTrue(
            "the credential the server issued must open the account it issued it for.");
    }

    [Theory]
    [InlineData("fixture-secret-that-was-never-minted")]
    [InlineData("")]
    [InlineData("   ")]
    public void Verify_refuses_a_secret_that_is_not_the_stored_one(string presented)
    {
        var stored = AuthFixtures.Digest("fixture-device-secret-alpha");

        DeviceCredentialFactory.Verify(presented, stored).ShouldBeFalse(
            "a wrong or empty secret is a refusal, not a fault — an empty string hashing to " +
            "something must never be mistaken for a match against an empty stored digest.");
    }

    [Fact]
    public void Verify_refuses_a_truncated_secret()
    {
        const string secret = "fixture-device-secret-alpha";

        DeviceCredentialFactory.Verify(secret[..^1], AuthFixtures.Digest(secret)).ShouldBeFalse(
            "a prefix of the right secret is not the right secret; a comparison over a prefix " +
            "length would accept every truncation.");
    }

    [Fact]
    public void Verify_refuses_a_null_secret()
    {
        DeviceCredentialFactory.Verify(null, AuthFixtures.Digest("fixture-device-secret-alpha"))
            .ShouldBeFalse("a request that sent no secret is refused, never a 500.");
    }

    /// <summary>
    /// The comparison's RESULT does not depend on where the first difference falls. Its timing is
    /// what actually matters and is not observable from a unit test — asserted by the production
    /// call going through a fixed-time primitive, which this suite cannot see, so this case pins
    /// only the result half and says so rather than pretending otherwise.
    /// </summary>
    [Fact]
    public void DigestsMatch_answers_the_same_wherever_the_first_difference_falls_timing_aside()
    {
        var digest = AuthFixtures.Digest("fixture-device-secret-alpha");

        var differsFirst = (byte[])digest.Clone();
        differsFirst[0] ^= 0xFF;

        var differsLast = (byte[])digest.Clone();
        differsLast[^1] ^= 0xFF;

        DeviceCredentialFactory.DigestsMatch(digest, (byte[])digest.Clone()).ShouldBeTrue();
        DeviceCredentialFactory.DigestsMatch(digest, differsFirst).ShouldBeFalse();
        DeviceCredentialFactory.DigestsMatch(digest, differsLast).ShouldBeFalse();
    }

    [Fact]
    public void DigestsMatch_refuses_a_digest_of_the_wrong_length_instead_of_throwing()
    {
        var digest = AuthFixtures.Digest("fixture-device-secret-alpha");

        DeviceCredentialFactory.DigestsMatch(digest, digest[..16]).ShouldBeFalse(
            "a stored value of the wrong width is corrupt data on the login path; it must refuse " +
            "the login, not fault the request.");
    }

    [Fact]
    public void DigestOf_is_sha256_over_the_utf8_bytes()
    {
        // The published SHA-256 vector for "abc", so a changed algorithm or encoding fails here
        // rather than silently invalidating every stored credential.
        Convert.ToHexString(DeviceCredentialFactory.DigestOf("abc")).ToLowerInvariant().ShouldBe(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }
}
