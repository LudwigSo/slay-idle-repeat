using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core;

/// <summary>One message as the claim rules see it: what it is, what it carries, and whether it is spent.</summary>
/// <param name="Id">The message's identity. Every grant in the game is idempotent on this value.</param>
/// <param name="Category">Which of the six kinds it is.</param>
/// <param name="TemplateId">The authored template the client renders. The rules never read it.</param>
/// <param name="Attachments">What it carries, in the order the row stores them. May be empty.</param>
/// <param name="CreatedAtUtc">When the server sent it. The first key claims are ordered by.</param>
/// <param name="ExpiresAtUtc">
/// When it disappears, or <c>null</c> for a message that never expires. The rules do not expire
/// anything — the nightly job does — but a claim reads this to tell a live message from a stale row.
/// </param>
/// <param name="ClaimedAtUtc">When its attachments were granted, or <c>null</c> while they have not been.</param>
/// <remarks>
/// <para>
/// 🔒 It carries no read timestamp and no rendered parameters, and both absences are deliberate. The
/// rules decide a grant; what a message SAYS is the client's, and nothing in this build marks a
/// message read — that surface has no screen and no command, and inventing either here would be a
/// field nothing ever writes.
/// </para>
/// <para>
/// Equality is hand-written over the attachment sequence: synthesized record equality would compare
/// the list by reference, so two identical messages read out of the same row twice would compare
/// unequal.
/// </para>
/// </remarks>
public sealed record InboxMessage(
    MessageId Id,
    MessageCategory Category,
    string TemplateId,
    IReadOnlyList<MailAttachment> Attachments,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? ClaimedAtUtc)
{
    /// <inheritdoc cref="InboxMessage"/>
    public string TemplateId { get; } = IdText.Require(TemplateId, nameof(InboxMessage));

    /// <inheritdoc cref="InboxMessage"/>
    public IReadOnlyList<MailAttachment> Attachments { get; } =
        InboxSequences.Copy(Attachments, nameof(Attachments));

    /// <summary>Whether the attachments have already been granted.</summary>
    public bool IsClaimed => ClaimedAtUtc is not null;

    /// <summary>
    /// Whether a claim would grant anything: the attachments are unspent, there is at least one, and
    /// every one of them is a kind this build can grant.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>All of the attachments or none of them.</b> A message that paid out its currencies and
    /// held back its chest would be claimed — its row stamped — with a reward still owed, and
    /// nothing left holding the debt. So one ungrantable attachment holds the whole message, which
    /// is the state the register's owners are pointed at.
    /// <para>
    /// A message with nothing attached is not claimable, and that is not a refusal to show it: an
    /// announcement is read, not collected, and stamping it claimed would spend the one field that
    /// says "these rewards were paid".
    /// </para>
    /// </remarks>
    public bool IsClaimable =>
        !IsClaimed &&
        Attachments.Count > 0 &&
        Attachments.All(a => MailAttachmentKinds.Resolve(a).IsGrantable);

    /// <summary>The first attachment this build cannot grant, or <c>null</c> when it can grant them all.</summary>
    /// <remarks>
    /// The named half of <see cref="IsClaimable"/>: a caller refusing a claim reports WHICH absence
    /// held it, so "not built yet" never reads as "nothing to claim".
    /// </remarks>
    public MailAttachmentRefusal? FirstRefusal =>
        Attachments
            .Select(a => MailAttachmentKinds.Resolve(a).Refusal)
            .FirstOrDefault(r => r is not null);

    /// <summary>Two messages are equal when every member and every attachment matches.</summary>
    /// <param name="other">The other message.</param>
    /// <returns>Whether they describe the same message.</returns>
    public bool Equals(InboxMessage? other) =>
        other is not null &&
        Id == other.Id &&
        Category == other.Category &&
        string.Equals(TemplateId, other.TemplateId, StringComparison.Ordinal) &&
        CreatedAtUtc == other.CreatedAtUtc &&
        ExpiresAtUtc == other.ExpiresAtUtc &&
        ClaimedAtUtc == other.ClaimedAtUtc &&
        Attachments.SequenceEqual(other.Attachments);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(Id);
        hash.Add(Category);
        hash.Add(TemplateId, StringComparer.Ordinal);
        hash.Add(CreatedAtUtc);
        hash.Add(ExpiresAtUtc);
        hash.Add(ClaimedAtUtc);

        foreach (var attachment in Attachments)
        {
            hash.Add(attachment);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// The player's inbox as <c>Apply</c> sees it: a read-only projection, loaded by the Application
/// layer, that the domain reads and never returns a mutated copy of.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>A projection, not an aggregate</b>, and the shape is `30` §5's guild resolution applied to a
/// second contended store. The messages live in their own table, are written by ops tooling and by a
/// nightly job as well as by the player, and are indexed by owner and expiry — so a claim that
/// loaded, rewrote and saved the whole inbox as player state would put an ops send and a claim in a
/// race the player's snapshot would win. The domain therefore reads this view, grants into
/// <c>Player</c>, and returns the claims it made as events; applying them to the store is the
/// Application layer's.
/// </para>
/// <para>
/// It lives in the <c>SlayIdleRepeat.Core</c> root beside <c>WorldSlice</c> for
/// <c>WorldSlice</c>'s own reason: the Application layer must be able to construct one on every
/// command, which a public type under <c>Core/Model/</c> may not allow.
/// </para>
/// <para>
/// Ordered on construction by <c>(CreatedAtUtc, Id)</c>, and that is load-bearing rather than tidy:
/// an Energy attachment that overflows into the Reserve depends on how full the bar already is, so
/// two claims of the same set in a different order would bank different amounts.
/// </para>
/// </remarks>
public sealed record InboxView
{
    /// <summary>The inbox of a player who has no messages. Not the same thing as an inbox nobody loaded.</summary>
    public static readonly InboxView Empty = new(Array.Empty<InboxMessage>());

    /// <summary>Builds the view over the messages the repository answered with.</summary>
    /// <param name="messages">The player's live messages, in any order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="messages"/> is null.</exception>
    /// <exception cref="ArgumentException">A message is null, or two messages share an id.</exception>
    public InboxView(IReadOnlyList<InboxMessage> messages)
    {
        Messages = Ordered(InboxSequences.Copy(messages, nameof(messages)));
    }

    /// <summary>The player's messages, ordered oldest first and then by id.</summary>
    public IReadOnlyList<InboxMessage> Messages { get; }

    /// <summary>The messages a claim would actually grant, in claim order.</summary>
    /// <returns>A fresh list on every call; it is filtered and copied rather than stored.</returns>
    public IReadOnlyList<InboxMessage> Claimable() =>
        Messages.Where(m => m.IsClaimable).ToArray();

    /// <summary>The message with this id, or <c>null</c> when the player has no such message.</summary>
    /// <param name="id">The id to look up.</param>
    public InboxMessage? Find(MessageId id) => Messages.FirstOrDefault(m => m.Id == id);

    /// <summary>Two views are equal when they hold the same messages in the same order.</summary>
    /// <param name="other">The other view.</param>
    /// <returns>Whether they describe the same inbox.</returns>
    public bool Equals(InboxView? other) =>
        other is not null && Messages.SequenceEqual(other.Messages);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var message in Messages)
        {
            hash.Add(message);
        }

        return hash.ToHashCode();
    }

    /// <summary>Renders the count rather than every message, so a slice's <c>ToString()</c> stays readable.</summary>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    private bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(
            CultureInfo.InvariantCulture,
            $"{nameof(Messages)} = {Messages.Count}, {nameof(Claimable)} = {Claimable().Count}");

        return true;
    }

    private static IReadOnlyList<InboxMessage> Ordered(IReadOnlyList<InboxMessage> messages)
    {
        var duplicate = messages
            .GroupBy(m => m.Id)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            var count = duplicate.Count().ToString(CultureInfo.InvariantCulture);

            throw new ArgumentException(
                "The inbox was loaded with " + count + " messages sharing the id " + duplicate.Key +
                ". A grant is idempotent ON THAT ID, so two rows under one id are two rows the " +
                "claim path would treat as one — paying one of them and stamping both, or paying " +
                "both and owing a reward twice.",
                nameof(messages));
        }

        return new ReadOnlyCollection<InboxMessage>(
            messages
                .OrderBy(m => m.CreatedAtUtc)
                .ThenBy(m => m.Id.Value, StringComparer.Ordinal)
                .ToArray());
    }
}

/// <summary>The copy-and-guard both projection types share, so there is one guard rather than two.</summary>
internal static class InboxSequences
{
    /// <summary>An immutable copy of the sequence, refusing a null element.</summary>
    /// <param name="values">The sequence to copy.</param>
    /// <param name="parameter">The parameter a refusal blames.</param>
    internal static IReadOnlyList<T> Copy<T>(IReadOnlyList<T>? values, string parameter)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(values, parameter);

        var copied = values.ToArray();

        foreach (var value in copied)
        {
            if (value is null)
            {
                throw new ArgumentException(
                    "A null element reached an inbox projection. The projection is built from stored " +
                    "rows, so a null here is a decode that half-succeeded — and the claim path would " +
                    "then skip a reward the row says is owed.",
                    parameter);
            }
        }

        return new ReadOnlyCollection<T>(copied);
    }
}
