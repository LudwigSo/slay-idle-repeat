using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Wire;

namespace SlayIdleRepeat.Application.Services.Persistence;

/// <summary>The durable <see cref="ICommandLedgerStore"/>: the gateway's seam, carried by <see cref="IIdempotencyStore"/>.</summary>
/// <remarks>
/// <para>
/// Lives in the application on purpose. The store of record is reached only through its port, and
/// an adapter implementing this seam directly would quietly turn the seam into a port with no
/// contract gate — the exact hole the port catalogue's <c>IGameApiPort</c> entry documents. Here,
/// the seam keeps in-application implementations only, and the port underneath carries the A5 pair.
/// </para>
/// <para>
/// The ledger contract's atomicity clause is discharged by delegation:
/// <see cref="IIdempotencyStore.RecordAsync"/> commits the record and the last-sequence advance as
/// one atomic effect.
/// </para>
/// <para>
/// A record's command is stored as its wire type name plus canonical payload JSON and re-decoded
/// through <c>WireCommandCodec</c> on read — the one vocabulary and the one equality. The bytes are
/// the codec's canonical re-encoding of the decoded command rather than the client's own, which
/// preserves the equality (both sides of a duplicate check are codec decodes) without a second
/// serialisation of anything.
/// </para>
/// </remarks>
public sealed class DurableCommandLedger : ICommandLedgerStore
{
    /// <summary>Builds the ledger over the durable store.</summary>
    /// <param name="store">The sequencing and idempotency store.</param>
    /// <param name="recordTtl">Record lifetime — the configured 48 h window. Positive.</param>
    public DurableCommandLedger(IIdempotencyStore store, TimeSpan recordTtl) =>
        throw new NotImplementedException("M5-05 phase 3 implements the durable ledger.");

    /// <inheritdoc/>
    public Task<long?> ReadLastSequenceAsync(string scope, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the durable ledger.");

    /// <inheritdoc/>
    public Task<LedgerRecord?> ReadRecordAsync(string scope, CommandId commandId, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the durable ledger.");

    /// <inheritdoc/>
    public Task OpenScopeAsync(string scope, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the durable ledger.");

    /// <inheritdoc/>
    public Task AppendAsync(string scope, LedgerRecord record, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the durable ledger.");
}
