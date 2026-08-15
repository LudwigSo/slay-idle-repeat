using System.Text.RegularExpressions;
using Shouldly;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.BoardEvents;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content.BoardEvents;

/// <summary>
/// 🔒 `19` Part A — <see cref="EventCatalogue"/>, against the <b>real</b>
/// <c>game-data/content/board_events/board_events.json</c>.
/// </summary>
/// <remarks>
/// ⚠️ <b>This is the one class in M3-03's suite that reads the shipped tree rather than a hermetic
/// fixture, and the exception is deliberate.</b> The claim being made is "all thirty authored cards
/// load and are well formed", which a fixture mirroring thirty cards could not make — it would only
/// say that thirty cards <em>I wrote in this file</em> load. <c>GameDataLoaderTests</c> is the
/// existing precedent for reaching the tree from <c>Core.Tests</c>, through
/// <see cref="GameDataLoader"/>, which the test project already references for exactly this. The
/// reader's own <em>refusals</em> are tested hermetically below, where a malformed document has to be
/// constructed.
/// </remarks>
public sealed class EventCatalogueTests
{
    private static readonly ContentSnapshot RealData = GameDataLoader.Load();

    private static readonly EventCatalogue Shipped = EventCatalogue.Read(RealData);

    /// <summary>🔒 `19` Part A authors thirty cards and all thirty load.</summary>
    [Fact]
    public void All_thirty_authored_cards_load()
    {
        Shipped.All.Count.ShouldBe(30);
    }

    /// <summary>Every id is unique and matches the schema's own <c>EVT_</c> pattern.</summary>
    [Fact]
    public void Every_card_id_is_unique_and_well_formed()
    {
        Shipped.All.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count().ShouldBe(30);

        foreach (var card in Shipped.All)
        {
            Regex.IsMatch(card.Id, "^EVT_[A-Z0-9_]+$")
                .ShouldBeTrue(card.Id + " does not match ^EVT_[A-Z0-9_]+$");
        }
    }

    /// <summary>Every card carries the title and body `19` Part A gives it.</summary>
    [Fact]
    public void Every_card_carries_a_title_and_a_body()
    {
        foreach (var card in Shipped.All)
        {
            card.Title.ShouldNotBeNullOrWhiteSpace(card.Id);
            card.Body.ShouldNotBeNullOrWhiteSpace(card.Id);
        }
    }

    /// <summary>`19` Part A gives every card two or three options, each with at least one outcome.</summary>
    [Fact]
    public void Every_card_offers_two_or_three_options_with_real_outcomes()
    {
        foreach (var card in Shipped.All)
        {
            card.Options.Count.ShouldBeInRange(2, 3, card.Id);

            foreach (var option in card.Options)
            {
                option.Label.ShouldNotBeNullOrWhiteSpace(card.Id);
                option.Outcomes.ShouldNotBeEmpty(card.Id + " / " + option.Label);

                foreach (var outcome in option.Outcomes)
                {
                    outcome.Weight.ShouldBeGreaterThan(0.0, card.Id + " / " + option.Label);
                    outcome.Effects.ShouldNotBeEmpty(card.Id + " / " + option.Label);
                }
            }
        }
    }

    /// <summary>
    /// 🔒 Every card's chapter band is one of `19` Part A's own three (A1 1-3, A2 3-6, A3 6-8), and
    /// together they cover every chapter `02` §1 runs.
    /// </summary>
    [Fact]
    public void Every_card_sits_in_one_of_part_As_three_chapter_bands()
    {
        foreach (var card in Shipped.All)
        {
            (card.MinChapter, card.MaxChapter).ShouldBeOneOf((1, 3), (3, 6), (6, 8));
        }

        for (var chapter = 1; chapter <= 8; chapter++)
        {
            Shipped.AvailableIn(chapter).ShouldNotBeEmpty("chapter " + chapter + " can draw no card");
        }
    }

    /// <summary>
    /// 🔒 The band filter is inclusive at both ends — chapters 3 and 6 sit in two bands each, which
    /// is `19` Part A's own overlap and not a transcription slip.
    /// </summary>
    [Fact]
    public void The_chapter_bands_overlap_at_three_and_six()
    {
        var atThree = Shipped.AvailableIn(3).Select(c => c.Id).ToArray();

        atThree.ShouldContain("EVT_WELL", "an A1 card, whose band closes AT chapter 3");
        atThree.ShouldContain("EVT_GAMBLER", "an A2 card, whose band opens AT chapter 3");
    }

    /// <summary>…and it genuinely excludes: a late card is not drawable early, or the reverse.</summary>
    [Fact]
    public void The_band_filter_excludes_out_of_band_cards()
    {
        Shipped.AvailableIn(1).Select(c => c.Id).ShouldNotContain("EVT_ORACLE");
        Shipped.AvailableIn(8).Select(c => c.Id).ShouldNotContain("EVT_WELL");
    }

