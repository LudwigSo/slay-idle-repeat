using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// `13` §4 / `06` §1 — the Perk Draft screen (S07): the three cards, the two priced ways off it,
/// and the three separate absences that sit around them.
/// </summary>
/// <remarks>
/// 🔴 This is the screen a run's whole build is chosen on, and it is the screen with the most ways
/// to say nothing. Two ad affordances are deferred to a later milestone, the free-reroll economy the
/// design describes was never written, four shipped perks cannot have their numbers rendered, and
/// the one refusal a player can act on — not enough Gold — arrives beside refusals they cannot.
/// Every one of those is a different sentence here.
/// </remarks>
public sealed class PerkDraftPresenterTests
{
    /// <summary>Property names that would mean this screen had invented a free-reroll count.</summary>
    private static readonly string[] WordsOfAnEconomyThatDoesNotExist = ["Free", "Charge", "Allowance"];

    /// <summary>The tile kind of the battle a draft opens after — an ordinary enemy fight.</summary>
    private const int EnemyBattleTileKind = 0;

    /// <summary>The stage that battle belonged to. Any of 1-3 would do; none of them may be 0.</summary>
    private const int FirstStage = 1;

    private static readonly PlayerId Player = new("PLAYER_draft_c410");
    private static readonly RunId Run = new("RUN_draft_36bd");

    // ---- the rule this screen exists to keep ----------------------------------------------------

