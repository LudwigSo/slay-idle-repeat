using SlayIdleRepeat.Application.Ports.Server;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>
/// The in-memory fake for <see cref="ITelemetryPort"/>: keeps every exception, measurement and
/// span it is handed, spans tracked through open and closed, so a scenario can assert exactly what
/// telemetry observed.
/// </summary>
public sealed class RecordingTelemetry : ITelemetryPort
{
    private readonly object _gate = new();
    private readonly List<(Exception Error, IReadOnlyDictionary<string, string>? Context)> _exceptions = [];
    private readonly List<(string Name, double Value, IReadOnlyList<(string Key, string Value)> Tags)> _metrics = [];
    private readonly List<SpanScope> _openSpans = [];
    private readonly List<string> _closedSpans = [];

    /// <summary>Every recorded exception with its context, in call order.</summary>
    public IReadOnlyList<(Exception Error, IReadOnlyDictionary<string, string>? Context)> Exceptions
    {
        get
        {
            lock (_gate)
            {
                return _exceptions.ToArray();
            }
        }
    }

    /// <summary>Every recorded measurement with its tags, in call order.</summary>
    public IReadOnlyList<(string Name, double Value, IReadOnlyList<(string Key, string Value)> Tags)> Metrics
    {
        get
        {
            lock (_gate)
            {
                return _metrics.ToArray();
            }
        }
    }

    /// <summary>The names of spans begun and not yet disposed, in begin order.</summary>
    /// <returns>A fresh list on every call, taken under the lock.</returns>
    public IReadOnlyList<string> OpenSpans()
    {
        lock (_gate)
        {
            return _openSpans.Select(span => span.Name).ToArray();
        }
    }

    /// <summary>The names of spans begun and since disposed, in close order. One entry per span however often it is disposed.</summary>
    public IReadOnlyList<string> ClosedSpans
    {
        get
        {
            lock (_gate)
            {
                return _closedSpans.ToArray();
            }
        }
    }

    /// <inheritdoc/>
    public void RecordException(Exception error, IReadOnlyDictionary<string, string>? context = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        lock (_gate)
        {
            _exceptions.Add((error, context));
        }
    }

    /// <inheritdoc/>
    public IDisposable BeginSpan(string name)
    {
        ThrowIfBlank(name);

        var span = new SpanScope(this, name);

        lock (_gate)
        {
            _openSpans.Add(span);
        }

        return span;
    }

    /// <inheritdoc/>
    public void RecordMetric(string name, double value, params (string Key, string Value)[] tags)
    {
        ThrowIfBlank(name);
        ArgumentNullException.ThrowIfNull(tags);

        lock (_gate)
        {
            _metrics.Add((name, value, tags.ToArray()));
        }
    }

    private static void ThrowIfBlank(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A blank name observes nothing anyone can find.", nameof(name));
        }
    }

    private void Close(SpanScope span)
    {
        lock (_gate)
        {
            if (_openSpans.Remove(span))
            {
                _closedSpans.Add(span.Name);
            }
        }
    }

    private sealed class SpanScope(RecordingTelemetry owner, string name) : IDisposable
    {
        internal string Name { get; } = name;

        public void Dispose() => owner.Close(this);
    }
}
