using System.Collections.Concurrent;
using SlayIdleRepeat.Application.Ports.Server;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>The in-memory <see cref="IBattleLogStore"/> — a direct backing: a put is readable the moment it returns.</summary>
public sealed class InMemoryBattleLogStore : IBattleLogStore
{
    private readonly ConcurrentDictionary<string, byte[]> _logs = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public Task PutAsync(BattleLogId id, ReadOnlyMemory<byte> log, CancellationToken ct)
    {
        RequireId(id);
        ct.ThrowIfCancellationRequested();

        _logs[id.Value] = log.ToArray();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<ReadOnlyMemory<byte>?> GetAsync(BattleLogId id, CancellationToken ct)
    {
        RequireId(id);
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(
            _logs.TryGetValue(id.Value, out var log)
                ? (ReadOnlyMemory<byte>?)log.ToArray()
                : null);
    }

    private static void RequireId(BattleLogId id)
    {
        if (id.Value is null)
        {
            throw new ArgumentException(
                "default(BattleLogId) carries no text — a store that keyed on it would file every " +
                "such log under one name.",
                nameof(id));
        }
    }
}
