using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Events;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>The in-memory unit of work: one commit lands the snapshots, the record and the economy rows, or none of them.</summary>
/// <remarks>
/// <para>
/// Composed over the same in-memory stores everything else in this assembly uses, so a commit is
/// visible through the ordinary ports afterwards rather than through a private view of its own.
/// </para>
/// <para>
/// <see cref="FailingSnapshotWrites"/> and <see cref="FailingRecordWrites"/> exist because
/// atomicity is only statable against a fault: without a way to make one half fail, "both or
/// neither" is a sentence no fixture can put a question to.
/// </para>
/// </remarks>
public sealed class InMemoryUnitOfWork : IUnitOfWork
{
    private readonly IPlayerRepository _players;
    private readonly IIdempotencyStore _idempotency;
    private readonly List<EconomyEventRecord> _economyEvents = [];

    /// <summary>Builds the unit of work over the stores it commits into.</summary>
    /// <param name="players">Where the aggregate snapshots land.</param>
    /// <param name="idempotency">Where the outcome record and the sequence advance land.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public InMemoryUnitOfWork(IPlayerRepository players, IIdempotencyStore idempotency)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(idempotency);

        _players = players;
        _idempotency = idempotency;
    }

    /// <summary>When set, the snapshot half of a commit raises instead of landing.</summary>
    public bool FailingSnapshotWrites { get; set; }

    /// <summary>When set, the record half of a commit raises instead of landing.</summary>
    public bool FailingRecordWrites { get; set; }

    /// <summary>Every economy-log row committed so far, in commit order.</summary>
    public IReadOnlyList<EconomyEventRecord> EconomyEvents => _economyEvents;

    /// <inheritdoc/>
    public Task CommitAsync(CommandCommit commit, CancellationToken ct) =>
        throw new NotImplementedException(
            "The in-memory unit of work does not commit yet. It must land the snapshots, the "
            + "outcome record with its sequence advance, and the economy rows as one effect, and "
            + "leave none of them behind when either half is told to fail.");
}
