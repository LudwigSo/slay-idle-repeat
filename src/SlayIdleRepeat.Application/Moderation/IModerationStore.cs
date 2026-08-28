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

    /// <summary>The last observation recorded for an account, or <c>null</c> when this is its first.</summary>
    /// <param name="player">The account.</param>
    /// <param name="ct">Cancellation.</param>
    Task<PlausibilityObservation?> ReadPreviousObservationAsync(PlayerId player, CancellationToken ct);

    /// <summary>Stores an observation so the next sweep has something to measure against.</summary>
    /// <param name="observation">The reading.</param>
    /// <param name="ct">Cancellation.</param>
    Task RecordObservationAsync(PlausibilityObservation observation, CancellationToken ct);

    /// <summary>Appends an entry to the review queue.</summary>
    /// <param name="entry">The entry. Open, by every producer.</param>
    /// <param name="ct">Cancellation.</param>
    Task RaiseReviewAsync(ReviewQueueEntry entry, CancellationToken ct);

    /// <summary>Every queue entry in one state, oldest first.</summary>
    /// <param name="state">Which state.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<ReviewQueueEntry>> ReadReviewsAsync(ReviewState state, CancellationToken ct);

    /// <summary>Every sanction on record, lifted or not. The caller decides what "active" means at which instant.</summary>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<PlayerSanction>> ReadSanctionsAsync(CancellationToken ct);
}
