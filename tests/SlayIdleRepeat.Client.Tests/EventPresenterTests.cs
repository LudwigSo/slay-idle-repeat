using Shouldly;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// `19` Part A / `03` §2 — the Event screen: the tile draws a card, the card offers two or three
/// options, and one of them is spent.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>The screen owns its draw.</b> Landing on an Event tile leaves the run with an Event tile
/// and no card, and this screen is what submits <c>RESOLVE_TILE</c> to get one. So the two facts
/// that matter most are that a cold start draws EXACTLY once and that a resume draws not at all —
/// a second draw replaces the card the player was reading, and neither is visible in the state the
/// screen settles on.
/// </para>
/// <para>
/// 🔴 <b>A refused command comes back with an EMPTY state slice.</b> The host answers a rejection
/// with the player's own row and no run, so a screen that read its card and options back off a
/// refused outcome would blank the screen the player is still looking at. The pre-submit snapshot
/// is what these cases hold it to.
/// </para>
/// <para>
/// 🔴 <b>Most shipped outcomes are <c>UNSUPPORTED</c>.</b> "Nothing happened" is the common result
/// on this screen rather than an edge case, so it has a sentence of its own and a case of its own.
/// </para>
/// <para>
/// ⚠️ Card prose — title, body and option labels — is authored English rather than loc keys, so
/// exactly one case here asserts a string that did NOT come out of the catalogue. It says so.
/// </para>
/// </remarks>
public sealed class EventPresenterTests
{
    /// <summary>An ordinary combat tile — the negative control for "this is not an event".</summary>
    /// <remarks>
    /// 🔒 Read off the rules layer's enum for the same reason <c>EventPresenter.EventTileKind</c> is:
    /// a literal 0 here keeps pointing at whatever kind moves into that slot, and the day it becomes
    /// Event this case would be standing the run on the very tile it claims to be standing off.
    /// </remarks>
    private const int EnemyTileKind = (int)TileKind.Enemy;

    /// <summary>The index the third option of the three-option fixture card travels as.</summary>
    private const int ThirdOptionIndex = 2;

    /// <summary>An index past the last option of the three-option fixture card.</summary>
    private const int IndexPastTheLastOption = 3;

    private static readonly PlayerId Player = new("PLAYER_event_7a31");
    private static readonly RunId Run = new("RUN_event_1c8e");

    // ---- the numbering ----------------------------------------------------------------------------

    /// <summary>
    /// 🔒 The tile kind this screen opens on is read off the rules layer's own enum, and this holds
    /// that reading against the table the client transcribes the whole enum into.
    /// </summary>
    [Fact]
    public void The_tile_kind_this_screen_opens_on_is_the_one_the_shared_table_names()
    {
        BoardTileKinds.NameKeyFor(EventPresenter.EventTileKind).ShouldBe(
            "loc.tile.event.name",
            "this screen reads one tile kind off the rules layer's enum and the shared table " +
            "transcribes that same numbering, so this is the one place the two can be held against " +
            "each other. If this is red a kind was inserted or reordered, and either the table is " +
            "stale or the screen now opens on the wrong tile — which means drawing a card on a tile " +
            "that has none.");
    }

    // ---- the read -------------------------------------------------------------------------------

