using System.Collections.ObjectModel;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Ports.Server;

/// <summary>One stored inbox message: the whole row, as <c>28</c> A3 writes it.</summary>
/// <param name="Id">The message's identity, and the value every grant of it is idempotent on.</param>
/// <param name="Player">Whose inbox it is in.</param>
/// <param name="Category">Which of the six kinds it is.</param>
/// <param name="TemplateId">The authored template's loc key. Messages are templates, never free text.</param>
/// <param name="Params">
/// The template's typed parameters, already rendered invariantly by whoever sent the message —
/// <c>AnalyticsEvent.Properties</c>' rule, for its reason: this row carries text and formats nothing,
/// so a message cannot come out reading differently on a host with a different culture.
/// </param>
/// <param name="Attachments">What it carries, in the order the sender wrote them. May be empty.</param>
/// <param name="CreatedAtUtc">When the server sent it.</param>
/// <param name="ExpiresAtUtc">
/// When it disappears, or <c>null</c> for a message that never expires. The two record categories
/// carry <c>null</c>; nothing else may.
/// </param>
/// <param name="ReadAtUtc">
/// When the player read it, or <c>null</c>. ⚠️ Nothing in this build ever writes it: there is no
/// read command in the frozen command vocabulary and no screen to send one from. The column is
/// M5-05's and the field is <c>28</c> A3's; the absence is registered rather than filled in.
/// </param>
/// <param name="ClaimedAtUtc">When its attachments were granted, or <c>null</c> while they have not been.</param>
/// <remarks>
/// A port payload rather than a domain type: it carries the presentation half — the template and its
/// parameters — that the rules never read. What the rules do read is projected out of it into
/// <see cref="InboxMessage"/>.
/// </remarks>
public sealed record PlayerMessage(
    MessageId Id,
    PlayerId Player,
    MessageCategory Category,
    string TemplateId,
    IReadOnlyDictionary<string, string> Params,
    IReadOnlyList<MailAttachment> Attachments,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? ReadAtUtc,
    DateTimeOffset? ClaimedAtUtc)
{
    /// <inheritdoc cref="PlayerMessage"/>
    public string TemplateId { get; } = Required(TemplateId);

    /// <inheritdoc cref="PlayerMessage"/>
    public IReadOnlyDictionary<string, string> Params { get; } = CopyParams(Params);

    /// <inheritdoc cref="PlayerMessage"/>
    public IReadOnlyList<MailAttachment> Attachments { get; } = CopyAttachments(Attachments);

    /// <summary>Whether this message is one of the two the expiry and capacity rules must never touch.</summary>
    public bool IsPermanentRecord => MessageCategories.IsPermanentRecord(Category);

    /// <summary>What the claim rules see of this message.</summary>
    /// <returns>The projection, without the template or its parameters.</returns>
    public InboxMessage ToProjection() =>
        new(Id, Category, TemplateId, Attachments, CreatedAtUtc, ExpiresAtUtc, ClaimedAtUtc);

    /// <summary>Two messages are equal when every member, parameter and attachment matches.</summary>
    /// <param name="other">The other message.</param>
    /// <returns>Whether they describe the same stored row.</returns>
    /// <remarks>
    /// Hand-written for steering S17's reason: synthesized record equality compares the dictionary
    /// and the list by reference, so a row read back out of a store would never equal the row that
    /// was written — which is exactly the assertion a contract suite is made of.
    /// </remarks>
    public bool Equals(PlayerMessage? other) =>
        other is not null &&
        Id == other.Id &&
        Player == other.Player &&
        Category == other.Category &&
        string.Equals(TemplateId, other.TemplateId, StringComparison.Ordinal) &&
        CreatedAtUtc == other.CreatedAtUtc &&
        ExpiresAtUtc == other.ExpiresAtUtc &&
        ReadAtUtc == other.ReadAtUtc &&
        ClaimedAtUtc == other.ClaimedAtUtc &&
        Attachments.SequenceEqual(other.Attachments) &&
        Params.Count == other.Params.Count &&
        Params.All(p =>
            other.Params.TryGetValue(p.Key, out var value) &&
            string.Equals(p.Value, value, StringComparison.Ordinal));

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(Id);
        hash.Add(Player);
        hash.Add(Category);
        hash.Add(TemplateId, StringComparer.Ordinal);
        hash.Add(CreatedAtUtc);
        hash.Add(ExpiresAtUtc);
        hash.Add(ReadAtUtc);
        hash.Add(ClaimedAtUtc);

        foreach (var attachment in Attachments)
        {
            hash.Add(attachment);
        }

        // Ordinal-sorted, because a dictionary's enumeration order is not part of its value and two
        // equal rows must hash alike.
        foreach (var (key, value) in Params.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            hash.Add(key, StringComparer.Ordinal);
            hash.Add(value, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    private static string Required(string templateId) =>
        string.IsNullOrWhiteSpace(templateId)
            ? throw new ArgumentException(
                "A message names no template. Messages are authored templates with typed " +
                "parameters and never free text, so a row with no template is one nothing can " +
                "render and nobody reviewed.",
                nameof(TemplateId))
            : templateId;

    private static IReadOnlyDictionary<string, string> CopyParams(
        IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters, nameof(Params));

        var owned = new Dictionary<string, string>(parameters.Count, StringComparer.Ordinal);

        foreach (var (key, value) in parameters)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException(
                    "A template parameter has a blank name, so the template has no slot to put its " +
                    "value in and the message would render with a hole in it.",
                    nameof(Params));
            }

            owned[key] = value ?? throw new ArgumentException(
                "Template parameter '" + key + "' has no value. A null renders as nothing, which " +
                "turns a compensation notice into a sentence with a missing number.",
                nameof(Params));
        }

        return new ReadOnlyDictionary<string, string>(owned);
    }

    private static IReadOnlyList<MailAttachment> CopyAttachments(
        IReadOnlyList<MailAttachment> attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments, nameof(Attachments));

        var owned = attachments.ToArray();

        foreach (var attachment in owned)
        {
            if (attachment is null)
            {
                throw new ArgumentException(
                    "An attachment is null. A row that decoded half its attachments would pay a " +
                    "player less than it says it owes them and still stamp itself claimed.",
                    nameof(Attachments));
            }
        }

        return new ReadOnlyCollection<MailAttachment>(owned);
    }
}
