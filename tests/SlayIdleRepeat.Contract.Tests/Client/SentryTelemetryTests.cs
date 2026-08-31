using Shouldly;
using SlayIdleRepeat.Adapters.Telemetry.Sentry;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Client;

/// <summary>
/// The Sentry adapter's honestly assertable behaviour with no DSN: the SDK's documented disabled
/// state, in which every valid call is a quiet no-op and the argument guards still fire. Nothing
/// here touches a network — an uninitialised SDK sends nothing anywhere.
/// </summary>
public sealed class SentryTelemetryTests
{
    [Fact]
    public void RecordException_refuses_a_null_error()
    {
        var telemetry = new SentryTelemetry();

        Should.Throw<ArgumentNullException>(
            () => telemetry.RecordException(null!),
            "argument guards are the adapter's own, ahead of the SDK — disabled must not also " +
            "mean unvalidated, or the first deployment with a DSN meets the bad call in production.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BeginSpan_refuses_a_blank_name(string? name)
    {
        var telemetry = new SentryTelemetry();

        // The identity, not the symptom: ArgumentNullException derives from ArgumentException,
        // so the null row passed on the wrong guard. ThrowIfBlank is the one under test.
        Should.Throw<ArgumentException>(() => telemetry.BeginSpan(name!))
            .ParamName.ShouldBe("name", "the refusal must name the parameter it refused.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RecordMetric_refuses_a_blank_name(string? name)
    {
        var telemetry = new SentryTelemetry();

        Should.Throw<ArgumentException>(() => telemetry.RecordMetric(name!, 1))
            .ParamName.ShouldBe("name", "the refusal must name the parameter it refused.");
    }

    [Fact]
    public void BeginSpan_returns_a_scope_that_survives_a_double_dispose_with_no_backend()
    {
        var telemetry = new SentryTelemetry();

        var span = telemetry.BeginSpan("client_work");

        span.ShouldNotBeNull(
            "the scope is the caller's only handle on the span, DSN or no DSN — a null here would " +
            "make every using statement crash exactly when telemetry is off.");
        span.Dispose();
        Should.NotThrow(span.Dispose, "a second dispose is harmless by the port's contract.");
    }

    [Fact]
    public void Valid_calls_are_quiet_no_ops_while_the_sdk_is_disabled()
    {
        var telemetry = new SentryTelemetry();

        Should.NotThrow(
            () =>
            {
                telemetry.RecordException(new InvalidOperationException("boom"));
                telemetry.RecordException(
                    new InvalidOperationException("boom"),
                    new Dictionary<string, string> { ["where"] = "test" });
                telemetry.RecordMetric("frame_time_ms", 16.6, ("screen", "home"));
                telemetry.BeginSpan("client_work").Dispose();
            },
            "no DSN is the SDK's documented disabled state, and the port promises a valid call " +
            "never throws — the client must run identically with telemetry off.");
    }
}
