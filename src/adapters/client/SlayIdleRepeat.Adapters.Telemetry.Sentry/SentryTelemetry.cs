using System.Globalization;
using global::Sentry;
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
    public void RecordException(Exception error, IReadOnlyDictionary<string, string>? context = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        SentrySdk.CaptureException(error, scope =>
        {
            foreach (var (key, value) in context ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                scope.SetExtra(key, value);
            }
        });
    }

    /// <inheritdoc/>
    public IDisposable BeginSpan(string name)
    {
        ThrowIfBlank(name);

        return new SpanScope(SentrySdk.StartTransaction(name, name));
    }

    /// <summary>
    /// Records one measurement as a breadcrumb — Sentry 6.8.0 has no metrics API, so the honest
    /// mapping is a <c>metric</c>-category breadcrumb on whatever event is captured next, never a
    /// silent no-op.
    /// </summary>
    /// <inheritdoc/>
    public void RecordMetric(string name, double value, params (string Key, string Value)[] tags)
    {
        ThrowIfBlank(name);
        ArgumentNullException.ThrowIfNull(tags);

        SentrySdk.AddBreadcrumb(
            message: name + "=" + value.ToString(CultureInfo.InvariantCulture),
            category: "metric",
            data: tags.ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.Ordinal));
    }

    private static void ThrowIfBlank(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A blank name observes nothing anyone can find.", nameof(name));
        }
    }

    /// <summary>One transaction's scope; a disabled SDK hands out a no-op transaction, still safe.</summary>
    private sealed class SpanScope(ITransactionTracer transaction) : IDisposable
    {
        private ITransactionTracer? _transaction = transaction;

        public void Dispose() => Interlocked.Exchange(ref _transaction, null)?.Finish();
    }
}
