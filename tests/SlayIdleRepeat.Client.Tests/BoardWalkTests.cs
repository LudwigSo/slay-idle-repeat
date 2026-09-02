using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// How the board screen learns what to animate: <see cref="BoardPresenter.WalkFrom"/> reconstructs
/// the nodes a movement walked, and <see cref="BoardWalk"/> plays them out.
/// </summary>
/// <remarks>
/// The reconstruction is tested through the presenter, which is the outermost seam a case can
/// reach it at. The playhead is tested directly, because nothing outside it can observe whether a
/// walk has finished — and "has it finished" is the answer the whole screen's turn order hangs on.
/// </remarks>
public sealed class BoardWalkTests
{
    private const int Chapter = 5;
    private static readonly int[] Stages = [12, 14, 16];

    private static readonly PlayerId Player = new("PLAYER_walk_1d72");
    private static readonly RunId Run = new("RUN_walk_63ba");

    // ------------------------------------------------------------------------------------------
    // A — reconstructing the walk.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// A move reports every node between the two positions, in order, excluding the one it started
    /// on — the defect being a walk that reports only the destination, which draws a four-step roll
    /// as a single hop and makes the number on the die unreadable from the motion.
    /// </summary>
    [Fact]
    public async Task A_move_walks_every_node_between_the_two_positions_in_order()
    {
        var board = StraightBoard();
        var from = board.Spine[3].NodeId;
        var to = board.Spine[7].NodeId;

        var walk = (await PresenterOnAStraightBoardAt(to)).WalkFrom(from, viaNodeId: null);

        walk.ShouldBe(new[]
        {
            board.Spine[4].NodeId, board.Spine[5].NodeId, board.Spine[6].NodeId, to,
        });
    }

    /// <summary>
    /// 🔒 The walk is measured from where the run ENDED, never from the number rolled — so a roll
    /// the stage-end clamp cut short walks the distance it actually moved.
    /// </summary>
    /// <remarks>
    /// The defect: a hop count taken from <see cref="BoardPresenter.LastRolledPips"/>. It agrees
    /// with the run on every unclamped move, and on a clamped one it walks the hero past the stage
    /// boundary onto nodes it never visited and then snaps it back. `03` §1.1's stage-end clamp,
    /// the boss-exact rule and <c>CUR_SLIPPERY</c> all shorten a move, and none of them is visible
    /// in the number the die showed.
    /// </remarks>
    [Fact]
    public async Task A_move_cut_short_walks_only_as_far_as_the_run_actually_went()
    {
        var board = StraightBoard();
        var from = board.Spine[3].NodeId;
        var to = board.Spine[5].NodeId;

        var walk = (await PresenterOnAStraightBoardAt(to)).WalkFrom(from, viaNodeId: null);

        walk.Count.ShouldBe(
            2,
            "the run moved two nodes, whatever the die showed — the walk is a fact about the board, " +
            "not about the roll.");
    }

    /// <summary>
    /// A movement that left a junction walks the edge the player chose, not the one the spine
    /// continues onto.
    /// </summary>
    /// <remarks>
    /// 🔒 The case the whole <c>viaNodeId</c> parameter exists for. A junction has two successors,
    /// so endpoints alone do not name a route: the branch and the spine both reach the rejoin. A
    /// reconstruction that quietly took the spine would walk the hero down one path while the run
    /// stood on the other — the exact confusion <c>BoardView.ForksOf</c>'s refusal message is
    /// written about, arriving through the animation instead of through the command.
    /// </remarks>
    [Fact]
    public async Task A_move_that_left_a_junction_walks_the_branch_the_player_chose()
    {
        var board = Board();
        board.Forks.ShouldNotBeEmpty("a board with no fork would make every fork case here vacuous.");
        var fork = board.Forks[0];
        var branchEntry = fork.BranchNodeIds[0];

        var walk = (await PresenterStandingOn(branchEntry))
            .WalkFrom(fork.JunctionNodeId, viaNodeId: branchEntry);

        walk.ShouldBe(new[] { branchEntry });
    }

