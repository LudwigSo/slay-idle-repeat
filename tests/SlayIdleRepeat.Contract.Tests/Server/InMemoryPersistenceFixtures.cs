using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Adapters.ObjectStore.S3;
using SlayIdleRepeat.Application.Ports.Server;

namespace SlayIdleRepeat.Contract.Tests.Server;

/// <summary>Runs the shared player-repository suite against the in-memory fake.</summary>
[ContractFixtureFor(typeof(InMemoryPlayerRepository))]
public sealed class InMemoryPlayerRepositoryContractTests : IPlayerRepositoryContractTests
{
    /// <inheritdoc/>
    protected override IPlayerRepository Create() => new InMemoryPlayerRepository();
}

/// <summary>Runs the shared run-store suite against the in-memory fake.</summary>
[ContractFixtureFor(typeof(InMemoryRunStateStore))]
public sealed class InMemoryRunStateStoreContractTests : IRunStateStoreContractTests
{
    /// <inheritdoc/>
    protected override IRunStateStore Create() => new InMemoryRunStateStore(new AdjustableClock());
}

/// <summary>Runs the shared idempotency suite against the in-memory fake.</summary>
[ContractFixtureFor(typeof(InMemoryIdempotencyStore))]
public sealed class InMemoryIdempotencyStoreContractTests : IIdempotencyStoreContractTests
{
    /// <inheritdoc/>
    protected override IIdempotencyStore Create() => new InMemoryIdempotencyStore(new AdjustableClock());
}

/// <summary>Runs the shared battle-log suite against the in-memory fake — the direct backing, so no settling.</summary>
[ContractFixtureFor(typeof(InMemoryBattleLogStore))]
public sealed class InMemoryBattleLogStoreContractTests : IBattleLogStoreContractTests
{
    /// <inheritdoc/>
    protected override IBattleLogStore Create() => new InMemoryBattleLogStore();
}

/// <summary>
/// Runs the shared battle-log suite against the queued write-behind layer, over the in-memory fake.
/// </summary>
/// <remarks>
/// The one store-adjacent implementation with a REAL in-repo fixture: the queue is vendor-free, so
/// the suite can hold its acceptance-then-readability shape honest here — settling is a drain.
/// </remarks>
[ContractFixtureFor(typeof(QueuedBattleLogStore))]
public sealed class QueuedBattleLogStoreContractTests : IBattleLogStoreContractTests
{
    private QueuedBattleLogStore? _lastCreated;

    /// <inheritdoc/>
    protected override IBattleLogStore Create() =>
        _lastCreated = new QueuedBattleLogStore(
            new InMemoryBattleLogStore(), capacity: 64, new BattleLogLossCounter());

    /// <inheritdoc/>
    protected override async Task SettleAsync()
    {
        if (_lastCreated is { } queued)
        {
            await queued.DrainPendingAsync(PersistenceWorlds.Cancel);
        }
    }
}
