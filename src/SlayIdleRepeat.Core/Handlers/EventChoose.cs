using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content.BoardEvents;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Board.Resolution;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>EVENT_CHOOSE</c> handler: takes one option of the card <c>RESOLVE_TILE</c> already drew,
/// charges its cost, and applies one drawn outcome.
/// </summary>
/// <remarks>
/// <para>
/// Affordability is checked before anything is spent or drawn, so an unaffordable choice costs the
/// player nothing and consumes no RNG draw index either.
/// </para>
/// <para>
/// The pending tile is cleared whatever the outcome was, including when the drawn outcome was
/// unsupported and nothing observable happened — the player has made their choice and gets no second
/// one.
/// </para>
/// </remarks>
internal static class EventChoose
{
    /// <summary>
    /// Income-attribution token for an event option's cost, kept distinct from
    /// <see cref="EventTileResolver.Reason"/> so what a card takes can be told apart from what it pays.
    /// </summary>
    internal const string CostReason = "event_choice_cost";

    /// <summary>Applies <c>EVENT_CHOOSE</c>.</summary>
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
            // Gold is the run's own currency; every other cost currency lives on the Player wallet.
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
