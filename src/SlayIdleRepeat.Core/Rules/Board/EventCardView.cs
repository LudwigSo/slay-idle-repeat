using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>
/// ⚠️ <b>Phase 1 stub — the public surface only.</b> Phase 3a writes the real body.
/// </summary>
/// <remarks>
/// <para>
/// The event card a pending Event tile has already drawn, projected read-only so the Event screen
/// can show its prose and its options before <c>EVENT_CHOOSE</c> spends one of them.
/// </para>
/// <para>
/// 🔒 <b>Outcomes and their odds are deliberately not exposed.</b> A player who could read the
/// weights before choosing would be playing a different game from the one the card describes — the
/// card's whole shape is a gamble whose terms are prose.
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
    /// <summary>The card's id, as the run persisted it.</summary>
    public string CardId { get; } = "";

    /// <summary>The card's heading, verbatim as authored.</summary>
    public string Title { get; } = "";

    /// <summary>The card's prose, verbatim as authored.</summary>
    public string Body { get; } = "";

    /// <summary>The options the card offers, in the order the document authors them.</summary>
    public IReadOnlyList<EventOptionView> Options { get; } = [];

    /// <summary>
    /// Projects the event card <paramref name="run"/> has drawn, or <c>null</c> when its pending
    /// tile is not an Event or no card has been drawn on it yet.
    /// </summary>
    /// <param name="run">The run whose drawn card is being read.</param>
    /// <param name="content">The loaded content set, read for the card catalogue.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="ArgumentException">The run's drawn card id names no authored card.</exception>
    public static EventCardView? Project(RunSnapshot run, ContentSnapshot content) =>
        throw new NotImplementedException(
            "Phase 1 stub. Phase 3a projects the drawn card here; the tests that name this method " +
            "are the specification it is written against.");
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
