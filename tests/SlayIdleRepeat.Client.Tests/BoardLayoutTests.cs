using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// <see cref="BoardLayout"/> — where every node of a board stands in three dimensions.
/// </summary>
/// <remarks>
/// 🔒 <b>The size cases are the point of this file.</b> The two shipped chapters author 43 nodes
/// across three stages, so a layout that had 43 or 3 written into it anywhere would satisfy every
/// other case here forever. A chapter authors its own stage lengths and its own fork density
/// (`16` D69), and a regular one is expected to run several times longer — so the long board below
/// is not an edge case, it is the ordinary one arriving early.
/// </remarks>
public sealed class BoardLayoutTests
{
    private const int Chapter = 4;

    private static readonly PlayerId Player = new("PLAYER_layout_5c11");
    private static readonly RunId Run = new("RUN_layout_9e04");

    /// <summary>
    /// Distances chosen to make a wrong one visible: the wander is wider than the spacing, so a
    /// branch offset applied along the wrong axis lands somewhere a case can see.
    /// </summary>
    private static readonly BoardLayoutMetrics Metrics = new(
        NodeSpacing: 2.5f, WindAmplitude: 3f, WindWavelength: 40f, BranchOffset: 4f);

    /// <summary>
    /// Every node of the board is placed, exactly once, and nothing else is — the defect being a
    /// layout that walks the spine and leaves a fork's branch nodes with nowhere to stand, which
    /// draws the hero at the origin for as long as the run is on a branch.
    /// </summary>
    [Fact]
    public void Every_node_the_board_holds_is_placed_exactly_once()
    {
        var board = Project(12, 14, 16);
        var layout = BoardLayout.Of(board, Metrics);

        layout.Placements.Count.ShouldBe(board.Nodes.Count);

        layout.Placements.Select(p => p.NodeId).ShouldBe(
            board.Nodes.Select(n => n.NodeId),
            "the placements are ordered as BoardView.Nodes is, so a caller can walk the two together.");

        foreach (var node in board.Nodes)
        {
            layout.Placement(node.NodeId).ShouldNotBeNull(
                "node " + node.NodeId + " is on the board and stands nowhere.");
        }

        layout.Placement(-1).ShouldBeNull("a node this board does not hold has no placement.");
    }

    /// <summary>
    /// A branch node stands beside the spine node carrying its own linear index, offset by exactly
    /// the authored distance — `03` §1.1's forward-index rule expressed as geometry.
    /// </summary>
    /// <remarks>
    /// The defect: a branch laid out by walking further ALONG the track instead of sideways. That
    /// makes the two ways out of a junction different lengths, which draws a fork as a shortcut —
    /// the one thing `03` §1.1 says a fork must never be.
    /// </remarks>
    [Fact]
    public void A_branch_node_stands_beside_the_spine_node_carrying_its_linear_index()
    {
        var board = Project(12, 14, 16);
        var layout = BoardLayout.Of(board, Metrics);
        var checkedNodes = 0;

        foreach (var fork in board.Forks)
        {
            foreach (var branchNodeId in fork.BranchNodeIds)
            {
                var branchNode = board.Node(branchNodeId).ShouldNotBeNull();
                var branch = layout.Placement(branchNodeId).ShouldNotBeNull();
                var alongside = layout.Placement(board.Spine[branchNode.LinearIndex].NodeId).ShouldNotBeNull();

                Distance(branch.Centre, alongside.Centre).ShouldBe(
                    Metrics.BranchOffset,
                    tolerance: 0.001f,
                    "a branch node stands exactly the authored offset from the spine node level with " +
                    "it, so the two ways out of a junction are parallel and equally long.");

                branch.OnSpine.ShouldBeFalse();
                checkedNodes++;
            }
        }

        checkedNodes.ShouldBeGreaterThan(0, "no fork on this board, so this case asserted nothing.");
    }

    /// <summary>
    /// The node a branch rejoins on is placed once, as a spine node, and the branch's last run of
    /// track leads back to it — so the fork visibly closes rather than trailing off.
    /// </summary>
    [Fact]
    public void A_branch_leads_back_onto_the_spine_node_it_rejoins()
    {
        var board = Project(12, 14, 16);
        var layout = BoardLayout.Of(board, Metrics);

        foreach (var fork in board.Forks)
        {
            var rejoin = layout.Placement(fork.RejoinNodeId).ShouldNotBeNull();

            rejoin.OnSpine.ShouldBeTrue("the rejoin is a spine node and is placed as one, once.");

            layout.Segments.ShouldContain(
                new BoardTrackSegment(fork.BranchNodeIds[^1], fork.RejoinNodeId, OnSpine: false),
                "without this run of track the branch stops in mid-air and `03` §8's clear join is " +
                "never drawn.");
        }
    }

