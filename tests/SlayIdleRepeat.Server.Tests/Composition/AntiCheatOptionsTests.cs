using Microsoft.Extensions.Configuration;
using Shouldly;
using SlayIdleRepeat.Server.Composition;
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
}
