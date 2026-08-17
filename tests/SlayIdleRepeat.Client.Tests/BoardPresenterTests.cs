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

    /// <summary>The reroll ring's authored duration (`04` §3, "Reroll prompt UX").</summary>
    private static readonly TimeSpan AuthoredRingDuration = TimeSpan.FromSeconds(4);

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
        var host = Rolling(DieFace.Pip(4));
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.RollAsync(CancellationToken.None);

        submission.ShouldBe(BoardSubmission.Submitted);
        host.SubmitCommand.ShouldBeOfType<RollDiceCommand>();
        host.SubmitRun.ShouldBe(Run);
    }

    /// <summary>
    /// 🔒 No persisted field carries a rolled face, so the events the command answers with are the
    /// only route from the die to the screen. A presenter that ignored them would leave the board
    /// unable to say what was just rolled at all.
    /// </summary>
    [Fact]
    public async Task The_face_a_roll_reported_comes_off_the_commands_own_events()
    {
        var presenter = Build(Rolling(DieFace.Pip(5)));

        await presenter.StartAsync(CancellationToken.None);
        await presenter.RollAsync(CancellationToken.None);

        presenter.LastRolledFaces.Count.ShouldBe(1);
        presenter.LastRolledFaces[0].Kind.ShouldBe(nameof(DieFaceKind.Pip));
        presenter.LastRolledFaces[0].Value.ShouldBe(5);
    }

    /// <summary>
    /// A chain face rolls again immediately, so one command reports several faces. Reporting only
    /// the last would hide the roll that produced the movement the player just watched.
    /// </summary>
    [Fact]
    public async Task Every_face_one_command_reported_is_carried_in_order()
    {
        var host = RecordingGameHost
            .Finding(AnyPlayer(), PlayerState.Run(Run, Player, RunPhase.InProgress))
            .AcceptingInto(PlayerState.Run(Run, Player, RunPhase.InProgress, position: 4))
            .Emitting(
                new DiceRolled(0, DieFace.Special(DieFaceKind.Chain)),
                new DiceRolled(1, DieFace.Pip(2)));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);
        await presenter.RollAsync(CancellationToken.None);

        presenter.LastRolledFaces.Select(f => f.Kind)
                 .ShouldBe([nameof(DieFaceKind.Chain), nameof(DieFaceKind.Pip)]);
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
            .Emitting(new DiceRolled(0, DieFace.Pip(4)));

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

    [Fact]
    public async Task Resolving_the_pending_tile_submits_RESOLVE_TILE()
    {
        var host = RecordingGameHost
            .Finding(
                AnyPlayer(),
                PlayerState.Run(Run, Player, RunPhase.InProgress, pendingTileKind: EnemyTileKind))
            .AcceptingInto(PlayerState.Run(Run, Player, RunPhase.InProgress));

        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        var submission = await presenter.ResolveTileAsync(CancellationToken.None);

        submission.ShouldBe(BoardSubmission.Submitted);
        host.SubmitCommand.ShouldBeOfType<ResolveTileCommand>();

        // S24: the tile really is left behind, so the roll is live again.
        presenter.PendingTile.ShouldBeNull();
        presenter.RollBlock.ShouldBe(BoardRollBlock.None);
    }

    [Fact]
    public async Task Resolving_with_no_tile_pending_submits_nothing()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), PlayerState.Run(Run, Player, RunPhase.InProgress));
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        (await presenter.ResolveTileAsync(CancellationToken.None))
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

    // ---- the reroll ring ----------------------------------------------------------------------

    [Fact]
    public async Task No_prompt_is_open_until_a_roll_has_landed()
    {
        var presenter = Build(Rolling(DieFace.Pip(3)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Prompt.ShouldBeNull();
    }

    /// <summary>
    /// 🔒 The ring's duration is authored (`04` §3, "Reroll prompt UX": a 4-second ring timer), not
    /// chosen by this screen. Pinned against a literal built here from the section rather than read
    /// off the presenter, so a presenter that changed it fails rather than agreeing with itself.
    /// </summary>
    [Fact]
    public async Task A_landed_roll_opens_a_prompt_with_the_authored_ring()
    {
        var presenter = Build(Rolling(DieFace.Pip(3)), Frozen());

        await presenter.StartAsync(CancellationToken.None);
        await presenter.RollAsync(CancellationToken.None);

        presenter.Prompt.ShouldNotBeNull();
        presenter.Prompt!.Remaining.ShouldBe(AuthoredRingDuration);
        presenter.Prompt.Face.Value.ShouldBe(3);
    }

    /// <summary>
    /// 🔒 The identity, not the symptom. "The second reading is smaller than the first" is
    /// satisfied by any countdown at any rate, and mostly proves the fixture clock steps. What the
    /// ring must actually report is the authored window minus the time that has passed.
    /// </summary>
    [Fact]
    public async Task The_ring_reports_the_authored_window_minus_the_time_that_has_passed()
    {
        var step = TimeSpan.FromSeconds(1);
        var presenter = Build(Rolling(DieFace.Pip(3)), SteppingClock.Advancing(Noon, step));

        await presenter.StartAsync(CancellationToken.None);
        await presenter.RollAsync(CancellationToken.None);

        // Each read of this clock advances it by one step, so the n-th read of the prompt sits n
        // steps after the reading the prompt was opened at.
        presenter.Prompt!.Remaining.ShouldBe(AuthoredRingDuration - step);
        presenter.Prompt!.Remaining.ShouldBe(AuthoredRingDuration - (2 * step));
    }

    /// <summary>
    /// The fraction a ring is drawn from is the presenter's, because working it out means dividing
    /// by the authored window — and a renderer that knew the window would be a second copy of it.
    /// </summary>
    [Fact]
    public async Task The_ring_fraction_is_the_share_of_the_authored_window_left()
    {
        var half = AuthoredRingDuration / 2;
        var presenter = Build(Rolling(DieFace.Pip(3)), SteppingClock.Advancing(Noon, half));

        await presenter.StartAsync(CancellationToken.None);
        await presenter.RollAsync(CancellationToken.None);

        presenter.Prompt!.RingFraction.ShouldBe(0.5, tolerance: 1e-9);
    }

    [Fact]
    public async Task A_ring_that_never_lapses_is_drawn_full()
    {
        var presenter = Build(
            Rolling(DieFace.Pip(3)),
            SteppingClock.Advancing(Noon, TimeSpan.FromHours(1)),
            ringLapses: false);

        await presenter.StartAsync(CancellationToken.None);
        await presenter.RollAsync(CancellationToken.None);

        presenter.Prompt!.RingFraction.ShouldBe(1);
    }

    /// <summary>
    /// 🔒 <b>The authored behaviour on expiry, and the one worth getting right.</b> `04` §3: letting
    /// the timer lapse ACCEPTS the roll. A ring that rerolled on expiry would spend the run's
    /// scarcest resource on a player who did nothing at all.
    /// </summary>
    [Fact]
    public async Task A_lapsed_ring_accepts_the_roll_and_spends_no_charge()
    {
        var clock = SteppingClock.Advancing(Noon, AuthoredRingDuration);
        var host = Rolling(DieFace.Pip(3));
        var presenter = Build(host, clock);

        await presenter.StartAsync(CancellationToken.None);
        await presenter.RollAsync(CancellationToken.None);

        var submissionsBefore = host.SubmitCallCount;

        presenter.TickRerollPrompt().ShouldBeTrue();

        presenter.Prompt.ShouldBeNull();
        host.SubmitCallCount.ShouldBe(
            submissionsBefore,
            "the ring lapsing sent a command. 04 §3 makes a lapse identical to tapping elsewhere — " +
            "it accepts the roll — so nothing at all may be submitted, and above all not the reroll " +
            "that would spend a charge the player never asked to spend.");
    }

    [Fact]
    public async Task A_ring_that_has_not_lapsed_leaves_the_prompt_open()
    {
        var clock = SteppingClock.Advancing(Noon, TimeSpan.FromSeconds(1));
        var presenter = Build(Rolling(DieFace.Pip(3)), clock);

        await presenter.StartAsync(CancellationToken.None);
        await presenter.RollAsync(CancellationToken.None);

        presenter.TickRerollPrompt().ShouldBeFalse();
        presenter.Prompt.ShouldNotBeNull();
    }

    /// <summary>
    /// `13` §8's no-timer accessibility mode: every soft timer is removed and each prompt waits
    /// indefinitely. A ring that lapsed anyway would be the one setting that does not work.
    /// </summary>
    [Fact]
    public async Task A_prompt_that_never_lapses_survives_any_amount_of_time()
    {
        var clock = SteppingClock.Advancing(Noon, TimeSpan.FromHours(1));
        var presenter = Build(Rolling(DieFace.Pip(3)), clock, ringLapses: false);

        await presenter.StartAsync(CancellationToken.None);
        await presenter.RollAsync(CancellationToken.None);

        presenter.TickRerollPrompt().ShouldBeFalse();
        presenter.Prompt.ShouldNotBeNull();
        presenter.Prompt!.Remaining.ShouldBeNull(
            "a prompt that never lapses reported a countdown, so the screen would draw a ring that " +
            "empties and then does nothing — which reads as a broken timer rather than as no timer.");
    }

    [Fact]
    public async Task Tapping_elsewhere_accepts_the_roll_and_spends_no_charge()
    {
        var host = Rolling(DieFace.Pip(3));
        var presenter = Build(host, Frozen());

        await presenter.StartAsync(CancellationToken.None);
        await presenter.RollAsync(CancellationToken.None);

        var submissionsBefore = host.SubmitCallCount;

        presenter.AcceptRoll().ShouldBeTrue();

        presenter.Prompt.ShouldBeNull();
        host.SubmitCallCount.ShouldBe(submissionsBefore);
    }

    /// <summary>
    /// 🔒 A ring covered by the die panel loses nothing. The window is a deadline the player is
    /// answering, and the board offers the panel as a thing to consult before answering — so a ring
    /// that drained behind it would spend the answer on the act of looking something up.
    /// </summary>
    [Fact]
    public async Task A_suspended_ring_gives_back_everything_the_interruption_covered()
    {
        var step = TimeSpan.FromSeconds(1);
        var presenter = Build(Rolling(DieFace.Pip(3)), SteppingClock.Advancing(Noon, step));

        await presenter.StartAsync(CancellationToken.None);
        await presenter.RollAsync(CancellationToken.None);

        presenter.SuspendRerollPrompt().ShouldBeTrue();

        var frozen = presenter.Prompt!.Remaining!.Value;

        // However long the panel stays up — and every read of this clock is another second — the
        // ring does not move at all while it is suspended.
        presenter.Prompt!.Remaining.ShouldBe(frozen);
        presenter.Prompt!.Remaining.ShouldBe(frozen);
        presenter.TickRerollPrompt().ShouldBeFalse(
            "a suspended ring lapsed, so a player who opened the die panel had their roll accepted " +
            "out from under them while they were reading it.");
        presenter.Prompt.ShouldNotBeNull();

        presenter.ResumeRerollPrompt().ShouldBeTrue();

        // One step, because reading the prompt is itself a tick of this clock — so exactly one
        // second of real time has passed since the ring started again, and none of the several
        // seconds it spent covered. Anything smaller means the interruption was charged to the
        // player after all.
        presenter.Prompt!.Remaining.ShouldBe(
            frozen - step,
            "the ring did not resume from where it was covered, so the time the die panel was up " +
            "was taken out of the window the player had to answer in.");
    }

    [Fact]
    public void Suspending_and_resuming_a_ring_that_is_not_running_does_nothing()
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer());

        presenter.SuspendRerollPrompt().ShouldBeFalse();
        presenter.ResumeRerollPrompt().ShouldBeFalse();
    }

    [Fact]
    public async Task A_ring_suspended_twice_is_only_suspended_once()
    {
        var presenter = Build(Rolling(DieFace.Pip(3)), SteppingClock.Advancing(Noon, TimeSpan.FromSeconds(1)));

        await presenter.StartAsync(CancellationToken.None);
        await presenter.RollAsync(CancellationToken.None);

        presenter.SuspendRerollPrompt().ShouldBeTrue();
        presenter.SuspendRerollPrompt().ShouldBeFalse(
            "a second suspension moved the frozen instant forward, so the ring would come back with " +
            "more time than it was covered for.");
    }

    [Fact]
    public async Task The_reroll_is_refused_outright_when_no_prompt_is_open()
    {
        var host = RecordingGameHost.Finding(AnyPlayer(), PlayerState.Run(Run, Player, RunPhase.InProgress));
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        (await presenter.UseRerollAsync(CancellationToken.None))
            .ShouldBe(BoardSubmission.RefusedNotAvailable);

        host.SubmitCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Taking_the_reroll_submits_USE_REROLL_and_closes_the_prompt()
    {
        var host = Rolling(DieFace.Pip(3));
        var presenter = Build(host, Frozen());

        await presenter.StartAsync(CancellationToken.None);
        await presenter.RollAsync(CancellationToken.None);

        var submission = await presenter.UseRerollAsync(CancellationToken.None);

        submission.ShouldBe(BoardSubmission.Submitted);
        host.SubmitCommand.ShouldBeOfType<UseRerollCommand>();
        presenter.Prompt.ShouldBeNull();
    }

    /// <summary>
    /// 🔒 The exhausted reroll gets its own sentence, because it is the one refusal on this screen a
    /// player can plan around — and it is the one that arrives on the wire distinctly enough to be
    /// recognised. The per-stage allowance itself is not readable from a client, so this is learned
    /// from the answer rather than predicted.
    /// </summary>
    [Fact]
    public async Task An_exhausted_reroll_is_told_apart_from_every_other_refusal()
    {
        var host = Rolling(DieFace.Pip(3));
        var presenter = Build(host, Frozen());

        await presenter.StartAsync(CancellationToken.None);
        await presenter.RollAsync(CancellationToken.None);

        host.RefusingCommands(RejectionReason.CAP_REACHED);

        await presenter.UseRerollAsync(CancellationToken.None);

        presenter.RulesRejection.ShouldBe(RejectionReason.CAP_REACHED);
        presenter.RerollExhausted.ShouldBeTrue();
        presenter.RejectionText.ShouldBe(
            BoardContent.EnglishValueOf(BoardContent.RerollExhaustedStatusKey));
        presenter.RejectionText.ShouldNotBe(
            BoardContent.EnglishValueOf(BoardContent.RefusedStatusKey));
    }

    /// <summary>
    /// 🔒 And the exhausted-reroll sentence does not outlive the refusal it came from. A latch
    /// cleared only on the accepted path survives every refusal, and the next unrelated one — a
    /// tile that would not resolve, a branch the rules layer would not take — would then be
    /// explained to the player as a reroll they have no charges for.
    /// </summary>
    [Fact]
    public async Task The_exhausted_sentence_does_not_survive_onto_the_next_refusal()
    {
        var host = RecordingGameHost
            .Finding(
                AnyPlayer(),
                PlayerState.Run(Run, Player, RunPhase.InProgress))
            .AcceptingInto(PlayerState.Run(
                Run, Player, RunPhase.InProgress, pendingTileKind: EnemyTileKind))
            .Emitting(new DiceRolled(0, DieFace.Pip(3)));

        var presenter = Build(host, Frozen());

        await presenter.StartAsync(CancellationToken.None);
        await presenter.RollAsync(CancellationToken.None);

        host.RefusingCommands(RejectionReason.CAP_REACHED);
        await presenter.UseRerollAsync(CancellationToken.None);

        presenter.RerollExhausted.ShouldBeTrue();

        // Now something else is refused, for a completely different reason.
        host.RefusingCommands(RejectionReason.ILLEGAL_STATE);
        await presenter.ResolveTileAsync(CancellationToken.None);

        presenter.RulesRejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        presenter.RerollExhausted.ShouldBeFalse();
        presenter.RejectionText.ShouldBe(
            BoardContent.EnglishValueOf(BoardContent.RefusedStatusKey),
            "a refusal of something else is still being explained as an exhausted reroll, so the " +
            "one refusal on this screen a player can plan around has been smeared over one they " +
            "cannot.");
    }

    [Fact]
    public async Task The_spent_charge_count_is_the_one_the_run_carries()
    {
        var presenter = Build(RecordingGameHost.Finding(
            AnyPlayer(),
            PlayerState.Run(Run, Player, RunPhase.InProgress, rerollChargesSpentThisStage: 2)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.RerollChargesSpent.ShouldBe(2);
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

    private static PlayerSnapshot AnyPlayer() => PlayerState.Player(Player);

    private static RunSnapshot AnyRun() => PlayerState.Run(Run, Player, RunPhase.InProgress);

    private static SteppingClock Frozen() => SteppingClock.Frozen(Noon);

    /// <summary>A host whose run is clear to roll and whose roll reports one face.</summary>
    private static RecordingGameHost Rolling(DieFace face) =>
        RecordingGameHost
            .Finding(AnyPlayer(), PlayerState.Run(Run, Player, RunPhase.InProgress))
            .AcceptingInto(PlayerState.Run(Run, Player, RunPhase.InProgress, position: face.Value))
            .Emitting(new DiceRolled(0, face));

    private static BoardPresenter Build(
        RecordingGameHost host,
        SteppingClock? clock = null,
        ContentSnapshot? content = null,
        bool ringLapses = true) =>
        new(host,
            BoardContent.Catalogue(content ?? BoardContent.Strings()),
            content ?? BoardContent.Strings(),
            clock ?? SteppingClock.Frozen(Noon),
            Player,
            Run,
            ringLapses);
}
