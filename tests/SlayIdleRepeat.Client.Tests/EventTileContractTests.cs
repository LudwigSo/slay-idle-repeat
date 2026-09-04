using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using Xunit;
using CorePlayer = SlayIdleRepeat.Core.Model.Player;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The one contract between "every shipped event card" and "a run that has nothing can still get
/// off the tile".
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>An event card whose every option has a price is a node a broke run cannot leave.</b>
/// <c>EVENT_CHOOSE</c> refuses an unaffordable option with <c>INSUFFICIENT_FUNDS</c> before any RNG,
/// and nothing else clears an Event tile — <c>RESOLVE_TILE</c> is refused once a card is drawn. So a
/// run that lands on such a card with an empty purse has the board redrawing the identical state
/// after every press, and abandoning the run as its only remaining control. `19` Part A authors a
/// free option on all thirty cards today; this is what notices the thirty-first that does not.
/// </para>
/// <para>
/// 🔒 <b>Core's half is ASKED, not transcribed.</b> The old placeholder read the cards' JSON and
/// checked for the absence of a <c>cost</c> member, which is a claim about the document rather than
/// about the rules layer: an affordability rule that started reading a different balance would leave
/// that check green. Here a run is stood on each card in turn and <c>EVENT_CHOOSE</c> is submitted
/// through <c>GameRules.Apply</c>, the one public mutation, and whether the command was accepted IS
/// the answer.
/// </para>
/// <para>
/// 🔒 <b>Both halves of the claim.</b> Accepted is not enough — the option also has to CLEAR the
/// tile, because an accepted command that leaves the tile pending is the same dead end by another
/// route. Every accepted option is checked, not just the first found.
/// </para>
/// <para>
/// ⚠️ It reads the checkout's own content set, because <c>Apply</c> does: a command runs the profile
/// catch-up before dispatch. That is <see cref="BootContent.Shipped"/>, which this suite already
/// loads, so the answers measured are the shipped game's.
/// </para>
/// <para>
/// ⚠️ Every card is stood on at chapter 1 regardless of its authored band. <c>EVENT_CHOOSE</c> gates
/// on nothing but the card id, so the band is the draw's business and not this contract's — and the
/// two shipped chapters are 1 and 2, so a card authored for chapter 6 has no chapter row to be stood
/// in.
/// </para>
/// </remarks>
public sealed class EventTileContractTests
{
    /// <summary>
    /// How many cards `19` Part A ships, so a sweep of none cannot pass.
    /// </summary>
    /// <remarks>
    /// 🔴 A loop over a document is the shape that silently measures nothing: an empty card array
    /// would satisfy every assertion below and report a clean contract. The count is therefore
    /// pinned rather than merely non-zero — a thirty-first card is exactly the event this case
    /// exists to be read on, so it must arrive as a failure here that says to check the new card's
    /// options, not as a sweep that quietly grew by one.
    /// </remarks>
    private const int ShippedEventCardCount = 30;

    private const string BoardEventsFile = "board_events.json";

    /// <summary>The value a run with no pending tile reports.</summary>
    private const int NoPendingTile = -1;

    private static readonly PlayerId Player = new("PLAYER_eventcontract_3f70");
    private static readonly RunId Run = new("RUN_eventcontract_be21");

    /// <summary>Later than the fixture rows' own instant, so the catch-up before dispatch is legal.</summary>
    private static readonly DateTimeOffset Noon = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A seed for the one command this case sends, if the dispatch table asks for one.</summary>
    /// <remarks>
    /// Asked of <c>GameRules.RequiresCommandSeed</c> rather than always supplied or always omitted:
    /// which commands need a seed is the dispatch table's answer, and a case that guessed would be
    /// refused for the wrong reason and report every card as unaffordable.
    /// </remarks>
    private const ulong CommandSeed = 0xEE9Du;

