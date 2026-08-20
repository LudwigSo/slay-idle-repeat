using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.BoardEvents;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content.BoardEvents;

/// <summary><see cref="EventCatalogue"/>'s read: what it answers, and everything it refuses.</summary>
public sealed class EventCatalogueTests
{
    private static EventCatalogue Catalogue(params ContentValue[] cards) =>
        EventCatalogue.Read(InRunIncomeDocuments.With(cards: ContentValue.Array(cards)));

    /// <summary>Order is load-bearing: the resolver draws by index over this list.</summary>
    [Fact]
    public void Read_preserves_the_documents_card_order()
    {
        var catalogue = Catalogue(
            FixtureCards.Card("EVT_FIXTURE_B", 1, 8, FixtureCards.Option("Only", null, FixtureCards.Outcome(1, FixtureCards.None()))),
            FixtureCards.Card("EVT_FIXTURE_A", 1, 8, FixtureCards.Option("Only", null, FixtureCards.Outcome(1, FixtureCards.None()))));

        catalogue.All.Select(c => c.Id).ShouldBe(["EVT_FIXTURE_B", "EVT_FIXTURE_A"]);
    }

    /// <summary>The chapter band is inclusive at both ends.</summary>
    [Theory]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(6, true)]
    [InlineData(2, false)]
    [InlineData(7, false)]
    public void AvailableIn_applies_the_band_inclusively_at_both_ends(int chapter, bool drawable)
    {
        var catalogue = Catalogue(FixtureCards.Card(
            "EVT_FIXTURE_MID", 3, 6, FixtureCards.Option("Only", null, FixtureCards.Outcome(1, FixtureCards.None()))));

        catalogue.AvailableIn(chapter).Select(c => c.Id).Contains("EVT_FIXTURE_MID").ShouldBe(drawable);
    }

    [Fact]
    public void Find_answers_by_id_and_refuses_an_unknown_one()
    {
        var catalogue = Catalogue(FixtureCards.Card(
            "EVT_FIXTURE_FOUND", 1, 8, FixtureCards.Option("Only", null, FixtureCards.Outcome(1, FixtureCards.None()))));

        catalogue.Find("EVT_FIXTURE_FOUND").Id.ShouldBe("EVT_FIXTURE_FOUND");

        Should.Throw<ArgumentException>(() => catalogue.Find("EVT_NOT_REAL"))
            .Message.ShouldContain("EVT_NOT_REAL", Case.Sensitive);
    }

    // ------------------------------------------------------------------ refusals

    /// <summary>An empty card list leaves an event tile with nothing to draw.</summary>
    [Fact]
    public void An_empty_card_list_is_refused()
    {
        Should.Throw<InvalidTunableException>(() =>
            EventCatalogue.Read(InRunIncomeDocuments.With(cards: ContentValue.EmptyArray)));
    }

    /// <summary>A duplicate id would make one card unreachable by the id EVENT_CHOOSE finds it with.</summary>
    [Fact]
    public void A_duplicate_card_id_is_refused()
    {
        var card = FixtureCards.Card(
            "EVT_FIXTURE_TWICE", 1, 8, FixtureCards.Option("Only", null, FixtureCards.Outcome(1, FixtureCards.None())));

        Should.Throw<InvalidTunableException>(() =>
                EventCatalogue.Read(InRunIncomeDocuments.With(cards: ContentValue.Array([card, card]))))
            .Message.ShouldContain("EVT_FIXTURE_TWICE", Case.Sensitive);
    }

    [Fact]
    public void A_card_with_a_blank_title_is_refused()
    {
        var card = InRunIncomeDocuments.Obj(
            ("id", ContentValue.Text("EVT_FIXTURE_BLANK")),
            ("title", ContentValue.Text("   ")),
            ("body", ContentValue.Text("body")),
            ("minChapter", ContentValue.Number(1)),
            ("maxChapter", ContentValue.Number(8)),
            ("options", ContentValue.Array(
                [FixtureCards.Option("Only", null, FixtureCards.Outcome(1, FixtureCards.None()))])));

        Should.Throw<InvalidTunableException>(() => Catalogue(card));
    }

    /// <summary>
    /// A zero-weighted outcome is a branch nobody can ever see — <c>WeightedPick</c>'s strict walk
    /// makes it unreachable wherever it sits.
    /// </summary>
    [Fact]
    public void A_zero_weighted_outcome_is_refused()
    {
        var card = FixtureCards.Card(
            "EVT_FIXTURE_ZERO",
            1,
            8,
            FixtureCards.Option("Only", null, FixtureCards.Outcome(0, FixtureCards.None())));

        Should.Throw<InvalidTunableException>(() => Catalogue(card));
    }

    /// <summary>An outcome with no effects is indistinguishable from one nobody finished authoring.</summary>
    [Fact]
    public void An_outcome_with_no_effects_is_refused()
    {
        var card = FixtureCards.Card(
            "EVT_FIXTURE_EMPTY",
            1,
            8,
            FixtureCards.Option("Only", null, FixtureCards.Outcome(1)));

        Should.Throw<InvalidTunableException>(() => Catalogue(card));
    }

    /// <summary>A cost's direction is the field's meaning, not its sign — the handler debits it.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void A_cost_that_is_not_a_positive_amount_is_refused(long amount)
    {
        var card = FixtureCards.Card(
            "EVT_FIXTURE_FREEBIE",
            1,
            8,
            FixtureCards.Option(
                "Pay nothing",
                FixtureCards.Cost("GOLD", amount),
                FixtureCards.Outcome(1, FixtureCards.None())));

        Should.Throw<InvalidTunableException>(() => Catalogue(card));
    }

    /// <summary>A zero delta is dropped by the resolver; a deliberate no-op is a NONE effect.</summary>
    [Fact]
    public void A_currency_effect_moving_nothing_is_refused()
    {
        var card = FixtureCards.Card(
            "EVT_FIXTURE_NOTHING",
            1,
            8,
            FixtureCards.Option("Only", null, FixtureCards.Outcome(1, FixtureCards.Currency("GOLD", 0, false))));

        Should.Throw<InvalidTunableException>(() => Catalogue(card));
    }

    /// <summary>An HP_PCT is a share of Max HP in [-1, 1].</summary>
    [Theory]
    [InlineData(1.5)]
    [InlineData(-1.5)]
    public void An_hp_share_outside_a_full_bar_either_way_is_refused(double share)
    {
        var card = FixtureCards.Card(
            "EVT_FIXTURE_OVERHEAL",
            1,
            8,
            FixtureCards.Option("Only", null, FixtureCards.Outcome(1, FixtureCards.HpPct((decimal)share))));

        Should.Throw<InvalidTunableException>(() => Catalogue(card));
    }

    /// <summary>An op outside the closed six is refused rather than skipped as a no-op.</summary>
    [Fact]
    public void An_unknown_effect_op_is_refused()
    {
        var card = FixtureCards.Card(
            "EVT_FIXTURE_ALIEN",
            1,
            8,
            FixtureCards.Option(
                "Only",
                null,
                FixtureCards.Outcome(1, InRunIncomeDocuments.Obj(("op", ContentValue.Text("GRANT_GEAR"))))));

        Should.Throw<InvalidTunableException>(() => Catalogue(card))
            .Message.ShouldContain("GRANT_GEAR", Case.Sensitive);
    }

    /// <summary>A curse the payout table cannot pay is refused at read time, not at resolve time.</summary>
    [Fact]
    public void A_card_naming_an_unpayable_curse_is_refused()
    {
        var card = FixtureCards.Card(
            "EVT_FIXTURE_HUNTED",
            1,
            8,
            FixtureCards.Option("Only", null, FixtureCards.Outcome(1, FixtureCards.CurseReward("CUR_HUNTED"))));

        Should.Throw<InvalidTunableException>(() => Catalogue(card))
            .Message.ShouldContain("CUR_HUNTED", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 A <c>FIXED_DIE</c> effect granting nothing is refused. A zero resolves to no grant at
    /// all and is indistinguishable from a row nobody finished; a negative is a debt the run has no
    /// way to pay back, because it does not know which of the player's dice this card gave it.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_fixed_die_effect_granting_nothing_is_refused(int count)
    {
        var card = FixtureCards.Card(
            "EVT_FIXTURE_EMPTY_GRANT",
            1,
            8,
            FixtureCards.Option("Only", null, FixtureCards.Outcome(1, FixtureCards.FixedDie(count))));

        Should.Throw<InvalidTunableException>(() => Catalogue(card))
            .Message.ShouldContain("at least one choice", Case.Sensitive);
    }

    /// <summary>A band that closes before it opens can never be drawn from.</summary>
    [Fact]
    public void A_card_whose_band_closes_before_it_opens_is_refused()
    {
        var card = FixtureCards.Card(
            "EVT_FIXTURE_BACKWARDS",
            6,
            3,
            FixtureCards.Option("Only", null, FixtureCards.Outcome(1, FixtureCards.None())));

        Should.Throw<InvalidTunableException>(() => Catalogue(card));
    }

    /// <summary>ENERGY is not a currency an event card may move — its banks are not a wallet row.</summary>
    [Fact]
    public void A_card_granting_energy_is_refused()
    {
        var card = FixtureCards.Card(
            "EVT_FIXTURE_ENERGY",
            1,
            8,
            FixtureCards.Option("Only", null, FixtureCards.Outcome(1, FixtureCards.Currency("ENERGY", 5, false))));

        Should.Throw<InvalidTunableException>(() => Catalogue(card))
            .Message.ShouldContain("ENERGY", Case.Sensitive);
    }
}
