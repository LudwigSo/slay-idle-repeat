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
    public async Task A_client_that_hung_up_is_rethrown_but_not_recorded()
    {
        var telemetry = new CapturingTelemetryPort();
        var context = new DefaultHttpContext();
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        context.RequestAborted = aborted.Token;

        var middleware = new TelemetryExceptionMiddleware(
            _ => throw new OperationCanceledException(aborted.Token), telemetry);

        await Should.ThrowAsync<OperationCanceledException>(() => middleware.InvokeAsync(context));

        telemetry.Exceptions.ShouldBeEmpty(
            "a disconnected client is not an incident anyone can act on, and one entry per flaky " +
            "mobile connection would bury the exceptions that are.");
    }

    [Fact]
    public async Task A_cancellation_the_client_did_not_cause_is_still_recorded()
    {
        var telemetry = new CapturingTelemetryPort();
        var error = new OperationCanceledException("a timeout inside the handler");
        var middleware = new TelemetryExceptionMiddleware(_ => throw error, telemetry);

        await Should.ThrowAsync<OperationCanceledException>(
            () => middleware.InvokeAsync(new DefaultHttpContext()));

        telemetry.Exceptions.ShouldHaveSingleItem(
                "the carve-out is for a client that hung up, not for the exception TYPE — a " +
                "handler that cancelled itself is a real fault and must still be reported.")
            .ShouldBeSameAs(error);
    }

    [Fact]
    public async Task A_telemetry_port_that_throws_does_not_replace_the_exception_that_escaped()
    {
        var error = new InvalidOperationException("the endpoint blew up");
        var middleware = new TelemetryExceptionMiddleware(_ => throw error, new ThrowingTelemetryPort());

        var escaped = await Should.ThrowAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(new DefaultHttpContext()));

        escaped.ShouldBeSameAs(
            error,
            "the port promises it never throws, but if one implementation ever breaks that promise " +
            "the caller must still see the fault that actually happened, not the telemetry bug.");
    }

    /// <summary>A port double that breaks the never-throws promise.</summary>
    private sealed class ThrowingTelemetryPort : ITelemetryPort
    {
        public void RecordException(Exception error, IReadOnlyDictionary<string, string>? context = null) =>
            throw new InvalidOperationException("the telemetry backend adapter is broken");

        public IDisposable BeginSpan(string name) => throw new NotSupportedException();

        public void RecordMetric(string name, double value, params (string Key, string Value)[] tags) =>
            throw new NotSupportedException();
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