    [Fact]
    public void Every_shipped_event_card_offers_an_option_a_run_with_nothing_can_take()
    {
        var cards = ShippedCards();

        cards.Count.ShouldBe(
            ShippedEventCardCount,
            "the shipped card catalogue changed size. Every card needs one option a run holding " +
            "nothing can take, because nothing else clears an Event tile — decide that for the new " +
            "card, and this is where the two are compared.");

        var wallet = BrokeAndOnCard(cards[0].Id).Player.ToSnapshot().Wallet;

        // 🔒 The count first, asked of the domain's own list rather than transcribed: "every balance
        // is zero" is true of a wallet carrying no balances at all, which is the one shape that would
        // let this whole sweep report a broke run while measuring nothing.
        wallet.Count.ShouldBe(
            CorePlayer.WalletCurrencies.Count,
            "the fixture profile carries " + wallet.Count + " of the " +
            CorePlayer.WalletCurrencies.Count + " currencies a wallet has. The check below asks " +
            "every balance to be zero, and a wallet with rows missing satisfies that by having " +
            "nothing to ask.");
        wallet.Values.ShouldAllBe(
            balance => balance == 0L,
            "the fixture profile is supposed to hold nothing. With a funded wallet every priced " +
            "option becomes affordable and this whole sweep stops being about a broke run at all.");

        var unaffordable = new List<string>();

        foreach (var (cardId, optionCount) in cards)
        {
            optionCount.ShouldBeGreaterThan(
                0, cardId + " authors no options, so no press on it could resolve anything.");

            var accepted = 0;

            for (var index = 0; index < optionCount; index++)
            {
                var outcome = Choose(cardId, index);

                if (!outcome.Accepted)
                {
                    continue;
                }

                accepted++;
                outcome.NewState.Run!.ToSnapshot().PendingTileKind.ShouldBe(
                    NoPendingTile,
                    cardId + " option " + index + " was ACCEPTED and left the tile pending. An " +
                    "accepted command that clears nothing is the same dead end as a refused one: " +
                    "the board redraws the identical state and the player's only remaining control " +
                    "is the one that throws the run away.");
            }

            if (accepted == 0)
            {
                unaffordable.Add(cardId);
            }
        }

        unaffordable.ShouldBeEmpty(
            "🔴 the rules layer refused every option of a shipped card for a run holding nothing, " +
            "so a run landing on it cannot leave the tile: RESOLVE_TILE is refused once a card is " +
            "drawn and every choice costs more than the run has. Either the card needs a free " +
            "option, or the Event screen needs a way off a card it cannot afford. Cards: " +
            $"[{string.Join(", ", unaffordable)}]");
    }

    /// <summary>
    /// 🔒 …and the sweep really does refuse things, so "accepted" above is a measurement.
    /// </summary>
    /// <remarks>
    /// The negative control. If <c>Apply</c> accepted every index it was handed — because the run
    /// row is being refused before the option lookup, or accepted before the affordability check —
    /// then the case above would report a clean contract whatever the cards say. An index past the
    /// end of the card is refused as <c>ILLEGAL_STATE</c> by the handler's own bounds check, and
    /// that is the cheapest thing to be sure of it with.
    /// </remarks>
    [Fact]
    public void An_index_that_names_no_option_is_refused_so_the_sweep_is_really_measuring()
    {
        var cards = ShippedCards();

        cards.ShouldNotBeEmpty("with no cards read there is nothing to submit against.");

        var outcome = Choose(cards[0].Id, cards[0].OptionCount);

        outcome.Accepted.ShouldBeFalse(
            cards[0].Id + " accepted a choice index past its last option, so this dispatch is not " +
            "reaching the handler's option lookup at all — and every 'accepted' in the sweep above " +
            "means something other than what it says.");
        outcome.Rejection.ShouldBe(
            RejectionReason.ILLEGAL_STATE,
            "an index naming no option is an illegal state rather than an affordability problem, " +
            "and the two must not be confused: INSUFFICIENT_FUNDS here would mean the bounds check " +
            "moved behind the price check.");
    }

    /// <summary>Submits one choice on one card, over a run and a profile holding nothing.</summary>
    private static CommandResult Choose(string cardId, int choiceIndex)
    {
        var command = new EventChooseCommand(choiceIndex);

        var context = new GameContext(
            Noon,
            GameRules.RequiresCommandSeed(command) ? CommandSeed : null,
            BootContent.Shipped,
            new Entitlements(hasPlus: false, expiresAtUtc: null),
            new FeatureFlags(
                pvpEnabled: true,
                plusOfferEnabled: true,
                mailEnabled: true,
                disabledAdPlacements: [],
                disabledChapters: []));

        return GameRules.Apply(BrokeAndOnCard(cardId), command, context);
    }

    /// <summary>A broke run, holding no Gold, standing on an Event tile that has drawn this card.</summary>
    private static WorldSlice BrokeAndOnCard(string cardId) =>
        PlayerState.SliceWith(
            PlayerState.EmptySlice(Player),
            PlayerState.Run(
                Run,
                Player,
                RunPhase.InProgress,
                position: 1,
                gold: 0,
                pendingTileKind: EventPresenter.EventTileKind,
                pendingTileLinearIndex: 1,
                pendingTileStage: 1,
                pendingEventCardId: cardId));

    /// <summary>Every shipped card's id and how many options it authors, read off the checkout.</summary>
    private static IReadOnlyList<(string Id, int OptionCount)> ShippedCards()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepoPaths.ContentDataRoot, "content", "board_events", BoardEventsFile)));

        return document.RootElement.GetProperty("cards")
                       .EnumerateArray()
                       .Select(card => (
                           card.GetProperty("id").GetString()!,
                           card.GetProperty("options").GetArrayLength()))
                       .ToArray();
    }
}
