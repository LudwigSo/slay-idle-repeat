using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Events;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>The in-memory unit of work: one commit lands the snapshots, the record, the economy rows and the claim's stamp, or none of them.</summary>
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
    private readonly IMessageRepository? _messages;
    private readonly List<EconomyEventRecord> _economyEvents = [];
    private readonly object _economyGate = new();

    /// <summary>Builds the unit of work over the stores it commits into.</summary>
    /// <param name="players">Where the aggregate snapshots land.</param>
    /// <param name="idempotency">Where the outcome record and the sequence advance land.</param>
    /// <param name="messages">Where a claim's stamp lands, or <c>null</c> for an arrangement with no inbox — one that is then handed a commit carrying a claim raises rather than dropping it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="players"/> or <paramref name="idempotency"/> is null.</exception>
    public InMemoryUnitOfWork(
        IPlayerRepository players, IIdempotencyStore idempotency, IMessageRepository? messages = null)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(idempotency);

        _players = players;
        _idempotency = idempotency;
        _messages = messages;
    }

    /// <summary>When set, the snapshot half of a commit raises instead of landing.</summary>
    public bool FailingSnapshotWrites { get; set; }

    /// <summary>When set, the record half of a commit raises instead of landing.</summary>
    public bool FailingRecordWrites { get; set; }

    /// <summary>When set, the claim's stamp raises instead of landing — the third half, same reason as the other two.</summary>
    public bool FailingClaimWrites { get; set; }

    /// <summary>Every economy-log row committed so far, in commit order.</summary>
    /// <remarks>
    /// A snapshot under the same gate the append takes, as the recording sink and the recording
    /// telemetry beside it both do. Handing out the live list makes a reader enumerating it while
    /// a parallel commit appends throw <c>InvalidOperationException</c> — a fixture failing on the
    /// arrangement rather than on the claim, which is the worst way for a suite to go red.
    /// </remarks>
    public IReadOnlyList<EconomyEventRecord> EconomyEvents
    {
        get
        {
            lock (_economyGate)
            {
                return _economyEvents.ToArray();
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Everything that can refuse the commit is asked BEFORE anything is written — the fault knobs
    /// and the token — so a commit that cannot complete leaves nothing behind at all. There is no
    /// transaction to roll back here, so the only honest way to be whole-or-nothing is to decide
    /// before the first write rather than to unwind after one.
    /// </remarks>
    public async Task CommitAsync(CommandCommit commit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ct.ThrowIfCancellationRequested();

        if (FailingSnapshotWrites)
        {
            throw new InvalidOperationException(
                "scripted: the snapshot half of this commit was told to fail.");
        }

        if (FailingRecordWrites)
        {
            throw new InvalidOperationException(
                "scripted: the record half of this commit was told to fail.");
        }

        if (FailingClaimWrites)
        {
            throw new InvalidOperationException(
                "scripted: the claim's stamp in this commit was told to fail.");
        }

        if (commit.Claim is not null && _messages is null)
        {
            throw new InvalidOperationException(
                "This commit carries an inbox claim and no message store was composed. Dropping it " +
                "would leave the reward paid by the snapshot below and the message still reading as " +
                "claimable — the double-grant the claim rides this commit to prevent.");
        }

        if (commit.State is { } profile)
        {
            await _players.SaveAsync(profile, ct).ConfigureAwait(false);
        }

        await _idempotency.RecordAsync(commit.Scope, commit.Outcome, commit.OutcomeTtl, ct)
            .ConfigureAwait(false);

        if (commit.OpensScope is { } opened)
        {
            await _idempotency.OpenScopeAsync(opened, ct).ConfigureAwait(false);
        }

        if (commit.Claim is { } claim)
        {
            await _messages!.MarkClaimedAsync(claim.Player, claim.Messages, ct).ConfigureAwait(false);
        }

        lock (_economyGate)
        {
            _economyEvents.AddRange(commit.EconomyEvents);
        }
    }
}
