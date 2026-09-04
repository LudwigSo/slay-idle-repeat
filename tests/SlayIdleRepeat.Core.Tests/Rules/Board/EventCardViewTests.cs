using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Handlers;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

/// <summary>
/// <c>EventCardView</c> — the card a pending Event tile has already drawn, projected so the Event
/// screen can show its prose and its options before <c>EVENT_CHOOSE</c> spends one.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 The gate has TWO halves and they are different states. A run standing on another tile has no
/// card; a run standing on an Event tile whose draw has not happened yet has no card either, and the
/// row spells that second absence as the empty string rather than as null — so a view reading it as
/// "some card id" would ask the catalogue for a card called <c>""</c>.
/// </para>
/// <para>
/// 🔒 Outcomes and odds are not part of the surface at all, so nothing here asks for them. What is
/// pinned is what a screen must be able to draw: the prose, the options in authored order, and each
/// option's price or the absence of one.
/// </para>
/// </remarks>
public sealed class EventCardViewTests
{
    private static ContentSnapshot Content => TileWorlds.Context.Content;

    // ------------------------------------------------------------------------------------------
    // The gate, both halves.
    // ------------------------------------------------------------------------------------------

    /// <summary>A run standing on no tile at all has drawn no card.</summary>
    [Fact]
    public void A_run_with_no_pending_tile_projects_nothing()
    {
        EventCardView.Project(TileWorlds.OnNoTile().Run!.ToSnapshot(), Content).ShouldBeNull();
    }

    /// <summary>…and a pending tile of another kind is not an event either.</summary>
    [Theory]
    [InlineData((int)TileKind.Campfire)]
    [InlineData((int)TileKind.Empty)]
    [InlineData((int)TileKind.Shrine)]
    [InlineData((int)TileKind.Shop)]
    public void A_pending_tile_of_another_kind_projects_nothing(int kind)
    {
        EventCardView.Project(
            TileWorlds.OnTile((TileKind)kind).Run!.ToSnapshot(), Content).ShouldBeNull();
    }

    /// <summary>
    /// 🔒 An Event tile whose card has NOT been drawn yet projects nothing — and the absence is the
    /// empty string, never null.
    /// </summary>
    /// <remarks>
    /// 🔴 The second half of the gate, and the half a view that only checked the tile kind would
    /// fail: the row spells "none drawn" as <c>""</c> so its canonical encoding carries no nullable
    /// slot, so a view keyed on null alone would hand <c>EventCatalogue.Find</c> an empty id and
    /// throw the content-rollback message at a run that is merely one command early.
    /// </remarks>
    [Fact]
    public void An_event_tile_with_no_card_drawn_yet_projects_nothing()
    {
        var run = TileWorlds.OnTile(TileKind.Event).Run!.ToSnapshot();

        run.PendingEventCardId.ShouldBe(
            "",
            "the fixture is meant to be standing on an Event tile with the draw still to happen, " +
            "and this case is about that exact row. A null here would mean the sentinel moved and " +
            "the assertion below is no longer about the state its name claims.");

        EventCardView.Project(run, Content).ShouldBeNull();
    }

    // ------------------------------------------------------------------------------------------
    // The card.
    // ------------------------------------------------------------------------------------------

    /// <summary>A drawn card projects its own id and its own prose, verbatim.</summary>
    /// <remarks>
    /// The fixture authors a title and a body that differ from each other and from the id, so a view
    /// that showed the id in the heading, or the body twice, cannot satisfy this.
    /// </remarks>
    [Fact]
    public void A_drawn_card_projects_its_id_and_its_prose()
    {
        var view = Projected(FixtureCards.Vitals);

        view.CardId.ShouldBe(FixtureCards.Vitals);
        view.Title.ShouldBe(FixtureCards.Vitals + " title");
        view.Body.ShouldBe(
            FixtureCards.Vitals + " body",
            "the body is the card's own prose. A view handing the screen the title here would put " +
            "the heading on the screen twice and lose the only text describing what is being offered.");
    }

