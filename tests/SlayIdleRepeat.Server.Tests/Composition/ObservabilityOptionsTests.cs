using Microsoft.Extensions.Configuration;
using Shouldly;
using SlayIdleRepeat.Adapters.Analytics.PostHog;
using SlayIdleRepeat.Adapters.Telemetry.Sentry;
using Xunit;

namespace SlayIdleRepeat.Server.Tests.Composition;

/// <summary>
/// The observability options as deployment contracts: both vendors are OFF by a bare default, and
/// the binding key names are exactly the ones the compose file and infra/README.md pre-author
/// (<c>PostHog__Enabled</c>, <c>Sentry__Dsn</c>).
/// </summary>
public sealed class ObservabilityOptionsTests
{
    [Fact]
    public void PostHog_is_disabled_by_default()
    {
        var options = new PostHogOptions();

        options.Enabled.ShouldBeFalse(
            "an unconfigured deployment must send nothing anywhere — analytics is opt-in, never " +
            "opt-out.");
        options.Host.ShouldBe("", "no host is baked in; a deployment names its own instance.");
        options.ProjectKey.ShouldBe("", "no key is baked in, ever.");
    }

    [Fact]
    public void Sentry_dsn_is_empty_by_default()
    {
        new SentryTelemetryOptions().Dsn.ShouldBe(
            "", "empty is the SDK's own documented 'disabled' — the honest default.");
    }

    [Fact]
    public void PostHog_options_bind_from_the_posthog_section_by_the_authored_key_names()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PostHog:Enabled"] = "true",
                ["PostHog:Host"] = "https://eu.posthog.test",
                ["PostHog:ProjectKey"] = "phc_bound",
            })
            .Build();

        var options = configuration.GetSection("PostHog").Get<PostHogOptions>();

        options.ShouldNotBeNull();
        options.Enabled.ShouldBeTrue(
            "PostHog__Enabled is the key the compose file pre-authors; a property the binder " +
            "cannot reach makes that contract a dead letter.");
        options.Host.ShouldBe("https://eu.posthog.test");
        options.ProjectKey.ShouldBe("phc_bound");
    }

    [Fact]
    public void Sentry_options_bind_from_the_sentry_section_by_the_authored_key_name()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sentry:Dsn"] = "https://key@sentry.test/1",
            })
            .Build();

        var options = configuration.GetSection("Sentry").Get<SentryTelemetryOptions>();

        options.ShouldNotBeNull();
        options.Dsn.ShouldBe(
            "https://key@sentry.test/1",
            "Sentry__Dsn is the key the compose file pre-authors.");
    }
}
