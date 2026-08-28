using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Moderation;

/// <summary>The moderation schema's one door: plausibility observations, the review queue, sanctions.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>A seam, not a port</b>, on the same footing as the command ledger's and the command
/// throttle's: the port catalogue is the transcription of the architecture document's port list, and
/// moderation storage appears nowhere in it — inventing a thirteenth port would put a name into that
/// catalogue that no specification authored. What makes this honest rather than a loophole is that
/// this interface names no technology either; the three groups behind it are one bounded context
/// with one migration, and one door is what keeps a review entry and the sanction that came out of
/// it from being written through two unrelated seams.
/// </para>
/// <para>
/// ⚠️ <b>The only shipped implementation is the in-memory one.</b> The tables exist in the migration
/// history and this interface is the shape a durable implementation must take, but no
/// Postgres-backed implementation is written yet: nothing in this repository could observe one — no
/// contract fixture may reach a live database, and no probe posts a moderation row — so writing one
/// now would ship an adapter nothing anywhere exercises. The task that gives the review queue a
/// human surface is the one that owes the durable implementation and the probe that watches it.
/// Until then, a deployment that enables the sweep is told at startup that its findings are volatile.
/// </para>
/// </remarks>
public interface IModerationStore
{
    /// <summary>Every account's cumulative measures as of <paramref name="at"/> — the sweep's input.</summary>
    /// <param name="at">The instant to stamp the readings with.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<PlausibilityObservation>> ObserveAccountsAsync(DateTimeOffset at, CancellationToken ct);

    /// <summary>The last observation recorded for each of these accounts. An account on its first sweep is absent.</summary>
    /// <param name="players">The accounts, as one batch.</param>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// A batch rather than one call per account, decided while there is still one implementation: a
    /// sweep touches every account there is, and a per-row shape would commit whoever writes the
    /// durable store to a round trip per player per pass.
    /// </remarks>
    Task<IReadOnlyDictionary<PlayerId, PlausibilityObservation>> ReadPreviousObservationsAsync(
        IReadOnlyCollection<PlayerId> players, CancellationToken ct);

    /// <summary>Stores this pass's readings so the next sweep has something to measure against.</summary>
    /// <param name="observations">The readings, as one batch.</param>
    /// <param name="ct">Cancellation.</param>
    Task RecordObservationsAsync(IReadOnlyCollection<PlausibilityObservation> observations, CancellationToken ct);

    /// <summary>Appends an entry to the review queue.</summary>
    /// <param name="entry">The entry. Open, by every producer.</param>
    /// <param name="ct">Cancellation.</param>
    Task RaiseReviewAsync(ReviewQueueEntry entry, CancellationToken ct);

    /// <summary>Stores a reviewer's verdict over the entry it decides.</summary>
    /// <param name="decided">The entry as the verdict left it.</param>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// ⚠️ No production caller: nothing in this repository has a surface a human reviews through,
    /// and this assembly deliberately exposes no endpoint that writes the queue — an unauthenticated
    /// door onto the sanctions ladder would be a far larger hole than the one it closes. Declared
    /// anyway, because without it the CONFIRMED and DISMISSED states are unreachable by construction
    /// and the schema's verdict columns describe something no shape can produce.
    /// </remarks>
    Task RecordVerdictAsync(ReviewQueueEntry decided, CancellationToken ct);

    /// <summary>Every queue entry in one state, oldest first.</summary>
    /// <param name="state">Which state.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<ReviewQueueEntry>> ReadReviewsAsync(ReviewState state, CancellationToken ct);

    /// <summary>Every sanction on record, lifted or not. The caller decides what "active" means at which instant.</summary>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<PlayerSanction>> ReadSanctionsAsync(CancellationToken ct);
}
