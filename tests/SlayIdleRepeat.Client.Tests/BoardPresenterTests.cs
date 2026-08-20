using Shouldly;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// `13` §3 / `04` §3 — the Board screen (S05): what it draws, what it refuses, and the four
/// commands a board turn is made of.
/// </summary>
public sealed class BoardPresenterTests
{
    /// <summary>The kind number the rules layer carries for "no tile is pending".</summary>
    private const int NoPendingTile = -1;

    /// <summary>An ordinary combat tile — kind 0, the first member of the rules layer's own enum.</summary>
    private const int EnemyTileKind = 0;

    /// <summary>An ordinary tile that <c>RESOLVE_TILE</c> really does clear — the other arm's subject.</summary>
    /// <remarks>
    /// 🔒 Read off the rules layer's enum rather than transcribed, unlike the literal above it: this
    /// one was added with a branch that depends on which kinds are fights, so a renumbering that moved
    /// Treasure into a fight's slot has to be visible here.
    /// </remarks>
    private const int TreasureTileKind = (int)SlayIdleRepeat.Core.Rules.Board.TileKind.Treasure;

    /// <summary>An Event tile — one of the two whose own screen this build has not written.</summary>
    /// <remarks>
    /// 🔒 Read off the rules layer's enum for <c>TreasureTileKind</c>'s reason, and one more: these
    /// two numbers decide whether a tile is SKIPPED, so a renumbering that moved a working tile into
    /// one of these slots would have that tile's screen quietly replaced by a placeholder reward.
    /// </remarks>
    private const int EventTileKind = (int)SlayIdleRepeat.Core.Rules.Board.TileKind.Event;

    /// <summary>A Minigame tile — the other one.</summary>
    private const int MinigameTileKind = (int)SlayIdleRepeat.Core.Rules.Board.TileKind.Minigame;

    /// <summary>The card a case's event tile has drawn.</summary>
    private const string DrawnCard = "EVT_FIXTURE";

