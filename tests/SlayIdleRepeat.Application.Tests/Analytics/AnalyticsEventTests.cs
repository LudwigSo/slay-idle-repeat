using Shouldly;
using SlayIdleRepeat.Application.Ports.Server;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Analytics;

/// <summary>
/// <see cref="AnalyticsEvent"/>'s construction guards: a name outside the lower-snake vocabulary
/// shape, a null property bag or a blank property key never reaches a backend as data.
/// </summary>
public sealed class AnalyticsEventTests
{
    private static readonly IReadOnlyDictionary<string, string> NoProperties =
        new Dictionary<string, string>();

    [Theory]
    [InlineData(null, "null is not a name")]
    [InlineData("", "empty is not a name")]
    [InlineData("   ", "whitespace is not a name")]
    [InlineData("RunStart", "an upper-case variant would reach the backend as a second fact")]
    [InlineData("run start", "a space is outside the shape")]
    [InlineData("run-start", "a hyphen is outside the shape")]
    [InlineData("1run", "the first character must be a letter")]
    [InlineData("_run", "the first character must be a letter, not an underscore")]
    public void Construction_refuses_a_name_outside_the_lower_snake_shape(string? name, string why)
    {
        Should.Throw<ArgumentException>(
            () => new AnalyticsEvent(name!, NoProperties),
            "'" + name + "' was accepted: " + why + ". The name must match ^[a-z][a-z0-9_]*$, or " +
            "one fact splits into casing and spacing variants the dashboard counts separately.");
    }

    [Theory]
    [InlineData("run_start")]
    [InlineData("a")]
    [InlineData("die_rolled_2")]
    public void Construction_accepts_a_lower_snake_name(string name)
    {
        // The negative control for the refusals above: a guard that refused every name would pass them all.
        Should.NotThrow(() => new AnalyticsEvent(name, NoProperties));
    }

    [Fact]
    public void Construction_refuses_null_properties()
    {
        Should.Throw<ArgumentNullException>(
            () => new AnalyticsEvent("run_start", null!),
            "properties are never null — empty is the shape of 'no properties', so no consumer " +
            "ever has to branch on the difference.");
    }

    [Fact]
    public void Construction_accepts_empty_properties()
    {
        Should.NotThrow(() => new AnalyticsEvent("run_start", NoProperties));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Construction_refuses_a_blank_property_key(string key)
    {
        var properties = new Dictionary<string, string> { [key] = "value" };

        Should.Throw<ArgumentException>(
            () => new AnalyticsEvent("run_start", properties),
            "a blank key names nothing, so its value would arrive at the backend unaddressable.");
    }
}
