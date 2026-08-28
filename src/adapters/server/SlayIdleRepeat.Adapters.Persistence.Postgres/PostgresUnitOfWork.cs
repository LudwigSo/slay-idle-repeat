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
    private readonly TimeSpan _runTtl;

    internal PostgresUnitOfWork(NpgsqlDataSource dataSource, TimeSpan runTtl)
    {
        _dataSource = dataSource;
        _runTtl = runTtl;
    }

    /// <inheritdoc/>
    public Task CommitAsync(CommandCommit commit, CancellationToken ct) =>
        throw new NotImplementedException(
            "The one-transaction commit rule is not implemented yet: the snapshots, the outcome "
            + "record with its sequence advance, the economy rows and the opened scope must land "
            + "inside a single transaction on a single connection, or none of them may land.");
}
