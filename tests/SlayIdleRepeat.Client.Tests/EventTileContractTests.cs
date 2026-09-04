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
/// <para>
/// 🔴 <b>And the second contract: the price rule is written down TWICE, once on each side of the
/// engine boundary.</b> <see cref="EventPresenter"/> answers whether an option can be taken before
/// the press — it has to, because the refusal travels as a wire value four other things share, so a
/// round trip would replace the sentence naming the price with one naming nothing — and the rules
/// layer answers it again when the command lands. Two copies of one rule, and the failure when they
/// disagree is silent on both sides: an option drawn live and refused on press, or one drawn out of
/// use that the run could have paid for. Nothing here can make them one rule (the projection holds no
/// profile, so it cannot answer it at all), so what is left is to pin the same three facts the
/// screen's own cases pin, against the handler that actually charges — the boundary is inclusive, Gold
/// is the RUN's purse, and every other currency is the PLAYER's.
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

    /// <summary>Gold well past any authored price, so a Gold-reading handler would be satisfied.</summary>
    private const long AmplyFunded = 1_000L;

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

    /// <summary>
    /// A priced option is taken by a run holding EXACTLY its price, and refused one short.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The boundary itself, not a comfortable margin either side of it.</b>
    /// <see cref="EventPresenter"/> draws an option live at <c>balance &gt;= cost</c>; the handler
    /// refuses at <c>balance &lt; cost</c>. Those are the same line only while both are inclusive, and
    /// a run holding exactly the price is the commonest way to meet a priced option at all — a player
    /// who has just been paid the last option's takings. Move either side by one and this case reddens
    /// while every "affordable" and "unaffordable" case that tested a margin stays green.
    /// </para>
    /// <para>
    /// 🔒 It also pins WHICH purse a Gold price is read from: the wallet is empty in this arrangement,
    /// so a handler reading the profile for Gold would refuse the exact-price arm too.
    /// </para>
    /// <para>
    /// The card and the amount are read off the shipped catalogue rather than named, for the reason
    /// the sweep above is: a transcribed price is a claim about a document, and the claim being made
    /// is about the rule that charges it.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_priced_option_is_taken_at_exactly_its_price_and_refused_one_short()
    {
        var (cardId, index, _, amount) = FirstPricedIn(CurrencyId.GOLD);

        amount.ShouldBeGreaterThan(
            0L,
            cardId + " option " + index + " is priced at nothing, so paying exactly it and one " +
            "short are the same arrangement and the boundary below is not being measured.");

        Choose(cardId, index, gold: amount).Accepted.ShouldBeTrue(
            cardId + " option " + index + " costs " + amount + " Gold and was REFUSED to a run " +
            "holding exactly that. The rules layer's price boundary has stopped being inclusive, " +
            "and EventPresenter still draws the option live at balance >= cost — so the screen now " +
            "offers a choice the press cannot take, with the refusal arriving as a wire value that " +
            "names no price.");

        var oneShort = Choose(cardId, index, gold: amount - 1);

        oneShort.Accepted.ShouldBeFalse(
            cardId + " option " + index + " costs " + amount + " Gold and was ACCEPTED to a run " +
            "holding one less. Either the price is not being charged at all, or it is charged " +
            "against something other than the run's Gold — and EventPresenter would be drawing the " +
            "option out of use with a sentence about a price the run could in fact pay.");
        oneShort.Rejection.ShouldBe(
            RejectionReason.INSUFFICIENT_FUNDS,
            "a run one Gold short of the price is an affordability refusal and nothing else. Any " +
            "other reason here means the arrangement never reached the price check, so the " +
            "acceptance above is not evidence about the boundary.");
    }

    /// <summary>
    /// A non-Gold price is charged to the PLAYER's wallet, never to the run's Gold.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The discriminating arm of the purse split.</b> The run is funded with Gold far beyond the
    /// price and the profile's wallet holds nothing, so a handler that read one balance for both
    /// purses would ACCEPT — which is exactly the mistake <see cref="EventPresenter"/> would make if
    /// its own split were dropped, and the mistake no "unaffordable" case with an empty run and an
    /// empty wallet can see, because there both readings answer zero.
    /// </para>
    /// <para>
    /// ⚠️ Only the refusing half is measured here. Funding a wallet means moving currency through the
    /// aggregate rather than arranging a row, so the accepting half is the screen's own case (a
    /// wallet-priced option offered live against a funded wallet) and this is the half that says the
    /// run's Gold is not what answers it.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_non_Gold_price_is_charged_to_the_wallet_and_not_to_the_runs_Gold()
    {
        var (cardId, index, currency, amount) = FirstPricedOutside(CurrencyId.GOLD);

        var outcome = Choose(cardId, index, gold: amount + AmplyFunded);

        outcome.Accepted.ShouldBeFalse(
            cardId + " option " + index + " costs " + amount + " " + currency + " and was ACCEPTED " +
            "by a run holding only Gold. The price is being read off the run's own currency instead " +
            "of the profile's wallet, and EventPresenter — which reads the wallet for every currency " +
            "but Gold — would draw the option out of use on a profile that could pay, or live on one " +
            "that could not.");
        outcome.Rejection.ShouldBe(
            RejectionReason.INSUFFICIENT_FUNDS,
            "an empty wallet against a priced option is an affordability refusal. Another reason " +
            "means the arrangement was turned away before the price was ever read, and this case is " +
            "then measuring nothing about which purse is charged.");
    }

    /// <summary>The first shipped option priced in one named currency.</summary>
    private static (string CardId, int Index, CurrencyId Currency, long Amount) FirstPricedIn(
        CurrencyId currency) =>
        PricedOptions().FirstOrDefault(priced => priced.Currency == currency) is
            { CardId.Length: > 0 } found
            ? found
            : throw new InvalidOperationException(
                $"No shipped event card prices an option in {currency}, so the boundary case has " +
                "nothing to stand a run on. It is a case about the rule rather than about any one " +
                "card, but it needs one card to submit — re-point it, or drop it and say why.");

    /// <summary>And the first priced in anything else.</summary>
    /// <remarks>
    /// ⚠️ Whatever that currency turns out to be. <c>Player.BalanceOf</c> throws rather than answering
    /// zero for <c>ENERGY</c>, so a shipped card that priced an option in Energy would surface here as
    /// a thrown exception rather than a refusal — which is the pre-existing hole this case would be
    /// the first thing in the repo to run into, not a fault in the arrangement.
    /// </remarks>
    private static (string CardId, int Index, CurrencyId Currency, long Amount) FirstPricedOutside(
        CurrencyId currency) =>
        PricedOptions().FirstOrDefault(priced => priced.Currency != currency) is
            { CardId.Length: > 0 } found
            ? found
            : throw new InvalidOperationException(
                $"Every shipped priced option charges {currency}, so nothing here can tell the run's " +
                "purse from the profile's. The split is still real in both the handler and the " +
                "screen — re-point this case at a fixture card, or drop it and say why.");

    /// <summary>Every priced option the shipped catalogue authors, in catalogue order.</summary>
    private static IReadOnlyList<(string CardId, int Index, CurrencyId Currency, long Amount)>
        PricedOptions()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepoPaths.ContentDataRoot, "content", "board_events", BoardEventsFile)));

        var priced = new List<(string, int, CurrencyId, long)>();

        foreach (var card in document.RootElement.GetProperty("cards").EnumerateArray())
        {
            var options = card.GetProperty("options");

            for (var index = 0; index < options.GetArrayLength(); index++)
            {
                if (!options[index].TryGetProperty("cost", out var cost))
                {
                    continue;
                }

                priced.Add((
                    card.GetProperty("id").GetString()!,
                    index,
                    Enum.Parse<CurrencyId>(cost.GetProperty("currency").GetString()!),
                    cost.GetProperty("amount").GetInt64()));
            }
        }

        return priced;
    }

    /// <summary>Submits one choice on one card, over a run holding the Gold a case funded it with.</summary>
    private static CommandResult Choose(string cardId, int choiceIndex, long gold = 0L)
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

        return GameRules.Apply(BrokeAndOnCard(cardId, gold), command, context);
    }

    /// <summary>
    /// A run standing on an Event tile that has drawn this card, holding the Gold a case funded it
    /// with and — always — a profile whose wallet is empty.
    /// </summary>
    private static WorldSlice BrokeAndOnCard(string cardId, long gold = 0L) =>
        PlayerState.SliceWith(
            PlayerState.EmptySlice(Player),
            PlayerState.Run(
                Run,
                Player,
                RunPhase.InProgress,
                position: 1,
                gold: gold,
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
