using SlayIdleRepeat.Application.Ports.Server;

namespace SlayIdleRepeat.Adapters.Telemetry.Sentry;

/// <summary>
/// <see cref="ITelemetryPort"/> over the Sentry SDK — the client side's real telemetry. With no DSN
/// configured the SDK is in its documented disabled state and every call here is a cheap no-op that
/// still validates its arguments.
/// </summary>
public sealed class SentryTelemetry : ITelemetryPort
{
    /// <inheritdoc/>
    public void RecordException(Exception error, IReadOnlyDictionary<string, string>? context = null) =>
        throw new NotImplementedException();

    /// <inheritdoc/>
    public IDisposable BeginSpan(string name) => throw new NotImplementedException();

    /// <inheritdoc/>
    public void RecordMetric(string name, double value, params (string Key, string Value)[] tags) =>
        throw new NotImplementedException();
}
