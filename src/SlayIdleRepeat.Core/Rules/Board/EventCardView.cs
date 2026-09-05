using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.BoardEvents;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>
/// The event card a pending Event tile has already drawn, projected read-only so the Event screen
/// can show its prose and its options before <c>EVENT_CHOOSE</c> spends one of them.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Outcomes and their odds are deliberately not exposed.</b> A player who could read the
/// weights before choosing would be playing a different game from the one the card describes — the
/// card's whole shape is a gamble whose terms are prose, and a screen able to name the branch that
/// pays best would turn every card into arithmetic. So the surface carries what a player must see to
/// decide honestly — the prose, the options in the order the wire numbers them, and each option's
/// price — and nothing about what any of them pays.
/// </para>
/// <para>
/// 🔒 <b>The gate has two halves, and they are different states.</b> A run standing on another kind
/// of tile has no card; a run standing on an Event tile whose draw has not happened yet has no card
/// either, and the row spells that second absence as the empty string rather than as null. So a view
/// keyed on null alone would hand <see cref="EventCatalogue.Find"/> an empty id and throw the
/// content-rollback message at a run that is merely one command early.
/// </para>
/// <para>
/// 🔒 <b>Read-only.</b> Projecting mutates no <c>Run</c> and moves no stream position: the outcome
/// draw belongs to the command, and a view that consumed a draw index would leave the choice
/// resolving against a different roll than the one the screen was looking at.
/// </para>
/// <para>
/// <c>EventCard</c> and <c>EventOption</c> are internal and must not leak, so this view copies the
/// fields a screen needs out of them.
/// </para>
/// </remarks>
public sealed class EventCardView
{
    private EventCardView(
        string cardId, string title, string body, IReadOnlyList<EventOptionView> options)
    {
        CardId = cardId;
        Title = title;
        Body = body;
        Options = options;
    }

    /// <summary>The card's id, as the run persisted it.</summary>
    public string CardId { get; }

    /// <summary>The card's heading, verbatim as authored.</summary>
    public string Title { get; }

    /// <summary>The card's prose, verbatim as authored.</summary>
    public string Body { get; }

    /// <summary>The options the card offers, in the order the document authors them.</summary>
    /// <remarks>
    /// The order is the wire contract rather than a presentation choice: each row's
    /// <see cref="EventOptionView.ChoiceIndex"/> is its position, and that is the number
    /// <c>EVENT_CHOOSE</c> travels as. A screen that re-ordered these rows without carrying the index
    /// with them would submit a different option than the player pressed, and nothing would refuse
    /// it — both are legal choices on the same card.
    /// </remarks>
    public IReadOnlyList<EventOptionView> Options { get; }

    /// <summary>
    /// Projects the event card <paramref name="run"/> has drawn, or <c>null</c> when its pending
    /// tile is not an Event or no card has been drawn on it yet.
    /// </summary>
    /// <param name="run">The run whose drawn card is being read.</param>
    /// <param name="content">The loaded content set, read for the card catalogue.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="ArgumentException">The run's drawn card id names no authored card.</exception>
    public static EventCardView? Project(RunSnapshot run, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(content);

        if (run.PendingTileKind != (int)TileKind.Event ||
            string.IsNullOrEmpty(run.PendingEventCardId))
        {
            return null;
        }

        // Deliberately unguarded: an id this content version has lost is a content rollback across a
        // live run, and the catalogue's own throw is what says so. Swallowing it into a null would
        // report "no event here" to a run standing on one.
        var card = EventCatalogue.Read(content).Find(run.PendingEventCardId);

        var options = new EventOptionView[card.Options.Count];

        for (var index = 0; index < options.Length; index++)
        {
            var option = card.Options[index];

            options[index] = new EventOptionView(
                index, option.Label, option.CostCurrency, option.CostAmount);
        }

        return new EventCardView(card.Id, card.Title, card.Body, Array.AsReadOnly(options));
    }
}

/// <summary>One option of a drawn card, as a screen has to draw it.</summary>
/// <param name="ChoiceIndex">The index <c>EVENT_CHOOSE</c> carries to take this option.</param>
/// <param name="Label">The option's caption, verbatim as authored.</param>
/// <param name="CostCurrency">The currency the option charges, or <c>null</c> when it is free.</param>
/// <param name="CostAmount">
/// The positive magnitude charged, or <c>null</c> when the option is free. Its direction is the
/// field's meaning rather than its sign — the handler debits it.
/// </param>
public readonly record struct EventOptionView(
    int ChoiceIndex, string Label, CurrencyId? CostCurrency, long? CostAmount);
