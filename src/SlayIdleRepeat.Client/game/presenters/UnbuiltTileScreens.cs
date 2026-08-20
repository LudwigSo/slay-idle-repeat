using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Rules.Board;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>
/// The two tile kinds a run can land on that this build has no screen for, and the commands that
/// get a run off one anyway.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>A PLACEHOLDER, AND THE WHOLE OF IT IS IN THIS ONE FILE SO IT CAN BE DELETED.</b> An Event
/// tile and a Minigame tile are both resolved by a command a screen this build has not written would
/// submit — <c>EVENT_CHOOSE</c> and <c>MINIGAME_SUBMIT</c> — and <c>RESOLVE_TILE</c> leaves both
/// pending on purpose (<c>Handlers.ResolveTile</c>: *"acknowledged and not cleared"*). So a run that
/// landed on either was stuck there for good: the roll is refused while a tile is pending, the
/// board's Continue press either accepted and cleared nothing (Minigame) or was refused outright the
/// second time (Event, whose card may not be re-drawn), and abandoning the run was the only way off
/// the tile. The same shape of dead end <c>START_BATTLE</c> was the fix for, found the same way — by
/// playing an exported build.
/// </para>
/// <para>
/// 🔒 <b>Through the real commands, not through a rules change.</b> Nothing here relaxes what the
/// rules layer accepts and no tile is cleared behind its back: this picks a legal payload for the
/// command the missing screen would have submitted, so the tile resolves through the handler that
/// owns it and pays what that handler pays. That is what makes the rest of a run testable — the
/// board, the fights, the drafts, the shop, the campfire and the run's ending all sit BEHIND these
/// two tiles on any board that generates one.
/// </para>
/// <para>
/// ⚠️ <b>It makes the player's choice for them, and that is the cost of being a placeholder.</b> An
/// event card's option is chosen here rather than offered, and a minigame is scored here rather than
/// played. Both are biased to the least the tile can pay — the first cost-free option, and the
/// lowest outcome tier — so a placeholder can never be the profitable way to play a tile. When the
/// real screens land, this file goes and the two call sites in <see cref="BoardPresenter"/> go with
/// it.
/// </para>
/// </remarks>
public static class UnbuiltTileScreens
{
    /// <summary>The Event tile's kind number — <c>EVENT_CHOOSE</c>'s tile.</summary>
    public const int EventTileKind = (int)TileKind.Event;

    /// <summary>The Minigame tile's kind number — <c>MINIGAME_SUBMIT</c>'s tile.</summary>
    public const int MinigameTileKind = (int)TileKind.Minigame;

    /// <summary>
    /// The minigame a skipped Minigame tile is submitted as.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>A transcription of a format another assembly owns</b>, for
    /// <see cref="BoardTileKinds"/>'s reason: <c>Content.MinigameCatalogue</c> is internal to the
    /// rules assembly, so nothing a client can reference names the four <c>MG_*</c> ids, and
    /// <c>MINIGAME_SUBMIT</c> refuses an id the catalogue does not know.
    /// </para>
    /// <para>
    /// 🔒 <b>Which of the four, and why this one.</b> The pending tile does not say — a Minigame
    /// tile is the bare kind number 8 and nothing on the run names a minigame — so one has to be
    /// picked, and only a CLIENT-ASSERTED minigame lets the outcome be pinned. The two server-rolled
    /// ones (the chest pick, the dice duel) draw their own tier from the run's stream and ignore the
    /// claim, so a skip through either would pay a random reward and, for the chest pick, advance a
    /// lifetime pity counter for a minigame nobody played. Strike the Anvil is client-asserted, so
    /// the tier below is what the tile actually pays.
    /// </para>
    /// </remarks>
    public const string PlaceholderMinigameId = "MG_TIMING_BAR";

    /// <summary>
    /// The outcome tier a skipped minigame claims — the lowest, which is the worst.
    /// </summary>
    /// <remarks>
    /// 🔒 Zero rather than a middling tier or a best one. A minigame's reward table is an ordered
    /// zero-based tier read positionally out of content, and every one of the four authors its rows
    /// worst-first, so tier 0 is the smallest payout the tile has. It is also the only index legal by
    /// construction: a table has to have at least one row to be a table at all, so 0 is inside every
    /// authored one and no other number is.
    /// </remarks>
    public const int LowestOutcomeTier = 0;

