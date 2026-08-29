using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Services.Inbox;

/// <summary>What one send asks for, before it is turned into rows.</summary>
/// <param name="TemplateId">Which authored template.</param>
/// <param name="Params">The values to fill it with, already rendered invariantly.</param>
/// <param name="Attachments">What every recipient's copy carries. May be empty.</param>
public sealed record MailSendRequest(
    string TemplateId,
    IReadOnlyDictionary<string, string> Params,
    IReadOnlyList<MailAttachment> Attachments);

/// <summary>What a send did, or why it was not allowed to do anything.</summary>
/// <param name="Delivered">The messages that were written, one per recipient. Empty on a refusal.</param>
/// <param name="Refusals">Every reason the send was refused, each naming what is wrong.</param>
public sealed record MailSendResult(
    IReadOnlyList<PlayerMessage> Delivered,
    IReadOnlyList<(MailSendRefusal Refusal, string Detail)> Refusals)
{
    /// <summary>Whether anything was written. Exactly "there are no refusals".</summary>
    public bool Accepted => Refusals.Count == 0;
}

/// <summary>Writing messages into players' inboxes — the one production path a message arrives by.</summary>
/// <remarks>
/// <para>
/// 🔒 Server → player only. Every call names the template, the parameters and the recipients, and
/// there is no arm anywhere that takes a body or a sending player. A player-to-player message would
/// need a member this class does not have and a category the vocabulary does not carry.
/// </para>
/// <para>
/// The whole send is checked before any of it is written: a refusal is reported with nothing
/// delivered, so a malformed compensation cannot reach half a cohort. Delivery itself is per
/// recipient and idempotent on the message id, so a send interrupted partway is retried whole.
/// </para>
/// </remarks>
public sealed class MailSender
{
    /// <summary>The message id prefix, so a stored id reads as what it is.</summary>
    public const string MessageIdPrefix = "MSG_";

    private readonly IMessageRepository _messages;
    private readonly MailTemplateCatalogue _catalogue;
    private readonly IClockPort _clock;
    private readonly IIdGeneratorPort _ids;

    /// <summary>Builds the sender over the store it writes through and the catalogue it checks against.</summary>
    /// <param name="messages">The inbox store.</param>
    /// <param name="catalogue">The authored templates.</param>
    /// <param name="clock">Where the sent-at instant comes from.</param>
    /// <param name="ids">Where message ids come from.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public MailSender(
        IMessageRepository messages,
        MailTemplateCatalogue catalogue,
        IClockPort clock,
        IIdGeneratorPort ids)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);

        _messages = messages;
        _catalogue = catalogue;
        _clock = clock;
        _ids = ids;
    }

    /// <summary>Everything wrong with a send, without writing anything.</summary>
    /// <param name="request">The send to check.</param>
    /// <returns>Empty when the send may go.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <remarks>
    /// Public and separate from <see cref="SendAsync"/> so the dry run answers the same question the
    /// real send does, out of the same code — a tool that checked a send differently from the sender
    /// would report a clean dry run for a send that then refuses.
    /// </remarks>
    public IReadOnlyList<(MailSendRefusal Refusal, string Detail)> Check(MailSendRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var refusals = _catalogue.Check(request.TemplateId, request.Params).ToList();

        foreach (var attachment in request.Attachments)
        {
            if (MailAttachmentKinds.Resolve(attachment).Refusal is { } refusal)
            {
                refusals.Add((MailSendRefusal.UNGRANTABLE_ATTACHMENT,
                    "'" + attachment.Type + "' is refused as " + refusal + ". A message carrying one " +
                    "is a message the claim path holds back for ever, so it is stopped here instead " +
                    "of being written and never paid."));
            }
        }

        return refusals;
    }

    /// <summary>Writes one message per recipient, after checking the send as a whole.</summary>
    /// <param name="request">What to send.</param>
    /// <param name="recipients">Who to send it to. May be empty, which delivers nothing.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>What was delivered, or the refusals that stopped it.</returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public async Task<MailSendResult> SendAsync(
        MailSendRequest request, IReadOnlyList<PlayerId> recipients, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(recipients);

        var refusals = Check(request);

        if (refusals.Count > 0)
        {
            return new MailSendResult(Array.Empty<PlayerMessage>(), refusals);
        }

        var category = _catalogue.Find(request.TemplateId)!.Category;
        var now = _clock.UtcNow;
        var delivered = new List<PlayerMessage>(recipients.Count);

        foreach (var recipient in recipients)
        {
            var message = new PlayerMessage(
                new MessageId(MessageIdPrefix + _ids.NewGuid().ToString("N")),
                recipient,
                category,
                request.TemplateId,
                request.Params,
                request.Attachments,
                now,
                InboxRetention.ExpiryFor(category, now),
                ReadAtUtc: null,
                ClaimedAtUtc: null);

            await _messages.AppendAsync(message, ct).ConfigureAwait(false);

            // After the append, never before: pruning first would drop a message to make room for one
            // that then failed to write, and the room would have been made for nothing.
            var prunable = InboxRetention.ToPrune(
                await _messages.GetActiveAsync(recipient, ct).ConfigureAwait(false));

            if (prunable.Count > 0)
            {
                await _messages.DeleteAsync(prunable, ct).ConfigureAwait(false);
            }

            delivered.Add(message);
        }

        return new MailSendResult(delivered, Array.Empty<(MailSendRefusal, string)>());
    }
}
