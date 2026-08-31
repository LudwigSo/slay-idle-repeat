using Microsoft.Extensions.Configuration;
using Shouldly;
using SlayIdleRepeat.Application.Moderation;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Server.Composition;
using SlayIdleRepeat.Server.Endpoints;
using Xunit;

namespace SlayIdleRepeat.Server.Tests.Composition;

/// <summary>
/// The anti-cheat area's deployment contract: the two rate-limit numbers a deployment may move, and
/// 🔒 the plausibility thresholds it must author before the sweep flags anything.
/// </summary>
public sealed class AntiCheatOptionsTests
{
    private static IConfiguration Configured(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();

    [Fact]
    public void The_shipped_per_player_limit_is_five_per_second_with_a_burst_of_twenty()
    {
        var options = new PlayerRateLimitOptions();

        options.SustainedPerSecond.ShouldBe(5);
        options.Burst.ShouldBe(
            20,
            "an operations choice, not a design number — no document authors a per-endpoint rate.");
    }

    [Fact]
    public void The_per_player_numbers_bind_from_the_ratelimit_player_section()
    {
        var options = Configured(
                ("RateLimit:Player:SustainedPerSecond", "9"),
                ("RateLimit:Player:Burst", "44"))
            .GetSection("RateLimit:Player")
            .Get<PlayerRateLimitOptions>();

        options.ShouldNotBeNull();
        options.SustainedPerSecond.ShouldBe(9, "RateLimit__Player__SustainedPerSecond is the env spelling.");
        options.Burst.ShouldBe(44);
    }

    /// <summary>
    /// 🔒 The whole point of the skeleton, as a deployment contract: nothing is authored, so nothing
    /// is flagged, and the sweep is not even running.
    /// </summary>
    [Fact]
    public void The_plausibility_sweep_ships_off_with_every_threshold_unauthored()
    {
        var options = new PlausibilityOptions();

        options.Enabled.ShouldBeFalse("nobody is running it.");
        options.SweepIntervalMinutes.ShouldBe(60);
        options.MaxCurrencyPerDay.ShouldBeNull(
            "and nobody has written down what implausible looks like. Two independent switches, "
            + "because they say two different things.");
        options.MaxLegendXpPerDay.ShouldBeNull();
        options.MaxBattleHashMismatchesPerDay.ShouldBeNull();

        options.ToEnvelope().AuthoredThresholds.ShouldBe(0);
    }

    [Fact]
    public void An_unconfigured_section_still_produces_an_unauthored_envelope_rather_than_a_default_one()
    {
        var options = Configured(("Plausibility:Enabled", "true"))
            .GetSection("Plausibility")
            .Get<PlausibilityOptions>();

        options.ShouldNotBeNull();
        options.Enabled.ShouldBeTrue();
        options.ToEnvelope().AuthoredThresholds.ShouldBe(
            0,
            "enabling the sweep must never conjure thresholds. A deployment that turns it on gets a "
            + "job that observes, stores and flags nothing — and is told so at startup.");
    }

    [Fact]
    public void An_operator_who_authors_a_threshold_gets_exactly_that_threshold()
    {
        var envelope = Configured(
                ("Plausibility:Enabled", "true"),
                ("Plausibility:MaxCurrencyPerDay", "250000"),
                ("Plausibility:SweepIntervalMinutes", "15"))
            .GetSection("Plausibility")
            .Get<PlausibilityOptions>()!;

        envelope.SweepIntervalMinutes.ShouldBe(15);

        var authored = envelope.ToEnvelope();

        authored.AuthoredThresholds.ShouldBe(1);
        authored.MaxCurrencyPerDay.ShouldBe(250_000);
        authored.MaxLegendXpPerDay.ShouldBeNull(
            "authoring one threshold must not silently author the other two — the negative control "
            + "that keeps 'unauthored' meaning what it says.");
    }

    /// <summary>
    /// 🔒 That the process installs the CONFIGURED limiter, not just that a binder can read the
    /// keys. A burst of 2 discriminates from the shipped 20, so a composition that ignored
    /// configuration and used the default would fail here.
    /// </summary>
    [Fact]
    public void The_composed_throttle_is_the_configured_per_player_limiter()
    {
        var player = new PlayerId("PLAYER_alice");

        var throttle = new AntiCheatComposition.AntiCheatArea(Configured(
            ("RateLimit:Player:SustainedPerSecond", "1"),
            ("RateLimit:Player:Burst", "2"))).Throttle;

        throttle.ShouldReject(player).ShouldBeFalse();
        throttle.ShouldReject(player).ShouldBeFalse();
        throttle.ShouldReject(player).ShouldBeTrue(
            "the third instantaneous command is past the configured burst of 2 — not the shipped 20.");
    }

    [Fact]
    public void An_unconfigured_process_still_installs_a_limiter_rather_than_no_limit_at_all()
    {
        var player = new PlayerId("PLAYER_alice");
        var throttle = new AntiCheatComposition.AntiCheatArea(Configured()).Throttle;

        for (var i = 0; i < 20; i++)
        {
            throttle.ShouldReject(player).ShouldBeFalse($"command {i + 1} is inside the shipped burst.");
        }

        throttle.ShouldReject(player).ShouldBeTrue(
            "the placeholder this replaces answered 'no limit' to everything; an unconfigured "
            + "deployment must be limited, not unlimited.");
    }

    [Fact]
    public void The_composed_area_carries_the_configured_per_address_numbers_into_its_bucket()
    {
        var bucket = PerIpRateLimit.BucketFor(
            new AntiCheatComposition.AntiCheatArea(Configured(
                ("RateLimit:Ip:PermitsPerSecond", "7"),
                ("RateLimit:Ip:Burst", "13"))).Ip);

        bucket.TokensPerPeriod.ShouldBe(7);
        bucket.TokenLimit.ShouldBe(
            13, "a composition that bound the wrong section would silently ship the default 100.");
    }

    [Fact]
    public void The_composed_area_starts_with_an_empty_locked_set_and_a_volatile_store()
    {
        var area = new AntiCheatComposition.AntiCheatArea(Configured());

        area.Standing.LockedAccounts.ShouldBe(
            0,
            "a process that locked accounts it had never heard of would refuse every player on a "
            + "cold start.");
        area.Store.ShouldBeOfType<VolatileModerationStore>(
            "there is no durable IModerationStore implementation yet, and the startup announcement "
            + "that says so has to be describing what is actually composed.");
    }

    /// <summary>
    /// 🔒 A deployment typo must be a process that refuses to start, not one that looks healthy and
    /// throws out of the rate limiter on its first request — or out of OnRejected on its first
    /// refusal, which is the moment least able to absorb it.
    /// </summary>
    /// <remarks>
    /// 🔴 The BLAMED setting is a third column and is asserted on. Nine range rules across three
    /// option classes all throw the same exception type, so pinning only the type is the symptom
    /// rather than the identity: a validator that refused the wrong setting — or refused every
    /// setting — passed every arm of this theory.
    /// </remarks>
    [Theory]
    [InlineData("RateLimit:Player:Burst", "0", "Burst")]
    // ⚠️ The blamed name here is NOT the operator's key. RateLimit:Player:SustainedPerSecond is
    // passed to PlayerRateLimiter's `permitsPerSecond` parameter, so the startup crash names a
    // word that appears nowhere in the deployment's configuration. Pinned as it is rather than
    // as it ought to be — inventing the better name here would hide the mismatch instead of
    // recording it, and renaming the production parameter is not this review's to take.
    [InlineData("RateLimit:Player:SustainedPerSecond", "0", "permitsPerSecond")]
    [InlineData("RateLimit:Ip:PermitsPerSecond", "0", "PermitsPerSecond")]
    [InlineData("RateLimit:Ip:Burst", "-3", "Burst")]
    [InlineData("RateLimit:Ip:RetryAfterSeconds", "0", "RetryAfterSeconds")]
    [InlineData("Plausibility:SweepIntervalMinutes", "0", "SweepIntervalMinutes")]
    [InlineData("Plausibility:SweepIntervalMinutes", "-30", "SweepIntervalMinutes")]
    [InlineData("Plausibility:SweepIntervalMinutes", "1441", "SweepIntervalMinutes")]
    [InlineData("Plausibility:MaxCurrencyPerDay", "-1", "MaxCurrencyPerDay")]
    public void A_setting_outside_its_range_is_refused_while_the_process_is_still_starting(
        string key, string value, string blamed)
    {
        Should.Throw<ArgumentOutOfRangeException>(
                () => new AntiCheatComposition.AntiCheatArea(Configured((key, value))))
            .Message.ShouldContain(
                blamed,
                Case.Insensitive,
                "the refusal must name the setting it refused. Case-insensitive because the name "
                + "reaches the message through nameof(parameter), which is camelCase, while the "
                + "configuration key is Pascal. An operator reading a startup crash "
                + "has the deployment's whole configuration in front of them and one line of log.");
    }

    [Fact]
    public void A_sweep_interval_at_either_end_of_its_range_is_accepted()
    {
        new AntiCheatComposition.AntiCheatArea(Configured(("Plausibility:SweepIntervalMinutes", "1")))
            .SweepInterval.ShouldBe(TimeSpan.FromMinutes(1));

        new AntiCheatComposition.AntiCheatArea(Configured(("Plausibility:SweepIntervalMinutes", "1440")))
            .SweepInterval.ShouldBe(
                TimeSpan.FromDays(1),
                "the bound refuses what the timer cannot hold — it does not refuse a long interval "
                + "somebody meant.");
    }

    /// <summary>The 403 path, proven through the composition rather than only through the decorator.</summary>
    [Fact]
    public void The_composed_resolver_wraps_the_inner_one_with_the_account_standing_check()
    {
        var inner = new AlwaysResolves(new PlayerId("PLAYER_alice"));

        var wrapped = AntiCheatComposition.WithAccountStanding(inner, Configured());

        wrapped.ShouldBeOfType<SanctionAwarePrincipalResolver>(
            "the composition must actually decorate, not pass through.");
        wrapped.Resolve("Bearer whatever").Player.ShouldBe(
            new PlayerId("PLAYER_alice"),
            "with nothing sanctioned the wrapper is transparent — a process that locked accounts it "
            + "had never heard of would refuse every player on a cold start.");
    }

    private sealed class AlwaysResolves(PlayerId player) : IPrincipalResolver
    {
        public PrincipalResolution Resolve(string? authorizationHeader) =>
            PrincipalResolution.Resolved(player);
    }
}
