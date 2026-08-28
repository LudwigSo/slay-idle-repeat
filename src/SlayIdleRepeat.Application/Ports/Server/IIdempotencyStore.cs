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
/// 🔒 A run-scoped record lives as long as its run's ROW does — its EXISTENCE, never its liveness.
/// Whether a run may still be played is a game rule the domain decides from the row it loads, and a
/// store that hid the counter or the record of a run whose window had passed would answer
/// <c>RUN_NOT_FOUND</c> — "your run never existed" — before the domain was ever asked what had
/// actually happened to it. The in-memory fake approximates with the per-record lifetime it was
/// handed, which is the same 48 hours by configuration.
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

    /// <summary>One scope's records above a sequence, oldest first — what a client that has fallen behind missed.</summary>
    /// <param name="scope">The sequencing domain. Records of any other scope are never in the answer.</param>
    /// <param name="sinceSequence">The exclusive lower bound: the last sequence the caller already holds.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The scope's records with a sequence strictly above the bound, ascending by sequence. Never null.</returns>
    /// <remarks>
    /// <para>
    /// An empty list means one thing only: nothing is recorded in this scope above that sequence —
    /// genuinely nothing, so the caller is up to date. It never means "I could not look". A store
    /// that cannot answer must fail rather than borrow emptiness to say so, because a caller reads
    /// empty as an answer and will stop asking.
    /// </para>
    /// <para>
    /// Records whose lifetime has passed are simply absent, so the answer may have a HOLE — 4 and 6
    /// with no 5. That is why the order is part of the contract rather than a convenience: a caller
    /// walking the sequences from its own bound sees the gap and can order a full resync, instead of
    /// being handed a short list it would otherwise take for the complete set of what it missed.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<RecordedCommandOutcome>> ReadOutcomesAfterAsync(
        IdempotencyScope scope, long sinceSequence, CancellationToken ct);

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
