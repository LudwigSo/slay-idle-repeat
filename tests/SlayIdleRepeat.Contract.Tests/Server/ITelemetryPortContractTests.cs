using Shouldly;
using SlayIdleRepeat.Application.Ports.Server;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Server;

/// <summary>
/// States what <see cref="ITelemetryPort"/> <em>means</em> (<c>23</c> §4.2): bad arguments are the
/// only throws, a span is a real double-dispose-safe scope, and a telemetry backend in any state
/// never takes a caller down.
/// </summary>
[ContractSuiteFor(typeof(ITelemetryPort))]
public abstract class ITelemetryPortContractTests
{
    /// <summary>A port of the implementation under test.</summary>
    protected abstract ITelemetryPort Create();

    [Fact]
    public void RecordException_refuses_a_null_error()
    {
        var telemetry = Create();

        Should.Throw<ArgumentNullException>(
            () => telemetry.RecordException(null!),
            "a null error is the caller's bug — 'record nothing' spelled as a call has to fail " +
            "where it is made, not vanish into the side channel.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BeginSpan_refuses_a_blank_name(string? name)
    {
        var telemetry = Create();

        Should.Throw<ArgumentException>(
            () => telemetry.BeginSpan(name!),
            "a nameless span measures nothing anyone can find on a trace; blank or null is an " +
            "ArgumentException from every method here.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RecordMetric_refuses_a_blank_name(string? name)
    {
        var telemetry = Create();

        Should.Throw<ArgumentException>(
            () => telemetry.RecordMetric(name!, 1),
            "a nameless measurement lands on no series; blank or null is an ArgumentException " +
            "from every method here.");
    }

    [Fact]
    public void BeginSpan_returns_a_scope_that_survives_a_double_dispose()
    {
        var telemetry = Create();

        var span = telemetry.BeginSpan("contract_span");

        span.ShouldNotBeNull(
            "the scope is the caller's only handle on the span, and a null would put a null check " +
            "into every using statement the port was designed to enable.");
        span.Dispose();
        Should.NotThrow(
            span.Dispose,
            "IDisposable's own contract: a second dispose is harmless, because scopes end up in " +
            "finally blocks and error paths that cannot count.");
    }

    [Fact]
    public void Valid_telemetry_calls_never_throw()
    {
        var telemetry = Create();

        Should.NotThrow(
            () =>
            {
                telemetry.RecordException(new InvalidOperationException("contract probe"));
                telemetry.RecordException(
                    new InvalidOperationException("contract probe"),
                    new Dictionary<string, string> { ["where"] = "contract" });
                telemetry.RecordMetric("contract_metric", 1.5);
                telemetry.RecordMetric("contract_metric", 2, ("kind", "probe"));
                telemetry.BeginSpan("contract_span").Dispose();
            },
            "telemetry observes the system; whatever the backend's state, a valid call returns " +
            "quietly — an observer that can throw becomes the outage it was meant to report.");
    }
}
