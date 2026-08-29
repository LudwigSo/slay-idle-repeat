using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Economy;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary><c>CLAIM_INBOX</c> — pay the attachments the player's messages are holding.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>CLAIM ALL is the shape, and the named claim is the special case.</b> A command with no
/// filter claims everything claimable and cannot be refused for anything one message is holding — a
/// player with forty messages taps once, and a single row nobody can pay must not be able to block
/// the other thirty-nine. A command that NAMES messages is the opposite: the player asked for those,
/// so an id the account does not hold is <c>NOT_OWNED</c> and one that cannot be paid is
/// <c>ILLEGAL_STATE</c>, each said rather than silently skipped.
/// </para>
/// <para>
/// 🔒 <b>An already-claimed message is skipped, not refused.</b> Claims are idempotent on the
/// message id: the transport replays a repeated <c>commandId</c> from the ledger, but a SECOND
/// command claiming the same message is a new command with a new id, and the only thing stopping it
/// paying twice is this skip.
/// </para>
/// <para>
/// The kill switch is not read here. <c>FEATURE_DISABLED</c> is transport-tier and the wire gate
/// already refuses this command when mail is off; a second check would be a second gate, free to
/// disagree with the first.
/// </para>
/// <para>
/// Nothing is drawn: every v1 attachment is a fixed amount, so the meta seed this command is issued
/// goes unread. That is not an oversight — a rolled reward would need a seeded draw, which is one of
/// the named reasons gear attachments are refused.
/// </para>
/// </remarks>
internal static class ClaimInbox
{
    internal static HandlerResult Handle(ClaimInboxCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var inbox = input.State.Inbox ?? throw new InvalidOperationException(
            "CLAIM_INBOX was dispatched with a WorldSlice carrying no inbox projection. Loading the " +
            "right slice is the Application layer's job, and a player with no messages loads " +
            "InboxView.Empty — so a null here is a miswired caller, not a player with an empty " +
            "inbox. Answering it as 'nothing to claim' would tell a player their rewards are gone.");

        var claimed = new List<InboxMessage>();

        // Omitted and empty both mean "claim everything claimable" — the command's own contract.
        // The wire keeps them apart so a client's intent survives the round trip; the ANSWER does
        // not, because an empty filter that claimed nothing would be a CLAIM ALL that quietly did
        // nothing whenever a client sent [] instead of omitting the member.
        if (command.MessageIds is { Count: > 0 } named)
        {
            foreach (var id in named)
            {
                var message = inbox.Find(new MessageId(id));

                if (message is null)
                {
                    return HandlerResult.Reject(RejectionReason.NOT_OWNED);
                }

                if (message.IsClaimed)
                {
                    continue;
                }

                if (!message.IsClaimable)
                {
                    return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
                }

                claimed.Add(message);
            }

            // A named list may repeat an id; the claim happens once. Without this, "claim this
            // message twice in one command" would pay twice and stamp once.
            claimed = claimed.DistinctBy(m => m.Id).ToList();
        }
        else
        {
            claimed.AddRange(inbox.Claimable);
        }

        var events = new List<DomainEvent>();

        foreach (var message in claimed)
        {
            events.AddRange(
                MailAttachmentGrants.Pay(
                    input.Player, message.Id, message.Attachments, input.Context.Content));

            // Last for this message, after its attachments are paid: the event IS the instruction to
            // stamp the row claimed, so emitting it before the grant would let a throw mid-payment
            // leave the Application layer with an instruction to spend a message that was not paid.
            events.Add(new MailClaimed(DomainEvent.UnstampedSequence, message.Id, message.Category));
        }

        return HandlerResult.Accept(events);
    }
}