    /// <summary>
    /// The discriminating pair for the case above: the SAME start and destination walk two different
    /// routes depending on which edge the command took.
    /// </summary>
    /// <remarks>
    /// A branch rejoins the spine, so a junction and a node beyond the rejoin are reachable both
    /// ways. Without this case, a reconstruction that ignored <c>viaNodeId</c> entirely and always
    /// took the spine would still satisfy the branch case above whenever the branch happened to be
    /// the shorter route.
    /// </remarks>
    [Fact]
    public async Task The_same_two_positions_walk_different_routes_for_different_chosen_edges()
    {
        var board = Board();
        var fork = board.Forks[0];
        var destination = fork.RejoinNodeId;

        var presenter = await PresenterStandingOn(destination);

        var alongTheSpine = presenter.WalkFrom(fork.JunctionNodeId, fork.ContinueNodeId);
        var alongTheBranch = presenter.WalkFrom(fork.JunctionNodeId, fork.BranchNodeIds[0]);

        alongTheSpine.ShouldNotBeEmpty();
        alongTheBranch.ShouldNotBeEmpty();

        alongTheBranch.ShouldNotBe(
            alongTheSpine,
            "both edges reach the rejoin, so a reconstruction that ignored the chosen edge would " +
            "return one answer for two different moves.");

        alongTheBranch[0].ShouldBe(fork.BranchNodeIds[0]);
        alongTheSpine[0].ShouldBe(fork.ContinueNodeId);
    }

    /// <summary>
    /// The last node of a branch walks back onto the spine — a branch move's final hop is the join,
    /// and without it the hero snaps across it every single time.
    /// </summary>
    [Fact]
    public async Task A_walk_off_the_last_branch_node_rejoins_the_spine()
    {
        var board = Board();
        var fork = board.Forks[0];

        var walk = (await PresenterStandingOn(fork.RejoinNodeId))
            .WalkFrom(fork.BranchNodeIds[^1], viaNodeId: null);

        walk.ShouldBe(new[] { fork.RejoinNodeId });
    }

    /// <summary>
    /// A run at the trailhead walks onto the board's first node — the trailhead is not a node, and
    /// without this the first roll of every run would animate nothing at all.
    /// </summary>
    [Fact]
    public async Task A_walk_from_the_trailhead_starts_at_the_boards_first_node()
    {
        var board = StraightBoard();

        var walk = (await PresenterOnAStraightBoardAt(board.Spine[0].NodeId))
            .WalkFrom(fromNodeId: null, viaNodeId: null);

        walk.ShouldBe(new[] { board.Spine[0].NodeId });
    }