    [Fact]
    public void A_freshly_built_presenter_has_not_read_anything()
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer());

        presenter.Stage.ShouldBe(EventStage.NotYetRead);
        presenter.Options.ShouldBeEmpty();
        presenter.CardId.ShouldBeEmpty();
        presenter.StatusText.ShouldBe(EventContent.EnglishValueOf(EventContent.LoadingStatusKey));
    }

    /// <summary>
    /// 🔒 Building the screen draws nothing. The draw is a real command that spends a card, so a
    /// screen that submitted on construction would burn one before the player ever saw the screen.
    /// </summary>
    [Fact]
    public void Building_the_screen_submits_nothing_and_reads_nothing()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), AtAnUndrawnEvent());

        _ = Build(host);

        host.SubmitCallCount.ShouldBe(0);
        host.ReadCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task The_read_is_addressed_to_this_run_and_not_to_the_player_alone()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), AtAnEvent(EventContent.FreeCard));
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        host.ReadRun.ShouldBe(Run);
        host.ReadPlayer.ShouldBe(Player);
    }

    /// <summary>🔒 A resume reads the run once and draws nothing at all.</summary>
    [Fact]
    public async Task A_resume_with_a_card_already_drawn_reads_once_and_submits_nothing()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), AtAnEvent(EventContent.FreeCard));
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        host.ReadCallCount.ShouldBe(
            1,
            "opening this screen cost more than one read of the same run. The card, its options and " +
            "the balances they are priced against all come out of one answer.");
        host.SubmitCallCount.ShouldBe(
            0,
            "🔴 the screen drew again on a tile that already had a card. DecisionFor routes a " +
            "re-entered Event tile back here, so this is the ordinary resume path — and a second " +
            "RESOLVE_TILE would replace the card the player was in the middle of reading.");
        presenter.Stage.ShouldBe(EventStage.Choosing);
        presenter.CardId.ShouldBe(EventContent.FreeCard);
    }

    [Fact]
    public async Task A_read_that_finds_no_run_says_so()
    {
        var presenter = Build(RecordingGameHost.Reading(
            new OwnStateResult(OwnStateLookup.NoSuchRun, View: null)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(EventStage.RunMissing);
        presenter.StatusText.ShouldBe(EventContent.EnglishValueOf(EventContent.RunMissingStatusKey));
    }

    [Fact]
    public async Task A_read_that_faults_is_a_state_and_not_an_escape()
    {
        var presenter = Build(RecordingGameHost.FaultingItsRead(new TimeoutException("no answer")));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(EventStage.ReadUnavailable);
        presenter.StatusText.ShouldBe(
            EventContent.EnglishValueOf(EventContent.ReadUnavailableStatusKey));
    }

    /// <summary>
    /// 🔒 A run standing on some other tile is NAMED, and nothing is drawn on it.
    /// </summary>
    /// <remarks>
    /// The draw is unconditional on the tile being an Event: <c>RESOLVE_TILE</c> on an Enemy tile is
    /// ACCEPTED and opens a fight, so a screen that submitted here because it was open would start a
    /// battle nobody asked for.
    /// </remarks>
    [Fact]
    public async Task A_run_standing_on_another_tile_is_named_rather_than_drawn_on()
    {
        var host = RecordingGameHost.Finding(
            AnyPlayer(),
            PlayerState.Run(
                Run, Player, RunPhase.InProgress,
                pendingTileKind: EnemyTileKind, pendingTileLinearIndex: 2, pendingTileStage: 1));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(EventStage.NotAtAnEvent);
        presenter.Options.ShouldBeEmpty();
        presenter.StatusText.ShouldBe(
            EventContent.EnglishValueOf(EventContent.NotAtAnEventStatusKey));
        host.SubmitCallCount.ShouldBe(
            0,
            "RESOLVE_TILE is ACCEPTED on an Enemy tile and opens a fight, so a draw submitted " +
            "because this screen happened to be open would start a battle the player never chose.");
    }

    /// <summary>
    /// 🔒 A drawn card the content set cannot describe is a sentence, not a blank screen and not a
    /// crash.
    /// </summary>
    [Fact]
    public async Task A_card_the_content_set_cannot_describe_says_so_rather_than_drawing_nothing()
    {
        // The strings-only fixture carries no card catalogue, which is the shape a content set
        // stripped of the document the projection depends on actually has.
        var presenter = Build(
            RecordingGameHost.Finding(AnyPlayer(), AtAnEvent(EventContent.FreeCard)),
            EventContent.Strings());

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(EventStage.CardUnavailable);
        presenter.Options.ShouldBeEmpty();
        presenter.StatusText.ShouldBe(
            EventContent.EnglishValueOf(EventContent.CardUnavailableStatusKey));
    }

    // ---- the draw -------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 <b>A cold start draws exactly once, and the card it drew is what the screen then shows.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 The count is the claim. A screen that drew twice settles on exactly the same stage with
    /// exactly the same card on it, so nothing else in this suite can see the second command — and
    /// the second command spends another card off the run's event stream.
    /// </remarks>
    [Fact]
    public async Task A_cold_start_draws_the_card_itself_exactly_once()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAnUndrawnEvent())
            .AcceptingInto(AtAnEvent(EventContent.FreeCard));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        host.SubmittedCommands.Count.ShouldBe(
            1,
            "the draw is one command. Anything else here is a card spent that the player never saw: " +
            $"[{string.Join(", ", host.SubmittedCommands.Select(c => c.GetType().Name))}]");
        host.SubmittedCommands[0].ShouldBeOfType<ResolveTileCommand>(
            "the board's Continue is not on this path — the screen draws for itself, and the draw " +
            "is RESOLVE_TILE on a pending Event tile.");
        host.SubmitRun.ShouldBe(Run);
        host.ReadCallCount.ShouldBe(
            1,
            "the drawn card came back with the draw's own outcome, so a second read is a window in " +
            "which the screen is deciding from the state it had BEFORE the command it just sent.");

        presenter.Stage.ShouldBe(EventStage.Choosing);
        presenter.CardId.ShouldBe(
            EventContent.FreeCard,
            "the card the screen shows is the one the accepted draw handed back, read off the " +
            "outcome rather than by reading the run a second time.");
    }

    /// <summary>
    /// 🔒 A draw the rules layer refuses leaves no card on the screen and says why.
    /// </summary>
    [Fact]
    public async Task A_draw_the_rules_layer_refuses_leaves_no_card_and_a_sentence()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAnUndrawnEvent())
            .RefusingCommands(RejectionReason.ILLEGAL_STATE);

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        host.SubmitCallCount.ShouldBe(1, "the draw was attempted once and refused once.");
        presenter.Stage.ShouldBe(
            EventStage.Drawing,
            "the run really is on an Event tile with no card — the failure is the refusal's, so the " +
            "screen stays in the state it is actually in rather than pretending to a card.");
        presenter.Options.ShouldBeEmpty();
        presenter.CardId.ShouldBeEmpty();
        presenter.CanLeave.ShouldBeFalse("the tile is still pending, so there is nowhere to go.");
        presenter.RulesRejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        presenter.RejectionText.ShouldBe(
            EventContent.EnglishValueOf(EventContent.RefusedStatusKey));
        presenter.StatusText.ShouldBe(
            EventContent.EnglishValueOf(EventContent.DrawingStatusKey),
            "the drawing sentence is the only thing on the screen naming the state the run is " +
            "actually in, and this is the one arm that settles in it — left unasserted, the key is " +
            "one the content set is required to carry and nothing renders.");
    }

    // ---- affordability, before the press --------------------------------------------------------

    /// <summary>
    /// 🔒 <b>The Gold boundary is <c>balance &gt;= cost</c>, at 99 and at 100.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 One either side of the line, because that is the only pair that can tell <c>&gt;=</c> from
    /// <c>&gt;</c> — and a run with exactly the price is the commonest way to meet a priced option
    /// at all, since the card is what took the Gold in the first place.
    /// </remarks>
    [Theory]
    [InlineData(99L, false)]
    [InlineData(100L, true)]
    public async Task A_gold_priced_option_is_available_from_exactly_its_price(
        long gold, bool expectedAvailable)
    {
        var presenter = Build(RecordingGameHost.Finding(
            AnyPlayer(), AtAnEvent(EventContent.PricedCard, gold: gold)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Options.Count.ShouldBe(
            2, "with no options drawn there is nothing below for this case to be about.");

        var priced = presenter.Options[0];

        priced.Available.ShouldBe(
            expectedAvailable,
            "the option costs " + EventContent.PricedCardGoldCost + " Gold and the run holds " +
            gold + ". EVENT_CHOOSE pays it when balance >= cost, so a screen using > blocks a run " +
            "that can afford it exactly and a screen using >= on the wrong side offers one that " +
            "cannot.");
        priced.CostText.ShouldContain(
            EventContent.EnglishValueOf(EventContent.CurrencyGoldNameKey),
            Case.Sensitive,
            "the price names its currency through tuning/currencies.json's own caption key, not " +
            "through a literal this screen spells.");
        priced.CostText.ShouldContain("100", Case.Sensitive, "…and it names the amount charged.");

        presenter.Options[1].Available.ShouldBeTrue(
            "the second option is authored free, so no balance can block it — and it is the one " +
            "way off this card a broke run has.");
        presenter.Options[1].CostText.ShouldBeEmpty("a free option has no price to print.");
    }

    /// <summary>…and an option the run cannot pay for carries the sentence saying so.</summary>
    [Fact]
    public async Task An_unaffordable_option_is_blocked_with_its_own_sentence_before_the_press()
    {
        var presenter = Build(RecordingGameHost.Finding(
            AnyPlayer(), AtAnEvent(EventContent.PricedCard, gold: 0)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Options[0].BlockText.ShouldBe(
            EventContent.EnglishValueOf(EventContent.UnaffordableBlockKey),
            "EVENT_CHOOSE refuses this with INSUFFICIENT_FUNDS, which is a wire value the player " +
            "never sees a reason behind — so the price has to be named on the card before the press.");
        presenter.Options[1].BlockText.ShouldBeEmpty(
            "…and an option that can be taken has nothing to explain.");
    }

    /// <summary>
    /// 🔒 <b>A non-Gold cost is checked against the PLAYER's wallet, not the run's Gold.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 The run's Gold is zero in both arms. A screen reading one balance for every currency
    /// therefore answers "unaffordable" for both, and the funded arm is what catches it — mirroring
    /// <c>EventChoose</c>, which charges Gold to the run and everything else to the profile.
    /// </remarks>
    [Theory]
    [InlineData(5L, false)]
    [InlineData(6L, true)]
    public async Task A_wallet_priced_option_is_read_off_the_wallet_and_not_off_run_gold(
        long stonesHeld, bool expectedAvailable)
    {
        var presenter = Build(RecordingGameHost.Finding(
            PlayerHolding(stonesHeld), AtAnEvent(EventContent.WalletCard, gold: 0)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Options.Count.ShouldBe(2, "with no options drawn this case measures nothing.");
        presenter.Options[0].Available.ShouldBe(
            expectedAvailable,
            "the option costs " + EventContent.WalletCardStoneCost + " Enhance Stones and the " +
            "profile holds " + stonesHeld + ", while the run's Gold is zero. A screen pricing this " +
            "against Gold blocks it whatever the wallet says.");
        presenter.Options[0].CostText.ShouldContain(
            EventContent.EnglishValueOf(EventContent.CurrencyEnhanceStonesNameKey),
            Case.Sensitive,
            "…and the caption is the wallet currency's own, so the player can see WHICH purse pays.");
    }

    // ---- the choice -----------------------------------------------------------------------------

    /// <summary>
    /// 🔒 An option this screen draws as unavailable never reaches the host.
    /// </summary>
    /// <remarks>
    /// The rules layer would refuse it too, with <c>INSUFFICIENT_FUNDS</c> — but a round trip would
    /// replace the sentence naming the price with the one that covers every refusal, and would cost
    /// a press that could never work.
    /// </remarks>
    [Fact]
    public async Task An_option_drawn_as_unaffordable_never_reaches_the_host()
    {
        var host = RecordingGameHost.Finding(
            AnyPlayer(), AtAnEvent(EventContent.PricedCard, gold: 0));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.ChooseAsync(0, CancellationToken.None);

        submission.ShouldBe(EventSubmission.RefusedNotAvailable);
        host.SubmitCallCount.ShouldBe(0);
    }

    /// <summary>
    /// 🔒 An index that names no option on the drawn card is turned away rather than sent.
    /// </summary>
    /// <remarks>
    /// 🔴 The index arrives from the scene, where a row's position is bound once and the card behind
    /// it can change under it — a resume onto a shorter card, or a rebuild between the press and the
    /// handler. Two things go wrong if it is not checked here: the screen indexes its own row list
    /// and throws where a refusal was wanted, and the command reaches <c>EVENT_CHOOSE</c>, which
    /// refuses it as <c>ILLEGAL_STATE</c> — the same wire value a run standing on no event at all
    /// gets, so the sentence the player is shown stops being about anything.
    /// </remarks>
    [Theory]
    [InlineData(IndexPastTheLastOption)]
    [InlineData(-1)]
    public async Task An_index_that_names_no_option_on_the_card_never_reaches_the_host(int index)
    {
        var host = RecordingGameHost.Finding(
            AnyPlayer(), AtAnEvent(EventContent.ThreeOptionCard));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Options.Count.ShouldBe(
            3, "the fixture card authors three options, so " + index + " names none of them.");

        (await presenter.ChooseAsync(index, CancellationToken.None)).ShouldBe(
            EventSubmission.RefusedNotAvailable);
        host.SubmitCallCount.ShouldBe(
            0,
            "the command went out carrying an index the card cannot answer. EVENT_CHOOSE refuses it " +
            "as ILLEGAL_STATE, which is the value four other situations also travel as.");
        presenter.Stage.ShouldBe(
            EventStage.Choosing, "nothing was spent, so the choice is still the player's.");
    }

    /// <summary>
    /// 🔒 The command carries the index of the option that was pressed.
    /// </summary>
    /// <remarks>
    /// 🔴 Index 2 of a THREE-option card, because index 2 is the first a two-option card cannot
    /// produce: with two options, a screen that always submitted the last one and a screen that
    /// submitted the pressed one are indistinguishable, and every choice on a card is legal so
    /// nothing would refuse the wrong one.
    /// </remarks>
    [Fact]
    public async Task Choosing_the_third_option_submits_EVENT_CHOOSE_carrying_index_two()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAnEvent(EventContent.ThreeOptionCard))
            .AcceptingInto(OffTheTile());

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Options.Count.ShouldBe(
            3, "the fixture card authors three options, and index 2 has to name one of them.");

        var submission = await presenter.ChooseAsync(ThirdOptionIndex, CancellationToken.None);

        submission.ShouldBe(EventSubmission.Submitted);
        host.SubmitCommand.ShouldBeOfType<EventChooseCommand>()
            .ChoiceIndex.ShouldBe(ThirdOptionIndex);
        host.SubmitRun.ShouldBe(Run);
    }

    /// <summary>
    /// 🔒 <b>The draw and the choice are two commands, in that order, off one screen.</b>
    /// </summary>
    /// <remarks>
    /// The cold-start path end to end. Each command's answer is a different run — drawn, then
    /// cleared — so a screen that decided the second from the state it started with rather than from
    /// what the first handed back cannot satisfy this.
    /// </remarks>
    [Fact]
    public async Task A_cold_start_draws_and_then_the_choice_spends_the_card_it_drew()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAnUndrawnEvent())
            .AcceptingInto(AtAnEvent(EventContent.ThreeOptionCard))
            .ThenAcceptingInto(OffTheTile());

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        presenter.CardId.ShouldBe(EventContent.ThreeOptionCard);

        (await presenter.ChooseAsync(1, CancellationToken.None))
            .ShouldBe(EventSubmission.Submitted);

        host.SubmittedCommands.Count.ShouldBe(2);
        host.SubmittedCommands[0].ShouldBeOfType<ResolveTileCommand>();
        host.SubmittedCommands[1].ShouldBeOfType<EventChooseCommand>().ChoiceIndex.ShouldBe(1);

        presenter.Stage.ShouldBe(EventStage.Resolved);
        presenter.CanLeave.ShouldBeTrue();
    }

    // ---- S24: the tile is cleared, or the player stays -------------------------------------------

    /// <summary>
    /// 🔒 An acceptance that lands with no pending tile resolves the screen and lets the player out.
    /// </summary>
    [Fact]
    public async Task An_acceptance_that_clears_the_tile_resolves_and_lets_the_player_leave()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAnEvent(EventContent.FreeCard))
            .AcceptingInto(OffTheTile(gold: 300));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);
        await presenter.ChooseAsync(0, CancellationToken.None);

        presenter.Stage.ShouldBe(EventStage.Resolved);
        presenter.CanLeave.ShouldBeTrue(
            "EVENT_CHOOSE clears the pending tile as its last step, and the run came back with " +
            "none — so Continue may hand back to the board.");
    }

    /// <summary>
    /// 🔒 …and an acceptance that leaves the tile PENDING gives neither.
    /// </summary>
    /// <remarks>
    /// 🔴 The negative control, and the one that keeps the board out of a loop: the decision latch
    /// logs "halted" when the same decision re-opens with the tile unchanged, so a screen that read
    /// "accepted" as "done" would hand back and be sent straight here again.
    /// </remarks>
    [Fact]
    public async Task An_acceptance_that_leaves_the_tile_pending_neither_resolves_nor_lets_the_player_leave()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAnEvent(EventContent.FreeCard))
            .AcceptingInto(AtAnEvent(EventContent.FreeCard));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);
        await presenter.ChooseAsync(0, CancellationToken.None);

        presenter.Stage.ShouldNotBe(
            EventStage.Resolved,
            "the command was accepted but the tile it was supposed to clear is still pending, so " +
            "there is nothing resolved to report.");
        presenter.CanLeave.ShouldBeFalse(
            "handing back with the tile still pending re-opens this same decision, and the board " +
            "logs that as halted rather than drawing anything new.");
    }

    // ---- what actually moved ---------------------------------------------------------------------

    /// <summary>
    /// 🔒 <b>The result lines are the SIGNED difference between before and after.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 All four rows and all four signs in one case, because the sign is the claim: a card that
    /// takes 40 Gold and one that gives 40 read identically unsigned, and both are outcomes the same
    /// option can have. HP emits no event at all, so it can only be diffed; the wallet row can only
    /// come off the outcome's own <c>CurrencyChanged</c>, because the profile row the refusal path
    /// hands back is not the one that moved.
    /// </remarks>
    [Fact]
    public async Task The_result_lines_are_the_signed_difference_the_choice_actually_made()
    {
        var host = RecordingGameHost
            .Finding(
                PlayerHolding(EventContent.WalletCardStoneCost),
                AtAnEvent(EventContent.WalletCard, gold: 100, currentHp: 40))
            .AcceptingInto(OffTheTile(
                gold: 600,
                currentHp: 30,
                fixedDice: new Dictionary<int, int> { [3] = 1 }))
            .Emitting(new CurrencyChanged(
                0, CurrencyId.ENHANCE_STONES, -EventContent.WalletCardStoneCost, "event_choice_cost"));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);
        (await presenter.ChooseAsync(0, CancellationToken.None)).ShouldBe(EventSubmission.Submitted);

        presenter.ResultLines.Count.ShouldBe(
            4,
            "four things moved — Gold, HP, a fixed die and the Enhance Stones the option cost — and " +
            $"the screen listed [{string.Join(", ", presenter.ResultLines.Select(l => l.Label + " " + l.Delta))}]");

        Line(presenter, EventContent.GoldLabelKey).Delta.ShouldBe(
            500L, "Gold went from 100 to 600, and income is shown positive.");
        Line(presenter, EventContent.HpLabelKey).Delta.ShouldBe(
            -10L,
            "HP went from 40 to 30. An HP move emits no domain event at all, so a screen that only " +
            "read the outcome's events would show a card that cost the hero ten points as one that " +
            "did nothing to him.");
        Line(presenter, EventContent.FixedDiceLabelKey).Delta.ShouldBe(
            1L, "the run holds one fixed die it did not hold before.");
        Line(presenter, EventContent.CurrencyEnhanceStonesNameKey).Delta.ShouldBe(
            -EventContent.WalletCardStoneCost,
            "the option's own cost, read off the outcome's CurrencyChanged and shown as a spend. " +
            "Captioned through tuning/currencies.json's key rather than a caption this screen owns.");
    }

    /// <summary>
    /// 🔒 <b>A choice that moved nothing says so.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 Not an edge case. Most of `19` Part A's outcomes are <c>UNSUPPORTED</c> effects that
    /// resolve to nothing observable, so this is the commonest thing this screen has to report — and
    /// an empty result panel under a cleared tile reads as a bug rather than as an answer.
    /// </remarks>
    [Fact]
    public async Task A_choice_that_moved_nothing_at_all_says_so_in_its_own_words()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAnEvent(EventContent.FreeCard, gold: 100, currentHp: 40))
            .AcceptingInto(OffTheTile(gold: 100, currentHp: 40));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);
        (await presenter.ChooseAsync(0, CancellationToken.None)).ShouldBe(EventSubmission.Submitted);

        presenter.Stage.ShouldBe(EventStage.Resolved);
        presenter.CanLeave.ShouldBeTrue();
        presenter.ResultLines.ShouldBeEmpty("nothing moved, so there is no row to draw.");
        presenter.StatusText.ShouldBe(
            EventContent.EnglishValueOf(EventContent.NothingHappenedStatusKey),
            "an outcome the build cannot apply is still an outcome the player chose. Left silent, a " +
            "cleared tile with an empty panel reads as a screen that failed rather than as a card " +
            "that did nothing.");
    }

    // ---- the refusals ----------------------------------------------------------------------------

    /// <summary>
    /// 🔒 <b>An <c>INSUFFICIENT_FUNDS</c> refusal keeps the card and the options on the screen.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The trap.</b> A refused command answers with the player's row and NO run, so a screen
    /// that re-read its state off a refused outcome blanks the card, empties the option list and
    /// leaves the player looking at nothing on a tile that is still pending. The pre-submit snapshot
    /// is what has to survive.
    /// </remarks>
    [Fact]
    public async Task A_refused_choice_keeps_the_card_the_screen_was_already_showing()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAnEvent(EventContent.PricedCard, gold: 100))
            .RefusingCommands(RejectionReason.INSUFFICIENT_FUNDS);

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        (await presenter.ChooseAsync(0, CancellationToken.None))
            .ShouldBe(EventSubmission.RefusedByRules);

        presenter.Stage.ShouldBe(
            EventStage.Choosing,
            "the choice was refused, so the card is unspent and the choice is still the player's.");
        presenter.CardId.ShouldBe(
            EventContent.PricedCard,
            "🔴 the refused outcome carried an empty state slice, and the screen took it as the " +
            "truth: the card the player is still looking at was blanked by a command that changed " +
            "nothing.");
        presenter.Options.Count.ShouldBe(
            2, "…and its options went with it, leaving a pending tile and no way off it.");
        presenter.CanLeave.ShouldBeFalse();
        presenter.ResultLines.ShouldBeEmpty("nothing was applied, so nothing moved.");
        presenter.RulesRejection.ShouldBe(RejectionReason.INSUFFICIENT_FUNDS);
        presenter.RejectionText.ShouldBe(
            EventContent.EnglishValueOf(EventContent.RefusedStatusKey));
    }

    /// <summary>
    /// 🔒 <b>A faulting host is not silence, and it does not read as a rules refusal.</b>
    /// </summary>
    [Fact]
    public async Task A_host_that_faults_on_a_choice_is_told_apart_from_a_refusal()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAnEvent(EventContent.FreeCard))
            .FaultingItsCommands(new TimeoutException("the submission never completed"));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        (await presenter.ChooseAsync(0, CancellationToken.None))
            .ShouldBe(EventSubmission.HostUnavailable);

        presenter.HostFaulted.ShouldBeTrue();
        presenter.RulesRejection.ShouldBeNull(
            "a faulted call carried no outcome, so reporting a rejection would be inventing an " +
            "answer the game never gave.");
        presenter.RejectionText.ShouldBe(
            EventContent.EnglishValueOf(EventContent.HostUnavailableStatusKey));
        presenter.Stage.ShouldBe(
            EventStage.Choosing, "nothing was spent, so the card is still there to choose from.");
        presenter.CanLeave.ShouldBeFalse();
    }

    /// <summary>
    /// 🔒 <b>No double submit.</b> The latch is taken before the await, not after it — taken
    /// afterwards, a double-tap spends two options off one card.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 Worse here than on most screens: the second press names a DIFFERENT option, so the run
    /// would pay two costs and draw two outcomes off a card that offers one choice.
    /// </para>
    /// <para>
    /// 🔴 <b>The submission is genuinely PAUSED, and that is the whole case.</b> Against a host that
    /// answers synchronously the first call runs to completion before it returns, so the second press
    /// meets a screen that has already settled on <c>Resolved</c> and is turned away by the stage
    /// guard — which a screen with the latch after its await, or with no latch at all, passes just as
    /// happily. Measured on the sibling screen: with <c>CampfirePresenter</c>'s latch moved to after
    /// its await, that suite's identically-shaped case still reported one submission. Holding the
    /// first command open is what puts the second press where a double-tap actually lands.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_second_choice_while_one_is_in_flight_never_reaches_the_host_twice()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAnEvent(EventContent.ThreeOptionCard))
            .AcceptingInto(OffTheTile())
            .PausingItsCommands();

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var first = presenter.ChooseAsync(0, CancellationToken.None);

        // The first command is outstanding at this line: the card is unspent, the tile is still
        // pending and the screen is still Choosing, so the latch is the only thing that can refuse
        // the second press. Released before anything is awaited, because a screen with no latch
        // sends the second command and then waits on the paused host — and this case has to FAIL
        // on that rather than hang the suite waiting for an answer nobody is going to give.
        var second = presenter.ChooseAsync(1, CancellationToken.None);

        host.ReleaseSubmissions();

        await Task.WhenAll(first, second);

        host.SubmittedCommands.OfType<EventChooseCommand>().Count().ShouldBe(
            1,
            "both presses reached the host, so a double-tap spent two options off one card. The " +
            "second call is refused by the screen's own latch, and the latch is taken before the " +
            "await rather than after it.");
        (await second).ShouldBe(
            EventSubmission.RefusedNotAvailable,
            "the second press arrived while the first was still in flight, so nothing was sent for " +
            "it — and a screen reporting anything else is reporting on a command it did not make.");
        (await first).ShouldBe(
            EventSubmission.Submitted,
            "the press that WAS sent still has to come back as sent once the host answers. A latch " +
            "that swallowed its own submission would leave a card spent and a screen that never " +
            "heard about it.");
    }

    // ---- the way out ------------------------------------------------------------------------------

    /// <summary>
    /// 🔴 <b>No state this screen settles in leaves the player with nothing to press.</b>
    /// </summary>
    /// <remarks>
    /// The screen is opened by the board's routing table in the middle of a run, and in each of
    /// these three states it draws no options at all — so the one control it carries is the only
    /// thing on it. Gated on <c>CanLeave</c>, that control was drawn out of use here, and the run
    /// could then be left only by killing the application. Two of the three say what the run is
    /// doing and the board can route on it; the third says the content set cannot describe a card
    /// the run really drew, and the board is where giving the run up is offered.
    /// </remarks>
    [Fact]
    public async Task Every_settled_state_that_cannot_be_acted_on_still_offers_the_way_back()
    {
        var missing = Build(RecordingGameHost.Reading(
            new OwnStateResult(OwnStateLookup.NoSuchRun, View: null)));

        var elsewhere = Build(RecordingGameHost.Finding(
            AnyPlayer(),
            PlayerState.Run(
                Run, Player, RunPhase.InProgress,
                pendingTileKind: EnemyTileKind, pendingTileLinearIndex: 2, pendingTileStage: 1)));

        var undescribable = Build(
            RecordingGameHost.Finding(AnyPlayer(), AtAnEvent(EventContent.FreeCard)),
            EventContent.Strings());

        await missing.StartAsync(CancellationToken.None);
        await elsewhere.StartAsync(CancellationToken.None);
        await undescribable.StartAsync(CancellationToken.None);

        missing.Exit.ShouldBe(EventExit.ToTheBoard, "there is no run here to stand on.");
        elsewhere.Exit.ShouldBe(
            EventExit.ToTheBoard,
            "the run holds a DIFFERENT pending tile, so this screen cannot clear it and the board " +
            "is what routes the run to the screen that can. CanLeave is false on exactly this row, " +
            "which is what used to strand it.");
        elsewhere.CanLeave.ShouldBeFalse(
            "the tile is still pending — the way out here is not the tile having cleared, which is " +
            "the whole reason the control needed a second reason to be live.");
        undescribable.Exit.ShouldBe(
            EventExit.ToTheBoard,
            "a card this content set cannot build is a run that cannot go on, and abandoning it is " +
            "offered on the board rather than here.");
    }

    /// <summary>
    /// 🔒 <b>A read that never answered asks again, and does NOT hand back.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 The difference is not cosmetic. This state knows nothing about the run, so handing back
    /// would give the board an event tile it has already latched a handover for — which it refuses
    /// as halted, leaving a live board whose own control has nothing legal to send for that tile.
    /// Asking again settles this screen on whatever the run actually says.
    /// </remarks>
    [Fact]
    public async Task A_read_that_never_answered_is_asked_again_rather_than_handed_back()
    {
        var presenter = Build(RecordingGameHost.FaultingItsRead(new TimeoutException("no answer")));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(EventStage.ReadUnavailable);
        presenter.Exit.ShouldBe(
            EventExit.ReadAgain,
            "the press is the retry. Handing back on a read this screen never got an answer to " +
            "would hand the board a decision it has latched, and the board's own press cannot " +
            "resolve an event tile.");
    }

    /// <summary>
    /// 🔒 A card still being chosen on offers no way out, and a spent one offers the ordinary one.
    /// </summary>
    /// <remarks>
    /// The first half is the guard: leaving mid-card abandons a tile that is still pending, and the
    /// board would send the player straight back here.
    /// </remarks>
    [Fact]
    public async Task A_card_still_on_the_screen_is_the_one_state_with_no_way_out()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAnEvent(EventContent.ThreeOptionCard))
            .AcceptingInto(OffTheTile());

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(EventStage.Choosing);
        presenter.Exit.ShouldBe(
            EventExit.Nowhere,
            "the choice is still the player's to make, and a screen that could be left here would " +
            "leave the tile pending behind it.");

        (await presenter.ChooseAsync(0, CancellationToken.None)).ShouldBe(EventSubmission.Submitted);

        presenter.Exit.ShouldBe(
            EventExit.ToTheBoard, "the tile has cleared, which is the ordinary way off this screen.");
    }

    // ---- what a press looks like while it is out --------------------------------------------------

    /// <summary>
    /// 🔴 <b>The option waiting on the host is NAMED while it waits.</b>
    /// </summary>
    /// <remarks>
    /// A submission is a round trip, and the only thing the screen changes while one is out is that
    /// nothing on it can be pressed — which on a card of three options looks exactly like all three
    /// having become unaffordable. Naming the one that was pressed is what lets the screen draw the
    /// wait as an answer being fetched for THAT option rather than as the card going dead.
    /// </remarks>
    [Fact]
    public async Task The_option_a_press_is_waiting_on_is_named_only_while_it_is_waiting()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AtAnEvent(EventContent.ThreeOptionCard))
            .AcceptingInto(OffTheTile())
            .PausingItsCommands();

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        presenter.ChoiceInFlight.ShouldBe(
            EventPresenter.NoChoiceInFlight,
            "nothing has been pressed, so no card may be drawn as the one being answered. The draw " +
            "itself is not a choice and names none.");

        var chosen = presenter.ChooseAsync(ThirdOptionIndex, CancellationToken.None);

        presenter.ChoiceInFlight.ShouldBe(
            ThirdOptionIndex,
            "the third option is outstanding at this line, and it is the third that has to be named " +
            "— a screen answering with the first would light the wrong card.");

        host.ReleaseSubmissions();

        (await chosen).ShouldBe(EventSubmission.Submitted);

        presenter.ChoiceInFlight.ShouldBe(
            EventPresenter.NoChoiceInFlight,
            "the answer arrived, so nothing is waiting any more.");
    }

    /// <summary>
    /// 🔒 "Nothing happened" is asked as its own question, because it is drawn as a RESULT.
    /// </summary>
    /// <remarks>
    /// 🔴 It is the commonest ending this build has, and the screen draws it inside the result panel
    /// rather than on the line that also carries "your run could not be read". A screen that could
    /// not tell the two apart drew the ordinary ending of an event exactly like the game breaking.
    /// </remarks>
    [Fact]
    public async Task A_card_that_moved_nothing_is_told_apart_from_one_that_moved_something()
    {
        var moved = RecordingGameHost
            .Finding(AnyPlayer(), AtAnEvent(EventContent.FreeCard, gold: 100, currentHp: 40))
            .AcceptingInto(OffTheTile(gold: 140, currentHp: 40));

        var still = RecordingGameHost
            .Finding(AnyPlayer(), AtAnEvent(EventContent.FreeCard, gold: 100, currentHp: 40))
            .AcceptingInto(OffTheTile(gold: 100, currentHp: 40));

        var afterAMovement = Build(moved);
        var afterNothing = Build(still);

        await afterAMovement.StartAsync(CancellationToken.None);
        await afterAMovement.ChooseAsync(0, CancellationToken.None);

        await afterNothing.StartAsync(CancellationToken.None);
        await afterNothing.ChooseAsync(0, CancellationToken.None);

        afterAMovement.NothingHappened.ShouldBeFalse(
            "forty Gold moved, so the panel has a row to draw and the sentence would be a lie.");
        afterNothing.NothingHappened.ShouldBeTrue(
            "the card was spent and moved nothing observable, which is what the panel then says in " +
            "words rather than leaving itself empty.");
        afterNothing.CanLeave.ShouldBeTrue(
            "an outcome the build cannot apply still cleared the tile.");
    }

    // ---- the vocabulary ---------------------------------------------------------------------------

    /// <summary>
    /// 🔒 None of this screen's enums has a zero member, so a default-initialised field can never
    /// read as a real state.
    /// </summary>
    [Theory]
    [InlineData(typeof(EventStage))]
    [InlineData(typeof(EventSubmission))]
    [InlineData(typeof(EventExit))]
    public void No_state_this_screen_reports_is_the_default_value_of_its_own_type(Type vocabulary)
    {
        Enum.IsDefined(vocabulary, 0).ShouldBeFalse(
            $"{vocabulary.Name} has a member valued zero, so an uninitialised field of that type " +
            "reads as that member rather than as an obviously wrong value. Every member takes an " +
            "explicit value starting at one.");
    }

    // ---- the strings ------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 Every caption this screen owns comes out of the catalogue, not out of a literal.
    /// </summary>
    /// <remarks>
    /// The fixture values are the key with a marker in front, so nothing a presenter spelled by hand
    /// can match one by accident. The status lines are asserted by the case for each arm they belong
    /// to; these four are on screen whatever the stage.
    /// </remarks>
    [Fact]
    public void Every_caption_this_screen_owns_is_resolved_through_the_catalogue()
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer());

        presenter.Title.ShouldBe(EventContent.EnglishValueOf(EventContent.TitleNameKey));
        presenter.CostLabel.ShouldBe(EventContent.EnglishValueOf(EventContent.CostLabelKey));
        presenter.ResultLabel.ShouldBe(EventContent.EnglishValueOf(EventContent.ResultLabelKey));
        presenter.ContinueText.ShouldBe(EventContent.EnglishValueOf(EventContent.ContinueActionKey));
    }

    /// <summary>
    /// 🔴 <b>The one exception, stated rather than hidden: the card's prose is authored English.</b>
    /// </summary>
    /// <remarks>
    /// <c>board_events.schema.json</c> types a card's <c>title</c>, its <c>body</c> and each
    /// option's <c>label</c> as free strings rather than <c>loc.*</c> keys, so the locale check never
    /// sees them and a German player reads the card in English. Re-authoring the thirty cards against
    /// loc keys is a content pass nobody has done — and a screen that pushed these through the
    /// catalogue anyway would render every card as its own untranslated key.
    /// </remarks>
    [Fact]
    public async Task The_cards_own_prose_is_shown_verbatim_rather_than_resolved_as_a_key()
    {
        var presenter = Build(RecordingGameHost.Finding(
            AnyPlayer(), AtAnEvent(EventContent.ThreeOptionCard)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.CardTitle.ShouldBe(EventContent.ThreeOptionCard + " title");
        presenter.CardBody.ShouldBe(EventContent.ThreeOptionCard + " body");
        presenter.Options[0].Label.ShouldBe(
            "Left door",
            "the option's caption is the card's own words. Pushed through the catalogue it would " +
            "come back as the caption's own text unresolved, because no locale carries a key for it.");
        presenter.CardTitle.ShouldNotContain(
            "FIXTURE",
            Case.Sensitive,
            "the fixture marks every catalogue value with this word, so its presence here means the " +
            "prose went through the string catalogue after all.");
    }

    // ---- construction -----------------------------------------------------------------------------

    /// <summary>
    /// 🔒 Every reference collaborator is required, and refused at the ctor rather than at first use.
    /// </summary>
    /// <remarks>
    /// The screen is composed once and used for the rest of the tile, so a null host or a null
    /// catalogue that is only noticed when a press arrives is a crash at the moment the player acts —
    /// with a drawn card already spent-or-not and no sentence to show. <c>CampfirePresenter</c> is
    /// held to the same three, and this screen is composed the same way.
    /// </remarks>
    [Fact]
    public void Every_reference_collaborator_is_required()
    {
        var content = EventContent.Strings();

        Should.Throw<ArgumentNullException>(() =>
            new EventPresenter(null!, EventContent.Catalogue(content), content, Player, Run));
        Should.Throw<ArgumentNullException>(() =>
            new EventPresenter(
                RecordingGameHost.FindingNoSuchPlayer(), null!, content, Player, Run));
        Should.Throw<ArgumentNullException>(() =>
            new EventPresenter(
                RecordingGameHost.FindingNoSuchPlayer(),
                EventContent.Catalogue(content),
                null!,
                Player,
                Run));
    }

    // ---- fixture ----------------------------------------------------------------------------------

    /// <summary>The one result line carrying a caption, or a failure naming what was drawn instead.</summary>
    private static EventResultLine Line(EventPresenter presenter, string labelKey)
    {
        var caption = EventContent.EnglishValueOf(labelKey);
        var matches = presenter.ResultLines.Where(
            line => string.Equals(line.Label, caption, StringComparison.Ordinal)).ToArray();

        matches.Length.ShouldBe(
            1,
            "the screen drew " + matches.Length + " rows captioned '" + caption + "'. Two rows " +
            "under one caption is a number the player cannot read, and none means the movement was " +
            "not reported at all.");

        return matches[0];
    }

    private static PlayerSnapshot AnyPlayer() => PlayerState.Player(Player);

    /// <summary>A profile holding the given Enhance Stones and nothing else.</summary>
    /// <remarks>
    /// All six meta currencies stated, because the domain refuses a partial wallet: a missing row
    /// read as zero is indistinguishable from a balance a migration dropped.
    /// </remarks>
    private static PlayerSnapshot PlayerHolding(long enhanceStones) =>
        PlayerState.Rehydratable(Player) with
        {
            Wallet = new Dictionary<CurrencyId, long>
            {
                [CurrencyId.CROWNS] = 0,
                [CurrencyId.SOUL_SHARDS] = 0,
                [CurrencyId.ENHANCE_STONES] = enhanceStones,
                [CurrencyId.MERGE_DUST] = 0,
                [CurrencyId.BEAST_FEED] = 0,
                [CurrencyId.HONOR] = 0,
            },
        };

    /// <summary>A run standing on an Event tile that has already drawn a card.</summary>
    private static RunSnapshot AtAnEvent(string cardId, long gold = 0, int currentHp = 100) =>
        PlayerState.Run(
            Run, Player, RunPhase.InProgress,
            position: 5, currentHp: currentHp, gold: gold,
            pendingTileKind: EventPresenter.EventTileKind,
            pendingTileLinearIndex: 5,
            pendingTileStage: 1,
            pendingEventCardId: cardId);

    /// <summary>A run that has just LANDED on an Event tile — the tile is pending, the card is not drawn.</summary>
    /// <remarks>
    /// 🔒 The absence is the empty string, which is how the row spells "none drawn". A fixture using
    /// null would describe a state the domain refuses to rehydrate.
    /// </remarks>
    private static RunSnapshot AtAnUndrawnEvent(long gold = 0) =>
        PlayerState.Run(
            Run, Player, RunPhase.InProgress,
            position: 5, gold: gold,
            pendingTileKind: EventPresenter.EventTileKind,
            pendingTileLinearIndex: 5,
            pendingTileStage: 1);

    /// <summary>A run whose tile has cleared — what an accepted choice hands back.</summary>
    private static RunSnapshot OffTheTile(
        long gold = 0,
        int currentHp = 100,
        IReadOnlyDictionary<int, int>? fixedDice = null) =>
        PlayerState.Run(
            Run, Player, RunPhase.InProgress,
            position: 5, currentHp: currentHp, gold: gold, fixedDice: fixedDice);

    private static EventPresenter Build(RecordingGameHost host, ContentSnapshot? content = null)
    {
        var strings = EventContent.Strings();

        return new EventPresenter(
            host,
            EventContent.Catalogue(strings),
            content ?? EventContent.AuthoringCards(),
            Player,
            Run);
    }
}
