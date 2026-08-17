using SlayIdleRepeat.Core.Rules.Board;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

/// <summary>
/// 🔒 <b><see cref="BoardGraph.IsStageEndNode"/>'s own contract</b>, asked of every node shape a
/// board can hold, over small hand-authored <see cref="BoardGraph.FromLayout"/> graphs.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Why the predicate needs cases of its own rather than only the handler cases that drive it.</b>
/// Two of its three clauses are unreachable through <c>ROLL_DICE</c>, <c>CHOOSE_FORK</c> and
/// <c>RESOLVE_TILE</c> as they stand: <c>MovementEngine</c> never comes to rest on a node with no
/// outgoing edge without also reporting the boss, and <c>BoardGenerator</c> never places a junction
/// late enough to be a stage's last node. Deleting either clause therefore leaves every
/// handler-driven case green while the predicate's documented answer is wrong — which is exactly
/// what happened when they were probed. These cases make both clauses load-bearing.
/// </para>
/// <para>
/// The boss clause matters most: it is the difference between a run crossing two gates and three,
/// and the only thing holding it up on the handler side is <c>Handlers.ChooseFork</c>'s
/// <c>ReachedBoss</c> test, which is itself redundant today.
/// </para>
/// </remarks>
public sealed class BoardGraphStageEndNodeTests
{
    private const int Stage1 = 1;
    private const int Stage2 = 2;
    private const int Stage3 = 3;

    /// <summary>
    /// Three stages, a junction with a branch that rejoins onto stage 1's last node, and the boss.
    /// </summary>
    /// <remarks>
    /// Spine <c>0 → 1 → 2 → 3 | 4 → 5 | 6 → boss</c>, with node 2 a junction whose branch runs
    /// <c>7 → 8</c> and rejoins at node 3. Nodes 7 and 8 are off the linear index, as branch nodes
    /// are on a generated board.
    /// </remarks>
    private static (BoardGraph Board, NodeId Boss) ThreeStageBoard()
    {
        var nodes = new[]
        {
            new BoardNode(new NodeId(0), TileKind.Enemy, 0, Stage1),
            new BoardNode(new NodeId(1), TileKind.Enemy, 1, Stage1),
            new BoardNode(new NodeId(2), TileKind.Empty, 2, Stage1),
            new BoardNode(new NodeId(3), TileKind.Enemy, 3, Stage1),
            new BoardNode(new NodeId(4), TileKind.Enemy, 4, Stage2),
            new BoardNode(new NodeId(5), TileKind.Enemy, 5, Stage2),
            new BoardNode(new NodeId(6), TileKind.Campfire, 6, Stage3),
            new BoardNode(new NodeId(9), TileKind.Boss, 7, BoardGraph.BossStage),
            new BoardNode(new NodeId(7), TileKind.Empty, 2, Stage1),
            new BoardNode(new NodeId(8), TileKind.Empty, 3, Stage1),
        };

        var edges = new[]
        {
            new BoardEdge(new NodeId(0), new NodeId(1), EdgeKind.Continue),
            new BoardEdge(new NodeId(1), new NodeId(2), EdgeKind.Continue),
            new BoardEdge(new NodeId(2), new NodeId(3), EdgeKind.Continue),
            new BoardEdge(new NodeId(2), new NodeId(7), EdgeKind.Branch),
            new BoardEdge(new NodeId(7), new NodeId(8), EdgeKind.Continue),
            new BoardEdge(new NodeId(8), new NodeId(3), EdgeKind.Continue),
            new BoardEdge(new NodeId(3), new NodeId(4), EdgeKind.Continue),
            new BoardEdge(new NodeId(4), new NodeId(5), EdgeKind.Continue),
            new BoardEdge(new NodeId(5), new NodeId(6), EdgeKind.Continue),
            new BoardEdge(new NodeId(6), new NodeId(9), EdgeKind.Continue),
        };

        var spine = new[]
        {
            new NodeId(0), new NodeId(1), new NodeId(2), new NodeId(3),
            new NodeId(4), new NodeId(5), new NodeId(6), new NodeId(9),
        };

        var board = BoardGraph.FromLayout(nodes, edges, spine, new[] { new NodeId(2) });

        return (board, new NodeId(9));
    }

    [Fact]
    public void The_last_node_of_a_stage_whose_successor_starts_the_next_one_is_a_stage_end()
    {
        var (board, _) = ThreeStageBoard();

        board.IsStageEndNode(new NodeId(3)).ShouldBeTrue(
            "node 3 is the last node of stage 1 — its only edge leads into stage 2 — so a run coming " +
            "to rest on it has finished the stage and owes a Stage Gate.");

        board.IsStageEndNode(new NodeId(5)).ShouldBeTrue(
            "node 5 is the last node of stage 2, so it is a stage end for the same reason node 3 is.");
    }

