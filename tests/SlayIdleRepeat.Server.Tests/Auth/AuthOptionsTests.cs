using Microsoft.Extensions.Configuration;
using Shouldly;
using SlayIdleRepeat.Server.Auth;
using Xunit;

namespace SlayIdleRepeat.Server.Tests.Auth;

/// <summary>
/// The auth options as a deployment contract: the signing key is required and loud in its absence,
/// and the three lifetime numbers carry the documented defaults and refuse nonsense.
/// </summary>
public sealed class AuthOptionsTests
{
    private static IConfiguration Configuration(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();

    [Fact]
    public void Bind_refuses_an_absent_signing_key_naming_the_environment_variable()
    {
        var refused = Should.Throw<InvalidOperationException>(
            () => AuthOptions.Bind(Configuration()));

        refused.Message.ShouldContain(
            "Auth__JwtSigningKey",
            Case.Sensitive,
            "a deployment that forgot the key must fail with the variable's own spelling in the " +
            "message — a generated or empty default would invalidate every live token on each " +
            "restart while looking like it worked.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Bind_refuses_a_blank_signing_key_naming_the_environment_variable(string blank)
    {
        Should.Throw<InvalidOperationException>(
                () => AuthOptions.Bind(Configuration(("Auth:JwtSigningKey", blank))))
            .Message.ShouldContain(
                "Auth__JwtSigningKey",
                Case.Sensitive,
                "a variable set to whitespace is a variable that was forgotten, and must fail the " +
                "same way an absent one does.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    public void Bind_refuses_a_signing_key_weaker_than_the_digest_it_signs_with(int keyLength)
    {
        var refused = Should.Throw<InvalidOperationException>(
            () => AuthOptions.Bind(Configuration(("Auth:JwtSigningKey", new string('k', keyLength)))));

        refused.Message.ShouldContain(
            "Auth__JwtSigningKey",
            Case.Sensitive,
            "the message names the variable a deployment has to fix.");
        refused.Message.ShouldContain(
            "32",
            Case.Sensitive,
            "HS256 with a key shorter than its own 256-bit digest is a real weakness, so the floor " +
            "is 32 UTF-8 bytes and the message states it rather than leaving an operator guessing.");
    }

    [Fact]
    public void Bind_accepts_a_signing_key_of_exactly_the_floor()
    {
        // 32 ASCII characters, one byte each: the shortest key the rule allows. The negative
        // control is the 31-character case above, which must fail.
        var key = new string('k', 32);

        AuthOptions.Bind(Configuration(("Auth:JwtSigningKey", key))).JwtSigningKey.ShouldBe(key);
    }

    [Fact]
    public void Bind_applies_the_documented_defaults_when_only_the_key_is_configured()
    {
        var options = AuthOptions.Bind(Configuration(("Auth:JwtSigningKey", AuthFixtures.SigningKey)));

        options.AccessTokenLifetime.ShouldBe(
            TimeSpan.FromMinutes(60), "the access token's documented default lifetime.");
        options.RefreshTokenLifetime.ShouldBe(
            TimeSpan.FromDays(30), "the refresh family's documented default lifetime.");
        options.SilentRenewalFraction.ShouldBe(
            0.8, "the documented point in the access lifetime at which the client renews.");
    }

    [Fact]
    public void Bind_reads_the_three_numbers_from_the_authored_key_names()
    {
        var options = AuthOptions.Bind(Configuration(
            ("Auth:JwtSigningKey", AuthFixtures.SigningKey),
            ("Auth:AccessTokenLifetimeMinutes", "15"),
            ("Auth:RefreshTokenLifetimeDays", "7"),
            ("Auth:SilentRenewalFraction", "0.5")));

        options.AccessTokenLifetime.ShouldBe(
            TimeSpan.FromMinutes(15),
            "Auth__AccessTokenLifetimeMinutes is the key the compose file pre-authors; a property " +
            "the binder cannot reach makes that contract a dead letter.");
        options.RefreshTokenLifetime.ShouldBe(TimeSpan.FromDays(7));
        options.SilentRenewalFraction.ShouldBe(0.5);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Bind_refuses_a_non_positive_access_lifetime(string minutes)
    {
        Should.Throw<InvalidOperationException>(
                () => AuthOptions.Bind(Configuration(
                    ("Auth:JwtSigningKey", AuthFixtures.SigningKey),
                    ("Auth:AccessTokenLifetimeMinutes", minutes))))
            .Message.ShouldContain(
                "Auth__AccessTokenLifetimeMinutes",
                Case.Sensitive,
                "a token that expires at or before it is issued makes every request a 401.");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-30")]
    public void Bind_refuses_a_non_positive_refresh_lifetime(string days)
    {
        Should.Throw<InvalidOperationException>(
                () => AuthOptions.Bind(Configuration(
                    ("Auth:JwtSigningKey", AuthFixtures.SigningKey),
                    ("Auth:RefreshTokenLifetimeDays", days))))
            .Message.ShouldContain(
                "Auth__RefreshTokenLifetimeDays",
                Case.Sensitive,
                "a family that expires at birth turns every silent renewal into a device-secret " +
                "re-authentication.");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.5")]
    [InlineData("1")]
    [InlineData("1.5")]
    public void Bind_refuses_a_renewal_fraction_outside_the_open_unit_interval(string fraction)
    {
        Should.Throw<InvalidOperationException>(
                () => AuthOptions.Bind(Configuration(
                    ("Auth:JwtSigningKey", AuthFixtures.SigningKey),
                    ("Auth:SilentRenewalFraction", fraction))))
            .Message.ShouldContain(
                "Auth__SilentRenewalFraction",
                Case.Sensitive,
                "at or below zero the client renews continuously; at or above one it renews after " +
                "the token it was renewing has already expired, which is the mid-run 401 the " +
                "silent-renewal design exists to prevent.");
    }

    [Fact]
    public void Bind_accepts_a_renewal_fraction_inside_the_open_unit_interval()
    {
        AuthOptions.Bind(Configuration(
                ("Auth:JwtSigningKey", AuthFixtures.SigningKey),
                ("Auth:SilentRenewalFraction", "0.99")))
            .SilentRenewalFraction.ShouldBe(0.99);
    }

    [Fact]
    public void RenewAfterSeconds_floors_the_access_lifetime_times_the_fraction()
    {
        // 7 s x 0.8 = 5.6. Rounding would answer 6 — a renewal instant PAST the point the fraction
        // names — so the case is chosen to tell floor and round apart.
        new AuthOptions(AuthFixtures.SigningKey, TimeSpan.FromSeconds(7), TimeSpan.FromDays(30), 0.8)
            .RenewAfterSeconds.ShouldBe(5);
    }

    [Fact]
    public void RenewAfterSeconds_is_eighty_percent_of_the_default_hour()
    {
        AuthFixtures.Options().RenewAfterSeconds.ShouldBe(
            2880, "3600 s x 0.8 — what the default deployment tells every client.");
    }
}
