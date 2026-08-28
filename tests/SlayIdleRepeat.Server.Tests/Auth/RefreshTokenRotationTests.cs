using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Server.Auth;
using Xunit;

namespace SlayIdleRepeat.Server.Tests.Auth;

/// <summary>
/// The single-use rotating refresh rule as the pure decision it is: what a live token does, what a
/// reuse costs, and what each refusal is allowed to touch.
/// </summary>
public sealed class RefreshTokenRotationTests
{
    private static readonly PlayerId Player = new("PLAYER_refresh_holder");

    private const string Device = "DEVICE_refresh_holder";

    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    // Obviously-fake token TEXT never leaves this file; only digests reach the subject.
    private static readonly byte[] LiveDigest = AuthFixtures.Digest("fixture-refresh-token-live");
    private static readonly byte[] SpentDigest = AuthFixtures.Digest("fixture-refresh-token-spent");
    private static readonly byte[] SiblingDigest = AuthFixtures.Digest("fixture-refresh-token-sibling");
    private static readonly byte[] ReplacementDigest = AuthFixtures.Digest("fixture-refresh-token-next");

    private static AuthTokenFamily Family(
        string familyId = "FAMILY_alpha",
        DateTimeOffset? createdAtUtc = null,
        DateTimeOffset? revokedAtUtc = null,
        TokenFamilyRevocation? reason = null) =>
        new(familyId, Device, Player, createdAtUtc ?? AuthFixtures.Now, revokedAtUtc, reason);

    private static AuthRefreshToken Token(
        byte[] digest,
        string familyId = "FAMILY_alpha",
        DateTimeOffset? expiresAtUtc = null,
        DateTimeOffset? rotatedAtUtc = null) =>
        new(digest, familyId, Device, Player, AuthFixtures.Now,
            expiresAtUtc ?? AuthFixtures.Now.Add(Lifetime), rotatedAtUtc);

    private static AuthTokenState State(AuthTokenFamily family, params AuthRefreshToken[] tokens) =>
        new(new[] { family }, tokens);

    [Fact]
    public void Decide_rotates_a_live_token_into_the_same_family()
    {
        var decision = RefreshTokenRotation.Decide(
            State(Family(), Token(LiveDigest)),
            LiveDigest,
            ReplacementDigest,
            Lifetime,
            AuthFixtures.Now.AddHours(1));

        decision.Accepted.ShouldBeTrue();
        decision.FamilyId.ShouldBe("FAMILY_alpha");
        decision.RotatedTokenDigest.ShouldBe(
            LiveDigest, "the presented token is spent by the rotation that replaced it — that is " +
                        "what makes a second presentation a detectable reuse.");
        decision.IssuedToken.ShouldNotBeNull();
        decision.IssuedToken.FamilyId.ShouldBe(
            "FAMILY_alpha",
            "a replacement in a NEW family would make every rotation look like a fresh login and " +
            "leave reuse undetectable.");
        decision.RevokedTokenDigests.ShouldBeEmpty("a healthy rotation revokes nothing.");
        decision.FamilyRevocation.ShouldBeNull();
    }

    [Fact]
    public void Decide_caps_the_replacement_at_the_familys_own_horizon()
    {
        // The family opened 29 days ago, so one day of its 30 remains. A replacement dated 30 days
        // from now would make a family immortal by refreshing it, which is the whole reason the
        // horizon is the family's and not the token's.
        var opened = AuthFixtures.Now.AddDays(-29);

        var decision = RefreshTokenRotation.Decide(
            State(Family(createdAtUtc: opened), Token(LiveDigest, expiresAtUtc: opened.Add(Lifetime))),
            LiveDigest,
            ReplacementDigest,
            Lifetime,
            AuthFixtures.Now);

        decision.IssuedToken.ShouldNotBeNull();
        decision.IssuedToken.ExpiresAtUtc.ShouldBe(
            opened.Add(Lifetime),
            "the family opened at a device-secret authentication and lives 30 days from THERE.");
    }

    [Fact]
    public void Decide_puts_the_supplied_digest_on_the_replacement_and_never_a_token()
    {
        const string rawReplacement = "fixture-refresh-token-next";

        var decision = RefreshTokenRotation.Decide(
            State(Family(), Token(LiveDigest)), LiveDigest, ReplacementDigest, Lifetime, AuthFixtures.Now);

        decision.IssuedToken.ShouldNotBeNull();
        decision.IssuedToken.TokenDigest.ShouldBe(
            ReplacementDigest,
            "the caller mints the opaque token and hands this rule only its digest, so there is no " +
            "point in the decision at which a raw token exists to be written down.");
        decision.IssuedToken.ToString().Contains(rawReplacement, StringComparison.Ordinal).ShouldBeFalse(
            "the row that reaches the store carries a digest and nothing that could be replayed.");
    }