    [Fact]
    public void The_last_node_of_the_final_stage_is_not_a_stage_end_because_its_successor_is_the_boss()
    {
        var (board, _) = ThreeStageBoard();

        board.IsStageEndNode(new NodeId(6)).ShouldBeFalse(
            "node 6 is the last node of stage 3 and its one edge leads to the boss, which belongs to " +
            "no stage. Counting it would give a run three gates where the cadence is authored for " +
            "two — this is the clause the boss exclusion exists for.");
    }

    [Fact]
    public void The_boss_node_is_not_a_stage_end()
    {
        var (board, boss) = ThreeStageBoard();

        board.OutgoingEdges(boss).ShouldBeEmpty(
            "the premise: the boss node is the end of the board and leads nowhere.");

        board.IsStageEndNode(boss).ShouldBeFalse(
            "a node with no outgoing edge ends the board, not a stage. Without the empty-edge guard " +
            "the loop over its edges is vacuous and answers true, and nothing on the handler side " +
            "asks the question — MovementEngine reports such a node as the boss and every caller " +
            "returns before the predicate is reached.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void A_node_whose_successor_is_still_inside_its_own_stage_is_not_a_stage_end(int id)
    {
        var (board, _) = ThreeStageBoard();

        board.IsStageEndNode(new NodeId(id)).ShouldBeFalse(
            "node " + id + " leads to another node of its own stage, so the stage is not over.");
    }

    [Fact]
    public void A_junction_whose_edges_both_stay_inside_the_stage_is_not_a_stage_end()
    {
        var (board, _) = ThreeStageBoard();

        board.IsJunction(new NodeId(2)).ShouldBeTrue("the premise: node 2 is the fork.");

        board.IsStageEndNode(new NodeId(2)).ShouldBeFalse(
            "both of the junction's edges lead deeper into stage 1, so neither of them ends it.");
    }

    [Fact]
    public void A_branch_node_rejoining_onto_a_stages_last_node_is_not_itself_a_stage_end()
    {
        var (board, _) = ThreeStageBoard();

        board.IsStageEndNode(new NodeId(8)).ShouldBeFalse(
            "node 8 is the branch's last node and rejoins onto node 3, which is still stage 1. The " +
            "gate belongs to the node the branch rejoins at, not to the branch node before it — " +
            "otherwise a run taking the branch would gate one node early and gate again at node 3.");
    }

    /// <summary>
    /// 🔒 The clause that quantifies over <em>every</em> outgoing edge rather than the first one.
    /// </summary>
    /// <remarks>
    /// Not constructible from a generated board — <c>BoardGenerator</c> caps junction placement well
    /// before a stage's last node — so this is the only thing standing between that clause and being
    /// silently deletable. <c>BoardGraph.FromLayout</c> is a public seam and an authored layout could
    /// put the fork here.
    /// </remarks>
    [Fact]
    public void A_junction_with_one_edge_leaving_the_stage_and_one_staying_is_not_a_stage_end()
    {
        var start = new BoardNode(new NodeId(0), TileKind.Enemy, 0, Stage1);
        var fork = new BoardNode(new NodeId(1), TileKind.Empty, 1, Stage1);
        var nextStage = new BoardNode(new NodeId(2), TileKind.Enemy, 2, Stage2);
        var stillStage1 = new BoardNode(new NodeId(3), TileKind.Enemy, 1, Stage1);
        var boss = new BoardNode(new NodeId(4), TileKind.Boss, 3, BoardGraph.BossStage);

        var edges = new[]
        {
            new BoardEdge(start.Id, fork.Id, EdgeKind.Continue),
            new BoardEdge(fork.Id, nextStage.Id, EdgeKind.Continue),
            new BoardEdge(fork.Id, stillStage1.Id, EdgeKind.Branch),
            new BoardEdge(stillStage1.Id, nextStage.Id, EdgeKind.Continue),
            new BoardEdge(nextStage.Id, boss.Id, EdgeKind.Continue),
        };

        var board = BoardGraph.FromLayout(
            new[] { start, fork, nextStage, stillStage1, boss },
            edges,
            new[] { start.Id, fork.Id, nextStage.Id, boss.Id },
            new[] { fork.Id });

        board.IsStageEndNode(fork.Id).ShouldBeFalse(
            "the fork's Continue edge leaves stage 1 but its Branch edge does not, so a run standing " +
            "here can still travel further inside the stage and has not ended it. A predicate that " +
            "read only the first edge would gate here and gate again wherever the branch rejoins.");
    }

    [Fact]
    public void Asking_about_a_node_that_is_not_on_the_board_throws()
    {
        var (board, _) = ThreeStageBoard();

        var ex = Should.Throw<KeyNotFoundException>(() => board.IsStageEndNode(new NodeId(999)));

        ex.Message.ShouldContain(
            "not a node on this board",
            Case.Sensitive,
            "an id the board has never heard of is a defect in the caller, not a node that happens " +
            "to end no stage — answering false would hide it.");
    }
}