    /// <summary>
    /// The segments are every spine adjacency plus every fork's junction-branch-rejoin chain, and
    /// nothing else.
    /// </summary>
    [Fact]
    public void The_segments_cover_the_spine_and_each_forks_whole_branch()
    {
        var board = Project(12, 14, 16);
        var layout = BoardLayout.Of(board, Metrics);

        var expected = new List<BoardTrackSegment>();

        for (var i = 1; i < board.Spine.Count; i++)
        {
            expected.Add(new BoardTrackSegment(board.Spine[i - 1].NodeId, board.Spine[i].NodeId, OnSpine: true));
        }

        foreach (var fork in board.Forks)
        {
            var previous = fork.JunctionNodeId;

            foreach (var branchNodeId in fork.BranchNodeIds)
            {
                expected.Add(new BoardTrackSegment(previous, branchNodeId, OnSpine: false));
                previous = branchNodeId;
            }

            expected.Add(new BoardTrackSegment(previous, fork.RejoinNodeId, OnSpine: false));
        }

        layout.Segments.OrderBy(s => s.FromNodeId).ThenBy(s => s.ToNodeId).ShouldBe(
            expected.OrderBy(s => s.FromNodeId).ThenBy(s => s.ToNodeId));
    }

    /// <summary>
    /// The trailhead stands one step short of the board's first node, on the same line.
    /// </summary>
    /// <remarks>
    /// The defect: no trailhead at all, so a run that has not rolled yet draws no hero — and the
    /// first thing a new player sees is a board they are not standing on. `03` §1.1 keeps the run at
    /// position −1 there and resolves nothing, but the token is drawn at the head of the track.
    /// </remarks>
    [Fact]
    public void The_trailhead_stands_one_step_short_of_the_boards_first_node()
    {
        var board = Project(12, 14, 16);
        var layout = BoardLayout.Of(board, Metrics);
        var first = layout.Placement(board.Spine[0].NodeId).ShouldNotBeNull();

        layout.Trailhead.NodeId.ShouldBe(
            BoardLayout.TrailheadNodeId,
            "the trailhead is not a node of any board, so it may not answer with an id one could hold.");

        layout.Placement(BoardLayout.TrailheadNodeId).ShouldBeNull(
            "and it is reachable only as the trailhead, never as a node lookup — a board that " +
            "answered it by id would have a fifty-fourth tile nothing generated.");

        var second = layout.Placement(board.Spine[1].NodeId).ShouldNotBeNull();

        (layout.Trailhead.Centre.Z - first.Centre.Z).ShouldBe(
            Metrics.NodeSpacing,
            tolerance: 0.001f,
            "exactly one node's step back along the track — and positive, because the board runs " +
            "away along negative Z, so the trailhead is BEHIND its first node rather than past it.");

        // Chord, not step: the track wanders, so consecutive nodes stand slightly further apart than
        // the spacing. Matching the first real step's chord is what says the trailhead is on the
        // same curve rather than merely the right distance away in some direction.
        Distance(layout.Trailhead.Centre, first.Centre).ShouldBe(
            Distance(first.Centre, second.Centre),
            tolerance: 0.05f,
            "the first roll must hop the same shape of step as every roll after it.");
    }

    /// <summary>The whole board's box encloses every placement, and no more than it needs to.</summary>
    [Fact]
    public void The_extent_encloses_every_placement()
    {
        var layout = BoardLayout.Of(Project(12, 14, 16), Metrics);

        foreach (var placement in layout.Placements)
        {
            placement.Centre.X.ShouldBeInRange(layout.Extent.Minimum.X, layout.Extent.Maximum.X);
            placement.Centre.Y.ShouldBeInRange(layout.Extent.Minimum.Y, layout.Extent.Maximum.Y);
            placement.Centre.Z.ShouldBeInRange(layout.Extent.Minimum.Z, layout.Extent.Maximum.Z);
        }

        layout.Placements.Select(p => p.Centre.Z).Min().ShouldBe(layout.Extent.Minimum.Z, 0.001f);
        layout.Placements.Select(p => p.Centre.Z).Max().ShouldBe(layout.Extent.Maximum.Z, 0.001f);
    }

    /// <summary>
    /// A stage's own box encloses that stage's nodes and none of another's — this is what the
    /// camera's first pull-back frames, and framing a stage means framing THAT stage.
    /// </summary>
    [Fact]
    public void A_stages_extent_encloses_that_stages_nodes_and_no_others()
    {
        var layout = BoardLayout.Of(Project(12, 14, 16), Metrics);
        var stages = layout.Placements.Select(p => p.Stage).Distinct().ToArray();

        stages.Length.ShouldBeGreaterThan(1, "a board of one stage would make this case vacuous.");

        foreach (var stage in stages)
        {
            var extent = layout.ExtentOfStage(stage).ShouldNotBeNull();
            var ownNodes = layout.Placements.Where(p => p.Stage == stage).ToArray();

            ownNodes.Select(p => p.Centre.Z).Min().ShouldBe(extent.Minimum.Z, 0.001f);
            ownNodes.Select(p => p.Centre.Z).Max().ShouldBe(extent.Maximum.Z, 0.001f);
        }
    }

