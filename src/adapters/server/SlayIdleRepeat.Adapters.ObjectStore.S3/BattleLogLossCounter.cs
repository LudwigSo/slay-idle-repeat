namespace SlayIdleRepeat.Adapters.ObjectStore.S3;

/// <summary>A metric-shaped count of battle logs dropped instead of stored.</summary>
/// <remarks>
/// Battle-log loss never blocks progress — a full queue drops the log and the command proceeds —
/// so this counter is the only trace a drop leaves until the telemetry task exports it. Monotone,
/// thread-safe.
/// </remarks>
public sealed class BattleLogLossCounter
{
    private long _count;

    /// <summary>The metric name this count is exported under.</summary>
    public const string MetricName = "battle_logs_dropped_total";

    /// <summary>How many logs have been dropped so far.</summary>
    public long Count => Interlocked.Read(ref _count);

    /// <summary>Counts one dropped log.</summary>
    public void Increment() => Interlocked.Increment(ref _count);
}
