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

    /// <summary>
    /// Fixture: the stage-end clamp, over a board with TWO boundaries so one move can cross the first
    /// and be stopped by the second.
    /// </summary>
    /// <remarks>
    /// Stage 1: N0 -> N1 (its last node). Stage 2: M0 -> M1 -> M2 (its last node). Stage 3: P0.
    /// Stage 2 is three nodes deep so a move leaving N1 can land inside it, and P0 exists so M2's
    /// clamp is a clamp rather than the end of the graph — a dangling node reports
    /// <c>ReachedBoss</c>, which would mask what is being measured.
    /// </remarks>
    private static (BoardGraph Board, NodeId N0, NodeId N1, NodeId M0, NodeId M1, NodeId M2) StageBoundaryBoard()
    {
        var n0 = new BoardNode(new NodeId(0), TileKind.Enemy, 0, 1);
        var n1 = new BoardNode(new NodeId(1), TileKind.Enemy, 1, 1); // stage 1's last node
        var m0 = new BoardNode(new NodeId(2), TileKind.Enemy, 2, 2); // stage 2's first node
        var m1 = new BoardNode(new NodeId(3), TileKind.Shrine, 3, 2);
        var m2 = new BoardNode(new NodeId(4), TileKind.Treasure, 4, 2); // stage 2's last node
        var p0 = new BoardNode(new NodeId(5), TileKind.Enemy, 5, 3);

        var nodes = new[] { n0, n1, m0, m1, m2, p0 };
        var edges = new[]
        {
            new BoardEdge(n0.Id, n1.Id, EdgeKind.Continue),
            new BoardEdge(n1.Id, m0.Id, EdgeKind.Continue),
            new BoardEdge(m0.Id, m1.Id, EdgeKind.Continue),
            new BoardEdge(m1.Id, m2.Id, EdgeKind.Continue),
            new BoardEdge(m2.Id, p0.Id, EdgeKind.Continue),
        };

        var board = BoardGraph.FromLayout(nodes, edges, nodes.Select(n => n.Id).ToArray(), Array.Empty<NodeId>());

        return (board, n0.Id, n1.Id, m0.Id, m1.Id, m2.Id);
    }

    [Fact]
    public void A_move_that_would_cross_a_stage_boundary_clamps_on_the_stages_last_node()
    {
        var (board, n0, n1, _, _, _) = StageBoundaryBoard();

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
        var (board, n0, n1, _, _, _) = StageBoundaryBoard();

        var result = MovementEngine.Advance(board, n0, 1);

        result.Node.ShouldBe(n1);
        result.RemainingSteps.ShouldBe(0, "the move landed exactly on the boundary node with nothing left over — not a clamp.");
    }

    /// <summary>
    /// 🔒 X-10. The clamp is a ONE-TIME stop, not a wall: the move it stops is the one that would
    /// have carried <em>past</em> the stage's last node, and the run standing on that node afterwards
    /// leaves it on its next move like any other node.
    /// </summary>
    /// <remarks>
    /// Two step counts rather than one (steering S1): a clamp that re-fired would answer
    /// <c>(n1, 2 unspent)</c> and <c>(n1, 3 unspent)</c> here, and both landings below discriminate
    /// against that — a single case could have been satisfied by an off-by-one in the step loop.
    /// </remarks>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public void A_move_taken_FROM_a_stages_last_node_crosses_into_the_next_stage(int steps, int expectedNode)
    {
        var (board, _, n1, _, _, _) = StageBoundaryBoard();

        var result = MovementEngine.Advance(board, n1, steps);

        result.Node.ShouldBe(
            new NodeId(expectedNode),
            "03 §1.1 clamps a move that would carry PAST a stage's last node. A move that STARTS " +
            "there has already paid that clamp, so it spends every step it was given — it does not " +
            "clamp again on the node it is standing on. A landing back on " + n1 + " is exactly that " +
            "second clamp, which is X-10.");
        result.RemainingSteps.ShouldBe(0, "every step was spent, so nothing is owed.");
        result.PausedAtJunction.ShouldBeFalse();
        result.ReachedBoss.ShouldBeFalse();

        // The identity, not the symptom (steering S2): the landing is in the NEXT stage, which is
        // what "the boundary was crossed" means. "It moved" alone is also true of a shorter hop.
        board.Node(result.Node).Stage.ShouldBe(2);
    }

    /// <summary>
    /// The clamp still bites once per move: a move leaving stage 1's last node that would carry past
    /// stage 2's last node stops on stage 2's last node.
    /// </summary>
    /// <remarks>
    /// S1's second probe shape for the same rule. It discriminates against a repair that disabled the
    /// clamp for the whole of a move that began on a boundary, rather than only for the node the move
    /// began on.
    /// </remarks>
    [Fact]
    public void A_move_leaving_one_boundary_still_clamps_at_the_NEXT_one()
    {
        var (board, _, n1, _, _, m2) = StageBoundaryBoard();

        // Stage 2 is three nodes deep, so 5 steps from n1 would carry two past its last node.
        var result = MovementEngine.Advance(board, n1, 5);

        result.Node.ShouldBe(m2, "the move crossed into stage 2 and then stopped on stage 2's last node.");
        result.RemainingSteps.ShouldBe(
            2, "the 5 requested minus the 3 spent reaching stage 2's last node — the clamp still owes them.");
        result.ReachedBoss.ShouldBeFalse();
    }

    /// <summary>
    /// 🔴 The <em>resumed</em> move — <c>Handlers.ChooseFork</c>'s shape — pinned, because the guard
    /// that repaired X-10 asks "has THIS call spent a step", and a resumed move is a fresh call
    /// continuing one roll.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A fork's chosen edge landing exactly on a stage's last node with movement still owed is the
    /// one shape where "this call began here" and "this move began here" disagree: the engine walks
    /// off the boundary instead of clamping on it, so that node is passed without resolving.
    /// </para>
    /// <para>
    /// 🔒 <b>Unreachable on a generated board, and the reason is not in this file.</b>
    /// <c>BoardGenerator</c> places every junction at a local index no later than
    /// <c>spineLength − 4</c> and every branch rejoins at <c>junction + branchLen</c>, so neither
    /// outgoing edge of a junction can land on the stage's last node. This case exists so the
    /// dependency is written down and a future widening of fork geometry turns up here rather than
    /// as a skipped tile in play.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_move_resumed_exactly_on_a_stages_last_node_walks_off_the_boundary()
    {
        var j = new BoardNode(new NodeId(0), TileKind.Enemy, 0, 1);
        var last1 = new BoardNode(new NodeId(1), TileKind.Enemy, 1, 1); // stage 1's last node
        var b0 = new BoardNode(new NodeId(2), TileKind.Shrine, 1, 1);
        var m0 = new BoardNode(new NodeId(3), TileKind.Enemy, 2, 2);
        var m1 = new BoardNode(new NodeId(4), TileKind.Enemy, 3, 2);

        var preview = new ForkPreview(ForkLabel.Sheltered, new[] { TileKind.Shrine });

        var board = BoardGraph.FromLayout(
            new[] { j, last1, b0, m0, m1 },
            new[]
            {
                new BoardEdge(j.Id, last1.Id, EdgeKind.Continue),
                new BoardEdge(j.Id, b0.Id, EdgeKind.Branch, preview),
                new BoardEdge(b0.Id, last1.Id, EdgeKind.Continue),
                new BoardEdge(last1.Id, m0.Id, EdgeKind.Continue),
                new BoardEdge(m0.Id, m1.Id, EdgeKind.Continue),
            },
            new[] { j.Id, last1.Id, m0.Id, m1.Id },
            new[] { j.Id });

        // ChooseFork's own shape: take the chosen edge, then resume with the rest.
        var chosen = board.OutgoingEdges(j.Id)[0];
        var result = MovementEngine.Advance(board, chosen.To, 1);

        chosen.To.ShouldBe(last1.Id, "the fixture only measures anything if the chosen edge lands on the boundary node.");
        result.Node.ShouldBe(m0.Id, "the resumed call began on the boundary node, so its clamp is treated as already paid.");
        result.RemainingSteps.ShouldBe(0);
        result.PausedAtJunction.ShouldBeFalse();
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

    /// <summary>
    /// 🔒 The boss-exact rule is the stage-end clamp's <em>one named exception</em>, and this is the
    /// case that shows it: a move that reaches stage 3's last node <em>mid-move</em> carries on onto
    /// the boss instead of being clamped there.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Written because a mutation proved the rule was untested (steering S1).</b> Deleting the
    /// boss branch outright left all 5 878 Core tests green.
    /// <see cref="Any_roll_from_stage_3s_last_node_moves_exactly_one_step_onto_the_boss"/> does not
    /// discriminate it: a move that starts on stage 3's last node reaches the boss either way, since
    /// the boss node has no outgoing edge and the loop's "out of edges" branch reports it. Only a
    /// move that has already spent a step — where the stage-end clamp would otherwise bite — tells
    /// the two implementations apart.
    /// </remarks>
    [Fact]
    public void A_move_that_reaches_stage_3s_last_node_mid_move_carries_on_onto_the_boss()
    {
        var (board, s0, s1, boss) = BossExactBoard();

        // 4 steps from S0: one reaches stage 3's last node, and the stage-end clamp would stop there.
        var result = MovementEngine.Advance(board, s0, 4);

        result.Node.ShouldBe(
            boss,
            "03 §1.1 makes the boss transition the stage-end clamp's named exception — the boss node " +
            "is always reached exactly, so a move that arrives at stage 3's last node with steps " +
            "still owed spends one more onto the boss rather than stopping.");
        result.Node.ShouldNotBe(s1, "the move was clamped on stage 3's last node, so the boss-exact rule did not fire.");
        result.ReachedBoss.ShouldBeTrue();
        result.RemainingSteps.ShouldBe(0, "reaching the boss ends the move whatever is left unspent.");
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
