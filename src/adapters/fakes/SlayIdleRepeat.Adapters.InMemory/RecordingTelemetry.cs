using SlayIdleRepeat.Application.Ports.Server;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>
/// The in-memory fake for <see cref="ITelemetryPort"/>: keeps every exception, measurement and
/// span it is handed, spans tracked through open and closed, so a scenario can assert exactly what
/// telemetry observed.
/// </summary>
public sealed class RecordingTelemetry : ITelemetryPort
{
    /// <summary>Every recorded exception with its context, in call order.</summary>
    public IReadOnlyList<(Exception Error, IReadOnlyDictionary<string, string>? Context)> Exceptions =>
        throw new NotImplementedException();

    /// <summary>Every recorded measurement with its tags, in call order.</summary>
    public IReadOnlyList<(string Name, double Value, IReadOnlyList<(string Key, string Value)> Tags)> Metrics =>
        throw new NotImplementedException();

    /// <summary>The names of spans begun and not yet disposed, in begin order.</summary>
    public IReadOnlyList<string> OpenSpans => throw new NotImplementedException();

    /// <summary>The names of spans begun and since disposed, in close order. One entry per span however often it is disposed.</summary>
    public IReadOnlyList<string> ClosedSpans => throw new NotImplementedException();

    /// <inheritdoc/>
    public void RecordException(Exception error, IReadOnlyDictionary<string, string>? context = null) =>
        throw new NotImplementedException();

    /// <inheritdoc/>
    public IDisposable BeginSpan(string name) => throw new NotImplementedException();

    /// <inheritdoc/>
    public void RecordMetric(string name, double value, params (string Key, string Value)[] tags) =>
        throw new NotImplementedException();
}
