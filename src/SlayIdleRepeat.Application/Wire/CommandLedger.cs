using System.Collections.Concurrent;
using SlayIdleRepeat.Core.Commands;

namespace SlayIdleRepeat.Application.Wire;

/// <summary>One processed command as the ledger remembers it — enough to replay it and to detect a conflict.</summary>
/// <param name="CommandId">The idempotency key, scoped to the ledger scope it is stored under.</param>
/// <param name="Sequence">The sequence the command consumed.</param>
/// <param name="Command">The typed command as decoded — record value equality is what "same payload" means (14 §16.3), so whitespace and key order in the original JSON cannot split a retry from its first send.</param>
/// <param name="ResponseBody">The exact response body the first processing produced. A duplicate replays these bytes, never a recomputation.</param>
/// <param name="OpensRunScope">
/// The run scope this command's acceptance opened, or <c>null</c> for every other record. Carried
/// so a replay can repair a missing open: the append and the open are two store calls, and a
/// durable backing may fail between them.
/// </param>
/// <remarks>
/// ⚠️ A durable backing does not persist <paramref name="Command"/> as a .NET object: it stores the
/// envelope's <c>type</c> and payload JSON as sent and re-decodes through <c>WireCommandCodec</c>
/// on read — the one vocabulary and the one equality, never a second command serialisation.
/// </remarks>
public sealed record LedgerRecord(
    CommandId CommandId,
    long Sequence,
    GameCommand Command,
    string ResponseBody,
    string? OpensRunScope = null);

/// <summary>Where the sequencing state and idempotency records of 14 §16.3 live.</summary>
/// <remarks>
/// <para>
/// A dumb store on purpose: last-sequence per scope, records by command id, and scope existence.
/// The rules — expected is last + 1, duplicate replays, stale, gap, conflict — live in
/// <see cref="CommandGateway"/> and nowhere else, so the store that replaces this seam inherits
/// them instead of re-deciding them.
/// </para>
/// <para>
/// A scope is one sequencing domain: <c>run:&lt;runId&gt;</c> for run commands,
/// <c>player:&lt;playerId&gt;</c> for the player's lifetime counter. A player scope exists
/// implicitly (the lifetime counter starts at 0 the moment the player does); a run scope exists
/// only once <see cref="OpenScopeAsync"/> opened it on the accepted <c>START_RUN</c> — an unknown
/// run scope is how the gateway answers <c>RUN_NOT_FOUND</c> before any state is loaded.
/// </para>
/// <para>
/// ⚠️ Record TTLs are storage semantics and deliberately absent from this seam's shape: the run
/// scope lives exactly as long as the run state's sliding 48 h TTL and a player record 48 h from
/// its command (14 §16.3), which the M5-05 Redis/Postgres backing enforces where expiry is real.
/// This seam is what that task implements; <see cref="VolatileCommandLedger"/> is the placeholder
/// until it does.
/// </para>
/// <para>
/// 🔒 Two contract clauses a durable backing must honour, stated here so M5-05 never re-decides
/// them. <b>One:</b> <see cref="AppendAsync"/> commits the record and the last-sequence advance as
/// ONE atomic effect — two statements with a crash between them would let a retry find no record,
/// pass <c>last + 1</c>, and double-apply a committed command. <b>Two:</b> the caller guarantees
/// one writer per scope within one process (the gateway's player gate); cross-instance sequencing
/// is deliberately outside this contract until 14 §16.4's one-transaction commit rule (M5-04)
/// makes the store itself the arbiter.
/// </para>
/// </remarks>
public interface ICommandLedgerStore
{
    /// <summary>The highest sequence a scope has consumed, or <c>null</c> when the scope was never opened.</summary>
    /// <param name="scope">The scope key.</param>
    /// <param name="ct">Cancellation.</param>
    Task<long?> ReadLastSequenceAsync(string scope, CancellationToken ct);

    /// <summary>The record stored under a command id in a scope, or <c>null</c>.</summary>
    /// <param name="scope">The scope key.</param>
    /// <param name="commandId">The idempotency key.</param>
    /// <param name="ct">Cancellation.</param>
    Task<LedgerRecord?> ReadRecordAsync(string scope, CommandId commandId, CancellationToken ct);

    /// <summary>Opens a scope at sequence 0 — the accepted <c>START_RUN</c>'s half of "the run's sequence starts at 1".</summary>
    /// <param name="scope">The scope key.</param>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// 🔒 Idempotent, and never a reset: opening a scope that already exists leaves its counter and
    /// records untouched. The replay path re-opens on every replayed opening acceptance, so a
    /// backing that reset here would zero a live run's sequence on a retried <c>START_RUN</c>.
    /// </remarks>
    Task OpenScopeAsync(string scope, CancellationToken ct);

    /// <summary>Appends one processed command's record and advances the scope's last sequence to its sequence.</summary>
    /// <param name="scope">The scope key.</param>
    /// <param name="record">The record.</param>
    /// <param name="ct">Cancellation.</param>
    Task AppendAsync(string scope, LedgerRecord record, CancellationToken ct);
}

/// <summary>
/// ⚠️ PLACEHOLDER — the in-process <see cref="ICommandLedgerStore"/> that stands in until M5-05
/// lands the Redis hot-cache over the Postgres-authoritative record. Process-lifetime memory: a
/// restart forgets every sequence and every outcome, which is tolerable only while no real client
/// depends on this server.
/// </summary>
/// <remarks>
/// In this assembly rather than the composition root, unlike <c>PlaceholderVolatileWorldStore</c>,
/// because it is the seam's reference implementation: the gateway's own suite runs the 16.3 rules
/// against it, so the semantics M5-05's backing must reproduce are exercised here rather than
/// restated there. It dies with that task.
/// </remarks>
public sealed class VolatileCommandLedger : ICommandLedgerStore
{
    private sealed record ScopeState(long LastSequence, ConcurrentDictionary<string, LedgerRecord> Records);

    private readonly ConcurrentDictionary<string, ScopeState> _scopes = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public Task<long?> ReadLastSequenceAsync(string scope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return Task.FromResult(_scopes.TryGetValue(scope, out var state) ? state.LastSequence : (long?)null);
    }

    /// <inheritdoc/>
    public Task<LedgerRecord?> ReadRecordAsync(string scope, CommandId commandId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return Task.FromResult(
            _scopes.TryGetValue(scope, out var state) && state.Records.TryGetValue(commandId.Value, out var record)
                ? record
                : null);
    }

    /// <inheritdoc/>
    public Task OpenScopeAsync(string scope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);

        _scopes.TryAdd(scope, new ScopeState(0, new ConcurrentDictionary<string, LedgerRecord>(StringComparer.Ordinal)));

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task AppendAsync(string scope, LedgerRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(record);

        _scopes.AddOrUpdate(
            scope,
            _ =>
            {
                var records = new ConcurrentDictionary<string, LedgerRecord>(StringComparer.Ordinal);
                records[record.CommandId.Value] = record;
                return new ScopeState(record.Sequence, records);
            },
            (_, state) =>
            {
                state.Records[record.CommandId.Value] = record;
                return state with { LastSequence = Math.Max(state.LastSequence, record.Sequence) };
            });

        return Task.CompletedTask;
    }
}