    private static readonly PlayerId Player = new("PLAYER_board_7f30");
    private static readonly RunId Run = new("RUN_board_2a95");
    private static readonly DateTimeOffset Noon = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_freshly_built_presenter_has_not_read_anything()
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer());

        presenter.Stage.ShouldBe(BoardStage.NotYetRead);
        presenter.RollBlock.ShouldBe(BoardRollBlock.NotYetRead);
        presenter.StatusText.ShouldBe(BoardContent.EnglishValueOf(BoardContent.LoadingStatusKey));
    }

    [Fact]
    public async Task The_read_is_addressed_to_this_run_and_not_to_the_player_alone()
    {
        // The two are different questions with the same return type: a read naming no run answers
        // with whatever run the player is in, which for a board is the wrong run as often as not.
        var host = RecordingGameHost.Finding(AnyPlayer(), AnyRun());
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        host.ReadRun.ShouldBe(Run);
        host.ReadPlayer.ShouldBe(Player);
    }

    [Fact]
    public async Task A_run_that_was_read_puts_the_values_it_carries_on_the_screen()
    {
        var presenter = Build(RecordingGameHost.Finding(
            AnyPlayer(),
            PlayerState.Run(Run, Player, RunPhase.InProgress, position: 7, currentHp: 41, maxHp: 96, gold: 1340)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(BoardStage.Ready);
        presenter.CurrentHp.ShouldBe(41);
        presenter.MaxHp.ShouldBe(96);
        presenter.Gold.ShouldBe(1340L);
        presenter.Position.ShouldBe(7);
        presenter.StatusText.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_read_that_finds_no_run_says_so_and_never_reads_as_playable()
    {
        var presenter = Build(RecordingGameHost.Reading(
            new OwnStateResult(OwnStateLookup.NoSuchRun, View: null)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(BoardStage.RunMissing);
        presenter.StatusText.ShouldBe(BoardContent.EnglishValueOf(BoardContent.RunMissingStatusKey));
    }

    [Fact]
    public async Task A_read_that_faults_is_a_state_and_not_an_escape()
    {
        var presenter = Build(RecordingGameHost.FaultingItsRead(new TimeoutException("no answer")));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(BoardStage.ReadUnavailable);
        presenter.StatusText.ShouldBe(BoardContent.EnglishValueOf(BoardContent.UnavailableStatusKey));
    }

    // ---- S2: the six refusals told apart -------------------------------------------------------

    /// <summary>
    /// 🔒 The case this screen exists for. Four of these five states refuse a roll with the SAME
    /// wire value, so a screen that read the rejection could not tell them apart — and each is
    /// escaped by doing something completely different.
    /// </summary>
    [Theory]
    [MemberData(nameof(BlockingStates))]
    public async Task Each_reason_a_roll_is_blocked_is_named_from_the_runs_own_state(
        RunSnapshot run, BoardRollBlock expected, string expectedSentenceKey)
    {
        var presenter = Build(RecordingGameHost.Finding(AnyPlayer(), run));

        await presenter.StartAsync(CancellationToken.None);

        presenter.RollBlock.ShouldBe(expected);
        presenter.BlockText.ShouldBe(BoardContent.EnglishValueOf(expectedSentenceKey));
    }

    public static TheoryData<RunSnapshot, BoardRollBlock, string> BlockingStates() => new()
    {
        {
            PlayerState.Run(Run, Player, RunPhase.InProgress, pendingTileKind: EnemyTileKind),
            BoardRollBlock.TilePending,
            BoardContent.BlockedTileStatusKey
        },
        {
            PlayerState.Run(
                Run, Player, RunPhase.InProgress,
                position: 5, pendingForkJunctionPosition: 5, pendingForkRemainingSteps: 2),
            BoardRollBlock.ForkOpen,
            BoardContent.BlockedForkStatusKey
        },
        {
            PlayerState.Run(Run, Player, RunPhase.BattlePending),
            BoardRollBlock.BattleOpen,
            BoardContent.BlockedBattleStatusKey
        },
        {
            PlayerState.Run(Run, Player, RunPhase.InProgress, draftPending: true),
            BoardRollBlock.DraftOpen,
            BoardContent.BlockedDraftStatusKey
        },
    };

    /// <summary>
    /// 🔒 The four sentences are four DIFFERENT sentences, <b>as authored</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 Stated over the shipped locale, not over a fixture, and the distinction is the whole
    /// value of the case. Every fixture string in this suite is derived from its own key, so four
    /// distinct keys give four distinct values by construction and a fixture-based version of this
    /// case could never fail whatever anyone wrote in <c>en.json</c>. The claim being made is about
    /// what a player reads — four instructions that send them to four different places — and only
    /// the authored strings are that.
    /// </remarks>
    [Fact]
    public async Task The_four_block_sentences_are_four_different_authored_sentences()
    {
        var keys = new List<string>();

        foreach (var row in BlockingStates())
        {
            var presenter = Build(RecordingGameHost.Finding(AnyPlayer(), (RunSnapshot)row[0]));

            await presenter.StartAsync(CancellationToken.None);

            keys.Add((string)row[2]);
        }

        keys.Count.ShouldBe(4);

        var authored = keys.Select(key =>
        {
            BoardContent.ShippedEnglish.TryGetValue(key, out var sentence).ShouldBeTrue(
                $"'{key}' is not in the shipped English locale, so the block it names has no sentence " +
                "and a player meeting it is shown its key.");

            return sentence!;
        }).ToArray();

        authored.ShouldAllBe(sentence => sentence.Length > 0);
        authored.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            authored.Length,
            "two of the roll blocks are AUTHORED with the same sentence, so a player meeting one of " +
            "them is told to do the other thing. The whole point of naming the block from the run's " +
            "own state is that each of these is escaped differently — which is undone if the four " +
            $"strings say the same thing: [{string.Join(" | ", authored)}]");
    }

    [Fact]
    public async Task An_ended_run_is_its_own_state_rather_than_a_blocked_roll_on_a_live_one()
    {
        var presenter = Build(RecordingGameHost.Finding(
            AnyPlayer(), PlayerState.Run(Run, Player, RunPhase.Ended)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(BoardStage.RunEnded);
        presenter.RollBlock.ShouldBe(BoardRollBlock.RunEnded);
        presenter.StatusText.ShouldBe(BoardContent.EnglishValueOf(BoardContent.RunEndedStatusKey));

        // The status line already says it, so the block line says nothing — two lines saying one
        // thing is the duplication BlockText's own remarks rule out.
        presenter.BlockText.ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 A run can carry several blocks at once, and the one NAMED must be the one that would
    /// actually have refused the command — otherwise the screen sends the player to do something
    /// that is not what is stopping them.
    /// </summary>
    [Fact]
    public async Task A_battle_open_over_an_unresolved_tile_names_the_battle()
    {
        var presenter = Build(RecordingGameHost.Finding(
            AnyPlayer(),
            PlayerState.Run(Run, Player, RunPhase.BattlePending, pendingTileKind: EnemyTileKind)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.RollBlock.ShouldBe(BoardRollBlock.BattleOpen);
    }

    [Fact]
    public async Task A_clear_run_blocks_nothing()
    {
        var presenter = Build(RecordingGameHost.Finding(
            AnyPlayer(), PlayerState.Run(Run, Player, RunPhase.InProgress)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.RollBlock.ShouldBe(BoardRollBlock.None);
        presenter.BlockText.ShouldBeEmpty();
    }

    // ---- the roll -----------------------------------------------------------------------------

    [Fact]
    public async Task A_blocked_roll_submits_nothing_at_all()
    {
        var host = RecordingGameHost.Finding(
            AnyPlayer(), PlayerState.Run(Run, Player, RunPhase.InProgress, pendingTileKind: EnemyTileKind));
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.RollAsync(CancellationToken.None);

        submission.ShouldBe(BoardSubmission.RefusedNotAvailable);
        host.SubmitCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_clear_roll_submits_ROLL_DICE_against_this_run()
    {
        var host = Rolling(4);
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.RollAsync(CancellationToken.None);

        submission.ShouldBe(BoardSubmission.Submitted);
        host.SubmitCommand.ShouldBeOfType<RollDiceCommand>();
        host.SubmitRun.ShouldBe(Run);
    }

    /// <summary>
    /// 🔒 No persisted field carries a rolled number, so the events the command answers with are the
    /// only route from the die to the screen. A presenter that ignored them would leave the board
    /// unable to say what was just rolled at all.
    /// </summary>
    [Fact]
    public async Task The_number_a_roll_reported_comes_off_the_commands_own_events()
    {
        var presenter = Build(Rolling(5));

        await presenter.StartAsync(CancellationToken.None);
        await presenter.RollAsync(CancellationToken.None);

        presenter.LastRolledPips.ShouldBe(5);
    }

    /// <summary>
    /// A command that reports no roll leaves the last number standing. Blanking it would wipe "what
    /// you rolled" the moment the player acknowledged the tile they landed on.
    /// </summary>
    [Fact]
    public async Task A_command_that_rolls_nothing_leaves_the_last_number_standing()
    {
        var presenter = Build(Rolling(5));

        await presenter.StartAsync(CancellationToken.None);
        await presenter.RollAsync(CancellationToken.None);
        await presenter.ChooseForkAsync(0, CancellationToken.None);

        presenter.LastRolledPips.ShouldBe(5);
    }

    /// <summary>
    /// 🔒 The state comes back with the outcome. A screen that only re-read would draw the run as it
    /// was before its own accepted command for one frame — and the client has already animated it.
    /// </summary>
    [Fact]
    public async Task An_accepted_command_redraws_from_the_state_it_answered_with()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), PlayerState.Run(Run, Player, RunPhase.InProgress, position: -1))
            .AcceptingInto(PlayerState.Run(
                Run, Player, RunPhase.InProgress,
                position: 3, gold: 55, pendingTileKind: EnemyTileKind, pendingTileLinearIndex: 3,
                pendingTileStage: 1))
            .Emitting(new DiceRolled(0, 4));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);
        await presenter.RollAsync(CancellationToken.None);

        presenter.Position.ShouldBe(3);
        presenter.Gold.ShouldBe(55L);
        presenter.PendingTile.ShouldNotBeNull();
        presenter.PendingTile!.LinearIndex.ShouldBe(3);

        // And the block it now reports is the one the new state implies, not the old one.
        presenter.RollBlock.ShouldBe(BoardRollBlock.TilePending);
        host.ReadCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_roll_the_rules_layer_refuses_carries_the_reason_across()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), PlayerState.Run(Run, Player, RunPhase.InProgress))
            .RefusingCommands(RejectionReason.ILLEGAL_STATE);

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.RollAsync(CancellationToken.None);

        submission.ShouldBe(BoardSubmission.RefusedByRules);
        presenter.RulesRejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        presenter.RejectionText.ShouldBe(BoardContent.EnglishValueOf(BoardContent.RefusedStatusKey));
    }

    // ---- the tile -----------------------------------------------------------------------------

    [Fact]
    public async Task The_tile_being_stood_on_is_named_from_its_kind_number()
    {
        var presenter = Build(RecordingGameHost.Finding(
            AnyPlayer(),
            PlayerState.Run(
                Run, Player, RunPhase.InProgress,
                pendingTileKind: EnemyTileKind, pendingTileLinearIndex: 2, pendingTileStage: 1)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.PendingTile.ShouldNotBeNull();
        presenter.PendingTile!.Kind.ShouldBe(EnemyTileKind);
        presenter.PendingTileName.ShouldBe(BoardContent.EnglishValueOf(BoardTileKinds.NameKeys[EnemyTileKind]));
    }

    /// <summary>
    /// 🔒 A kind this build has no name for borrows nobody else's caption. The number survives for
    /// the log; the screen says nothing rather than something plausible and wrong.
    /// </summary>
    [Fact]
    public async Task A_tile_kind_this_build_cannot_name_is_left_unnamed()
    {
        var beyondTheTable = BoardTileKinds.Count;

        var presenter = Build(RecordingGameHost.Finding(
            AnyPlayer(),
            PlayerState.Run(Run, Player, RunPhase.InProgress, pendingTileKind: beyondTheTable)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.PendingTile.ShouldNotBeNull();
        presenter.PendingTile!.Kind.ShouldBe(beyondTheTable);
        presenter.PendingTile.NameKey.ShouldBeNull();
        presenter.PendingTileName.ShouldBeEmpty();
    }

    /// <summary>
    /// 🔴 <b>A FIGHT TILE IS LEFT BY FIGHTING IT, and this case previously asserted the
    /// opposite.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// It was <c>Resolving_the_pending_tile_submits_RESOLVE_TILE</c>, it was arranged on an ENEMY tile,
    /// and it pinned <c>ResolveTileCommand</c>. <c>Handlers.ResolveTile</c> says of Enemy, Elite and
    /// Boss that they are *"acknowledged and not cleared"* — so the behaviour this case protected left
    /// a run stuck on its first enemy for good, and <c>START_BATTLE</c> had no caller anywhere in the
    /// client. Found by PLAYING an exported build, not by any test.
    /// </para>
    /// <para>
    /// 🔴 <b>Worth understanding why it passed, because the mechanism will do it again.</b> The
    /// case ended by asserting the tile was gone and the roll live — and it was, because
    /// <c>AcceptingInto</c> let the fixture DECLARE the resulting run by hand. So the arrangement said
    /// "after this command there is no pending tile", which the real handler never does for a fight.
    /// The fake agreed with the test instead of with the domain, and the two assertions that looked
    /// like proof of clearing were proof of the fixture.
    /// </para>
    /// <para>
    /// 🔒 The command is now asserted for BOTH arms — a fight tile and an ordinary resolvable one —
    /// because one arm alone cannot tell a branch from a constant.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_fight_tile_submits_START_BATTLE_and_an_ordinary_tile_submits_RESOLVE_TILE()
    {
        var fightHost = RecordingGameHost
            .Finding(
                AnyPlayer(),
                PlayerState.Run(Run, Player, RunPhase.InProgress, pendingTileKind: EnemyTileKind))
            .AcceptingInto(PlayerState.Run(Run, Player, RunPhase.BattlePending, pendingTileKind: EnemyTileKind));

        var fighting = Build(fightHost);

        await fighting.StartAsync(CancellationToken.None);

        fighting.PendingTileOpensAFight.ShouldBeTrue("the premise: an enemy tile is left by fighting it.");
        (await fighting.ResolvePendingTileAsync(CancellationToken.None)).ShouldBe(BoardSubmission.Submitted);

        fightHost.SubmitCommand.ShouldBeOfType<StartBattleCommand>(
            "an enemy tile was sent RESOLVE_TILE, which the rules ACCEPT and which clears nothing — so " +
            "the board would redraw the identical state for ever and the run could never fight. " +
            "START_BATTLE is what moves it to BattlePending, which is the state this screen already " +
            "opens the replay on.");

        // 🔒 And the run really is in the fight, which is what the board opens the replay on.
        fighting.RollBlock.ShouldBe(BoardRollBlock.BattleOpen);

        var treasureHost = RecordingGameHost
            .Finding(
                AnyPlayer(),
                PlayerState.Run(Run, Player, RunPhase.InProgress, pendingTileKind: TreasureTileKind))
            .AcceptingInto(PlayerState.Run(Run, Player, RunPhase.InProgress));

        var resolving = Build(treasureHost);

        await resolving.StartAsync(CancellationToken.None);

        resolving.PendingTileOpensAFight.ShouldBeFalse("a treasure tile is resolved, not fought.");
        (await resolving.ResolvePendingTileAsync(CancellationToken.None)).ShouldBe(BoardSubmission.Submitted);

        treasureHost.SubmitCommand.ShouldBeOfType<ResolveTileCommand>(
            "an ordinary tile was sent START_BATTLE, so the branch is inverted — or every tile now " +
            "opens a fight, which is the same defect wearing the other hat.");

        resolving.PendingTile.ShouldBeNull();
        resolving.RollBlock.ShouldBe(BoardRollBlock.None);
    }

    [Fact]
    public async Task Resolving_with_no_tile_pending_submits_nothing()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), PlayerState.Run(Run, Player, RunPhase.InProgress));
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        (await presenter.ResolvePendingTileAsync(CancellationToken.None))
            .ShouldBe(BoardSubmission.RefusedNotAvailable);

        host.SubmitCallCount.ShouldBe(0);
    }

    // ---- the two tiles with no screen ---------------------------------------------------------

    /// <summary>
    /// 🔴 <b>THE SECOND RUN THAT COULD NOT MOVE.</b> A Minigame tile is cleared by
    /// <c>MINIGAME_SUBMIT</c> and by nothing else — <c>Handlers.ResolveTile</c> says of it exactly
    /// what it says of a fight, *"acknowledged and not cleared"* — and this build has no minigame
    /// screen to submit it. So the board's own press accepted, cleared nothing, and redrew the
    /// identical state for ever, with the roll refused and <c>ABANDON_RUN</c> the only way off the
    /// tile. Found the same way the fight was: by playing an exported build.
    /// </summary>
    [Fact]
    public async Task A_minigame_tile_is_left_by_submitting_the_minigame_the_missing_screen_would_have()
    {
        var host = RecordingGameHost
            .Finding(
                AnyPlayer(),
                PlayerState.Run(Run, Player, RunPhase.InProgress, pendingTileKind: MinigameTileKind))
            .AcceptingInto(PlayerState.Run(Run, Player, RunPhase.InProgress));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        presenter.PendingTileHasNoScreen.ShouldBeTrue("the premise: no minigame screen is built.");
        presenter.PendingTileOpensAFight.ShouldBeFalse("a minigame is not a fight.");

        (await presenter.ResolvePendingTileAsync(CancellationToken.None))
            .ShouldBe(BoardSubmission.Submitted);

        var submit = host.SubmitCommand.ShouldBeOfType<MinigameSubmitCommand>(
            "a minigame tile was sent RESOLVE_TILE, which the rules ACCEPT and which clears nothing " +
            "— so the board redraws the identical state for ever and the run can never roll again.");

        submit.Result.ShouldBe(
            UnbuiltTileScreens.LowestOutcomeTier,
            "a skipped minigame pays the least it can. Any other tier would make not playing the " +
            "profitable way to play.");

        host.SubmitCallCount.ShouldBe(1, "a minigame needs no acknowledgement first.");
        presenter.PendingTile.ShouldBeNull();
        presenter.RollBlock.ShouldBe(BoardRollBlock.None);
    }

    /// <summary>
    /// 🔴 The Event tile's version of the same dead end, and it took TWO commands to leave rather
    /// than one: <c>RESOLVE_TILE</c> draws the card, <c>EVENT_CHOOSE</c> spends it, and the draw may
    /// not be re-sent — so the board's second press was REFUSED outright, which is the one arm of
    /// this that a player could actually see going wrong.
    /// </summary>
    [Fact]
    public async Task An_event_tile_is_drawn_and_then_chosen_from_one_press()
    {
        var host = RecordingGameHost
            .Finding(
                AnyPlayer(),
                PlayerState.Run(Run, Player, RunPhase.InProgress, pendingTileKind: EventTileKind))
            .AcceptingInto(PlayerState.Run(
                Run,
                Player,
                RunPhase.InProgress,
                pendingTileKind: EventTileKind,
                pendingEventCardId: DrawnCard))
            .ThenAcceptingInto(PlayerState.Run(Run, Player, RunPhase.InProgress));

        // The card's first option is PRICED and its second is free, which is the whole point of the
        // arrangement: an index of 0 here is a command EVENT_CHOOSE refuses for funds.
        var presenter = Build(host, BoardContent.AuthoringEventCard(1, DrawnCard, true, false));

        await presenter.StartAsync(CancellationToken.None);

        presenter.PendingTileHasNoScreen.ShouldBeTrue("the premise: no event screen is built.");

        (await presenter.ResolvePendingTileAsync(CancellationToken.None))
            .ShouldBe(BoardSubmission.Submitted);

        host.SubmittedCommands.Count.ShouldBe(
            2,
            "an event tile takes the draw and then the choice. One command alone leaves the tile " +
            "pending, and the press after it would be a different command from the same control.");

        host.SubmittedCommands[0].ShouldBeOfType<ResolveTileCommand>(
            "EVENT_CHOOSE before the card is drawn finds no card and is refused.");

        host.SubmittedCommands[1].ShouldBeOfType<EventChooseCommand>()
            .ChoiceIndex.ShouldBe(1, "option 0 costs Gold, so a run with none could not take it.");

        presenter.PendingTile.ShouldBeNull();
        presenter.RollBlock.ShouldBe(BoardRollBlock.None);
    }

    /// <summary>
    /// 🔒 The card is chosen from the run the DRAW came back with. A presenter that decided the
    /// choice before submitting anything has no card to decide it from, so it would fall back to
    /// option zero — which is the affordability hole wearing the shape of a working command.
    /// </summary>
    [Fact]
    public async Task A_draw_that_comes_back_with_no_card_submits_no_choice()
    {
        var host = RecordingGameHost
            .Finding(
                AnyPlayer(),
                PlayerState.Run(Run, Player, RunPhase.InProgress, pendingTileKind: EventTileKind))
            .AcceptingInto(PlayerState.Run(
                Run, Player, RunPhase.InProgress, pendingTileKind: EventTileKind));

        var presenter = Build(host, BoardContent.AuthoringEventCard(1, DrawnCard, true, false));

        await presenter.StartAsync(CancellationToken.None);

        (await presenter.ResolvePendingTileAsync(CancellationToken.None))
            .ShouldBe(BoardSubmission.Submitted);

        host.SubmitCallCount.ShouldBe(1);
        host.SubmitCommand.ShouldBeOfType<ResolveTileCommand>();
    }

    [Fact]
    public async Task A_refused_draw_stops_before_the_choice()
    {
        var host = RecordingGameHost
            .Finding(
                AnyPlayer(),
                PlayerState.Run(Run, Player, RunPhase.InProgress, pendingTileKind: EventTileKind))
            .RefusingCommands(RejectionReason.ILLEGAL_STATE);

        var presenter = Build(host, BoardContent.AuthoringEventCard(1, DrawnCard, false));

        await presenter.StartAsync(CancellationToken.None);

        (await presenter.ResolvePendingTileAsync(CancellationToken.None))
            .ShouldBe(BoardSubmission.RefusedByRules);

        host.SubmitCallCount.ShouldBe(
            1, "a choice submitted after a refused draw is a second refusal for one press.");
    }

    /// <summary>
    /// 🔒 The screen SAYS the screen is missing, on both surfaces a player can read: the sentence
    /// under the board and the caption on the control. "Resolve this tile before rolling again" told
    /// a player standing here to do something no control could do, and "Continue" promised the screen
    /// the tile is supposed to open.
    /// </summary>
    [Theory]
    [InlineData(EventTileKind)]
    [InlineData(MinigameTileKind)]
    public async Task A_tile_with_no_screen_says_so_rather_than_naming_the_block(int tileKind)
    {
        var presenter = Build(RecordingGameHost.Finding(
            AnyPlayer(),
            PlayerState.Run(Run, Player, RunPhase.InProgress, pendingTileKind: tileKind)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.RollBlock.ShouldBe(BoardRollBlock.TilePending);
        presenter.BlockText.ShouldBe(BoardContent.EnglishValueOf(BoardContent.UnbuiltScreenStatusKey));
        presenter.ResolveText.ShouldBe(BoardContent.EnglishValueOf(BoardContent.SkipUnbuiltActionKey));

        // 🔒 And it is still abandonable, which is what it was before this and has to stay: `16` D39
        // makes ABANDON_RUN legal on an unresolved tile whether or not anything else is.
        presenter.AbandonOffered.ShouldBeTrue();
    }

    [Fact]
    public async Task An_ordinary_pending_tile_still_names_the_block_and_offers_to_continue()
    {
        var presenter = Build(RecordingGameHost.Finding(
            AnyPlayer(),
            PlayerState.Run(Run, Player, RunPhase.InProgress, pendingTileKind: TreasureTileKind)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.PendingTileHasNoScreen.ShouldBeFalse();
        presenter.BlockText.ShouldBe(BoardContent.EnglishValueOf(BoardContent.BlockedTileStatusKey));
        presenter.ResolveText.ShouldBe(BoardContent.EnglishValueOf(BoardContent.ResolveActionKey));
    }

    [Fact]
    public async Task A_run_on_no_tile_at_all_is_not_standing_on_a_missing_screen()
    {
        var presenter = Build(RecordingGameHost.Finding(
            AnyPlayer(), PlayerState.Run(Run, Player, RunPhase.InProgress)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.PendingTileHasNoScreen.ShouldBeFalse();
        presenter.ResolveText.ShouldBe(BoardContent.EnglishValueOf(BoardContent.ResolveActionKey));
    }

    // ---- the fork -----------------------------------------------------------------------------

    [Fact]
    public async Task A_paused_junction_is_offered_as_two_branches()
    {
        var presenter = Build(RecordingGameHost.Finding(
            AnyPlayer(),
            PlayerState.Run(
                Run, Player, RunPhase.InProgress,
                position: 5, pendingForkJunctionPosition: 5, pendingForkRemainingSteps: 3)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Fork.ShouldNotBeNull();
        presenter.Fork!.JunctionPosition.ShouldBe(5);
        presenter.Fork.RemainingSteps.ShouldBe(3);

        // The indices are what CHOOSE_FORK carries, and a junction has exactly two edges: the first
        // continues the spine, the second enters the branch.
        presenter.Fork.Branches.Select(b => b.BranchIndex).ShouldBe([0, 1]);
    }

    /// <summary>
    /// The two branches are captioned differently <b>as authored</b>. A fork is the run's one real
    /// navigation choice, and two identical captions is not a choice — over the shipped locale for
    /// the same reason the block sentences are.
    /// </summary>
    [Fact]
    public async Task The_two_branches_are_authored_differently()
    {
        var presenter = Build(RecordingGameHost.Finding(
            AnyPlayer(),
            PlayerState.Run(
                Run, Player, RunPhase.InProgress,
                position: 5, pendingForkJunctionPosition: 5, pendingForkRemainingSteps: 1)));

        await presenter.StartAsync(CancellationToken.None);

        var captions = presenter.Fork!.Branches
            .Select(branch => BoardContent.ShippedEnglish[branch.CaptionKey])
            .ToArray();

        captions.Length.ShouldBe(2);
        captions.ShouldAllBe(caption => caption.Length > 0);
        captions[0].ShouldNotBe(
            captions[1],
            "the two fork branches are authored with the same words, so the run's one real " +
            "navigation decision is drawn as two identical buttons.");
    }

    [Fact]
    public async Task Choosing_a_branch_submits_CHOOSE_FORK_carrying_its_index()
    {
        var host = RecordingGameHost
            .Finding(
                AnyPlayer(),
                PlayerState.Run(
                    Run, Player, RunPhase.InProgress,
                    position: 5, pendingForkJunctionPosition: 5, pendingForkRemainingSteps: 2))
            .AcceptingInto(PlayerState.Run(Run, Player, RunPhase.InProgress, position: 7));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.ChooseForkAsync(1, CancellationToken.None);

        submission.ShouldBe(BoardSubmission.Submitted);
        host.SubmitCommand.ShouldBeOfType<ChooseForkCommand>().BranchIndex.ShouldBe(1);
        presenter.Fork.ShouldBeNull();
    }

    /// <summary>
    /// An index the prompt is not offering never reaches the host. The rules layer would refuse it
    /// with the reason four other things share, which would put the screen back where it started.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public async Task An_index_the_prompt_does_not_offer_submits_nothing(int branchIndex)
    {
        var host = RecordingGameHost.Finding(
            AnyPlayer(),
            PlayerState.Run(
                Run, Player, RunPhase.InProgress,
                position: 5, pendingForkJunctionPosition: 5, pendingForkRemainingSteps: 2));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        (await presenter.ChooseForkAsync(branchIndex, CancellationToken.None))
            .ShouldBe(BoardSubmission.RefusedNotAvailable);

        host.SubmitCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Choosing_a_branch_with_no_fork_open_submits_nothing()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), PlayerState.Run(Run, Player, RunPhase.InProgress));
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        (await presenter.ChooseForkAsync(0, CancellationToken.None))
            .ShouldBe(BoardSubmission.RefusedNotAvailable);

        host.SubmitCallCount.ShouldBe(0);
    }


    // ---- the stage readout --------------------------------------------------------------------

    /// <summary>
    /// 🔒 The stage count is READ from the chapter's own authored stage lengths, never transcribed.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>This used to vary the COUNT — a two-stage and a four-stage chapter — and it cannot any
    /// more.</b> `03` §1 fixes exactly three stages and <c>ChapterBoardTuning</c> refuses a chapter
    /// whose weight tables are not three, so once the presenter projects the board a chapter with any
    /// other count is a state the game cannot reach rather than a shape a fixture may author. What is
    /// still worth pinning, and is pinned in the case below, is that the LENGTHS are read: the two
    /// shipped chapters both author 12/14/16, so a transcribed 14 agrees with them forever.
    /// </remarks>
    [Fact]
    public async Task The_stage_count_comes_from_the_chapters_own_document()
    {
        var content = BoardContent.Authoring(chapterId: 7, 9, 10, 11);
        var presenter = Build(
            RecordingGameHost.Finding(
                AnyPlayer(),
                PlayerState.Run(Run, Player, RunPhase.InProgress, chapterId: 7, position: 0)),
            content: content);

        await presenter.StartAsync(CancellationToken.None);

        presenter.StageCount.ShouldBe(3);
    }

    /// <summary>
    /// The current stage's length is the one the chapter authors for that stage — not the first, and
    /// not a constant.
    /// </summary>
    /// <remarks>
    /// Authored 9/10/11, none of them a shipped value, so the assertion cannot be satisfied by a
    /// transcription of the shipped chapters. The stage itself now comes from the node the run stands
    /// on: on the spine a node's id and its linear index agree, so position 9 is the first node of
    /// stage 2 of a chapter whose first stage is 9 nodes long.
    /// </remarks>
    [Fact]
    public async Task The_current_stages_length_comes_from_the_same_document()
    {
        var content = BoardContent.Authoring(chapterId: 7, 9, 10, 11);
        var presenter = Build(
            RecordingGameHost.Finding(
                AnyPlayer(),
                PlayerState.Run(Run, Player, RunPhase.InProgress, chapterId: 7, position: 9)),
            content: content);

        await presenter.StartAsync(CancellationToken.None);

        presenter.StageNumber.ShouldBe(2, "position 9 is the first node past a 9-node first stage.");
        presenter.StageLength.ShouldBe(10);
    }

    [Fact]
    public async Task A_chapter_the_content_set_does_not_author_leaves_the_stage_shape_unknown()
    {
        var content = BoardContent.Authoring(chapterId: 7, 12, 14, 16);
        var presenter = Build(
            RecordingGameHost.Finding(
                AnyPlayer(),
                PlayerState.Run(Run, Player, RunPhase.InProgress, chapterId: 99)),
            content: content);

        await presenter.StartAsync(CancellationToken.None);

        presenter.StageCount.ShouldBeNull();
        presenter.StageLength.ShouldBeNull();
    }

    /// <summary>
    /// 🔒 <b>The whole board, every node of it.</b> `16` D42 makes the board completely visible at
    /// all times, so this is the claim that no window, range or clip is applied anywhere between the
    /// projection and the screen: the track is as long as the chapter's own stages plus the boss.
    /// </summary>
    /// <remarks>
    /// The length is computed from the authored stage lengths rather than written as 43, so the case
    /// follows a chapter authored differently instead of pinning the shipped shape twice.
    /// </remarks>
    [Fact]
    public async Task The_whole_board_is_drawn_and_never_a_window_on_it()
    {
        int[] stages = [12, 14, 16];

        var presenter = Build(
            RecordingGameHost.Finding(
                AnyPlayer(),
                PlayerState.Run(Run, Player, RunPhase.InProgress, chapterId: 7)),
            content: BoardContent.Authoring(chapterId: 7, stages));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Track.Count.ShouldBe(
            stages.Sum() + 1,
            "every node of every stage, and the boss — a track shorter than that is a board the "
            + "screen is clipping.");
    }

    /// <summary>
    /// Every node carries a real tile kind, the boss is the last of them, and they are not all the
    /// same — which is the whole point of projecting rather than counting.
    /// </summary>
    /// <remarks>
    /// 🔒 Three claims, none of them about WHICH tile the generator drew. That is deliberate: the
    /// weighted draw and its constraints are the rules layer's business and pinning one node's kind
    /// here would make a re-tune of `03` §2's weights fail on a client assertion. What this catches is
    /// the projection collapsing — a track reporting one kind everywhere, or a kind number outside the
    /// vocabulary, both of which the strip this replaced could not have told from a working board.
    /// ⚠️ The fixture paves each stage with one kind, so the variety asserted comes from the
    /// generator's own constraints (its elites, its guaranteed pre-boss campfire) rather than from the
    /// weights.
    /// </remarks>
    [Fact]
    public async Task Each_node_of_the_track_carries_its_own_tile_kind()
    {
        var presenter = Build(
            RecordingGameHost.Finding(
                AnyPlayer(),
                PlayerState.Run(Run, Player, RunPhase.InProgress, chapterId: 7)),
            content: BoardContent.Authoring(chapterId: 7, 12, 14, 16));

        await presenter.StartAsync(CancellationToken.None);

        var track = presenter.Track;

        track.ShouldAllBe(
            node => BoardTileKinds.NameKeyFor((int)node.Tile) != null,
            "a node whose kind this build cannot name is a projection reporting a number, not a tile.");

        track[^1].Tile.ShouldBe(
            SlayIdleRepeat.Core.Rules.Board.TileKind.Boss, "the boss is the last node of the track.");

        track.Select(node => node.Tile).Distinct().Count().ShouldBeGreaterThan(
            1, "one kind everywhere is what a projection that lost the tile would report.");
    }

    /// <summary>
    /// The run's two mini-bosses reach the track, and this build can name them. A kind the name
    /// table has not been taught renders as a number, which is the one thing the board may not draw
    /// — and a mini-boss is the one node a player may not walk past, so an unnamed one is a stop
    /// with no explanation on it.
    /// </summary>
    [Fact]
    public async Task The_track_carries_the_runs_two_mini_boss_nodes_and_can_name_them()
    {
        var presenter = Build(
            RecordingGameHost.Finding(
                AnyPlayer(),
                PlayerState.Run(Run, Player, RunPhase.InProgress, chapterId: 7)),
            content: BoardContent.Authoring(chapterId: 7, 12, 14, 16));

        await presenter.StartAsync(CancellationToken.None);

        var miniBosses = presenter.Track
            .Where(node => node.Tile == SlayIdleRepeat.Core.Rules.Board.TileKind.MiniBoss)
            .ToArray();

        miniBosses.Select(node => node.LinearIndex).ShouldBe(
            new[] { 11, 25 },
            "12/14/16 puts the last node of stage 1 at 11 and of stage 2 at 25.");
        miniBosses.Select(node => BoardTileKinds.NameKeyFor((int)node.Tile))
                  .ShouldAllBe(key => key != null);
    }

    /// <summary>
    /// 🔒 <b>The position is exact between tiles, which is what the projection fixed.</b> This used
    /// to be answered from the pending tile alone, so a run that had just resolved one and not yet
    /// landed on the next reported null and the screen drew no token at all.
    /// </summary>
    [Fact]
    public async Task The_node_the_run_stands_on_is_known_with_no_tile_pending()
    {
        var presenter = Build(
            RecordingGameHost.Finding(
                AnyPlayer(),
                PlayerState.Run(
                    Run, Player, RunPhase.InProgress, chapterId: 7, position: 9,
                    pendingTileKind: NoPendingTile)),
            content: BoardContent.Authoring(chapterId: 7, 12, 14, 16));

        await presenter.StartAsync(CancellationToken.None);

        presenter.PendingTile.ShouldBeNull("the premise: nothing pins the position but the board.");
        presenter.StandingOn.ShouldNotBeNull().NodeId.ShouldBe(9);
        presenter.TrackIndex.ShouldBe(9, "on the spine the node's identity and its distance agree.");
        presenter.StageNumber.ShouldBe(1);
    }

    /// <summary>
    /// A chapter this build does not ship draws no track, and does not throw on the way to saying so.
    /// </summary>
    /// <remarks>
    /// 🔒 The state a saved run whose chapter was removed leaves, and the screen has to open on it:
    /// the abandon control is the only way out of such a run, and it is on this screen.
    /// </remarks>
    [Fact]
    public async Task A_chapter_the_content_set_does_not_author_draws_no_track()
    {
        var presenter = Build(
            RecordingGameHost.Finding(
                AnyPlayer(),
                PlayerState.Run(Run, Player, RunPhase.InProgress, chapterId: 99)),
            content: BoardContent.Authoring(chapterId: 7, 12, 14, 16));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(BoardStage.Ready, "the run is still playable, and still abandonable.");
        presenter.Track.ShouldBeEmpty();
        presenter.StandingOn.ShouldBeNull();
        presenter.TrackIndex.ShouldBeNull();
        presenter.AbandonOffered.ShouldBeTrue();
    }

    // ---- the fixed dice -----------------------------------------------------------------------

    /// <summary>The tray is ordered by number, so a grant never re-arranges the controls.</summary>
    /// <remarks>
    /// 🔒 The run persists a multiset, whose enumeration order is an accident of insertion. A tray
    /// drawn straight off it would move under the player's thumb every time a die was granted.
    /// </remarks>
    [Fact]
    public async Task The_tray_lists_the_dice_the_run_owns_in_ascending_order()
    {
        var presenter = Build(RecordingGameHost.Finding(
            AnyPlayer(),
            PlayerState.Run(
                Run, Player, RunPhase.InProgress,
                fixedDice: new Dictionary<int, int> { [5] = 1, [2] = 3, [6] = 1 })));

        await presenter.StartAsync(CancellationToken.None);

        presenter.FixedDice.Select(held => held.Pips).ShouldBe([2, 5, 6]);
        presenter.FixedDice.Select(held => held.Count).ShouldBe([3, 1, 1]);
        presenter.FixedDiceOffered.ShouldBeTrue();
    }

    [Fact]
    public async Task A_run_holding_no_dice_offers_no_tray()
    {
        var presenter = Build(RecordingGameHost.Finding(AnyPlayer(), AnyRun()));

        await presenter.StartAsync(CancellationToken.None);

        presenter.FixedDice.ShouldBeEmpty();
        presenter.FixedDiceOffered.ShouldBeFalse();
        presenter.FixedDieChoiceOffered.ShouldBeFalse();
    }

    /// <summary>Spending one submits <c>USE_FIXED_DIE</c> carrying the number pressed.</summary>
    [Fact]
    public async Task Spending_a_die_submits_use_fixed_die_for_that_number()
    {
        var host = RecordingGameHost.Finding(
            AnyPlayer(),
            PlayerState.Run(
                Run, Player, RunPhase.InProgress,
                fixedDice: new Dictionary<int, int> { [4] = 1 }));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        (await presenter.UseFixedDieAsync(4, CancellationToken.None))
            .ShouldBe(BoardSubmission.Submitted);

        host.SubmitCommand.ShouldBeOfType<UseFixedDieCommand>().Pips.ShouldBe(4);
    }

    /// <summary>
    /// 🔒 A number the run does not hold is never submitted. That refusal comes back as the same
    /// wire value as the four the block already tells apart, so spending a command on it would leave
    /// the player reading the generic sentence for something the screen already knew.
    /// </summary>
    [Fact]
    public async Task A_number_the_run_does_not_hold_is_not_submitted_at_all()
    {
        var host = RecordingGameHost.Finding(
            AnyPlayer(),
            PlayerState.Run(
                Run, Player, RunPhase.InProgress,
                fixedDice: new Dictionary<int, int> { [4] = 1 }));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        (await presenter.UseFixedDieAsync(3, CancellationToken.None))
            .ShouldBe(BoardSubmission.RefusedNotAvailable);

        host.SubmitCommand.ShouldBeNull("nothing may reach the host at all.");
    }

    /// <summary>
    /// 🔒 The die is gated by exactly what gates the roll, because the rules layer refuses both
    /// movement commands from the same states. Offering one where the other is refused would promise
    /// a way out of a state the game has none of.
    /// </summary>
    [Fact]
    public async Task An_unresolved_tile_refuses_a_fixed_die_the_way_it_refuses_a_roll()
    {
        var host = RecordingGameHost.Finding(
            AnyPlayer(),
            PlayerState.Run(
                Run, Player, RunPhase.InProgress,
                pendingTileKind: EnemyTileKind,
                fixedDice: new Dictionary<int, int> { [4] = 1 }));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        presenter.RollBlock.ShouldBe(BoardRollBlock.TilePending);

        (await presenter.UseFixedDieAsync(4, CancellationToken.None))
            .ShouldBe(BoardSubmission.RefusedNotAvailable);

        host.SubmitCommand.ShouldBeNull("nothing may reach the host at all.");
    }

    /// <summary>An owed choice is offered, and naming a number submits it.</summary>
    [Fact]
    public async Task Naming_a_number_submits_choose_fixed_die()
    {
        var host = RecordingGameHost.Finding(
            AnyPlayer(),
            PlayerState.Run(Run, Player, RunPhase.InProgress, pendingFixedDieChoices: 1));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        presenter.PendingFixedDieChoices.ShouldBe(1);
        presenter.FixedDieChoiceOffered.ShouldBeTrue();

        (await presenter.ChooseFixedDieAsync(6, CancellationToken.None))
            .ShouldBe(BoardSubmission.Submitted);

        host.SubmitCommand.ShouldBeOfType<ChooseFixedDieCommand>().Pips.ShouldBe(6);
    }

    /// <summary>
    /// 🔒 <b>Naming a number is NOT gated on the block, unlike every other control here.</b> A grant
    /// can land while a tile is unresolved or a battle is open, and naming a number moves nothing —
    /// refusing it until the board was clear would leave the player holding a reward they cannot open
    /// in the states they most want to open it.
    /// </summary>
    [Theory]
    [InlineData(EnemyTileKind)]
    [InlineData(TreasureTileKind)]
    public async Task An_unresolved_tile_does_not_block_naming_a_number(int pendingTileKind)
    {
        var host = RecordingGameHost.Finding(
            AnyPlayer(),
            PlayerState.Run(
                Run, Player, RunPhase.InProgress,
                pendingTileKind: pendingTileKind, pendingFixedDieChoices: 1));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        presenter.RollBlock.ShouldBe(BoardRollBlock.TilePending);
        presenter.FixedDieChoiceOffered.ShouldBeTrue();

        (await presenter.ChooseFixedDieAsync(2, CancellationToken.None))
            .ShouldBe(BoardSubmission.Submitted);
    }

    /// <summary>A number no die can show is not submitted, and neither is a choice nothing owes.</summary>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 7)]
    [InlineData(1, -1)]
    [InlineData(0, 3)]
    public async Task A_choice_that_cannot_be_made_is_not_submitted(int owed, int pips)
    {
        var host = RecordingGameHost.Finding(
            AnyPlayer(),
            PlayerState.Run(Run, Player, RunPhase.InProgress, pendingFixedDieChoices: owed));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        (await presenter.ChooseFixedDieAsync(pips, CancellationToken.None))
            .ShouldBe(BoardSubmission.RefusedNotAvailable);

        host.SubmitCommand.ShouldBeNull("nothing may reach the host at all.");
    }


    // ---- fixture ------------------------------------------------------------------------------

    // ---- abandoning ------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 <b>The abandon control is offered from every state a live run can stand in.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>This is the product owner's requirement, and it is the only surface that carries it.</b>
    /// <c>16</c> D39 makes <c>ABANDON_RUN</c> legal mid-battle, mid-draft, at a paused junction and on
    /// an unresolved tile; the domain proves it in <c>RunLivenessTests</c>. None of that reaches a
    /// player unless a control offers it, and this screen has the only one.
    /// </para>
    /// <para>
    /// ⚠️ The theory's rows are the four states that BLOCK the roll plus the clear one — every value
    /// <c>BoardRollBlock</c> has except <c>RunEnded</c>, which is not a live run. A control gated on
    /// the block would pass the first row and fail the other four.
    /// </para>
    /// <para>
    /// 🔒 The tile row is the one that matters most: a run standing on an Event or a Minigame is
    /// standing on a tile this client has no screen for (M7-07b), so the abandon is not merely a way
    /// out — it is the ONLY way out.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("nothing in the way")]
    [InlineData("a tile is pending")]
    [InlineData("a fork is open")]
    [InlineData("a battle is open")]
    [InlineData("a draft is open")]
    public async Task Abandoning_is_offered_from_every_state_a_live_run_can_stand_in(string state)
    {
        var presenter = Build(RecordingGameHost.Finding(AnyPlayer(), Standing(state)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(BoardStage.Ready);
        presenter.AbandonOffered.ShouldBeTrue(
            $"a run with {state} can be abandoned, and this screen is the only place a player can " +
            "say so. Standing on a tile the client has no screen for, it is the only way out at all.");
        presenter.AbandonText.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The negative control: a run that is already over is not offered a way to give it up.
    /// </summary>
    /// <remarks>
    /// Without this, the case above is satisfied by a property that is simply always true — and a
    /// control offered over a closed run would spend a round trip on <c>RUN_ALREADY_ENDED</c>.
    /// </remarks>
    [Fact]
    public async Task A_run_that_has_already_ended_is_not_offered_the_abandon()
    {
        var presenter = Build(
            RecordingGameHost.Finding(AnyPlayer(), PlayerState.Run(Run, Player, RunPhase.Ended)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(BoardStage.RunEnded);
        presenter.AbandonOffered.ShouldBeFalse();
    }

    /// <summary>
    /// 🔒 The first press does not abandon anything — it arms, and the caption changes to say so.
    /// </summary>
    [Fact]
    public async Task The_first_press_arms_the_control_rather_than_ending_the_run()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), AnyRun());
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var offer = presenter.AbandonText;

        var submission = await presenter.AbandonRunAsync(CancellationToken.None);

        submission.ShouldBe(BoardSubmission.RefusedNotAvailable);
        host.SubmitCallCount.ShouldBe(
            0, "a single mis-tap must not be able to throw a run away.");
        presenter.AbandonArmed.ShouldBeTrue();
        presenter.AbandonText.ShouldNotBe(
            offer,
            "the armed control has to READ differently from the one the player just pressed, or the " +
            "confirmation is invisible and the second press is the same tap again.");
    }

    [Fact]
    public async Task The_second_press_submits_ABANDON_RUN()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AnyRun())
            .AcceptingInto(PlayerState.Run(Run, Player, RunPhase.Ended));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);
        await presenter.AbandonRunAsync(CancellationToken.None);

        var submission = await presenter.AbandonRunAsync(CancellationToken.None);

        submission.ShouldBe(BoardSubmission.Submitted);
        host.SubmitCommand.ShouldBeOfType<AbandonRunCommand>();
        host.SubmitRun.ShouldBe(Run);
        presenter.AbandonArmed.ShouldBeFalse("the run is gone; there is nothing left to confirm.");
    }

    /// <summary>
    /// 🔒 An armed confirmation does not survive the player carrying on.
    /// </summary>
    /// <remarks>
    /// A board is played for many minutes. An arming that outlived the roll it was abandoned for
    /// would sit there for the rest of the run, one stray press from ending it — and the press that
    /// ended it would be a press on a control whose caption the player last read as an offer.
    /// </remarks>
    [Fact]
    public async Task Rolling_after_arming_disarms_the_control()
    {
        var presenter = Build(Rolling(3));

        await presenter.StartAsync(CancellationToken.None);
        await presenter.AbandonRunAsync(CancellationToken.None);

        presenter.AbandonArmed.ShouldBeTrue();

        await presenter.RollAsync(CancellationToken.None);

        presenter.AbandonArmed.ShouldBeFalse();
    }

    /// <summary>A refusal from the rules layer is reported rather than swallowed.</summary>
    [Fact]
    public async Task An_abandon_the_rules_layer_refuses_is_told_to_the_player()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), AnyRun())
            .RefusingCommands(RejectionReason.ILLEGAL_STATE);

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);
        await presenter.AbandonRunAsync(CancellationToken.None);

        var submission = await presenter.AbandonRunAsync(CancellationToken.None);

        submission.ShouldBe(BoardSubmission.RefusedByRules);
        presenter.RulesRejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        presenter.RejectionText.ShouldNotBeNullOrWhiteSpace();
    }

    // ---- fixture ---------------------------------------------------------------------------------

    /// <summary>One run per row of the abandon theory, in the state that row names.</summary>
    private static RunSnapshot Standing(string state) => state switch
    {
        "nothing in the way" => AnyRun(),
        "a tile is pending" => PlayerState.Run(
            Run, Player, RunPhase.InProgress, pendingTileKind: TreasureTileKind),
        "a fork is open" => PlayerState.Run(
            Run, Player, RunPhase.InProgress,
            pendingForkJunctionPosition: 4, pendingForkRemainingSteps: 2),
        "a battle is open" => PlayerState.Run(
            Run, Player, RunPhase.BattlePending, pendingTileKind: EnemyTileKind),
        "a draft is open" => PlayerState.Run(
            Run, Player, RunPhase.InProgress, draftPending: true, draftBattleKind: EnemyTileKind),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "No run is built for it."),
    };

    private static PlayerSnapshot AnyPlayer() => PlayerState.Player(Player);

    private static RunSnapshot AnyRun() => PlayerState.Run(Run, Player, RunPhase.InProgress);

    /// <summary>A host whose run is clear to roll and whose roll reports one number.</summary>
    private static RecordingGameHost Rolling(int pips) =>
        RecordingGameHost
            .Finding(AnyPlayer(), PlayerState.Run(Run, Player, RunPhase.InProgress))
            .AcceptingInto(PlayerState.Run(Run, Player, RunPhase.InProgress, position: pips))
            .Emitting(new DiceRolled(0, pips));

    private static BoardPresenter Build(RecordingGameHost host, ContentSnapshot? content = null) =>
        new(host,
            BoardContent.Catalogue(content ?? BoardContent.Strings()),
            content ?? BoardContent.Strings(),
            Player,
            Run);
}