    /// <summary>A position that did not move walks nothing.</summary>
    /// <remarks>
    /// The defect: an empty walk handed to the playhead as though it were a real one. It never
    /// completes, so the screen's turn latch never releases and every control stays disabled for the
    /// rest of the run.
    /// </remarks>
    [Fact]
    public async Task A_move_that_went_nowhere_walks_nothing()
    {
        var board = StraightBoard();
        var standingStill = board.Spine[4].NodeId;

        (await PresenterOnAStraightBoardAt(standingStill)).WalkFrom(standingStill, viaNodeId: null)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// A destination that is not forward of the start walks nothing, rather than running the length
    /// of the board looking for it.
    /// </summary>
    [Fact]
    public async Task A_destination_behind_the_start_walks_nothing()
    {
        var board = StraightBoard();

        (await PresenterOnAStraightBoardAt(board.Spine[3].NodeId))
            .WalkFrom(board.Spine[9].NodeId, viaNodeId: null)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// A move further than any single command can reach walks nothing, so the hero is put down
    /// rather than crawling.
    /// </summary>
    /// <remarks>
    /// A die shows at most 6 and a Portal draws at most 6, so nothing legal produces a longer walk.
    /// One that appears is a reconstruction fault, and animating it would hold the screen for as
    /// many hops as the board is long.
    /// <para>
    /// Stated on the fork-free board deliberately: on a forked one the junction in between would
    /// refuse the walk first, and this case would pass without the length cap existing at all.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_move_further_than_any_command_can_reach_walks_nothing()
    {
        var board = StraightBoard();

        board.Forks.ShouldBeEmpty("the point of this fixture is that nothing but the cap can refuse.");

        (await PresenterOnAStraightBoardAt(board.Spine[20].NodeId))
            .WalkFrom(board.Spine[0].NodeId, viaNodeId: null)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// A junction departure with no chosen edge walks nothing rather than guessing which way the run
    /// went.
    /// </summary>
    [Fact]
    public async Task A_junction_departure_with_no_chosen_edge_walks_nothing()
    {
        var board = Board();
        var fork = board.Forks[0];

        (await PresenterStandingOn(fork.RejoinNodeId))
            .WalkFrom(fork.JunctionNodeId, viaNodeId: null)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// A chosen edge that is not one of the ways out of the start walks nothing — a stale fork
    /// press must snap the hero, never teleport it onto an unrelated node.
    /// </summary>
    [Fact]
    public async Task A_chosen_edge_that_does_not_leave_the_start_walks_nothing()
    {
        var board = Board();
        var fork = board.Forks[0];

        (await PresenterStandingOn(fork.RejoinNodeId))
            .WalkFrom(fork.JunctionNodeId, viaNodeId: board.Spine[^1].NodeId)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// The negative control: a presenter that never projected a board answers empty instead of
    /// throwing. Without it every refusal above would pass against a reconstruction that threw on
    /// everything.
    /// </summary>
    [Fact]
    public void A_presenter_with_no_board_walks_nothing_and_does_not_throw()
    {
        var presenter = new BoardPresenter(
            RecordingGameHost.Finding(PlayerState.Player(Player)),
            BoardContent.Catalogue(BoardContent.Strings()),
            BoardContent.Strings(),
            Player,
            Run);

        presenter.WalkFrom(fromNodeId: 0, viaNodeId: null).ShouldBeEmpty();
    }

    // ------------------------------------------------------------------------------------------
    // B — the playhead.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// A walk reports finished on the advance that lands its LAST hop, and not before.
    /// </summary>
    /// <remarks>
    /// 🔒 The claim the screen's turn order rests on. The board defers everything a command does
    /// next — opening the battle, the shop, the perk draft, leaving for the run-end screen — until
    /// the walk lands. A playhead that reported finished after the first hop would open the battle
    /// replay over a hero still visibly mid-board, and nothing would go red.
    /// </remarks>
    [Fact]
    public void A_walk_reports_finished_only_after_its_last_hop()
    {
        var walk = new BoardWalk();
        walk.SnapTo(0);
        walk.Begin([1, 2, 3]);

        walk.Advance(0.19d).ShouldBeFalse("one hop of three has landed.");
        walk.ShownNodeId.ShouldBe(1);

        walk.Advance(0.19d).ShouldBeFalse("two of three.");
        walk.ShownNodeId.ShouldBe(2);

        walk.Advance(0.19d).ShouldBeTrue("the third hop lands, and this is the call that says so.");
        walk.ShownNodeId.ShouldBe(3);
        walk.InProgress.ShouldBeFalse();

        walk.Advance(0.19d).ShouldBeFalse("a finished walk finishes once, not on every later frame.");
    }

    /// <summary>
    /// One long frame lands the hero exactly where several short ones summing to the same time do.
    /// </summary>
    /// <remarks>
    /// The defect: a playhead that advances one hop per call. It runs at half speed on a
    /// thirty-frame handset and at full speed on the sixty-frame desktop it was authored against,
    /// so the animation is a different length on the device nobody tested it on.
    /// </remarks>
    [Fact]
    public void A_long_frame_lands_where_several_short_ones_land()
    {
        var oneFrame = new BoardWalk();
        oneFrame.SnapTo(0);
        oneFrame.Begin([1, 2, 3]);
        oneFrame.Advance(0.60d);

        var manyFrames = new BoardWalk();
        manyFrames.SnapTo(0);
        manyFrames.Begin([1, 2, 3]);

        foreach (var _ in Enumerable.Range(0, 12))
        {
            manyFrames.Advance(0.05d);
        }

        oneFrame.ShownNodeId.ShouldBe(manyFrames.ShownNodeId);
        oneFrame.InProgress.ShouldBe(manyFrames.InProgress);
    }

    /// <summary>
    /// A hop in flight runs from the node the hero is standing on to the node it is arriving at,
    /// for every hop and not only the first.
    /// </summary>
    /// <remarks>
    /// The defect this catches is the natural one for a stored field: writing "where the hop came
    /// from" before moving the shown node, so every hop after the first is drawn starting at the
    /// node before last and the hero visibly jumps backwards between steps.
    /// </remarks>
    [Fact]
    public void Each_hop_runs_from_the_node_the_hero_is_standing_on()
    {
        var walk = new BoardWalk();
        walk.SnapTo(0);
        walk.Begin([1, 2, 3]);

        walk.HopFromNodeId.ShouldBe(0);
        walk.HopToNodeId.ShouldBe(1);

        walk.Advance(0.19d);
        walk.HopFromNodeId.ShouldBe(1, "the second hop leaves the node the first one landed on.");
        walk.HopToNodeId.ShouldBe(2);

        walk.Advance(0.19d);
        walk.HopFromNodeId.ShouldBe(2);
        walk.HopToNodeId.ShouldBe(3);
    }

    /// <summary>
    /// A walk longer than any command can produce puts the hero down at its last node instead of
    /// crawling there.
    /// </summary>
    [Fact]
    public void A_walk_longer_than_a_command_can_produce_is_put_down_at_its_end()
    {
        var walk = new BoardWalk();
        walk.SnapTo(0);

        walk.Begin(Enumerable.Range(1, BoardPathMaxHopsPlusOne).ToArray());

        walk.InProgress.ShouldBeFalse("nothing legal produces a walk this long, so it is not walked.");
        walk.ShownNodeId.ShouldBe(BoardPathMaxHopsPlusOne);
    }

    /// <summary>
    /// With reduced motion the whole walk completes on its first advance — the hero still visits
    /// each node in order, and no frame is spent in between.
    /// </summary>
    /// <remarks>
    /// The defect: a seam taken as a constructor argument and then never read, which is the finding
    /// M7-07's review raised against another screen in this client.
    /// </remarks>
    [Fact]
    public void A_reduced_motion_walk_finishes_on_its_first_advance()
    {
        var walk = new BoardWalk(reducedMotion: true);
        walk.SnapTo(0);
        walk.Begin([1, 2, 3]);

        walk.Advance(0.001d).ShouldBeTrue();
        walk.ShownNodeId.ShouldBe(3);
        walk.InProgress.ShouldBeFalse();
    }

    /// <summary>
    /// Snapping leaves nothing in flight — a board resumed after another screen handed back must
    /// not come back already animating.
    /// </summary>
    [Fact]
    public void Snapping_leaves_nothing_in_progress()
    {
        var walk = new BoardWalk();
        walk.SnapTo(0);
        walk.Begin([1, 2, 3]);
        walk.Advance(0.19d);

        walk.SnapTo(9);

        walk.InProgress.ShouldBeFalse();
        walk.ShownNodeId.ShouldBe(9);
        walk.HopFromNodeId.ShouldBeNull();
        walk.Advance(1d).ShouldBeFalse();
    }

    /// <summary>An empty walk changes nothing and leaves nothing in flight.</summary>
    [Fact]
    public void An_empty_walk_changes_nothing()
    {
        var walk = new BoardWalk();
        walk.SnapTo(4);

        walk.Begin([]);

        walk.InProgress.ShouldBeFalse();
        walk.ShownNodeId.ShouldBe(4);
    }

    // ----------------------------------------------------------------------------------------
    // Fixtures.
    // ----------------------------------------------------------------------------------------

    /// <summary>One past the longest walk a single command can produce — see <c>BoardPath.MaxHops</c>.</summary>
    private const int BoardPathMaxHopsPlusOne = 9;

    private static ContentSnapshot Content() => BoardContent.Authoring(Chapter, Stages);

    /// <summary>
    /// A chapter authoring no fork, so its board is one unbroken spine.
    /// </summary>
    /// <remarks>
    /// Used by every case about DISTANCE. A junction refuses a walk — movement pauses there rather
    /// than choosing an edge for the player — so on a forked board a case that picked two indices
    /// apart could be refused by a junction between them and pass for a reason it never stated.
    /// </remarks>
    private static ContentSnapshot StraightContent() =>
        BoardContent.AuthoringWithoutForks(Chapter, Stages);

    /// <summary>The board this suite's cases read, projected the way the screen projects it.</summary>
    private static BoardView Board() =>
        BoardView.Project(
            PlayerState.Run(Run, Player, RunPhase.InProgress, chapterId: Chapter), Content());

    /// <summary>The fork-free board this suite's distance cases read.</summary>
    private static BoardView StraightBoard() =>
        BoardView.Project(
            PlayerState.Run(Run, Player, RunPhase.InProgress, chapterId: Chapter), StraightContent());

    /// <summary>A started presenter whose run stands on a chosen node of the forked board.</summary>
    private static Task<BoardPresenter> PresenterStandingOn(int nodeId) =>
        PresenterAt(nodeId, Content());

    /// <summary>A started presenter whose run stands on a chosen node of the fork-free board.</summary>
    private static Task<BoardPresenter> PresenterOnAStraightBoardAt(int nodeId) =>
        PresenterAt(nodeId, StraightContent());

    private static async Task<BoardPresenter> PresenterAt(int nodeId, ContentSnapshot content)
    {
        var presenter = new BoardPresenter(
            RecordingGameHost.Finding(
                PlayerState.Player(Player),
                PlayerState.Run(Run, Player, RunPhase.InProgress, position: nodeId, chapterId: Chapter)),
            BoardContent.Catalogue(content),
            content,
            Player,
            Run);

        await presenter.StartAsync(CancellationToken.None);

        return presenter;
    }
}
