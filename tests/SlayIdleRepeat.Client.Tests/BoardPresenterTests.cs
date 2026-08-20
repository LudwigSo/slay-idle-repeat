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
    /// Both shipped chapters author three stages, so a hard-coded 3 agrees with them forever — the
    /// fixture authors a different shape precisely so it cannot.
    /// </summary>
    [Theory]
    [InlineData(new[] { 12, 14, 16 }, 3)]
    [InlineData(new[] { 8, 9 }, 2)]
    [InlineData(new[] { 5, 6, 7, 8 }, 4)]
    public async Task The_stage_count_comes_from_the_chapters_own_document(int[] stageLengths, int expected)
    {
        var content = BoardContent.Authoring(chapterId: 7, stageLengths);
        var presenter = Build(
            RecordingGameHost.Finding(
                AnyPlayer(),
                PlayerState.Run(
                    Run, Player, RunPhase.InProgress,
                    chapterId: 7, pendingTileKind: EnemyTileKind, pendingTileStage: 1)),
            content: content);

        await presenter.StartAsync(CancellationToken.None);

        presenter.StageCount.ShouldBe(expected);
    }

    [Fact]
    public async Task The_current_stages_length_comes_from_the_same_document()
    {
        var content = BoardContent.Authoring(chapterId: 7, 12, 14, 16);
        var presenter = Build(
            RecordingGameHost.Finding(
                AnyPlayer(),
                PlayerState.Run(
                    Run, Player, RunPhase.InProgress,
                    chapterId: 7, pendingTileKind: EnemyTileKind, pendingTileStage: 2)),
            content: content);

        await presenter.StartAsync(CancellationToken.None);

        presenter.StageNumber.ShouldBe(2);
        presenter.StageLength.ShouldBe(14);
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
    /// 🔒 The exact distance along the track is carried only by a pending tile. Between resolving
    /// one tile and landing on the next it is not knowable, and the screen says so rather than
    /// drawing the token at a plausible node.
    /// </summary>
    [Fact]
    public async Task The_track_index_is_absent_when_no_tile_pins_it()
    {
        var presenter = Build(RecordingGameHost.Finding(
            AnyPlayer(), PlayerState.Run(Run, Player, RunPhase.InProgress, position: 9)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Position.ShouldBe(9);
        presenter.TrackIndex.ShouldBeNull();
        presenter.StageNumber.ShouldBeNull();
    }

    [Fact]
    public async Task The_track_index_is_the_pending_tiles_own_and_not_the_node_identity()
    {
        // Inside a fork branch the two genuinely differ: the branch node's identity is far past the
        // spine, while its distance from the start is the spine node level with it.
        var presenter = Build(RecordingGameHost.Finding(
            AnyPlayer(),
            PlayerState.Run(
                Run, Player, RunPhase.InProgress,
                position: 44, pendingTileKind: EnemyTileKind, pendingTileLinearIndex: 6,
                pendingTileStage: 1)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Position.ShouldBe(44);
        presenter.TrackIndex.ShouldBe(6);
    }

    /// <summary>
    /// 🔴 <b>The stage-local pip, over stages of DIFFERENT lengths.</b> The run's index runs
    /// continuously across the whole chapter, so placing the token inside a stage means subtracting
    /// the real sum of the stages before it. The shipped chapters author 12, 14 then 16, so any
    /// arithmetic that multiplies one stage's length by the stage number lands on the wrong node
    /// everywhere but stage one — which is exactly what the first version of this did, in the scene,
    /// where nothing could catch it.
    /// </summary>
    [Theory]
    [InlineData(1, 0, 0)]    // the very first node of the chapter
    [InlineData(1, 11, 11)]  // the last node of stage 1
    [InlineData(2, 12, 0)]   // the first node of stage 2 — offset 12, not 14
    [InlineData(2, 25, 13)]  // the last node of stage 2
    [InlineData(3, 26, 0)]   // the first node of stage 3 — offset 26, not 32
    [InlineData(3, 41, 15)]  // the last node of stage 3
    public async Task The_token_sits_where_the_chapters_own_stage_lengths_put_it(
        int stage, int linearIndex, int expectedPip)
    {
        var presenter = Build(
            RecordingGameHost.Finding(
                AnyPlayer(),
                PlayerState.Run(
                    Run, Player, RunPhase.InProgress,
                    chapterId: 7, pendingTileKind: EnemyTileKind,
                    pendingTileLinearIndex: linearIndex, pendingTileStage: stage)),
            content: BoardContent.Authoring(chapterId: 7, 12, 14, 16));

        await presenter.StartAsync(CancellationToken.None);

        presenter.StageTrackIndex.ShouldBe(expectedPip);
    }

    /// <summary>
    /// An index that does not land inside the stage it claims lights no pip. A clamp would draw the
    /// token at a plausible node, which is the board quietly lying about where the player is.
    /// </summary>
    [Theory]
    [InlineData(1, 40)]
    [InlineData(3, 0)]
    public async Task An_index_outside_its_stage_places_no_token(int stage, int linearIndex)
    {
        var presenter = Build(
            RecordingGameHost.Finding(
                AnyPlayer(),
                PlayerState.Run(
                    Run, Player, RunPhase.InProgress,
                    chapterId: 7, pendingTileKind: EnemyTileKind,
                    pendingTileLinearIndex: linearIndex, pendingTileStage: stage)),
            content: BoardContent.Authoring(chapterId: 7, 12, 14, 16));

        await presenter.StartAsync(CancellationToken.None);

        presenter.StageTrackIndex.ShouldBeNull();
    }

    [Fact]
    public async Task No_pending_tile_places_no_token()
    {
        var presenter = Build(
            RecordingGameHost.Finding(
                AnyPlayer(),
                PlayerState.Run(Run, Player, RunPhase.InProgress, chapterId: 7, position: 9)),
            content: BoardContent.Authoring(chapterId: 7, 12, 14, 16));

        await presenter.StartAsync(CancellationToken.None);

        presenter.StageTrackIndex.ShouldBeNull();
        presenter.StageLength.ShouldBeNull();
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
