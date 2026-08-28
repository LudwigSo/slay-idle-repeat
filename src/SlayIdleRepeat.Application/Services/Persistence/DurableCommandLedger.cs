using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Contracts;

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
/// the codec's canonical re-encoding of the decoded command rather than the client's own (this seam
/// receives the typed command, not the envelope), which preserves the equality: both sides of a
/// duplicate check are codec decodes.
/// </para>
/// </remarks>
public sealed class DurableCommandLedger : ICommandLedgerStore
{
    private readonly IIdempotencyStore _store;
    private readonly TimeSpan _recordTtl;

    /// <summary>Builds the ledger over the durable store.</summary>
    /// <param name="store">The sequencing and idempotency store.</param>
    /// <param name="recordTtl">Record lifetime — the configured 48 h window. Positive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="recordTtl"/> is zero or negative.</exception>
    public DurableCommandLedger(IIdempotencyStore store, TimeSpan recordTtl)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (recordTtl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(recordTtl), recordTtl,
                "A non-positive record lifetime would expire every outcome at birth, turning every " +
                "retry into a double-apply.");
        }

        _store = store;
        _recordTtl = recordTtl;
    }

    /// <inheritdoc/>
    public Task<long?> ReadLastSequenceAsync(string scope, CancellationToken ct) =>
        _store.ReadLastSequenceAsync(CommandScopes.Resolve(scope), ct);

    /// <inheritdoc/>
    public async Task<LedgerRecord?> ReadRecordAsync(string scope, CommandId commandId, CancellationToken ct)
    {
        var stored = await _store
            .GetRecordedOutcomeAsync(CommandScopes.Resolve(scope), commandId, ct)
            .ConfigureAwait(false);

        return stored is null ? null : LedgerRecord.From(stored);
    }

    /// <summary>Opens a scope at sequence 0. Idempotent, and never a reset.</summary>
    /// <param name="scope">The scope key.</param>
    /// <param name="ct">Cancellation.</param>
    /// <inheritdoc cref="AppendAsync" path="/remarks"/>
    public Task OpenScopeAsync(string scope, CancellationToken ct) =>
        _store.OpenScopeAsync(CommandScopes.Resolve(scope), ct);

    /// <summary>Records one processed command and advances the scope's last sequence, atomically.</summary>
    /// <param name="scope">The scope key.</param>
    /// <param name="record">The record.</param>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// Its own public member rather than part of <see cref="ICommandLedgerStore"/>: a command's
    /// record now lands inside the commit that carries the snapshots it describes, so the gateway
    /// has no reason to write through the ledger at all and a seam offering it a second way to
    /// would be a way to record a command without the state it produced.
    /// </remarks>
    public Task AppendAsync(string scope, LedgerRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);

        return _store.RecordAsync(
            CommandScopes.Resolve(scope),
            new RecordedCommandOutcome(
                record.CommandId,
                record.Sequence,
                WireCommandCodec.WireNameOf(record.Command),
                WireCommandCodec.EncodePayload(record.Command),
                record.ResponseBody,
                record.OpensRunScope),
            _recordTtl,
            ct);
    }
}