    /// <summary>The event option a card with no cost-free option at all would fall back to.</summary>
    /// <remarks>
    /// Reached only when the card cannot be read or authors nothing free, and every one of the
    /// thirty shipped cards authors at least one free option — so this is the fallback for a content
    /// set this build was not shipped with, not for anything a player can reach today. It can be
    /// refused for funds, which is why it is the fallback rather than the rule.
    /// </remarks>
    public const int FirstOption = 0;

    private const string CardsReference = "content/board_events/board_events.json#/cards";
    private const string CardIdMember = "id";
    private const string OptionsMember = "options";
    private const string OptionCostMember = "cost";

    /// <summary>Whether this tile kind is one whose screen this build has not written.</summary>
    /// <param name="tileKind">The number the run carries in its pending-tile field.</param>
    public static bool HasNoScreen(int tileKind) =>
        tileKind is EventTileKind or MinigameTileKind;

    /// <summary>
    /// The command that gets a run off one of these tiles, or null when the run is not on one.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>An Event tile takes TWO commands and this answers with whichever is next.</b>
    /// <c>RESOLVE_TILE</c> is what draws the card, <c>EVENT_CHOOSE</c> is what spends it, and
    /// <c>EVENT_CHOOSE</c> is refused until the card exists — so the answer is decided from whether
    /// the run is carrying a drawn card, not from the tile kind alone. A Minigame tile needs no
    /// acknowledgement first: <c>MINIGAME_SUBMIT</c> reads the pending tile and clears it in one
    /// step.
    /// </remarks>
    /// <param name="tileKind">The pending tile's kind.</param>
    /// <param name="drawnEventCardId">
    /// The event card the run has already drawn, or null/empty when it has drawn none.
    /// </param>
    /// <param name="content">The loaded content set the card's options are read from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public static GameCommand? CommandThatLeaves(
        int tileKind, string? drawnEventCardId, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return tileKind switch
        {
            MinigameTileKind => new MinigameSubmitCommand(PlaceholderMinigameId, LowestOutcomeTier),
            EventTileKind when !string.IsNullOrEmpty(drawnEventCardId) =>
                new EventChooseCommand(FreeOptionIndexOf(content, drawnEventCardId)),
            EventTileKind => new ResolveTileCommand(),
            _ => null,
        };
    }

    /// <summary>
    /// The index of the first option of this card that costs nothing, or
    /// <see cref="FirstOption"/> when the card authors none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Cost-free specifically, and that is the difference between resolvable and resolvable
    /// while rich.</b> <c>EVENT_CHOOSE</c> refuses an option the player cannot pay for, and seven of
    /// the thirty shipped cards put a priced option first — so a placeholder that always took option
    /// zero would put a broke run straight back on the dead end this whole file exists to remove.
    /// Every shipped card authors at least one free option, which is what makes this reliable rather
    /// than merely likelier.
    /// </para>
    /// <para>
    /// ⚠️ Free of a CURRENCY cost, not free of consequence: an outcome behind a cost-free option can
    /// still take hit points or hang a curse. The rules layer's own resolver decides that from the
    /// card's authored weights, and a placeholder that went looking for a harmless option instead
    /// would be inventing a reading of the content the game itself does not have.
    /// </para>
    /// <para>
    /// 🔴 <b>Read out of the raw document rather than the catalogue.</b>
    /// <c>Content.BoardEvents.EventCatalogue</c> — which parses these same cards, and validates them
    /// — is internal to the rules assembly. So this is a second reader over one file, and the ONE
    /// thing it reads is whether an option object carries a <c>cost</c> member; every other shape in
    /// the document is left alone, and anything malformed answers with the fallback rather than
    /// throwing on a screen.
    /// </para>
    /// </remarks>
    /// <param name="content">The loaded content set.</param>
    /// <param name="cardId">The card the run has drawn.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static int FreeOptionIndexOf(ContentSnapshot content, string cardId)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(cardId);

        if (!content.TryRead(CardsReference, out var cards) || cards!.Kind != ContentValueKind.Array)
        {
            return FirstOption;
        }

        foreach (var card in cards.Items)
        {
            if (!card.TryGetMember(CardIdMember, out var id) ||
                id!.Kind != ContentValueKind.Text ||
                !string.Equals(id.AsText(), cardId, StringComparison.Ordinal) ||
                !card.TryGetMember(OptionsMember, out var options) ||
                options!.Kind != ContentValueKind.Array)
            {
                continue;
            }

            for (var index = 0; index < options.Items.Count; index++)
            {
                if (!options.Items[index].TryGetMember(OptionCostMember, out _))
                {
                    return index;
                }
            }

            return FirstOption;
        }

        return FirstOption;
    }
}
