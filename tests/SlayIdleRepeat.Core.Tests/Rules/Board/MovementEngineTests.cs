using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

/// <summary>
/// Tests <see cref="MovementEngine"/>'s stepwise traversal, over small, hand-authored
/// <see cref="BoardGraph.FromLayout"/> graphs so every rule (junction pause, stage-end clamp,
/// boss-exact, Portal's campfire clamp) is exercised against a KNOWN layout.
/// </summary>
public sealed class MovementEngineTests
{
    // Fixture: a straight run of 5 spine nodes, one stage, no junction — the negative control
    // every rule below is checked against.
    private static BoardGraph LinearFiveNodeBoard()
    {
        var nodes = new[]
        {
            new BoardNode(new NodeId(0), TileKind.Enemy, 0, 1),
            new BoardNode(new NodeId(1), TileKind.Enemy, 1, 1),
            new BoardNode(new NodeId(2), TileKind.Enemy, 2, 1),
            new BoardNode(new NodeId(3), TileKind.Enemy, 3, 1),
            new BoardNode(new NodeId(4), TileKind.Enemy, 4, 1),
        };

        var edges = new[]
        {
            new BoardEdge(nodes[0].Id, nodes[1].Id, EdgeKind.Continue),
            new BoardEdge(nodes[1].Id, nodes[2].Id, EdgeKind.Continue),
            new BoardEdge(nodes[2].Id, nodes[3].Id, EdgeKind.Continue),
            new BoardEdge(nodes[3].Id, nodes[4].Id, EdgeKind.Continue),
        };

        return BoardGraph.FromLayout(
            nodes, edges, nodes.Select(n => n.Id).ToArray(), Array.Empty<NodeId>());
    }

    [Fact]
    public void Advance_steps_forward_one_edge_at_a_time_on_a_junction_free_board()
    {
        var board = LinearFiveNodeBoard();

        var result = MovementEngine.Advance(board, new NodeId(0), 3);

        result.Node.ShouldBe(new NodeId(3));
        result.PausedAtJunction.ShouldBeFalse();
        result.ReachedBoss.ShouldBeFalse();
        result.RemainingSteps.ShouldBe(0);
    }

    [Fact]
    public void Advance_of_zero_steps_stays_put()
    {
        var board = LinearFiveNodeBoard();

        var result = MovementEngine.Advance(board, new NodeId(2), 0);

        result.Node.ShouldBe(new NodeId(2));
        result.PausedAtJunction.ShouldBeFalse();
        result.RemainingSteps.ShouldBe(0);
    }

    [Fact]
    public void A_negative_step_count_is_refused()
    {
        var board = LinearFiveNodeBoard();

        Should.Throw<ArgumentOutOfRangeException>(() => MovementEngine.Advance(board, new NodeId(0), -1));
    }

    // Fixture: N0 -> J (junction) -> { Continue: N2 -> N3 ; Branch: B0 -> B1 -> N3 (rejoin) },
    // all stage 1. The branch's own linear indices mirror the spine's at equal forward distance
    // from the junction, exactly as BoardGenerator builds one.
    private static (BoardGraph Board, NodeId N0, NodeId J, NodeId N2, NodeId N3, NodeId B0, NodeId B1) JunctionBoard()
    {
        var n0 = new BoardNode(new NodeId(0), TileKind.Enemy, 0, 1);
        var j = new BoardNode(new NodeId(1), TileKind.Enemy, 1, 1);
        var n2 = new BoardNode(new NodeId(2), TileKind.Enemy, 2, 1);
        var n3 = new BoardNode(new NodeId(3), TileKind.Enemy, 3, 1);
        var b0 = new BoardNode(new NodeId(4), TileKind.Shrine, 2, 1); // same linear index as n2
        var b1 = new BoardNode(new NodeId(5), TileKind.Treasure, 3, 1); // same linear index as n3

        var preview = new ForkPreview(ForkLabel.Sheltered, new[] { TileKind.Shrine, TileKind.Treasure });

        var nodes = new[] { n0, j, n2, n3, b0, b1 };
        var edges = new[]
        {
            new BoardEdge(n0.Id, j.Id, EdgeKind.Continue),
            new BoardEdge(j.Id, n2.Id, EdgeKind.Continue),
            new BoardEdge(j.Id, b0.Id, EdgeKind.Branch, preview),
            new BoardEdge(n2.Id, n3.Id, EdgeKind.Continue),
            new BoardEdge(b0.Id, b1.Id, EdgeKind.Continue),
            new BoardEdge(b1.Id, n3.Id, EdgeKind.Continue), // rejoin
        };

        var board = BoardGraph.FromLayout(
            nodes, edges, new[] { n0.Id, j.Id, n2.Id, n3.Id }, new[] { j.Id });

        return (board, n0.Id, j.Id, n2.Id, n3.Id, b0.Id, b1.Id);
    }

