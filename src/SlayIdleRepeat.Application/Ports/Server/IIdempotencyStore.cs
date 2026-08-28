using SlayIdleRepeat.Application.Wire;

namespace SlayIdleRepeat.Application.Ports.Server;

/// <summary>The durable home of sequencing counters and idempotency outcome records, per scope.</summary>
/// <remarks>
/// <para>
/// <c>23</c> §4.2's sketch widened: records key on (scope, commandId) because the same command id is
/// legal in a run's domain and in the player's lifetime domain, with different lifetimes — a run
/// record lives exactly as long as the run's sliding lifetime, a player record for the given
/// lifetime from its own command. Two extra members carry the counters, because the counters live
/// beside the state the commands produced, never inside it.
/// </para>
/// <para>
/// 🔒 <see cref="RecordAsync"/> commits the record and the scope's last-sequence advance as ONE
/// atomic effect. Two separate effects with a crash between them would let a retry find no record,
/// pass the sequence gate, and double-apply a committed command — the exact failure the command
/// ledger's own contract names.
/// </para>
/// <para>
/// Recording presumes the scope's subject was already stored (an accepted command's own save
/// precedes its record): a real store may refuse a record for a player or run it has never seen,
/// and the in-memory fake accepts any scope. A caller that records before saving is miswired.
/// </para>
/// <para>
/// A run-scoped record's lifetime follows its run; the in-memory fake approximates with the
/// per-record lifetime it was handed, which is the same 48 hours by configuration.
/// </para>
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>The record stored under a command id in a scope, or <c>null</c> when none is stored or its lifetime has passed.</summary>
    /// <param name="scope">The sequencing domain.</param>
    /// <param name="commandId">The idempotency key.</param>
    /// <param name="ct">Cancellation.</param>
    Task<RecordedCommandOutcome?> GetRecordedOutcomeAsync(
        IdempotencyScope scope, CommandId commandId, CancellationToken ct);

    /// <summary>Records one processed command and advances the scope's last sequence to its sequence, atomically.</summary>
    /// <param name="scope">The sequencing domain.</param>
    /// <param name="outcome">The record.</param>
    /// <param name="ttl">The record's lifetime, measured from now. Positive.</param>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// A second record under an already-recorded (scope, commandId) is unreachable by contract —
    /// the ledger reads before it appends, and a found record is replayed, never re-recorded — so
    /// its behaviour is deliberately unspecified rather than pinned to whichever store's default
    /// happened to win.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ttl"/> is zero or negative.</exception>
    Task RecordAsync(IdempotencyScope scope, RecordedCommandOutcome outcome, TimeSpan ttl, CancellationToken ct);

    /// <summary>The highest sequence a scope has consumed, or <c>null</c> when the scope was never opened.</summary>
    /// <param name="scope">The sequencing domain.</param>
    /// <param name="ct">Cancellation.</param>
    Task<long?> ReadLastSequenceAsync(IdempotencyScope scope, CancellationToken ct);

    /// <summary>Opens a scope at sequence 0. Idempotent, and never a reset: an open scope's counter and records stay untouched.</summary>
    /// <param name="scope">The sequencing domain.</param>
    /// <param name="ct">Cancellation.</param>
    Task OpenScopeAsync(IdempotencyScope scope, CancellationToken ct);
}