    /// <summary>
    /// 🔒 The options come back in the document's authored order, and each carries the index
    /// <c>EVENT_CHOOSE</c> needs to take it.
    /// </summary>
    /// <remarks>
    /// 🔴 The index IS the wire value, so an order that drifted or an index that was not the
    /// position would submit a different option than the player pressed — silently, because both
    /// are legal choices on the same card. Two cards of different lengths are swept so a view that
    /// hard-coded two rows, or one, cannot pass.
    /// </remarks>
    [Theory]
    [InlineData(FixtureCards.Vitals, 3)]
    [InlineData(FixtureCards.FixedDice, 2)]
    [InlineData(FixtureCards.Gold, 1)]
    public void The_options_are_projected_in_authored_order_with_the_index_that_takes_them(
        string cardId, int expectedCount)
    {
        var view = Projected(cardId);
        var authored = Authored(cardId);

        view.Options.Count.ShouldBe(
            expectedCount,
            cardId + " authors " + expectedCount + " options and the view drew " +
            view.Options.Count + ". A screen showing fewer hides a choice the rules layer accepts.");
        view.Options.Count.ShouldBe(
            authored.Count, "…and the count is the fixture document's own, not a transcription.");

        for (var index = 0; index < view.Options.Count; index++)
        {
            view.Options[index].ChoiceIndex.ShouldBe(
                index,
                "option " + index + " of " + cardId + " carries index " +
                view.Options[index].ChoiceIndex + ". The index is what EVENT_CHOOSE travels as, so " +
                "a row whose index is not its position submits a different option than the player " +
                "pressed — and both are legal choices on the same card, so nothing refuses it.");
            view.Options[index].Label.ShouldBe(
                authored[index],
                "option " + index + " of " + cardId + " is captioned with another option's label, " +
                "so the card offers the same words twice or names the wrong one.");
        }
    }

    /// <summary>
    /// A priced option carries its currency and its amount; a free one carries neither.
    /// </summary>
    /// <remarks>
    /// 🔒 Both arms in one case, over one card, because the pair is the claim: a view that reported
    /// every option as free would satisfy the second arm alone, and one that invented a zero cost
    /// for the free option would satisfy the first.
    /// </remarks>
    [Fact]
    public void A_priced_option_names_its_price_and_a_free_one_names_none()
    {
        var view = Projected(FixtureCards.Costly);

        view.Options[0].CostCurrency.ShouldBe(CurrencyId.GOLD);
        view.Options[0].CostAmount.ShouldBe(
            100L,
            "the fixture card charges 100 Gold for its first option, and the screen blocks the " +
            "option below that balance — an amount the view got wrong is a card that reads as " +
            "affordable and is refused on the press.");

        view.Options[1].CostCurrency.ShouldBeNull(
            "the second option is authored free, so naming a currency for it would put a price on " +
            "the one way off this card that never costs anything.");
        view.Options[1].CostAmount.ShouldBeNull();
    }

    /// <summary>…and a cost in a wallet currency is projected as that currency, not as Gold.</summary>
    /// <remarks>
    /// The negative control for the case above: with only the Gold card swept, a view that reported
    /// every price as Gold would pass, and the screen would then check the run's Gold against a
    /// price the handler charges to the player's wallet.
    /// </remarks>
    [Fact]
    public void A_wallet_currency_cost_is_projected_as_that_currency()
    {
        var view = Projected(FixtureCards.CostlyMeta);

        view.Options[0].CostCurrency.ShouldBe(CurrencyId.ENHANCE_STONES);
        view.Options[0].CostAmount.ShouldBe(6L);
    }

