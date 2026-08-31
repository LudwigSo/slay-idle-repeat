using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Moderation;

/// <summary>What raised a review entry. One queue, several producers.</summary>
/// <remarks>
/// The queue is shared on purpose: the plausibility sweep, the duel anti-cheat pass and player
/// reports all end in the same place, reviewed by the same people, escalating through the same
/// ladder. Each producer's milestone adds its own value here rather than its own queue.
/// </remarks>
public enum ReviewSource
{
    /// <summary>The background plausibility sweep in this assembly.</summary>
    PLAUSIBILITY_SWEEP = 1,

    /// <summary>The duel submission checks. ⚠️ No producer yet — the duel anti-cheat milestone's.</summary>
    DUEL_ANTI_CHEAT = 2,

    /// <summary>A player reporting another player. ⚠️ No producer yet — the reporting milestone's.</summary>
    PLAYER_REPORT = 3,
}

/// <summary>Where an entry stands. A human moves it; nothing computes a transition.</summary>
public enum ReviewState
{
    /// <summary>Raised and waiting. The only state a producer may create.</summary>
    OPEN = 1,

    /// <summary>A reviewer found manipulation. Sanctions may follow — as their own records, never automatically.</summary>
    CONFIRMED = 2,

    /// <summary>A reviewer found nothing. The entry stays as the evidence that it was looked at.</summary>
    DISMISSED = 3,
}

/// <summary>One item in the moderation review queue.</summary>
/// <param name="EntryId">The entry's identity.</param>
/// <param name="Source">Which producer raised it.</param>
/// <param name="Subject">The account under review.</param>
/// <param name="State">Where the entry stands.</param>
/// <param name="Reason">What the producer observed, in enough detail for a reviewer to check it.</param>
/// <param name="RaisedAtUtc">When the producer raised it.</param>
/// <param name="ReviewedBy">
/// 🔒 The operator who decided it — <b>an unverified string</b>. See <see cref="ReviewQueueEntry"/>'s
/// own remarks: there is no operator identity anywhere in this system.
/// </param>
/// <param name="ReviewedAtUtc">When it was decided, or <c>null</c> while open.</param>
/// <param name="ReviewNotes">The reviewer's own note, or <c>null</c> while open.</param>
/// <remarks>
/// <para>
/// 🔒 <b>There is no operator identity system in this repository, and this record cannot invent
/// one.</b> Player authentication exists; nothing authenticates a reviewer. <paramref name="ReviewedBy"/>
/// is therefore a string somebody typed, recorded because a decision with no name attached is worse
/// than one with an unverified name — not because anything checked it. Every read of that field
/// carries the same caveat. Nothing in this assembly exposes an endpoint that writes to this queue:
/// an unauthenticated mutation door onto the sanctions ladder would be a far larger hole than the
/// one it closes.
/// </para>
/// <para>
/// A decided entry is final. <see cref="Confirm"/> and <see cref="Dismiss"/> refuse an entry that
/// already carries a verdict rather than overwriting one reviewer's decision with another's.
/// </para>
/// </remarks>
public sealed record ReviewQueueEntry(
    string EntryId,
    ReviewSource Source,
    PlayerId Subject,
    ReviewState State,
    string Reason,
    DateTimeOffset RaisedAtUtc,
    string? ReviewedBy = null,
    DateTimeOffset? ReviewedAtUtc = null,
    string? ReviewNotes = null)
{
    /// <summary>Raises a new, open entry.</summary>
    /// <param name="entryId">The entry's identity. Non-blank.</param>
    /// <param name="source">Which producer is raising it.</param>
    /// <param name="subject">The account under review.</param>
    /// <param name="reason">What was observed. Non-blank — a flag a reviewer cannot check is noise.</param>
    /// <param name="raisedAtUtc">When.</param>
    /// <exception cref="ArgumentException"><paramref name="entryId"/> or <paramref name="reason"/> is blank.</exception>
    public static ReviewQueueEntry Raise(
        string entryId, ReviewSource source, PlayerId subject, string reason, DateTimeOffset raisedAtUtc)
    {
        RequireText(entryId, nameof(entryId), "an entry nobody can address is an entry nobody can close.");
        RequireText(reason, nameof(reason), "a flag a reviewer cannot check is noise in a human's queue.");

        return new ReviewQueueEntry(entryId, source, subject, ReviewState.OPEN, reason, raisedAtUtc);
    }

    /// <summary>Records a reviewer's finding of manipulation.</summary>
    /// <param name="reviewedBy">The unverified operator string. Non-blank.</param>
    /// <param name="notes">The reviewer's note. Non-blank.</param>
    /// <param name="reviewedAtUtc">When.</param>
    /// <exception cref="ArgumentException"><paramref name="reviewedBy"/> or <paramref name="notes"/> is blank.</exception>
    /// <exception cref="InvalidOperationException">The entry already carries a verdict.</exception>
    public ReviewQueueEntry Confirm(string reviewedBy, string notes, DateTimeOffset reviewedAtUtc) =>
        Decide(ReviewState.CONFIRMED, reviewedBy, notes, reviewedAtUtc);

    /// <summary>Records a reviewer finding nothing.</summary>
    /// <param name="reviewedBy">The unverified operator string. Non-blank.</param>
    /// <param name="notes">The reviewer's note. Non-blank.</param>
    /// <param name="reviewedAtUtc">When.</param>
    /// <exception cref="ArgumentException"><paramref name="reviewedBy"/> or <paramref name="notes"/> is blank.</exception>
    /// <exception cref="InvalidOperationException">The entry already carries a verdict.</exception>
    public ReviewQueueEntry Dismiss(string reviewedBy, string notes, DateTimeOffset reviewedAtUtc) =>
        Decide(ReviewState.DISMISSED, reviewedBy, notes, reviewedAtUtc);

    private ReviewQueueEntry Decide(
        ReviewState verdict, string reviewedBy, string notes, DateTimeOffset reviewedAtUtc)
    {
        if (State != ReviewState.OPEN)
        {
            throw new InvalidOperationException(
                $"entry '{EntryId}' is already {State} and cannot be recorded as {verdict}. A second "
                + "verdict would overwrite one reviewer's decision with another's, leaving no trace "
                + "that two people disagreed — reopen the question as a new entry instead.");
        }

        RequireText(
            reviewedBy, nameof(reviewedBy),
            "a decision on the sanctions ladder with no name attached is a decision nobody can be "
            + "asked about. Nothing verifies this name; recording it is the whole of the control.");
        RequireText(notes, nameof(notes), "the next reviewer of this account reads these, and only these.");

        return this with
        {
            State = verdict,
            ReviewedBy = reviewedBy,
            ReviewNotes = notes,
            ReviewedAtUtc = reviewedAtUtc,
        };
    }

    private static void RequireText(string? value, string parameterName, string why)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(why, parameterName);
        }
    }
}
