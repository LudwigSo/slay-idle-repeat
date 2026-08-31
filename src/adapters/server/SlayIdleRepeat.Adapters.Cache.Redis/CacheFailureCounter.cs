namespace SlayIdleRepeat.Adapters.Cache.Redis;

/// <summary>A metric-shaped count of cache operations that failed and were absorbed.</summary>
/// <remarks>
/// A failed cache write never fails a command — the decorators swallow it — so this counter is the
/// only trace such a failure leaves until the telemetry task exports it. Monotone, thread-safe.
/// </remarks>
public sealed class CacheFailureCounter
{
    private long _count;

    /// <summary>The metric name this count is exported under.</summary>
    public const string MetricName = "cache_failures_total";

    /// <summary>How many cache operations have been absorbed so far.</summary>
    public long Count => Interlocked.Read(ref _count);

    /// <summary>Counts one absorbed failure.</summary>
    public void Increment() => Interlocked.Increment(ref _count);
}
