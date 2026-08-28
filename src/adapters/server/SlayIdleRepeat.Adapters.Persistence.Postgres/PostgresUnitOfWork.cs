using Npgsql;
using SlayIdleRepeat.Application.Ports.Server;

namespace SlayIdleRepeat.Adapters.Persistence.Postgres;

/// <summary>The real transaction boundary: one processed command, one database transaction.</summary>
/// <remarks>
/// <para>
/// Everything a <see cref="CommandCommit"/> carries is written inside one transaction on one
/// connection — the player upsert (and the run upsert beside it), the outcome record with its
/// sequence advance, the economy-log batch, and the scope an opening command created. The stores
/// this adapter already exposes carry connection/transaction overloads shaped for exactly that, so
/// the SQL lives with the table it writes and only the boundary lives here.
/// </para>
/// <para>
/// Nothing loss-tolerant is inside it. Analytics dispatch and cache population happen after the
/// commit returns, where a failure costs a metric rather than a player's command.
/// </para>
/// </remarks>
public sealed class PostgresUnitOfWork : IUnitOfWork
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgresPlayerRepository _players;
    private readonly PostgresIdempotencyStore _idempotency;
    private readonly PostgresEconomyEventLog _economyEvents;

    internal PostgresUnitOfWork(
        NpgsqlDataSource dataSource,
        PostgresPlayerRepository players,
        PostgresIdempotencyStore idempotency,
        PostgresEconomyEventLog economyEvents)
    {
        _dataSource = dataSource;
        _players = players;
        _idempotency = idempotency;
        _economyEvents = economyEvents;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The order inside the transaction is not free. The snapshots go first because both writes
    /// after them land on the rows the snapshots create: the sequence advance is an UPDATE of the
    /// player's or the run's own row, and the scope open is an UPDATE of the run row the accepted
    /// opening command just produced. Each of those refuses a subject it cannot find, so a save that
    /// ran second would turn an ordinary first command into a fault.
    /// </remarks>
    public async Task CommitAsync(CommandCommit commit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(commit);

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        if (commit.State is { } profile)
        {
            await _players.SaveAsync(connection, transaction, profile, ct).ConfigureAwait(false);
        }

        await _idempotency
            .RecordAsync(connection, transaction, commit.Scope, commit.Outcome, commit.OutcomeTtl, ct)
            .ConfigureAwait(false);

        await _economyEvents
            .AppendAsync(connection, transaction, commit.EconomyEvents, ct)
            .ConfigureAwait(false);

        if (commit.OpensScope is { } opened)
        {
            await _idempotency.OpenScopeAsync(connection, transaction, opened, ct).ConfigureAwait(false);
        }

        // Nothing above this line is visible to any other connection, and nothing below it is
        // inside the command at all.
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }
}
