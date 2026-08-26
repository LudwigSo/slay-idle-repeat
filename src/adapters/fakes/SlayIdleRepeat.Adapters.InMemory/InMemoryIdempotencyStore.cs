using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Wire;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>The in-memory <see cref="IIdempotencyStore"/> — record lifetimes are real here, against the injected clock.</summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    /// <summary>Builds a store whose record lifetimes are measured on <paramref name="clock"/>.</summary>
    /// <param name="clock">The clock expiry is measured against — an adjustable one in tests.</param>
    public InMemoryIdempotencyStore(IClockPort clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
    }

    /// <inheritdoc/>
    public Task<RecordedCommandOutcome?> GetRecordedOutcomeAsync(
        IdempotencyScope scope, CommandId commandId, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements this fake.");

    /// <inheritdoc/>
    public Task RecordAsync(
        IdempotencyScope scope, RecordedCommandOutcome outcome, TimeSpan ttl, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements this fake.");

    /// <inheritdoc/>
    public Task<long?> ReadLastSequenceAsync(IdempotencyScope scope, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements this fake.");

    /// <inheritdoc/>
    public Task OpenScopeAsync(IdempotencyScope scope, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements this fake.");
}
