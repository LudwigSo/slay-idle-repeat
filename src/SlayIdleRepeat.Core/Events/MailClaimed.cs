using System.Globalization;
using System.Text;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Events;

/// <summary>One inbox message's attachments were granted.</summary>
/// <param name="Sequence">The event's position in its command's batch.</param>
/// <param name="MessageId">Which message. The value every grant of it is idempotent on.</param>
/// <param name="Category">Which of the six kinds it was, so a reader need not join back to the row.</param>
/// <remarks>
/// <para>
/// 🔒 <b>An intent, in `30` §5's sense.</b> The inbox is not player state and the domain never
/// returns a mutated copy of it, so this event IS how a claim reaches the store: the Application
/// layer reads the batch and stamps exactly these ids claimed. An accepted <c>CLAIM_INBOX</c> that
/// produced no <see cref="MailClaimed"/> therefore stamped nothing, which is what makes "claimed"
/// and "granted" the same fact rather than two that can drift.
/// </para>
/// <para>
/// It carries no attachment list and no amounts: every movement it caused is already a
/// <see cref="CurrencyChanged"/> in the same batch, with the currency, the delta and the reason on
/// it. Restating them here would be a second ledger of the same grant, free to disagree with the
/// first.
/// </para>
/// </remarks>
public sealed record MailClaimed(int Sequence, MessageId MessageId, MessageCategory Category)
    : DomainEvent(Sequence)
{
    /// <inheritdoc/>
    protected override bool PrintMembers(StringBuilder builder)
    {
        base.PrintMembers(builder);

        builder.Append(CultureInfo.InvariantCulture, $", {nameof(MessageId)} = {MessageId}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(Category)} = {Category}");

        return true;
    }
}
