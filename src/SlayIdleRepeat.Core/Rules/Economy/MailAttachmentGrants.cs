using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Economy;

/// <summary>Paying an inbox message's attachments into the player, through the aggregate's own seams.</summary>
/// <remarks>
/// <para>
/// It moves nothing itself. A wallet attachment goes through <c>Player.MoveCurrency</c> and an
/// Energy one through <see cref="EnergyMath.Grant"/> and <c>Player.SetEnergy</c> — the same two
/// seams every other income in the game uses, so an inbox grant appears in the economy log as income
/// with a reason rather than as a balance that changed for no stated cause.
/// </para>
/// <para>
/// 🔒 Energy above the bar routes into the Energy Reserve and is not re-derived here:
/// <see cref="EnergyMath.Grant"/> already fills the bar, overflows into the Reserve and stops at its
/// capacity, and a second cascade written beside it would be the copy that drifts.
/// </para>
/// <para>
/// It takes the attachments and the message's id rather than the message, so this layer never names
/// the projection type in the <c>SlayIdleRepeat.Core</c> root: what a rule needs is the list to pay
/// and something to blame when a refusal reaches it.
/// </para>
/// </remarks>
internal static class MailAttachmentGrants
{
    /// <summary>The attribution every inbox grant is logged under.</summary>
    /// <remarks>
    /// One token for every category rather than one per category: what an income report needs to
    /// separate is "the inbox paid this" from a run payout, and the message's own event already
    /// carries which message and which category it was.
    /// </remarks>
    internal const string Reason = "mail_attachment";

    /// <summary>Pays one message's attachments in row order, and reports the movements it made.</summary>
    /// <param name="player">The player to pay.</param>
    /// <param name="messageId">The message being claimed — named only so a refusal can blame it.</param>
    /// <param name="attachments">What it carries. Every one must be grantable.</param>
    /// <param name="content">The content set the Energy numbers are read from.</param>
    /// <returns>The currency movements, in the order they happened.</returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// An attachment this build cannot grant reached the grant path. A defect, never a refusal: the
    /// claim rules ask <c>InboxMessage.IsClaimable</c> first, so a message that reaches here has
    /// already been judged payable.
    /// </exception>
    internal static IReadOnlyList<CurrencyChanged> Pay(
        Player player,
        MessageId messageId,
        IReadOnlyList<MailAttachment> attachments,
        ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(attachments);
        ArgumentNullException.ThrowIfNull(content);

        var moved = new List<CurrencyChanged>(attachments.Count);
        var energy = EnergyTuning.Read(content);

        foreach (var attachment in attachments)
        {
            var resolution = MailAttachmentKinds.Resolve(attachment);

            if (resolution.Refusal is { } refusal)
            {
                throw new InvalidOperationException(
                    "Message " + messageId + " carries a '" + attachment.Type + "' attachment, " +
                    "refused as " + refusal + ", and it reached the grant path anyway. The claim " +
                    "rules hold an unclaimable message back precisely so a reward this build cannot " +
                    "pay is never stamped as paid — reaching here means that gate was walked around.");
            }

            moved.Add(resolution.Kind switch
            {
                MailAttachmentGrantKind.ENERGY => GrantEnergy(player, energy, attachment.Amount),
                MailAttachmentGrantKind.WALLET_CURRENCY =>
                    player.MoveCurrency(resolution.Currency!.Value, attachment.Amount, Reason),
                _ => throw new InvalidOperationException(
                    "Message " + messageId + " resolved a '" + attachment.Type + "' attachment to " +
                    "grant kind " + resolution.Kind + ", which this payer has no arm for. A kind was " +
                    "added to the vocabulary without being given a way to be paid."),
            });
        }

        return moved;
    }

    /// <summary>
    /// Energy: the bar first, the Reserve for the overflow, and whatever neither can hold is not
    /// banked — the same deposit cascade regeneration and every other grant uses.
    /// </summary>
    private static CurrencyChanged GrantEnergy(Player player, EnergyTuning tuning, long amount)
    {
        // Clamped before the cast rather than after: an authored amount beyond int range would
        // otherwise wrap to a negative grant, which EnergyMath.Grant refuses as an argument fault
        // no attachment author could diagnose from that message.
        var granted = (int)Math.Min(amount, int.MaxValue);

        return player.SetEnergy(
            EnergyMath.Grant(tuning, player.LegendLevel, player.Energy, granted), tuning, Reason);
    }
}
