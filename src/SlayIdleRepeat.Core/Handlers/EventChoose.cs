using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content.BoardEvents;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Board.Resolution;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// 🔒 M3-03, `03` §5 / `19` Part A — the <c>EVENT_CHOOSE</c> handler: takes one option of the card
/// <c>RESOLVE_TILE</c> already drew, charges its cost, and applies one drawn outcome.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The cost is paid here, not in the resolver, and the order is the point.</b> Affordability is
/// checked <em>before</em> anything is spent or drawn, so an unaffordable choice costs the player
/// nothing and — because a rejected command's <c>RunRngScope</c> is discarded with the clone —
/// consumes no `14` §8.1 draw index either. A resolver that charged its own cost could only throw on
/// an unaffordable one, which is a defect's answer to a player's legal question.
/// </para>
/// <para>
/// ⚠️ <b>The pending tile is cleared whatever the outcome was</b>, including when every effect of
/// the drawn outcome was <c>UNSUPPORTED</c> and nothing observable happened. The player has made
/// their choice and `19` Part A gives them no second one; leaving the tile pending would let them
/// re-draw the outcome.
/// </para>
/// </remarks>
internal static class EventChoose
{
    /// <summary>
    /// 🔒 The `30` §7 attribution token an event option's cost is logged under, distinct from
    /// <see cref="EventTileResolver.Reason"/> so that `21` §8.3's <c>income_attribution.csv</c> can
    /// tell what event cards <em>take</em> from what they <em>pay</em>. Netting the two into one
    /// token would hide a card that charges 200 Gold to pay 250.
    /// </summary>
    internal const string CostReason = "event_choice_cost";

    /// <summary>`03` §5 — applies <c>EVENT_CHOOSE</c>.</summary>
    /// <param name="command">Which option, by its index in the card's authored order.</param>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <returns>
    /// <see cref="RejectionReason.ILLEGAL_STATE"/> when no event card is pending or the index names
    /// no option; <see cref="RejectionReason.INSUFFICIENT_FUNDS"/> when the option costs more than
    /// the player has; otherwise accepted, with the cost row followed by the outcome's rows.
    /// </returns>
    internal static HandlerResult Handle(EventChooseCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        // 🔒 All three conditions are one question — "is this run waiting on an event choice?" — and
        // they answer with one reason: 14 §16.2 has no finer-grained value, and inventing a
        // distinction here would put a vocabulary on the wire that the registry does not have.
        if (!run.HasPendingTile ||
            (TileKind)run.PendingTileKindValue != TileKind.Event ||
            run.PendingEventCardId is null)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var card = EventCatalogue.Read(input.Context.Content).Find(run.PendingEventCardId);

        if (command.ChoiceIndex < 0 || command.ChoiceIndex >= card.Options.Count)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var option = card.Options[command.ChoiceIndex];
        var events = new List<DomainEvent>(4);

        if (option.CostCurrency is { } currency && option.CostAmount is { } amount)
        {
            // 🔒 Checked BEFORE the debit and before the outcome draw. GOLD is the run's own currency
            // (10 §1); every other cost currency is a META wallet row on the Player.
            var balance = currency == CurrencyId.GOLD
                ? run.BalanceOf(currency)
                : input.Player.BalanceOf(currency);

            if (balance < amount)
            {
                return HandlerResult.Reject(RejectionReason.INSUFFICIENT_FUNDS);
            }

            events.Add(currency == CurrencyId.GOLD
                ? run.MoveCurrency(currency, -amount, CostReason)
                : input.Player.MoveCurrency(currency, -amount, CostReason));
        }

        events.AddRange(EventTileResolver.ResolveChoice(input, card, command.ChoiceIndex));

        run.ClearPendingTile();

        return HandlerResult.Accept(events);
    }
}
