using Npgsql;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Adapters.Persistence.Postgres;

/// <summary>The one door into this adapter: a connection string in, the stores and the migration runner out.</summary>
/// <remarks>
/// The composition root hands this a connection string and never sees an Npgsql type: the vendor
/// package is referenced by this project and no other, so the factory takes primitives.
/// </remarks>
public sealed class PostgresPersistence : IAsyncDisposable
{
    private PostgresPersistence() =>
        throw new NotImplementedException("M5-05 phase 3 implements the Postgres adapter.");

    /// <summary>Opens the adapter over one pooled data source.</summary>
    /// <param name="connectionString">The Postgres connection string.</param>
    public static PostgresPersistence Create(string connectionString) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Postgres adapter.");

    /// <summary>The player-profile store.</summary>
    public IPlayerRepository Players =>
        throw new NotImplementedException("M5-05 phase 3 implements the Postgres adapter.");

    /// <summary>The run-row store — the authoritative layer a cache decorates.</summary>
    public IRunStateStore RunStates =>
        throw new NotImplementedException("M5-05 phase 3 implements the Postgres adapter.");

    /// <summary>The sequencing and idempotency store.</summary>
    public IIdempotencyStore Idempotency =>
        throw new NotImplementedException("M5-05 phase 3 implements the Postgres adapter.");

    /// <summary>The append-only economy event log.</summary>
    public PostgresEconomyEventLog EconomyEvents =>
        throw new NotImplementedException("M5-05 phase 3 implements the Postgres adapter.");

    /// <summary>Applies every pending migration, once per cluster, under the advisory lock.</summary>
    /// <param name="ct">Cancellation.</param>
    public Task MigrateAsync(CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Postgres adapter.");

    /// <inheritdoc/>
    public ValueTask DisposeAsync() =>
        throw new NotImplementedException("M5-05 phase 3 implements the Postgres adapter.");
}

/// <summary>The Postgres <see cref="IPlayerRepository"/>: the players row (JSONB document + typed columns) and, with it, the run's row.</summary>
public sealed class PostgresPlayerRepository : IPlayerRepository
{
    internal PostgresPlayerRepository(NpgsqlDataSource dataSource) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Postgres adapter.");

    /// <inheritdoc/>
    public Task<PlayerProfile?> GetAsync(PlayerId id, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Postgres adapter.");

    /// <inheritdoc/>
    public Task SaveAsync(PlayerProfile profile, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Postgres adapter.");

    /// <inheritdoc/>
    public Task<PlayerId> CreateAnonymousAsync(PlayerProfile initial, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Postgres adapter.");
}

/// <summary>The Postgres <see cref="IRunStateStore"/>: the runs row, with expiry enforced at read.</summary>
public sealed class PostgresRunStateStore : IRunStateStore
{
    internal PostgresRunStateStore(NpgsqlDataSource dataSource) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Postgres adapter.");

    /// <inheritdoc/>
    public Task<RunSnapshot?> GetAsync(RunId id, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Postgres adapter.");

    /// <inheritdoc/>
    public Task SaveAsync(RunSnapshot state, TimeSpan ttl, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Postgres adapter.");

    /// <inheritdoc/>
    public Task DeleteAsync(RunId id, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Postgres adapter.");
}

/// <summary>The Postgres <see cref="IIdempotencyStore"/>: records in their own table, counters on the player and run rows.</summary>
public sealed class PostgresIdempotencyStore : IIdempotencyStore
{
    internal PostgresIdempotencyStore(NpgsqlDataSource dataSource) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Postgres adapter.");

    /// <inheritdoc/>
    public Task<RecordedCommandOutcome?> GetRecordedOutcomeAsync(
        IdempotencyScope scope, CommandId commandId, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Postgres adapter.");

    /// <inheritdoc/>
    public Task RecordAsync(
        IdempotencyScope scope, RecordedCommandOutcome outcome, TimeSpan ttl, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Postgres adapter.");

    /// <inheritdoc/>
    public Task<long?> ReadLastSequenceAsync(IdempotencyScope scope, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Postgres adapter.");

    /// <inheritdoc/>
    public Task OpenScopeAsync(IdempotencyScope scope, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Postgres adapter.");
}

/// <summary>The append-only economy event log. Not a port: 14 §16.4 appends it inside the commit transaction, whose composition is M5-04's.</summary>
public sealed class PostgresEconomyEventLog
{
    internal PostgresEconomyEventLog(NpgsqlDataSource dataSource) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Postgres adapter.");

    /// <summary>Appends one command's enriched events on a caller-owned transaction — the shape M5-04's unit of work composes.</summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="transaction">The transaction the append commits with.</param>
    /// <param name="records">The enriched rows, in event order.</param>
    /// <param name="ct">Cancellation.</param>
    public Task AppendAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<SlayIdleRepeat.Application.Services.Events.EconomyEventRecord> records,
        CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Postgres adapter.");
}