    [Fact]
    public void Landing_exactly_on_a_junction_with_nothing_left_does_not_pause()
    {
        var (board, n0, j, _, _, _, _) = JunctionBoard();

        var result = MovementEngine.Advance(board, n0, 1);

        result.Node.ShouldBe(j);
        result.PausedAtJunction.ShouldBeFalse(
            "03 §1.1: a junction landed on with zero movement left does not prompt CHOOSE_FORK.");
        result.RemainingSteps.ShouldBe(0);
    }

    [Fact]
    public void Movement_that_must_leave_a_junction_pauses_there()
    {
        var (board, n0, j, _, _, _, _) = JunctionBoard();

        var result = MovementEngine.Advance(board, n0, 2);

        result.Node.ShouldBe(j);
        result.PausedAtJunction.ShouldBeTrue();
        result.RemainingSteps.ShouldBe(1, "one step was spent reaching the junction; one is still owed.");
        result.ReachedBoss.ShouldBeFalse();
    }

    [Fact]
    public void A_junction_reached_mid_move_pauses_the_same_way_as_one_the_move_started_at()
    {
        var (board, n0, j, _, _, _, _) = JunctionBoard();

        // Starting AT the junction with movement to spend is the same pause as reaching it mid-move.
        var result = MovementEngine.Advance(board, j, 1);

        result.Node.ShouldBe(j);
        result.PausedAtJunction.ShouldBeTrue();
        result.RemainingSteps.ShouldBe(1);
    }

    [Fact]
    public void The_junctions_branch_list_is_Continue_then_Branch_and_both_are_choosable()
    {
        var (board, _, j, n2, n3, b0, b1) = JunctionBoard();

        var edges = board.OutgoingEdges(j);

        edges.Count.ShouldBe(2);
        edges[0].Kind.ShouldBe(EdgeKind.Continue);
        edges[0].To.ShouldBe(n2);
        edges[1].Kind.ShouldBe(EdgeKind.Branch);
        edges[1].To.ShouldBe(b0);

        // Resuming after each choice with 1 remaining step lands where it must.
        MovementEngine.Advance(board, edges[0].To, 0).Node.ShouldBe(n2);
        MovementEngine.Advance(board, edges[1].To, 1).Node.ShouldBe(b1);
    }

    /// <summary>S1 negative control: an ordinary node never pauses, even with plenty of movement left.</summary>
    [Fact]
    public void A_non_junction_node_never_pauses()
    {
        var board = LinearFiveNodeBoard();

        var result = MovementEngine.Advance(board, new NodeId(1), 3);

        result.PausedAtJunction.ShouldBeFalse();
    }

    // Fixture: stage-end clamp and the boss-exact rule.
    // Stage 1: N0 -> N1 (last node of stage 1) -> M0 (first node of stage 2, DIFFERENT stage).
    // Stage 3: S0 -> S1 (last node of stage 3) -> Boss (BossStage, Tile.Boss).
    private static (BoardGraph Board, NodeId N0, NodeId N1, NodeId M0) StageBoundaryBoard()
    {
        var n0 = new BoardNode(new NodeId(0), TileKind.Enemy, 0, 1);
        var n1 = new BoardNode(new NodeId(1), TileKind.Enemy, 1, 1); // stage 1's last node
        var m0 = new BoardNode(new NodeId(2), TileKind.Enemy, 2, 2); // stage 2's first node

        var nodes = new[] { n0, n1, m0 };
        var edges = new[]
        {
            new BoardEdge(n0.Id, n1.Id, EdgeKind.Continue),
            new BoardEdge(n1.Id, m0.Id, EdgeKind.Continue),
        };

        var board = BoardGraph.FromLayout(nodes, edges, nodes.Select(n => n.Id).ToArray(), Array.Empty<NodeId>());

        return (board, n0.Id, n1.Id, m0.Id);
    }