    /// <summary>
    /// 🔒 Every <c>CURSE_REWARD</c> effect names a curse <c>CurseRewards</c> can actually pay — the
    /// cross-file check that would otherwise only fail at run time, inside a live run.
    /// </summary>
    [Fact]
    public void Every_curse_reward_effect_names_a_payable_curse()
    {
        foreach (var effect in EveryEffect().Where(e => e.Op == EventEffectOp.CurseReward))
        {
            CurseRewards.IsPayable(effect.CurseId).ShouldBeTrue(effect.CurseId);
        }
    }

    /// <summary>
    /// 🔒 Every <c>UNSUPPORTED</c> effect carries a note. ⚠️ The note is what separates a deliberate
    /// deferral from an unfinished row, so an empty one would make the whole op meaningless.
    /// </summary>
    [Fact]
    public void Every_unsupported_effect_explains_itself()
    {
        var unsupported = EveryEffect().Where(e => e.Op == EventEffectOp.Unsupported).ToArray();

        unsupported.ShouldNotBeEmpty(
            "19 Part A's prose describes movement, battles, gear and perks that Core cannot execute, " +
            "so a transcription with NO unsupported effects would mean the translation invented " +
            "mechanisms for them.");

        foreach (var effect in unsupported)
        {
            effect.Note.ShouldNotBeNullOrWhiteSpace();
        }
    }

    /// <summary>
    /// 🔒 The <c>chapterScaled</c> convention holds across all thirty: GOLD amounts are flat and
    /// metacurrency amounts scale.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is a <b>transcription judgement</b> rather than a design number — `19` Part A's header
    /// says costs and rewards scale "unless marked flat" and marks nothing flat, so the reading that
    /// GOLD figures are already at the in-run scale is the authored decision M3-03 recorded. Pinning
    /// it here is what makes it a decision rather than a drift.
    /// </remarks>
    [Fact]
    public void Gold_amounts_are_flat_and_metacurrency_amounts_are_chapter_scaled()
    {
        foreach (var effect in EveryEffect().Where(e => e.Op == EventEffectOp.Currency))
        {
            if (effect.Currency == CurrencyId.GOLD)
            {
                effect.ChapterScaled.ShouldBeFalse("a GOLD amount is flat");
            }
            else
            {
                effect.ChapterScaled.ShouldBeTrue(effect.Currency + " is chapter-scaled");
            }
        }
    }

    /// <summary>A cost is always a positive magnitude, and always names a currency with it.</summary>
    [Fact]
    public void Every_authored_cost_is_a_positive_amount_of_a_named_currency()
    {
        foreach (var option in Shipped.All.SelectMany(c => c.Options))
        {
            (option.CostCurrency is null).ShouldBe(
                option.CostAmount is null,
                "a cost's currency and amount are one fact and are set together");

            if (option.CostAmount is { } amount)
            {
                amount.ShouldBeGreaterThan(0L);
            }
        }
    }

    /// <summary><see cref="EventCatalogue.Find"/> answers by id, and refuses one it does not carry.</summary>
    [Fact]
    public void Find_answers_by_id_and_refuses_an_unknown_one()
    {
        Shipped.Find("EVT_WELL").Title.ShouldBe("The Wishing Well");

        Should.Throw<ArgumentException>(() => Shipped.Find("EVT_NOT_REAL"))
            .Message.ShouldContain("EVT_NOT_REAL", Case.Sensitive);
    }

    // ------------------------------------------------------------------ hermetic refusals

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

        Should.Throw<InvalidTunableException>(() =>
            EventCatalogue.Read(InRunIncomeDocuments.With(cards: ContentValue.Array([card]))));
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

        Should.Throw<InvalidTunableException>(() =>
            EventCatalogue.Read(InRunIncomeDocuments.With(cards: ContentValue.Array([card]))));
    }

    /// <summary>An op outside the closed five is refused rather than skipped as a no-op.</summary>
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

        Should.Throw<InvalidTunableException>(() =>
                EventCatalogue.Read(InRunIncomeDocuments.With(cards: ContentValue.Array([card]))))
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

        Should.Throw<InvalidTunableException>(() =>
                EventCatalogue.Read(InRunIncomeDocuments.With(cards: ContentValue.Array([card]))))
            .Message.ShouldContain("CUR_HUNTED", Case.Sensitive);
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

        Should.Throw<InvalidTunableException>(() =>
            EventCatalogue.Read(InRunIncomeDocuments.With(cards: ContentValue.Array([card]))));
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

        Should.Throw<InvalidTunableException>(() =>
                EventCatalogue.Read(InRunIncomeDocuments.With(cards: ContentValue.Array([card]))))
            .Message.ShouldContain("ENERGY", Case.Sensitive);
    }

    private static IEnumerable<EventEffect> EveryEffect() =>
        Shipped.All
            .SelectMany(c => c.Options)
            .SelectMany(o => o.Outcomes)
            .SelectMany(o => o.Effects);
}