    /// <summary>
    /// 🔒 <b>The draft never advances itself.</b> Nothing here picks, skips or rerolls except a
    /// player's press: not building the screen, not reading the run, not reading it again.
    /// </summary>
    /// <remarks>
    /// 🔴 Stated as a rule a future edit would break rather than as an observation about today's
    /// code. An auto-pick for a "clearly best" card, a timeout that takes the middle option, or a
    /// convenience reroll when every card is already owned would each spend the one decision a run
    /// is actually made of — and would do it invisibly, because the run comes back already changed
    /// and the screen would simply look like it had been slow.
    /// </remarks>
    [Fact]
    public void Building_the_screen_advances_the_draft_in_no_way_at_all()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), WithADraftOpen());

        _ = Build(host);

        host.SubmitCallCount.ShouldBe(
            0,
            "constructing the screen reached the host, so something on this screen picks, skips or " +
            "rerolls without being pressed. The draft is the one decision a run is made of.");
        host.ReadCallCount.ShouldBe(
            0,
            "and constructing it read as well. The read belongs to StartAsync, where a failure has " +
            "somewhere to be reported; a read in a constructor can only throw.");
    }

    /// <summary>🔒 The same rule again, this time across the read the screen actually does.</summary>
    [Fact]
    public async Task Reading_the_run_advances_the_draft_in_no_way_at_all()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), WithADraftOpen());
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);
        await presenter.StartAsync(CancellationToken.None);

        host.SubmitCallCount.ShouldBe(
            0,
            "reading the run submitted something. No path from observing a draft to changing it may " +
            "exist — including a second read finding the same draft still open and deciding to " +
            "resolve it.");
    }

    /// <summary>
    /// 🔒 <b>No free-reroll count is invented.</b> The design authors one per stage, accumulating to
    /// three, plus one from a skip; none of it exists in code, and nothing here may report a number
    /// that has no source.
    /// </summary>
    /// <remarks>
    /// 🔴 Stated over the type's own surface rather than over one value, because the failure this
    /// guards against is a member being ADDED. The absence has a sentence — the only member here
    /// allowed to mention the free reroll is the one that carries that sentence, and a sentence is
    /// a string.
    /// </remarks>
    [Fact]
    public void No_member_of_this_screen_reports_a_free_reroll_count()
    {
        var members = typeof(PerkDraftPresenter)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // 🔒 The subject set, floored by the NAMED member the rule exempts rather than by a count
        // (steering S3). A screen carrying no free-reroll member at all satisfies the rule below
        // perfectly — and is also the screen that has stopped naming the absence, which is the one
        // thing this whole apparatus exists to keep on the page.
        members.ShouldContain(
            property => property.Name == nameof(PerkDraftPresenter.FreeRerollBlockText) &&
                        property.PropertyType == typeof(string),
            "the one member allowed to mention the free reroll is the sentence naming its absence, " +
            "and it is gone. Without it the rule below passes by having nothing to report, and the " +
            "screen quietly stops saying the allowance was never built.");

        var offenders =
            from property in members
            where WordsOfAnEconomyThatDoesNotExist.Any(word =>
                property.Name.Contains(word, StringComparison.Ordinal))
            where property.PropertyType != typeof(string)
            select $"{property.Name} : {property.PropertyType.Name}";

        offenders.ShouldBeEmpty(
            "a member of this screen reports the free-reroll economy as something other than a " +
            "sentence, which means it reports a NUMBER — and there is no number. REROLL_DRAFT " +
            "charges Gold on every call with no counter and no cap, and SKIP_DRAFT grants no " +
            "charge. Whatever this member answers was computed here, and it will be wrong in a way " +
            "no test outside this one can see.");
    }

    /// <summary>
    /// 🔒 Both ad affordances are unavailable, and they are two affordances rather than one.
    /// </summary>
    [Fact]
    public void Neither_ad_affordance_is_offered_and_they_are_two_different_offers()
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer());

        presenter.AdRerollAvailable.ShouldBeFalse(
            "the ad reroll grants an ad reward, and the command that would grant one is deferred to " +
            "a later milestone. Offering it means inventing a placement id, a cap and a grant.");
        presenter.AdFourthOptionAvailable.ShouldBeFalse(
            "and the fourth-option card is the same deferred command behind a different affordance. " +
            "Its layout slot is kept so the milestone that lands it fills a slot rather than " +
            "re-laying out the screen — kept, not made live.");

        presenter.AdRerollBlockText.ShouldNotBe(
            presenter.AdFourthOptionBlockText,
            "and they do not share a line. A player who pressed the quieter reroll and is told the " +
            "fourth card is unavailable has been answered about something they did not touch.");
    }

    /// <summary>
    /// 🔒 The four things that can be wrong with the reroll are four different sentences, <b>as
    /// authored</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 Stated over the shipped locale, never over the fixture: every fixture value here is
    /// derived from its own key, so four distinct keys give four distinct values by construction and
    /// a fixture-based version of this case could never fail whatever anyone wrote in
    /// <c>en.json</c>. The claim is about what a player reads — three of these are waits and one is
    /// a price they can go and earn.
    /// </remarks>
    [Fact]
    public void The_four_reasons_a_reroll_is_not_available_are_four_different_authored_sentences()
    {
        string[] keys =
        [
            RunDecisionContent.DraftAdRerollBlockKey,
            RunDecisionContent.DraftAdFourthOptionBlockKey,
            RunDecisionContent.DraftFreeRerollBlockKey,
            RunDecisionContent.DraftRerollUnaffordableStatusKey,
        ];

        var authored = keys.Select(key =>
        {
            RunDecisionContent.ShippedEnglish.TryGetValue(key, out var sentence).ShouldBeTrue(
                $"'{key}' is not in the shipped English locale, so one of the four things that can " +
                "be wrong with the reroll has no sentence and a player meeting it is shown its key.");

            return sentence!;
        }).ToArray();

        authored.ShouldAllBe(sentence => sentence.Length > 0);
        authored.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            authored.Length,
            "two of the four are AUTHORED the same. The ad path is a deferred command, the fourth " +
            "option is a different deferred affordance, the free allowance was never written at " +
            "all, and an unaffordable reroll is a price — sharing a sentence between any two of " +
            $"them sends a player to wait when they should be earning, or the reverse: " +
            $"[{string.Join(" | ", authored)}]");
    }

    /// <summary>
    /// 🔒 The five ways this screen has of saying something is not there are five different
    /// sentences, <b>as authored</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 Stated over the shipped locale, never over the fixture, for the reason the case above
    /// gives about its own four. The claim is about what a player reads, and these five are five
    /// completely different situations: the run has no draft; the cards could not be projected at
    /// all; the cards are there and one of them cannot state its numbers; the run could not be
    /// found; and the read never answered. A player told "the cards are unavailable" when the truth
    /// is "this one perk's numbers are unauthored" is being told the screen is broken when two of
    /// its three cards are perfectly good — and the reverse sends them looking at three cards that
    /// are not there.
    /// </remarks>
    [Fact]
    public void The_five_ways_this_screen_says_something_is_missing_are_five_different_authored_sentences()
    {
        string[] keys =
        [
            RunDecisionContent.DraftNoDraftStatusKey,
            RunDecisionContent.DraftCardsUnavailableStatusKey,
            RunDecisionContent.DraftEffectNumbersUnavailableStatusKey,
            RunDecisionContent.DraftRunMissingStatusKey,
            RunDecisionContent.DraftReadUnavailableStatusKey,
        ];

        var authored = keys.Select(key =>
        {
            RunDecisionContent.ShippedEnglish.TryGetValue(key, out var sentence).ShouldBeTrue(
                $"'{key}' is not in the shipped English locale, so one of the five ways this screen " +
                "can come to nothing has no sentence and a player meeting it is shown its key.");

            return sentence!;
        }).ToArray();

        authored.ShouldAllBe(sentence => sentence.Length > 0);
        authored.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            authored.Length,
            "two of the five are AUTHORED the same, so two unrelated failures read as one thing. " +
            "The absence a CARD names — its numbers are unauthored, on a card that is otherwise " +
            "fine — is the one most easily written as a copy of its neighbour, and copied it tells " +
            $"a player the whole draft failed: [{string.Join(" | ", authored)}]");
    }

    // ---- the read ------------------------------------------------------------------------------

    [Fact]
    public void A_freshly_built_presenter_has_not_read_anything()
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer());

        presenter.Stage.ShouldBe(PerkDraftStage.NotYetRead);
        presenter.Cards.ShouldBeEmpty();
        presenter.CardsAvailable.ShouldBeFalse();
        presenter.StatusText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.DraftLoadingStatusKey));
    }

    [Fact]
    public async Task The_read_is_addressed_to_this_run_and_not_to_the_player_alone()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), WithADraftOpen());
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        host.ReadRun.ShouldBe(Run);
        host.ReadPlayer.ShouldBe(Player);
    }

    /// <summary>
    /// 🔒 <b>The cold start reads the run ONCE.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 Pinned because nothing else here can see a second one. Every other case about the read
    /// asks what the screen ended up showing, and a screen that read twice shows exactly the same
    /// thing — so a duplicated read is invisible to the whole suite while costing a real round trip
    /// on the tile a run's whole build is chosen on, and doubling the window in which the run can
    /// move underneath the cards. The no-auto-advance rule above pins SUBMISSIONS at zero and says
    /// nothing about reads.
    /// </remarks>
    [Fact]
    public async Task A_cold_start_reads_the_run_exactly_once()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), WithADraftOpen());
        var presenter = Build(host, BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        host.ReadCallCount.ShouldBe(
            1,
            "opening this screen cost more than one read of the same run. Every card, every price " +
            "and every absence on it comes out of one answer, so a second call is a second round " +
            "trip that changes nothing a player sees.");
    }

    /// <summary>
    /// 🔒 A run with no draft open is its own state. Drawn as an empty draft it would be three blank
    /// cards on the screen a build is chosen on.
    /// </summary>
    [Fact]
    public async Task A_run_with_no_draft_open_is_named_rather_than_drawn_as_an_empty_one()
    {
        var presenter = Build(RecordingGameHost.Finding(
            AnyPlayer(), PlayerState.Run(Run, Player, RunPhase.InProgress)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(PerkDraftStage.NoDraft);
        presenter.Cards.ShouldBeEmpty();
        presenter.StatusText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.DraftNoDraftStatusKey));
    }

    [Fact]
    public async Task A_read_that_finds_no_run_says_so_and_never_reads_as_a_draft()
    {
        var presenter = Build(RecordingGameHost.Reading(
            new OwnStateResult(OwnStateLookup.NoSuchRun, View: null)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(PerkDraftStage.RunMissing);
        presenter.StatusText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.DraftRunMissingStatusKey));
    }

    [Fact]
    public async Task A_read_that_faults_is_a_state_and_not_an_escape()
    {
        var presenter = Build(RecordingGameHost.FaultingItsRead(new TimeoutException("no answer")));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(PerkDraftStage.ReadUnavailable);
        presenter.StatusText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.DraftReadUnavailableStatusKey));
    }

    // ---- the cards -----------------------------------------------------------------------------

    /// <summary>
    /// The three cards come from the rules layer's own projection of the run's committed draft
    /// stream — the same derivation <c>PICK_PERK</c> answers an option index against.
    /// </summary>
    /// <remarks>
    /// 🔒 The option index is the card's position and nothing else. A screen that reordered the
    /// cards for display and submitted the display index would take the card to the left of the one
    /// that was pressed.
    /// </remarks>
    [Fact]
    public async Task An_open_draft_puts_the_projected_cards_on_the_screen_in_option_order()
    {
        var presenter = Build(
            RecordingGameHost.Finding(AnyPlayer(), WithADraftOpen()),
            BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(PerkDraftStage.Ready);
        presenter.CardsAvailable.ShouldBeTrue();
        presenter.Cards.Count.ShouldBe(3);
        presenter.Cards.Select(card => card.OptionIndex).ShouldBe([0, 1, 2]);
        presenter.Cards.ShouldAllBe(card => card.PerkId.Length > 0);
    }

    /// <summary>
    /// The reroll's price and the skip's reward are the authored numbers, reached through the
    /// projection rather than transcribed here.
    /// </summary>
    /// <remarks>
    /// 🔒 Not pinned to a literal. Both are tunables — one of them flagged in its own authored
    /// document as a placeholder for the balance team — so a case asserting 60 would go red on a
    /// balance pass that changed nothing about this screen. What is pinned is that the screen has
    /// them at all and that they came from somewhere.
    /// </remarks>
    [Fact]
    public async Task The_reroll_price_and_the_skip_reward_are_read_rather_than_transcribed()
    {
        var presenter = Build(
            RecordingGameHost.Finding(AnyPlayer(), WithADraftOpen(gold: 500)),
            BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        presenter.RerollGoldCost.ShouldBeGreaterThan(
            0L,
            "the reroll button shows a Gold price, and the price is authored. Zero here means the " +
            "screen is drawing a free reroll — which is exactly the economy the design describes " +
            "and the code does not have.");
        presenter.SkipGoldReward.ShouldBeGreaterThan(
            0L,
            "and the skip shows what it grants. It grants Gold and nothing else: the design's " +
            "'+1 free reroll' half has no counter to increment.");
        presenter.Gold.ShouldBe(
            500L,
            "and the player's own balance is what the price is read against, so the screen can say " +
            "whether it is affordable before the rules layer does.");
    }

    /// <summary>
    /// 🔒 A content set the projection cannot answer is a sentence, not a crash — and not silence
    /// either.
    /// </summary>
    [Fact]
    public async Task A_draft_whose_cards_cannot_be_projected_says_so_rather_than_drawing_none()
    {
        // The strings-only fixture carries no perk catalogue at all, which is the shape a content
        // set stripped of a document the projection depends on actually has.
        var presenter = Build(RecordingGameHost.Finding(AnyPlayer(), WithADraftOpen()));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(
            PerkDraftStage.Ready,
            "the run really does have a draft open — the failure is the content set's, not the run's.");
        presenter.CardsAvailable.ShouldBeFalse();
        presenter.Cards.ShouldBeEmpty();
        presenter.StatusText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.DraftCardsUnavailableStatusKey),
            "three blank cards and no explanation is the failure mode this screen most has to " +
            "avoid: it looks exactly like a draft the player is expected to choose from.");
    }

    // ---- the badge and the effect line ----------------------------------------------------------

    /// <summary>
    /// The tier badge is composed here, from a translated word and an untranslated numeral.
    /// </summary>
    /// <remarks>
    /// 🔒 Composed on this side of the boundary on purpose. A badge reading "UPGRADE →III" built
    /// beside the projection would be a user-facing English literal produced outside the loc system;
    /// the numeral is the same mark in every language and the word in front of it is not.
    /// </remarks>
    [Theory]
    [InlineData(1, "I")]
    [InlineData(2, "II")]
    [InlineData(3, "III")]
    public void A_fresh_cards_badge_is_the_tier_numeral_alone(int tier, string expected)
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer());

        presenter.TierBadge(Card(tier: tier, isUpgrade: false)).ShouldBe(expected);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void An_upgrades_badge_is_the_translated_word_and_then_the_numeral(int tier)
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer());
        var word = RunDecisionContent.EnglishValueOf(RunDecisionContent.DraftUpgradeBadgeKey);

        var badge = presenter.TierBadge(Card(tier: tier, isUpgrade: true));

        badge.StartsWith(word, StringComparison.Ordinal).ShouldBeTrue(
            "the upgrade badge's wording is a translated string and has to come from the locale " +
            $"table. A badge that did not start with it was composed from a literal: '{badge}'");
        badge.EndsWith(tier == 2 ? "II" : "III", StringComparison.Ordinal).ShouldBeTrue(
            "and the tier it lands on is the numeral at the end, so a player can read the badge as " +
            $"'you will have this many' rather than 'this is an upgrade of something': '{badge}'");
    }

    /// <summary>
    /// 🔒 A card whose numbers could not be resolved shows the named line, never a half-substituted
    /// sentence.
    /// </summary>
    /// <remarks>
    /// 🔴 Four of the shipped perks are in this state, each because their authored description names
    /// a token that matches no field in their own effect data. A renderer that filled the hole with
    /// a plausible number would state a magnitude the game does not have, on a card the player is
    /// about to commit a run to.
    /// </remarks>
    [Fact]
    public void A_card_whose_numbers_cannot_be_rendered_shows_the_named_line_instead()
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer());

        var line = presenter.EffectLine(Card(effectText: null, unresolved: ["{value2}"]));

        line.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.DraftEffectNumbersUnavailableStatusKey),
            "the sentence could not be completed, so the card says so. Anything else here is either " +
            "a template with a token still visible in it or a number this build invented.");
    }

    // ---- the synergy hint -------------------------------------------------------------------------

    /// <summary>
    /// 🔒 <b>A synergy hint names the owned perk, never its id.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 The projection carries <c>PK_*</c> ids, because that is what a run stores and what a log
    /// needs. A card that drew them raw would read "Works with PK_APEX" — a database row shown to
    /// somebody playing a game. Naming one is a catalogue lookup against the loaded content set,
    /// which this side of the boundary holds and a scene does not, so the resolution has to happen
    /// here or not at all.
    /// <para>
    /// 🔒 Stated over the SHIPPED catalogue and with the id and the name proven different, so the
    /// case cannot pass by the two happening to be the same string.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_synergy_hint_names_the_owned_perk_rather_than_carrying_its_id()
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer(), BootContent.Shipped);
        var owned = PerkCatalogue.Read(BootContent.Shipped).All[0];

        owned.Name.ShouldNotBe(
            owned.Id,
            "the shipped catalogue authors this perk's name and its id identically, so this case " +
            "could not tell a hint that resolved the name from one that printed the id. Pick a row " +
            "whose two differ rather than deleting the assertion.");

        var hint = presenter.SynergyLine(Card(synergy: [owned.Id]));

        hint.ShouldBe(
            owned.Name,
            "the hint is what a player reads, so it carries the perk's authored name. Anything else " +
            "here is the run's storage key on a card.");
        hint.ShouldNotContain(
            owned.Id,
            Case.Sensitive,
            "and the id is not in it anywhere — not appended, not in brackets after the name.");
    }

    /// <summary>Several owned perks are all named, in the order the projection lists them.</summary>
    [Fact]
    public void A_hint_naming_several_owned_perks_names_every_one_of_them()
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer(), BootContent.Shipped);
        var catalogue = PerkCatalogue.Read(BootContent.Shipped);
        var owned = catalogue.All.Take(2).ToArray();

        var hint = presenter.SynergyLine(Card(synergy: [owned[0].Id, owned[1].Id]));

        hint.ShouldContain(owned[0].Name, Case.Sensitive);
        hint.ShouldContain(owned[1].Name, Case.Sensitive);
        hint.ShouldNotContain(
            "PK_",
            Case.Sensitive,
            "one of the two was left as an id, so a hint drops to raw storage keys as soon as it " +
            $"has more than one perk in it: '{hint}'");
    }

    /// <summary>A card interacting with nothing has no hint at all, rather than an empty label.</summary>
    [Fact]
    public void A_card_with_no_synergy_has_no_hint()
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer(), BootContent.Shipped);

        presenter.SynergyLine(Card()).ShouldBeEmpty(
            "the scene shows the hint row exactly when this line has something in it, so a card " +
            "with no interaction has to answer with nothing rather than with a separator.");
    }

    // ---- how a number is written ------------------------------------------------------------------

    /// <summary>
    /// 🔒 <b>Every number this screen shows goes through the one number rule.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 Stated against <c>PlayerNumber</c> rather than against a literal, so the case is about the
    /// screen OBEYING the rule and not a second transcription of it — a screen that grew its own
    /// slightly different shortening would still satisfy a literal.
    /// </remarks>
    [Theory]
    [InlineData(9_999L)]
    [InlineData(10_000L)]
    [InlineData(10_001L)]
    [InlineData(12_400L)]
    [InlineData(3_100_000L)]
    public async Task The_three_numbers_on_this_screen_are_written_the_way_a_player_reads_them(long gold)
    {
        var presenter = Build(
            RecordingGameHost.Finding(AnyPlayer(), WithADraftOpen(gold: gold)),
            BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        presenter.GoldText.ShouldBe(PlayerNumber.Abbreviated(presenter.Gold));
        presenter.RerollGoldCostText.ShouldBe(PlayerNumber.Abbreviated(presenter.RerollGoldCost));
        presenter.SkipGoldRewardText.ShouldBe(PlayerNumber.Abbreviated(presenter.SkipGoldReward));
    }

    /// <summary>
    /// 🔒 And the exact value comes back while the readout is held, which is the half of the rule a
    /// shortening alone would lose.
    /// </summary>
    /// <remarks>
    /// 🔴 The Gold here is deliberately past the boundary, so the two forms genuinely differ. Below
    /// it they are the same string and a case stated over a small balance would pass over a screen
    /// with no reveal in it at all.
    /// </remarks>
    [Fact]
    public async Task Holding_a_number_shows_its_exact_value_and_letting_go_shortens_it_again()
    {
        var presenter = Build(
            RecordingGameHost.Finding(AnyPlayer(), WithADraftOpen(gold: 12_400)),
            BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        presenter.FullValuesRevealed.ShouldBeFalse("nothing is being held yet.");
        presenter.GoldText.ShouldBe("12.4k");

        presenter.RevealFullValues();

        presenter.FullValuesRevealed.ShouldBeTrue();
        presenter.GoldText.ShouldBe(
            "12400",
            "a long press returns the exact value, and a screen that shortened a number with no way " +
            "back to it has rounded a balance the player is about to spend.");

        presenter.ConcealFullValues();

        presenter.GoldText.ShouldBe("12.4k", "and letting go puts the shortened form back.");
    }

    [Fact]
    public void A_card_that_rendered_shows_its_own_sentence()
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer());

        presenter.EffectLine(Card(effectText: "Deals 12 extra damage.", unresolved: [])).ShouldBe(
            "Deals 12 extra damage.",
            "a rendered sentence is the perk's own authored description with its real numbers in " +
            "it, and this screen passes it through rather than re-wording it.");
    }

    // ---- picking, rerolling, skipping -----------------------------------------------------------

    [Fact]
    public async Task Picking_a_card_submits_PICK_PERK_carrying_that_cards_option_index()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), WithADraftOpen())
            .AcceptingInto(PlayerState.Run(Run, Player, RunPhase.InProgress));

        var presenter = Build(host, BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.PickAsync(2, CancellationToken.None);

        submission.ShouldBe(PerkDraftSubmission.Submitted);
        host.SubmitCommand.ShouldBeOfType<PickPerkCommand>().OptionIndex.ShouldBe(2);
        host.SubmitRun.ShouldBe(Run);
    }

    /// <summary>
    /// 🔒 A card that is not on offer costs no round trip. Submitted anyway it comes back with a
    /// value four other things share, and the screen would have to guess which.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public async Task Picking_a_card_that_is_not_on_offer_submits_nothing_at_all(int optionIndex)
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), WithADraftOpen());
        var presenter = Build(host, BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.PickAsync(optionIndex, CancellationToken.None);

        submission.ShouldBe(PerkDraftSubmission.RefusedNotAvailable);
        host.SubmitCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Rerolling_submits_REROLL_DRAFT_against_this_run()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), WithADraftOpen())
            .AcceptingInto(WithADraftOpen());

        var presenter = Build(host, BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.RerollAsync(CancellationToken.None);

        submission.ShouldBe(PerkDraftSubmission.Submitted);
        host.SubmitCommand.ShouldBeOfType<RerollDraftCommand>();
    }

    [Fact]
    public async Task Skipping_submits_SKIP_DRAFT_against_this_run()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), WithADraftOpen())
            .AcceptingInto(PlayerState.Run(Run, Player, RunPhase.InProgress));

        var presenter = Build(host, BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.SkipAsync(CancellationToken.None);

        submission.ShouldBe(PerkDraftSubmission.Submitted);
        host.SubmitCommand.ShouldBeOfType<SkipDraftCommand>();
    }

    /// <summary>
    /// 🔒 With no draft open there is nothing to pick, reroll or skip, and none of the three costs a
    /// round trip to discover that.
    /// </summary>
    [Fact]
    public async Task With_no_draft_open_none_of_the_three_actions_reaches_the_host()
    {
        var host = RecordingGameHost.Finding(
            AnyPlayer(), PlayerState.Run(Run, Player, RunPhase.InProgress));

        var presenter = Build(host, BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        (await presenter.PickAsync(0, CancellationToken.None))
            .ShouldBe(PerkDraftSubmission.RefusedNotAvailable);
        (await presenter.RerollAsync(CancellationToken.None))
            .ShouldBe(PerkDraftSubmission.RefusedNotAvailable);
        (await presenter.SkipAsync(CancellationToken.None))
            .ShouldBe(PerkDraftSubmission.RefusedNotAvailable);

        host.SubmitCallCount.ShouldBe(0);
    }

    // ---- S2: the refusals told apart ------------------------------------------------------------

    /// <summary>
    /// 🔒 <b>A reroll the player cannot afford does not read like one the rules simply forbid.</b>
    /// One is a price they can go and earn; the other is a state they cannot influence at all.
    /// </summary>
    [Theory]
    [InlineData(RejectionReason.INSUFFICIENT_FUNDS, RunDecisionContent.DraftRerollUnaffordableStatusKey)]
    [InlineData(RejectionReason.ILLEGAL_STATE, RunDecisionContent.DraftRefusedStatusKey)]
    public async Task A_refused_reroll_is_told_apart_by_the_reason_that_refused_it(
        RejectionReason rejection, string expectedSentenceKey)
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), WithADraftOpen())
            .RefusingCommands(rejection);

        var presenter = Build(host, BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.RerollAsync(CancellationToken.None);

        submission.ShouldBe(PerkDraftSubmission.RefusedByRules);
        presenter.RulesRejection.ShouldBe(rejection);
        presenter.RejectionText.ShouldBe(RunDecisionContent.EnglishValueOf(expectedSentenceKey));
    }

    /// <summary>
    /// 🔒 <b>A faulting host is not silence, and it does not read as a rules refusal.</b> Told the
    /// wrong one, a player retries a press the rules would have allowed all along.
    /// </summary>
    [Fact]
    public async Task A_host_that_faults_on_a_pick_is_told_apart_from_a_refusal()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), WithADraftOpen())
            .FaultingItsCommands(new TimeoutException("the submission never completed"));

        var presenter = Build(host, BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.PickAsync(0, CancellationToken.None);

        submission.ShouldBe(PerkDraftSubmission.HostUnavailable);
        presenter.HostFaulted.ShouldBeTrue();
        presenter.RulesRejection.ShouldBeNull(
            "a faulted call carried no outcome, so there is no rejection to report — and reporting " +
            "one would be inventing an answer the game never gave.");
        presenter.RejectionText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.DraftHostUnavailableStatusKey));
    }

    /// <summary>
    /// 🔒 <b>The shortfall sentence is paired with the reroll, not printed under whatever refusal
    /// happens to carry the same reason.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 Today the reroll is the only thing this screen submits that spends anything, so an unpaired
    /// mapping is correct — and would stop being correct silently, the day a second spender lands
    /// here, with no case going red. This is that case. A pick costs nothing, so a shortfall reported
    /// under one did not come from the reroll's price, and telling the player to go and earn Gold
    /// answers a question they never asked.
    /// </remarks>
    [Fact]
    public async Task A_shortfall_reported_under_a_pick_does_not_read_as_the_rerolls_price()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), WithADraftOpen())
            .RefusingCommands(RejectionReason.INSUFFICIENT_FUNDS);

        var presenter = Build(host, BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.PickAsync(0, CancellationToken.None);

        submission.ShouldBe(PerkDraftSubmission.RefusedByRules);
        presenter.RejectionText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.DraftRefusedStatusKey),
            "PICK_PERK spends nothing, so the sentence about a reroll the player cannot afford is " +
            "an answer about a control they did not touch. The mapping is paired with the command " +
            "that could have caused it, not with the reason alone.");
    }

    /// <summary>
    /// 🔒 <b>A fault does not outlive the submission that faulted.</b> The next command that actually
    /// answers is what the player is told about.
    /// </summary>
    /// <remarks>
    /// The board's funnel clears its latched sentence BEFORE the command rather than after it, for
    /// exactly this reason: cleared afterwards it survives every path that returns early — which is
    /// every refusal — and the old line is printed under the new answer. Here that tells a player the
    /// game never answered, about a reroll it answered by refusing.
    /// </remarks>
    [Fact]
    public async Task A_fault_does_not_survive_into_the_next_answer()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), WithADraftOpen())
            .FaultingItsCommands(new TimeoutException("the submission never completed"), times: 1)
            .RefusingCommands(RejectionReason.ILLEGAL_STATE);

        var presenter = Build(host, BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        (await presenter.RerollAsync(CancellationToken.None))
            .ShouldBe(PerkDraftSubmission.HostUnavailable);
        (await presenter.RerollAsync(CancellationToken.None))
            .ShouldBe(
                PerkDraftSubmission.RefusedByRules,
                "the reroll is a control a player presses repeatedly, so a screen that stops " +
                "submitting after one faulted attempt has stranded them on the draft.");

        presenter.HostFaulted.ShouldBeFalse("the second submission completed, so nothing faulted.");
        presenter.RejectionText.ShouldBe(
            RunDecisionContent.EnglishValueOf(RunDecisionContent.DraftRefusedStatusKey),
            "the fault's sentence was printed under a refusal that did answer. The flag is settled " +
            "before the command goes out, not only when one fails.");
    }

    /// <summary>
    /// 🔒 <b>No double submit.</b> The latch is taken before the await, not after it: taken
    /// afterwards, a second press arriving while the first is in flight finds it unset and takes a
    /// second perk. That exact shape shipped once already in this milestone.
    /// </summary>
    [Fact]
    public async Task A_second_pick_while_one_is_in_flight_never_reaches_the_host_twice()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), WithADraftOpen())
            .AcceptingInto(PlayerState.Run(Run, Player, RunPhase.InProgress));

        var presenter = Build(host, BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        var first = presenter.PickAsync(0, CancellationToken.None);
        var second = presenter.PickAsync(1, CancellationToken.None);

        await Task.WhenAll(first, second);

        host.SubmitCallCount.ShouldBe(
            1,
            "both presses reached the host, so a double-tap on two different cards took two perks " +
            "and spent two draft slots. The second call is refused by the screen's own latch, and " +
            "the latch is taken before the await rather than after it.");
    }

    // ---- the vocabulary --------------------------------------------------------------------------

    /// <summary>
    /// 🔒 Neither of this screen's enums has a zero member, so a default-initialised field can never
    /// read as a real state.
    /// </summary>
    [Theory]
    [InlineData(typeof(PerkDraftStage))]
    [InlineData(typeof(PerkDraftSubmission))]
    public void No_state_this_screen_reports_is_the_default_value_of_its_own_type(Type vocabulary)
    {
        Enum.IsDefined(vocabulary, 0).ShouldBeFalse(
            $"{vocabulary.Name} has a member valued zero, so an uninitialised field of that type " +
            "reads as that member rather than as an obviously wrong value. Every member takes an " +
            "explicit value starting at one.");
    }

    // ---- construction ----------------------------------------------------------------------------

    [Fact]
    public void Every_reference_collaborator_is_required()
    {
        var content = RunDecisionContent.Strings();

        Should.Throw<ArgumentNullException>(() =>
            new PerkDraftPresenter(null!, RunDecisionContent.Catalogue(content), content, Player, Run));
        Should.Throw<ArgumentNullException>(() =>
            new PerkDraftPresenter(
                RecordingGameHost.FindingNoSuchPlayer(), null!, content, Player, Run));
        Should.Throw<ArgumentNullException>(() =>
            new PerkDraftPresenter(
                RecordingGameHost.FindingNoSuchPlayer(),
                RunDecisionContent.Catalogue(content),
                null!,
                Player,
                Run));
    }

    // ---- fixture ---------------------------------------------------------------------------------

    private static PlayerSnapshot AnyPlayer() => PlayerState.Player(Player);

    /// <summary>A run with a draft open, which is the only state this screen has anything to draw in.</summary>
    /// <remarks>
    /// 🔒 The battle that opened the draft is named, and it has to be. <c>Run.Rehydrate</c> refuses a
    /// row whose draft is open while its battle kind and stage stand at their no-draft values, and
    /// the rules layer's draft derivation is keyed on that stage — so a row left at the defaults
    /// describes a state the game could never have persisted, and cards projected from one would be
    /// projected from a run that cannot occur.
    /// </remarks>
    // ------------------------------------------------------------------------------------------
    // 🔒 24 §1.1 — the three DRAFT counters reach the screen, always and correctly.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// All three counter rows are offered, whatever they stand at. <c>24</c> §1.1's Visibility rule is
    /// <em>always</em>, so a run that has drafted nothing still discloses its ladder.
    /// </summary>
    [Fact]
    public async Task All_three_guarantee_rows_are_offered_on_a_fresh_run()
    {
        var presenter = Build(RecordingGameHost.Finding(AnyPlayer(), WithADraftOpen()), BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Guarantees.Count.ShouldBe(
            3,
            "24 §1.1's Visibility rule is a 🔒 and reads 'always'. A row dropped because its counter " +
            "stands at zero is the exact hidden-pity state that rule forbids.");
    }

    /// <summary>…each with a resolved caption rather than the loc key itself.</summary>
    [Fact]
    public async Task Every_guarantee_row_carries_a_resolved_caption()
    {
        var presenter = Build(RecordingGameHost.Finding(AnyPlayer(), WithADraftOpen()), BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Guarantees.ShouldAllBe(g => g.Label.Length > 0);
        presenter.Guarantees.ShouldAllBe(g => !g.Label.StartsWith("loc.", StringComparison.Ordinal));
    }

    /// <summary>…and three DIFFERENT captions, so no row borrows another's sentence.</summary>
    [Fact]
    public async Task The_three_captions_are_three_different_sentences()
    {
        var presenter = Build(RecordingGameHost.Finding(AnyPlayer(), WithADraftOpen()), BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Guarantees
            .Select(g => g.Label)
            .Distinct(StringComparer.Ordinal)
            .Count()
            .ShouldBe(
                3,
                "one caption serving two counters tells a player the game protects them once where " +
                "it protects them three times");
    }

    /// <summary>
    /// 🔒 The value carries the live countdown AND the authored rung, because <c>24</c> §1.1 asks
    /// for both — Visibility wants a real number now, Disclosure wants the <c>N</c> stated.
    /// </summary>
    [Fact]
    public async Task A_due_row_shows_the_countdown_over_its_authored_rung()
    {
        var presenter = Build(RecordingGameHost.Finding(
            AnyPlayer(),
            WithADraftOpen() with { DraftsSinceLegendaryOffered = 7 }), BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Guarantees[0].Value.ShouldBe(
            "8/15",
            "the run has stood 7 of a 15-draft ladder, so 8 remain and the rung is still stated");
    }

    /// <summary>…and the number moves with the counter rather than being a fixed caption.</summary>
    [Fact]
    public async Task The_countdown_moves_as_the_counter_stands_higher()
    {
        var host = RecordingGameHost.Finding(
            AnyPlayer(), WithADraftOpen() with { DraftsSinceLegendaryOffered = 14 });
        var presenter = Build(host, BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Guarantees[0].Value.ShouldBe("1/15", "the next draft is the forced one");
    }

    /// <summary>
    /// 🔒 A row whose guarantee is not due shows its rung and NO countdown, and the screen carries
    /// the reason in its own sentence — the fourth absence on this screen, kept distinct from the three
    /// the reroll already has.
    /// </summary>
    [Fact]
    public async Task A_row_that_is_not_due_shows_its_rung_without_a_countdown()
    {
        var presenter = Build(RecordingGameHost.Finding(AnyPlayer(), WithADraftOpen()), BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        var famine = presenter.Guarantees[2];

        famine.Live.ShouldBeFalse("a run owning nothing has no upgrade to be starved of");
        famine.Value.ShouldBe(
            "6",
            "the rung is disclosed whatever the run is doing, but a countdown would promise a " +
            "forced upgrade that cannot arrive");
    }

    /// <summary>…and that state, not the other three absences, is what the fourth sentence names.</summary>
    [Fact]
    public async Task The_not_due_sentence_appears_only_while_a_row_is_not_due()
    {
        var idle = Build(RecordingGameHost.Finding(AnyPlayer(), WithADraftOpen()), BootContent.Shipped);

        await idle.StartAsync(CancellationToken.None);

        idle.GuaranteeNotDueBlockText.ShouldNotBeEmpty();

        var owning = Build(RecordingGameHost.Finding(
            AnyPlayer(), WithADraftOpen() with { OwnedPerkTiers = OneUpgradablePerk }), BootContent.Shipped);

        await owning.StartAsync(CancellationToken.None);

        owning.Guarantees.ShouldAllBe(g => g.Live);
        owning.GuaranteeNotDueBlockText.ShouldBeEmpty(
            "with every guarantee due there is nothing to explain, and a sentence left standing " +
            "would describe a state the run is not in");
    }

    /// <summary>
    /// 🔒 And the fourth sentence never reuses the wording of the three the reroll carries. Steering
    /// S2: a player who reads "waiting on a later milestone" beside a guarantee that is built and live
    /// is misinformed in the direction this screen has already been careful about three times.
    /// </summary>
    [Fact]
    public async Task The_not_due_sentence_is_none_of_the_three_absences_around_the_reroll()
    {
        var presenter = Build(RecordingGameHost.Finding(AnyPlayer(), WithADraftOpen()), BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        var others = new[]
        {
            presenter.AdRerollBlockText,
            presenter.AdFourthOptionBlockText,
            presenter.FreeRerollBlockText,
        };

        others.ShouldNotContain(presenter.GuaranteeNotDueBlockText);
    }

    /// <summary>…and a run with no draft open discloses nothing, because there is no draft to disclose.</summary>
    [Fact]
    public async Task A_run_with_no_draft_open_offers_no_guarantee_rows()
    {
        var presenter = Build(RecordingGameHost.Finding(
            AnyPlayer(), WithADraftOpen() with { DraftPending = false }), BootContent.Shipped);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Guarantees.ShouldBeEmpty();
        presenter.GuaranteeNotDueBlockText.ShouldBeEmpty();
    }

    private static RunSnapshot WithADraftOpen(long gold = 0) =>
        PlayerState.Run(
            Run, Player, RunPhase.InProgress,
            gold: gold,
            draftPending: true,
            draftBattleKind: EnemyBattleTileKind,
            draftBattleStage: FirstStage);

    /// <summary>
    /// One owned perk sitting below its top tier, which is what makes the upgrade famine due.
    /// </summary>
    /// <remarks>
    /// Read off the fixture catalogue rather than named as a literal, so a renamed fixture perk fails
    /// the arrangement instead of quietly turning the famine's live case into its not-due one.
    /// </remarks>
    private static IReadOnlyDictionary<string, int> OneUpgradablePerk { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [PerkCatalogue.Read(BootContent.Shipped).All.First(e => e.TierCount > 1).Id] = 1,
        };

    /// <summary>One card, built directly, for the cases about how a card is WORDED rather than drawn.</summary>
    private static PerkDraftCard Card(
        int tier = 1,
        bool isUpgrade = false,
        string? effectText = "Fixture effect.",
        IReadOnlyList<string>? unresolved = null,
        IReadOnlyList<string>? synergy = null) =>
        new(
            OptionIndex: 0,
            PerkId: "PK_FIXTURE",
            Name: "Fixture Perk",
            Category: PerkCategory.Offense,
            Rarity: PerkRarity.Common,
            IconId: "ICON_FIXTURE",
            IsUpgrade: isUpgrade,
            NewTier: tier,
            EffectText: effectText,
            UnresolvedTokens: unresolved ?? [],
            SynergyPerkIds: synergy ?? []);

    private static PerkDraftPresenter Build(RecordingGameHost host, ContentSnapshot? content = null)
    {
        var strings = RunDecisionContent.Strings();

        return new PerkDraftPresenter(
            host, RunDecisionContent.Catalogue(strings), content ?? strings, Player, Run);
    }
}
