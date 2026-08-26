using System.Collections.Concurrent;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>The in-memory <see cref="IRunStateStore"/> — expiry is real here, against the injected clock.</summary>
/// <remarks>
/// Rows are the canonical codec bytes, for <c>InMemoryPlayerRepository</c>'s reason. Expiry is
/// enforced at read — an expired row answers as absent — because no retention rule schedules
/// deletion anywhere, and this fake must not invent one.
/// </remarks>
public sealed class InMemoryRunStateStore : IRunStateStore
{
    private readonly ConcurrentDictionary<string, (byte[] Row, DateTimeOffset ExpiresAtUtc)> _rows =
        new(StringComparer.Ordinal);

    private readonly IClockPort _clock;

    /// <summary>Builds a store whose sliding lifetimes are measured on <paramref name="clock"/>.</summary>
    /// <param name="clock">The clock expiry is measured against — an adjustable one in tests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> is null.</exception>
    public InMemoryRunStateStore(IClockPort clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        _clock = clock;
    }

    /// <inheritdoc/>
    public Task<RunSnapshot?> GetAsync(RunId id, CancellationToken ct)
    {
        RequireId(id);
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(
            _rows.TryGetValue(id.Value, out var entry) && entry.ExpiresAtUtc > _clock.UtcNow
                ? SnapshotCodec.DecodeRun(entry.Row)
                : (RunSnapshot?)null);
    }

    /// <inheritdoc/>
    public Task SaveAsync(RunSnapshot state, TimeSpan ttl, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(state);
        RequireId(state.Id);

        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ttl), ttl, "A row born expired is a delete spelled confusingly.");
        }

        ct.ThrowIfCancellationRequested();

        _rows[state.Id.Value] = (SnapshotCodec.EncodeRun(state), _clock.UtcNow + ttl);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(RunId id, CancellationToken ct)
    {
        RequireId(id);
        ct.ThrowIfCancellationRequested();

        _rows.TryRemove(id.Value, out _);

        return Task.CompletedTask;
    }

    private static void RequireId(RunId id)
    {
        if (id.Value is null)
        {
            throw new ArgumentException(
                "default(RunId) carries no text — a row keyed on it would pool every such " +
                "caller's state into one row.",
                nameof(id));
        }
    }
}