    // ------------------------------------------------------------------------------------------
    // The one throw.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// A card id the catalogue does not carry throws, carrying the catalogue's own explanation.
    /// </summary>
    /// <remarks>
    /// 🔒 Deliberately NOT a null: a drawn card id this content version has lost is a content
    /// rollback across a live run, not a player input, and the catalogue's message is the one that
    /// says so. A view that swallowed it into a null would report "no event here" on a run standing
    /// on one.
    /// </remarks>
    [Fact]
    public void A_drawn_card_the_catalogue_does_not_carry_throws_the_catalogues_own_message()
    {
        var run = TileWorlds
            .OnTile(TileKind.Event, eventCardId: "EVT_FIXTURE_NEVER_AUTHORED")
            .Run!.ToSnapshot();

        var thrown = Should.Throw<ArgumentException>(() => EventCardView.Project(run, Content));

        thrown.Message.ShouldContain(
            "authors no event card",
            Case.Sensitive,
            "the throw is the catalogue's, and its message is what tells an operator a content " +
            "rollback stranded a live run rather than that a player asked for something illegal. A " +
            "message this view invented would lose that.");
        thrown.Message.ShouldContain("EVT_FIXTURE_NEVER_AUTHORED", Case.Sensitive);
    }

    // ------------------------------------------------------------------------------------------
    // 🔒 Projecting moves nothing.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 A card that was LOOKED at resolves byte-for-byte like one that was not.
    /// </summary>
    /// <remarks>
    /// The outcome draw belongs to <c>EVENT_CHOOSE</c>. A view that opened the event stream and let
    /// it advance would leave the choice resolving against a different roll than the screen showed —
    /// invisible in the projection itself, and only visible by resolving on both paths.
    /// </remarks>
    [Fact]
    public void A_card_that_was_projected_resolves_exactly_like_one_that_was_not()
    {
        var looked = TileWorlds.OnTile(
            TileKind.Event, gold: 500, currentHp: 40, eventCardId: FixtureCards.Split);

        EventCardView.Project(looked.Run!.ToSnapshot(), Content);

        var blind = TileWorlds.OnTile(
            TileKind.Event, gold: 500, currentHp: 40, eventCardId: FixtureCards.Split);

        var afterLooking = SlayIdleRepeat.Core.GameRules.Apply(
            looked, new EventChooseCommand(0), TileWorlds.Context);
        var afterBlind = SlayIdleRepeat.Core.GameRules.Apply(
            blind, new EventChooseCommand(0), TileWorlds.Context);

        afterLooking.Accepted.ShouldBeTrue(
            "with the choice refused there is no resolution to compare and this case measures nothing.");
        CanonicalStateWriter.CanonicalBytes(afterLooking.NewState.Run!.ToSnapshot()).ShouldBe(
            CanonicalStateWriter.CanonicalBytes(afterBlind.NewState.Run!.ToSnapshot()),
            "looking at an event card changed what choosing on it did. The card's 70/30 split is " +
            "drawn off the event stream, so a projection that opened and advanced it moves the " +
            "outcome the player actually gets.");
    }

    // ------------------------------------------------------------------------------------------
    // Fixtures.
    // ------------------------------------------------------------------------------------------

    private static EventCardView Projected(string cardId) =>
        EventCardView.Project(
            TileWorlds.OnTile(TileKind.Event, eventCardId: cardId).Run!.ToSnapshot(), Content)
        ?? throw new InvalidOperationException(
            "the fixture run is standing on an Event tile carrying '" + cardId + "'");

    /// <summary>
    /// The option labels the fixture document authors for a card, read off the document rather than
    /// transcribed — so the order this case checks is the order the content really has.
    /// </summary>
    private static IReadOnlyList<string> Authored(string cardId)
    {
        var cards = Content.Read(InRunIncomeDocuments.BoardEventsPath + "#/cards");

        foreach (var card in cards.Items)
        {
            if (!card.TryGetMember("id", out var id) || id is null ||
                !string.Equals(id.AsText(), cardId, StringComparison.Ordinal))
            {
                continue;
            }

            return card.TryGetMember("options", out var options) && options is not null
                ? options.Items
                        .Select(option => option.TryGetMember("label", out var label) && label is not null
                            ? label.AsText()
                            : "")
                        .ToArray()
                : [];
        }

        throw new InvalidOperationException(
            "'" + cardId + "' is not a card of the fixture document, so this case would compare " +
            "the view against an empty authored list and pass whatever the view answered.");
    }
}
