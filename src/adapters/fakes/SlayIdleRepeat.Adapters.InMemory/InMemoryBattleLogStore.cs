using SlayIdleRepeat.Application.Ports.Server;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>The in-memory <see cref="IBattleLogStore"/> — durable-for-the-process, immediately readable.</summary>
public sealed class InMemoryBattleLogStore : IBattleLogStore
{
    /// <inheritdoc/>
    public Task PutAsync(BattleLogId id, ReadOnlyMemory<byte> log, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements this fake.");

    /// <inheritdoc/>
    public Task<ReadOnlyMemory<byte>?> GetAsync(BattleLogId id, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements this fake.");
}