    [Fact]
    public void A_move_that_would_cross_a_stage_boundary_clamps_on_the_stages_last_node()
    {
        var (board, n0, n1, _) = StageBoundaryBoard();

        var result = MovementEngine.Advance(board, n0, 5);

        result.Node.ShouldBe(n1, "03 §1.1: a move that would carry past the last node of a stage stops on that node.");
        result.PausedAtJunction.ShouldBeFalse();
        result.ReachedBoss.ShouldBeFalse();
        result.RemainingSteps.ShouldBe(4, "the 5 requested minus the 1 actually spent reaching the stage's last node.");
    }

    /// <summary>S1 negative control: staying short of the boundary is never clamped.</summary>
    [Fact]
    public void A_move_that_stays_inside_one_stage_is_not_clamped()
    {
        var (board, n0, n1, _) = StageBoundaryBoard();

        var result = MovementEngine.Advance(board, n0, 1);

        result.Node.ShouldBe(n1);
        result.RemainingSteps.ShouldBe(0, "the move landed exactly on the boundary node with nothing left over — not a clamp.");
    }

    private static (BoardGraph Board, NodeId S0, NodeId S1, NodeId Boss) BossExactBoard()
    {
        var s0 = new BoardNode(new NodeId(0), TileKind.Enemy, 0, 3);
        var s1 = new BoardNode(new NodeId(1), TileKind.Enemy, 1, 3); // stage 3's last node
        var boss = new BoardNode(new NodeId(2), TileKind.Boss, 2, BoardGraph.BossStage);

        var nodes = new[] { s0, s1, boss };
        var edges = new[]
        {
            new BoardEdge(s0.Id, s1.Id, EdgeKind.Continue),
            new BoardEdge(s1.Id, boss.Id, EdgeKind.Continue),
        };

        var board = BoardGraph.FromLayout(nodes, edges, nodes.Select(n => n.Id).ToArray(), Array.Empty<NodeId>());

        return (board, s0.Id, s1.Id, boss.Id);
    }

    [Fact]
    public void Any_roll_from_stage_3s_last_node_moves_exactly_one_step_onto_the_boss()
    {
        var (board, _, s1, boss) = BossExactBoard();

        // A roll of 6 — the largest a Pip die shows — still lands exactly on the boss, not 6 past it.
        var result = MovementEngine.Advance(board, s1, 6);

        result.Node.ShouldBe(boss);
        result.ReachedBoss.ShouldBeTrue();
        result.RemainingSteps.ShouldBe(0);
        result.PausedAtJunction.ShouldBeFalse();
    }

    [Fact]
    public void Reaching_the_boss_node_directly_also_reports_ReachedBoss()
    {
        var (board, _, _, boss) = BossExactBoard();

        var result = MovementEngine.Advance(board, boss, 3);

        result.ReachedBoss.ShouldBeTrue("the boss node has no outgoing edge; movement ends there whatever is left unspent.");
        result.Node.ShouldBe(boss);
    }

    /// <summary>S1 negative control: a stage-3 move that does NOT reach the last node is not boss-exact.</summary>
    [Fact]
    public void A_move_from_stage_3_that_does_not_reach_the_boss_is_an_ordinary_move()
    {
        var (board, s0, s1, _) = BossExactBoard();

        var result = MovementEngine.Advance(board, s0, 1);

        result.Node.ShouldBe(s1);
        result.ReachedBoss.ShouldBeFalse();
    }

