using SlayIdleRepeat.Application.Ports.Server;

namespace SlayIdleRepeat.Adapters.ObjectStore.S3;

/// <summary>The write-behind layer over a battle-log store: puts enqueue, a drain uploads, a full queue drops and counts.</summary>
/// <remarks>
/// <para>
/// Battle logs are written asynchronously and their loss never blocks progress: <see cref="PutAsync"/>
/// returns the moment the log is queued (or dropped), whatever the store underneath is doing. Reads
/// go straight through — a log still in the queue is simply not readable yet, which is the port's
/// stated acceptance-versus-readability latitude.
/// </para>
/// <para>
/// Vendor-free on purpose: it decorates any <see cref="IBattleLogStore"/>, so its policy is
/// unit-testable over the in-memory fake and reusable by the sibling backing when that lands.
/// </para>
/// </remarks>
public sealed class QueuedBattleLogStore : IBattleLogStore
{
    /// <summary>Builds the queue over a store.</summary>
    /// <param name="inner">The store uploads land in.</param>
    /// <param name="capacity">How many logs may wait. Positive. When full, a put drops its log and counts it.</param>
    /// <param name="losses">Where drops are counted.</param>
    public QueuedBattleLogStore(IBattleLogStore inner, int capacity, BattleLogLossCounter losses) =>
        throw new NotImplementedException("M5-05 phase 3 implements the object-store adapter.");

    /// <inheritdoc/>
    public Task PutAsync(BattleLogId id, ReadOnlyMemory<byte> log, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the object-store adapter.");

    /// <inheritdoc/>
    public Task<ReadOnlyMemory<byte>?> GetAsync(BattleLogId id, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the object-store adapter.");

    /// <summary>Uploads everything queued right now and returns how many logs landed. The deterministic drain tests and fixtures use.</summary>
    /// <param name="ct">Cancellation.</param>
    public Task<int> DrainPendingAsync(CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the object-store adapter.");

    /// <summary>Drains until cancelled — the hosted drain service's loop.</summary>
    /// <param name="ct">Stop signal.</param>
    public Task RunAsync(CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the object-store adapter.");
}
