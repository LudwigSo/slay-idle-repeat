using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Server.Auth;
using Xunit;

namespace SlayIdleRepeat.Server.Tests.Auth;

/// <summary>
/// The access token both directions: what a minted token asserts, and which validation rule refuses
/// each way a token can be wrong. Every refusal is asserted by NAME — a shared "invalid" would let
/// five of the six rules break while a test asserting the sixth stayed green.
/// </summary>
public sealed class AccessTokenIssuerTests
{
    private static readonly PlayerId Player = new("PLAYER_token_subject");

    private const string Device = "DEVICE_token_holder";

    private static AccessTokenIssuer Issuer(string signingKey = AuthFixtures.SigningKey) =>
        new(AuthFixtures.Options(signingKey));

    [Fact]
    public void Mint_carries_exactly_the_seven_specified_claims_and_no_others()
    {
        var token = Issuer().Mint(Player, Device, AuthFixtures.Now);

        AuthFixtures.NamesOf(AuthFixtures.PayloadOf(token)).ShouldBe(new[]
        {
            "aud", "deviceId", "exp", "iat", "iss", "jti", "sub",
        }, "the claim set is closed: an extra claim is data published to anyone holding the token, " +
           "and a missing one is a check the validator cannot make.");
    }

    [Fact]
    public void Mint_declares_HS256_and_nothing_else_in_its_header()
    {
        var header = AuthFixtures.HeaderOf(Issuer().Mint(Player, Device, AuthFixtures.Now));

        AuthFixtures.NamesOf(header).ShouldBe(new[] { "alg", "typ" });
        header.GetProperty("alg").GetString().ShouldBe("HS256");
        header.GetProperty("typ").GetString().ShouldBe("JWT");
    }

    [Fact]
    public void Mint_puts_the_player_in_sub_and_the_device_in_deviceId()
    {
        var payload = AuthFixtures.PayloadOf(Issuer().Mint(Player, Device, AuthFixtures.Now));

        payload.GetProperty("sub").GetString().ShouldBe(
            "PLAYER_token_subject", "sub IS the player id — every endpoint behind the seam reads it.");
        payload.GetProperty("deviceId").GetString().ShouldBe("DEVICE_token_holder");
        payload.GetProperty("iss").GetString().ShouldBe("slayidlerepeat-server");
        payload.GetProperty("aud").GetString().ShouldBe("slayidlerepeat-client");
    }

    [Fact]
    public void Mint_gives_two_tokens_minted_in_the_same_instant_different_jti()
    {
        var issuer = Issuer();

        var first = AuthFixtures.PayloadOf(issuer.Mint(Player, Device, AuthFixtures.Now))
            .GetProperty("jti").GetString();
        var second = AuthFixtures.PayloadOf(issuer.Mint(Player, Device, AuthFixtures.Now))
            .GetProperty("jti").GetString();

        second.ShouldNotBe(
            first,
            "jti derived from the instant, the player or the device would collide for two tokens " +
            "minted inside one tick, and two sessions would share one identity.");
    }

    [Fact]
    public void Mint_sets_exp_minus_iat_to_the_configured_access_lifetime()
    {
        var options = new AuthOptions(
            AuthFixtures.SigningKey, TimeSpan.FromMinutes(15), TimeSpan.FromDays(30), 0.8);

        var payload = AuthFixtures.PayloadOf(
            new AccessTokenIssuer(options).Mint(Player, Device, AuthFixtures.Now));

        (payload.GetProperty("exp").GetInt64() - payload.GetProperty("iat").GetInt64()).ShouldBe(
            900, "15 configured minutes, in seconds — the lifetime comes from configuration, not " +
                 "from a constant baked beside the minting code.");
    }

    [Fact]
    public void Validate_accepts_a_token_this_issuer_minted()
    {
        var issuer = Issuer();

        var validation = issuer.Validate(issuer.Mint(Player, Device, AuthFixtures.Now), AuthFixtures.Now);

        validation.Refusal.ShouldBeNull("a token this very issuer minted is the one token that must pass.");
        validation.Claims.ShouldNotBeNull();
        validation.Claims.Player.ShouldBe(Player);
        validation.Claims.DeviceId.ShouldBe(Device);
    }

    [Fact]
    public void Validate_refuses_a_token_signed_with_another_key_as_a_signature_mismatch()
    {
        var foreign = Issuer(AuthFixtures.OtherSigningKey).Mint(Player, Device, AuthFixtures.Now);

        Issuer().Validate(foreign, AuthFixtures.Now).Refusal.ShouldBe(
            AccessTokenRefusal.SIGNATURE_MISMATCH,
            "another deployment's key must not open this one; that is the entire point of signing.");
    }

