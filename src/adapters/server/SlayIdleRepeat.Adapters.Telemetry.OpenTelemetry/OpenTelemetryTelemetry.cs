using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
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

    private readonly ActivitySource _source = new(ActivitySourceName);
    private readonly Meter _meter = new(MeterName);
    private readonly ConcurrentDictionary<string, Histogram<double>> _histograms = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public void RecordException(Exception error, IReadOnlyDictionary<string, string>? context = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (Activity.Current is not { } activity)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, error.Message);

        var tags = new ActivityTagsCollection
        {
            ["exception.type"] = error.GetType().FullName,
            ["exception.message"] = error.Message,
            ["exception.stacktrace"] = error.ToString(),
        };

        foreach (var (key, value) in context ?? Enumerable.Empty<KeyValuePair<string, string>>())
        {
            tags[key] = value;
        }

        activity.AddEvent(new ActivityEvent("exception", tags: tags));
    }

    /// <inheritdoc/>
    public IDisposable BeginSpan(string name)
    {
        ThrowIfBlank(name);

        return new SpanScope(_source.StartActivity(name));
    }

    /// <inheritdoc/>
    public void RecordMetric(string name, double value, params (string Key, string Value)[] tags)
    {
        ThrowIfBlank(name);
        ArgumentNullException.ThrowIfNull(tags);

        var histogram = _histograms.GetOrAdd(name, metric => _meter.CreateHistogram<double>(metric));
        var tagList = new TagList();

        foreach (var (key, tagValue) in tags)
        {
            tagList.Add(key, tagValue);
        }

        histogram.Record(value, tagList);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _source.Dispose();
        _meter.Dispose();
    }

    private static void ThrowIfBlank(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A blank name observes nothing anyone can find.", nameof(name));
        }
    }

    /// <summary>One span's scope. A null activity (nothing listening) is a valid, inert scope.</summary>
    private sealed class SpanScope(Activity? activity) : IDisposable
    {
        private Activity? _activity = activity;

        public void Dispose() => Interlocked.Exchange(ref _activity, null)?.Dispose();
    }
}