    // Portal's extra pre-boss campfire clamp (stage 3 only). Layout:
    // S0 -> S1 -> S2 -> S3 -> Camp (campfire, bossLinearIndex - 2) -> S5 (stage 3's last node) -> Boss.
    // Room for a full 3-6 draw from S0 to land short of, on, or past the campfire.
    private static (BoardGraph Board, NodeId S0, NodeId S3, NodeId Camp, NodeId Boss) PortalStage3Board()
    {
        var s0 = new BoardNode(new NodeId(0), TileKind.Enemy, 0, 3);
        var s1 = new BoardNode(new NodeId(1), TileKind.Enemy, 1, 3);
        var s2 = new BoardNode(new NodeId(2), TileKind.Enemy, 2, 3);
        var s3 = new BoardNode(new NodeId(3), TileKind.Enemy, 3, 3);
        var camp = new BoardNode(new NodeId(4), TileKind.Campfire, 4, 3);
        var s5 = new BoardNode(new NodeId(5), TileKind.Enemy, 5, 3); // stage 3's last node
        var boss = new BoardNode(new NodeId(6), TileKind.Boss, 6, BoardGraph.BossStage);

        var nodes = new[] { s0, s1, s2, s3, camp, s5, boss };
        var edges = new[]
        {
            new BoardEdge(s0.Id, s1.Id, EdgeKind.Continue),
            new BoardEdge(s1.Id, s2.Id, EdgeKind.Continue),
            new BoardEdge(s2.Id, s3.Id, EdgeKind.Continue),
            new BoardEdge(s3.Id, camp.Id, EdgeKind.Continue),
            new BoardEdge(camp.Id, s5.Id, EdgeKind.Continue),
            new BoardEdge(s5.Id, boss.Id, EdgeKind.Continue),
        };

        var board = BoardGraph.FromLayout(nodes, edges, nodes.Select(n => n.Id).ToArray(), Array.Empty<NodeId>());

        return (board, s0.Id, s3.Id, camp.Id, boss.Id);
    }

    [Fact]
    public void A_portal_jump_that_would_pass_the_pre_boss_campfire_lands_on_it_instead()
    {
        var (board, s0, _, camp, _) = PortalStage3Board();

        // 5 steps from S0 would naturally land on S5 (past the campfire at 4 steps).
        var result = MovementEngine.AdvancePortal(board, s0, 5);

        result.Node.ShouldBe(camp);
        result.ReachedBoss.ShouldBeFalse();
        result.PausedAtJunction.ShouldBeFalse();
    }

    [Fact]
    public void A_portal_jump_that_would_reach_the_boss_lands_on_the_campfire_instead()
    {
        var (board, s0, _, camp, _) = PortalStage3Board();

        // 6 steps from S0 (the largest legal Portal draw) would naturally reach the boss.
        var result = MovementEngine.AdvancePortal(board, s0, 6);

        result.Node.ShouldBe(camp, "03 §1.1: Portal can never reach the boss node.");
        result.ReachedBoss.ShouldBeFalse();
    }

    /// <summary>S1 negative control: a jump that never reaches the campfire is untouched.</summary>
    [Fact]
    public void A_portal_jump_that_stays_short_of_the_campfire_is_not_clamped()
    {
        var (board, s0, s3, camp, _) = PortalStage3Board();

        // 3 is the smallest legal Portal draw; from S0 it lands exactly on S3, short of the campfire.
        var result = MovementEngine.AdvancePortal(board, s0, 3);

        result.Node.ShouldBe(s3);
        result.Node.ShouldNotBe(camp);
    }

    /// <summary>S1 negative control: the campfire clamp is stage-3-only.</summary>
    [Fact]
    public void The_campfire_clamp_does_not_apply_outside_stage_3()
    {
        var board = LinearFiveNodeBoard(); // all stage 1

        var natural = MovementEngine.Advance(board, new NodeId(0), 4);
        var portal = MovementEngine.AdvancePortal(board, new NodeId(0), 4);

        portal.ShouldBe(natural);
    }

    [Fact]
    public void A_portal_distance_outside_3_to_6_is_refused()
    {
        var (board, s0, _, _, _) = PortalStage3Board();

        Should.Throw<ArgumentOutOfRangeException>(() => MovementEngine.AdvancePortal(board, s0, 2));
        Should.Throw<ArgumentOutOfRangeException>(() => MovementEngine.AdvancePortal(board, s0, 7));
    }

    [Fact]
    public void DrawPortalDistance_always_answers_3_to_6_inclusive()
    {
        for (var position = 0UL; position < 40; position++)
        {
            var rng = DeterministicRng.OpenAt(0x1234UL, RngStreams.Board, position);

            MovementEngine.DrawPortalDistance(rng).ShouldBeInRange(3, 6);
        }
    }
}
