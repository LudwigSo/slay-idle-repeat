using Microsoft.AspNetCore.Http;
using Shouldly;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Server.Composition;
using Xunit;

namespace SlayIdleRepeat.Server.Tests.Composition;

/// <summary>
/// The exception middleware's whole contract: whatever escapes the pipeline is recorded on the
/// telemetry port and rethrown unchanged, and a healthy request passes through untouched. Driven
/// with a <c>DefaultHttpContext</c> — a plain class needs no host.
/// </summary>
public sealed class TelemetryExceptionMiddlewareTests
{
    /// <summary>A port double that keeps every recorded exception.</summary>
    private sealed class CapturingTelemetryPort : ITelemetryPort
    {
        internal List<Exception> Exceptions { get; } = [];

        public void RecordException(Exception error, IReadOnlyDictionary<string, string>? context = null) =>
            Exceptions.Add(error);

        public IDisposable BeginSpan(string name) => new Scope();

        public void RecordMetric(string name, double value, params (string Key, string Value)[] tags)
        {
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    [Fact]
    public async Task An_exception_escaping_the_pipeline_is_recorded_and_rethrown_unchanged()
    {
        var telemetry = new CapturingTelemetryPort();
        var error = new InvalidOperationException("the endpoint blew up");
        var middleware = new TelemetryExceptionMiddleware(_ => throw error, telemetry);

        var escaped = await Should.ThrowAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(new DefaultHttpContext()));

        escaped.ShouldBeSameAs(
            error,
            "the middleware observes, it does not handle: swallowing or wrapping would change " +
            "what the pipeline's own error handling and the client see.");
        telemetry.Exceptions.ShouldHaveSingleItem(
                "one escape is one recorded exception — this middleware is the only tracing the " +
                "server has for a request that died.")
            .ShouldBeSameAs(error, "the exception recorded is the one that escaped, not a copy.");
    }

    [Fact]
    public async Task A_healthy_request_passes_through_and_records_nothing()
    {
        var telemetry = new CapturingTelemetryPort();
        var reached = false;
        var middleware = new TelemetryExceptionMiddleware(
            _ =>
            {
                reached = true;
                return Task.CompletedTask;
            },
            telemetry);

        await middleware.InvokeAsync(new DefaultHttpContext());

        reached.ShouldBeTrue("the middleware sits in front of the pipeline, never instead of it.");
        telemetry.Exceptions.ShouldBeEmpty(
            "nothing escaped, so a recorded exception here would be a phantom incident.");
    }

    [Fact]
    public void Construction_refuses_a_null_next_delegate()
    {
        Should.Throw<ArgumentNullException>(
            () => new TelemetryExceptionMiddleware(null!, new CapturingTelemetryPort()));
    }

    [Fact]
    public void Construction_refuses_a_null_telemetry_port()
    {
        Should.Throw<ArgumentNullException>(
            () => new TelemetryExceptionMiddleware(_ => Task.CompletedTask, null!));
    }
}
