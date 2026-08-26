using SlayIdleRepeat.Application.Ports.Server;

namespace SlayIdleRepeat.Adapters.Telemetry.OpenTelemetry;

/// <summary>
/// <see cref="ITelemetryPort"/> over the runtime's own diagnostics primitives: spans are
/// <c>Activity</c> instances from <see cref="ActivitySourceName"/>, metrics are measurements on
/// <see cref="MeterName"/>. The OTel SDK subscribes to both at composition; nothing here names an
/// exporter.
/// </summary>
public sealed class OpenTelemetryTelemetry : ITelemetryPort, IDisposable
{
    /// <summary>The source every span here is started from — what the SDK subscribes to.</summary>
    public const string ActivitySourceName = "SlayIdleRepeat.Server";

    /// <summary>The meter every measurement here is recorded on — what the SDK subscribes to.</summary>
    public const string MeterName = "SlayIdleRepeat.Server";

    /// <inheritdoc/>
    public void RecordException(Exception error, IReadOnlyDictionary<string, string>? context = null) =>
        throw new NotImplementedException();

    /// <inheritdoc/>
    public IDisposable BeginSpan(string name) => throw new NotImplementedException();

    /// <inheritdoc/>
    public void RecordMetric(string name, double value, params (string Key, string Value)[] tags) =>
        throw new NotImplementedException();

    /// <inheritdoc/>
    public void Dispose()
    {
        // The owned ActivitySource and Meter arrive with the implementation.
    }
}