    [Fact]
    public void Decide_revokes_every_token_in_the_family_when_a_rotated_token_is_presented()
    {
        var decision = RefreshTokenRotation.Decide(
            State(
                Family(),
                Token(SpentDigest, rotatedAtUtc: AuthFixtures.Now.AddMinutes(-5)),
                Token(SiblingDigest)),
            SpentDigest,
            ReplacementDigest,
            Lifetime,
            AuthFixtures.Now);

        decision.Refusal.ShouldBe(
            RefreshRefusal.ALREADY_ROTATED,
            "a token presented twice is either a replay or a stolen one; either way the family is " +
            "no longer trustworthy.");
        decision.FamilyRevocation.ShouldBe(TokenFamilyRevocation.REFRESH_REUSE);
        decision.FamilyId.ShouldBe("FAMILY_alpha");
        decision.IssuedToken.ShouldBeNull("a burned family issues nothing.");

        decision.RevokedTokenDigests.Count(digest => digest.SequenceEqual(SpentDigest)).ShouldBe(1);
        decision.RevokedTokenDigests.Count(digest => digest.SequenceEqual(SiblingDigest)).ShouldBe(
            1,
            "the STILL-LIVE sibling is exactly the token a thief would be holding; revoking only " +
            "the one that was replayed would leave the theft working.");
        decision.RevokedTokenDigests.Count.ShouldBe(2);
    }

    [Fact]
    public void Decide_refuses_a_token_in_an_already_revoked_family_and_revokes_nothing_extra()
    {
        var decision = RefreshTokenRotation.Decide(
            State(
                Family(revokedAtUtc: AuthFixtures.Now.AddMinutes(-1), reason: TokenFamilyRevocation.REFRESH_REUSE),
                Token(LiveDigest)),
            LiveDigest,
            ReplacementDigest,
            Lifetime,
            AuthFixtures.Now);

        decision.Refusal.ShouldBe(RefreshRefusal.FAMILY_REVOKED);
        decision.IssuedToken.ShouldBeNull();
        decision.RevokedTokenDigests.ShouldBeEmpty(
            "the family is already closed; re-revoking would overwrite the reason it was closed for.");
        decision.FamilyRevocation.ShouldBeNull();
    }

    [Fact]
    public void Decide_refuses_an_expired_token_and_revokes_nothing()
    {
        var decision = RefreshTokenRotation.Decide(
            State(Family(), Token(LiveDigest, expiresAtUtc: AuthFixtures.Now.AddSeconds(-1))),
            LiveDigest,
            ReplacementDigest,
            Lifetime,
            AuthFixtures.Now);

        decision.Refusal.ShouldBe(RefreshRefusal.EXPIRED);
        decision.IssuedToken.ShouldBeNull();
        decision.RevokedTokenDigests.ShouldBeEmpty(
            "age is not theft: the cure is a device-secret re-authentication, and burning the " +
            "family would punish an idle player for being idle.");
        decision.FamilyRevocation.ShouldBeNull();
    }

    [Fact]
    public void Decide_refuses_an_unknown_digest_and_revokes_nothing()
    {
        var decision = RefreshTokenRotation.Decide(
            State(Family(), Token(LiveDigest)),
            AuthFixtures.Digest("fixture-refresh-token-never-issued"),
            ReplacementDigest,
            Lifetime,
            AuthFixtures.Now);

        decision.Refusal.ShouldBe(RefreshRefusal.UNKNOWN_TOKEN);
        decision.FamilyId.ShouldBeNull();
        decision.IssuedToken.ShouldBeNull();
        decision.RevokedTokenDigests.ShouldBeEmpty(
            "a digest nobody issued names no family, so there is nothing it could be allowed to close.");
    }

    [Fact]
    public void Decide_rotates_inside_the_family_the_digest_belongs_to_never_a_sibling_family()
    {
        var state = new AuthTokenState(
            new[] { Family(), Family("FAMILY_beta") },
            new[] { Token(SiblingDigest), Token(LiveDigest, familyId: "FAMILY_beta") });

        var decision = RefreshTokenRotation.Decide(
            state, LiveDigest, ReplacementDigest, Lifetime, AuthFixtures.Now);

        decision.Accepted.ShouldBeTrue();
        decision.FamilyId.ShouldBe(
            "FAMILY_beta",
            "families are per device and several are live at once; matching by anything but the " +
            "digest would rotate one device's session out from under another's.");
        decision.IssuedToken.ShouldNotBeNull();
        decision.IssuedToken.FamilyId.ShouldBe("FAMILY_beta");
        decision.RotatedTokenDigest.ShouldBe(LiveDigest);
        decision.RevokedTokenDigests.ShouldBeEmpty();
    }

    [Fact]
    public void Decide_refuses_a_digest_that_exists_in_no_loaded_family()
    {
        var decision = RefreshTokenRotation.Decide(
            AuthTokenState.Empty, LiveDigest, ReplacementDigest, Lifetime, AuthFixtures.Now);

        decision.Refusal.ShouldBe(
            RefreshRefusal.UNKNOWN_TOKEN,
            "an empty record set is what a lookup for an unrecognised digest returns; it must " +
            "refuse rather than fall through to an accepted rotation over nothing.");
        decision.IssuedToken.ShouldBeNull();
    }
}