    /// <summary>
    /// The negative control for the case above: a stage the board does not hold has no box, rather
    /// than an empty one at the origin that a camera would obediently fly to.
    /// </summary>
    [Fact]
    public void A_stage_the_board_does_not_hold_has_no_extent()
    {
        var layout = BoardLayout.Of(Project(12, 14, 16), Metrics);

        layout.ExtentOfStage(99).ShouldBeNull();
    }

    /// <summary>Laying the same board out twice gives the same answer.</summary>
    [Fact]
    public void The_same_board_and_metrics_lay_out_identically()
    {
        var board = Project(12, 14, 16);

        Describe(BoardLayout.Of(board, Metrics)).ShouldBe(Describe(BoardLayout.Of(board, Metrics)));
    }

    /// <summary>
    /// The discriminating negative: different metrics lay the same board out differently, so the
    /// determinism case above cannot be satisfied by a layout that ignores what it is handed.
    /// </summary>
    [Fact]
    public void Different_metrics_lay_the_same_board_out_differently()
    {
        var board = Project(12, 14, 16);
        var wider = Metrics with { NodeSpacing = Metrics.NodeSpacing * 2f };

        Describe(BoardLayout.Of(board, wider)).ShouldNotBe(Describe(BoardLayout.Of(board, Metrics)));
    }

    /// <summary>
    /// 🔒 A board several times longer than the shipped one, with several times the forks, lays out
    /// with every invariant above intact.
    /// </summary>
    /// <remarks>
    /// The defect this catches is any board size baked into the layout — a 43, a 3, a fork ceiling,
    /// an array sized to the shipped chapter. None of those shows up against a shipped board,
    /// because the shipped board is what they were copied from. A regular chapter is expected to run
    /// to roughly 150 nodes, so this is the board the renderer actually has to draw.
    /// </remarks>
    [Fact]
    public void A_board_several_times_longer_than_the_shipped_one_lays_out_intact()
    {
        var board = Project(45, 52, 52);
        var layout = BoardLayout.Of(board, Metrics);

        board.Spine.Count.ShouldBe(150, "45 + 52 + 52 spine nodes and the boss.");
        board.Nodes.Count.ShouldBeGreaterThan(
            board.Spine.Count, "a board with no branch node would not exercise the fork half.");

        layout.Placements.Count.ShouldBe(board.Nodes.Count);
        layout.Placements.Select(p => p.NodeId).Distinct().Count().ShouldBe(board.Nodes.Count);

        layout.Placements.Select(p => p.Stage).Distinct().Count().ShouldBe(
            4, "three stages and the boss's own, read off the placements rather than assumed.");

        foreach (var fork in board.Forks)
        {
            layout.Placement(fork.RejoinNodeId).ShouldNotBeNull().OnSpine.ShouldBeTrue();

            foreach (var branchNodeId in fork.BranchNodeIds)
            {
                layout.Placement(branchNodeId).ShouldNotBeNull().OnSpine.ShouldBeFalse();
            }
        }

        var length = layout.Extent.Maximum.Z - layout.Extent.Minimum.Z;

        length.ShouldBeGreaterThan(
            (board.Spine.Count - 1) * Metrics.NodeSpacing * 0.99f,
            "the board runs the whole length its node count and spacing imply — a layout that " +
            "wrapped or clamped a long board would come out short.");
    }

    // ----------------------------------------------------------------------------------------
    // Fixtures.
    // ----------------------------------------------------------------------------------------

    /// <summary>A board of the given stage lengths, projected the way the screen projects it.</summary>
    private static BoardView Project(params int[] stageLengths) =>
        BoardView.Project(
            PlayerState.Run(Run, Player, RunPhase.InProgress, chapterId: Chapter),
            BoardContent.Authoring(Chapter, stageLengths));

    private static float Distance(BoardPoint a, BoardPoint b) =>
        MathF.Sqrt(
            ((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)) + ((a.Z - b.Z) * (a.Z - b.Z)));

    /// <summary>A layout as text, so two of them compare as one value.</summary>
    private static string Describe(BoardLayout layout) =>
        string.Join(
            '\n',
            layout.Placements.Select(p =>
                $"{p.NodeId}@{p.Centre.X:F4},{p.Centre.Y:F4},{p.Centre.Z:F4}" +
                $"/{p.HeadingRadians:F4}/{p.OnSpine}/{p.Stage}"));
}