    [Fact]
    public void Validate_refuses_a_token_whose_payload_changed_after_signing_as_a_signature_mismatch()
    {
        var issuer = Issuer();
        var honest = issuer.Mint(Player, Device, AuthFixtures.Now);

        var elevated = AuthFixtures.WithSwappedPayload(
            honest, AuthFixtures.PayloadJson(subject: "PLAYER_somebody_else"));

        issuer.Validate(elevated, AuthFixtures.Now).Refusal.ShouldBe(
            AccessTokenRefusal.SIGNATURE_MISMATCH,
            "editing sub in a signed token is the whole attack; a validator that read the payload " +
            "before checking the signature would hand back somebody else's account.");
    }

    [Fact]
    public void Validate_refuses_an_expired_token_naming_expiry()
    {
        var issuer = Issuer();
        var token = issuer.Mint(Player, Device, AuthFixtures.Now);

        issuer.Validate(token, AuthFixtures.Now.AddMinutes(60).AddSeconds(1)).Refusal.ShouldBe(
            AccessTokenRefusal.EXPIRED,
            "exp is measured against the injected instant, never an ambient clock.");
    }

    [Fact]
    public void Validate_accepts_a_token_one_second_before_it_expires()
    {
        var issuer = Issuer();
        var token = issuer.Mint(Player, Device, AuthFixtures.Now);

        issuer.Validate(token, AuthFixtures.Now.AddMinutes(60).AddSeconds(-1)).Refusal.ShouldBeNull(
            "the negative control for the expiry arm: a live token must not be refused for age.");
    }

    [Fact]
    public void Validate_refuses_a_token_from_another_issuer_naming_the_issuer_rule()
    {
        // Signed with the CORRECT key, so the only thing wrong with it is iss.
        var token = AuthFixtures.Sign(
            AuthFixtures.Hs256Header,
            AuthFixtures.PayloadJson(issuer: "some-other-service"),
            AuthFixtures.SigningKey);

        Issuer().Validate(token, AuthFixtures.Now).Refusal.ShouldBe(
            AccessTokenRefusal.WRONG_ISSUER,
            "a key shared with another service of ours would otherwise let its tokens in here.");
    }

    [Fact]
    public void Validate_refuses_a_token_for_another_audience_naming_the_audience_rule()
    {
        var token = AuthFixtures.Sign(
            AuthFixtures.Hs256Header,
            AuthFixtures.PayloadJson(audience: "slayidlerepeat-admin"),
            AuthFixtures.SigningKey);

        Issuer().Validate(token, AuthFixtures.Now).Refusal.ShouldBe(
            AccessTokenRefusal.WRONG_AUDIENCE,
            "a token minted for a different reader of the same key must not be replayable here.");
    }

    [Fact]
    public void Validate_refuses_an_alg_none_token_as_an_unsupported_algorithm()
    {
        // The classic JWT forgery: a header claiming no signature is needed, and an empty third
        // segment. It must be refused for its ALGORITHM, before any signature check can "pass".
        var unsigned = AuthFixtures.Base64Url(
                System.Text.Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}")) + "." +
            AuthFixtures.Base64Url(System.Text.Encoding.UTF8.GetBytes(AuthFixtures.PayloadJson())) + ".";

        Issuer().Validate(unsigned, AuthFixtures.Now).Refusal.ShouldBe(
            AccessTokenRefusal.UNSUPPORTED_ALGORITHM,
            "a validator that honoured the header's own algorithm choice would accept any payload " +
            "anybody typed.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-jwt")]
    [InlineData("only.two")]
    [InlineData("a.b.c.d")]
    [InlineData("!!!.???.***")]
    public void Validate_refuses_a_syntactically_broken_token_as_malformed(string broken)
    {
        Issuer().Validate(broken, AuthFixtures.Now).Refusal.ShouldBe(
            AccessTokenRefusal.MALFORMED,
            "a token that is not three base64url segments of JSON must answer a refusal, never " +
            "throw: the seam in front of the endpoints turns a refusal into a 401 and an exception " +
            "into a 500.");
    }

    [Fact]
    public void Validate_refuses_a_null_token_as_malformed()
    {
        Issuer().Validate(null, AuthFixtures.Now).Refusal.ShouldBe(AccessTokenRefusal.MALFORMED);
    }
}
