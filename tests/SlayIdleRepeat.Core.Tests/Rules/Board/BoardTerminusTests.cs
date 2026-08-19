using SlayIdleRepeat.Core.Rules.Board;
using Shouldly;
using Xunit;
using CoreBoard = SlayIdleRepeat.Core.Rules.Board.BoardGraph;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

/// <summary>
/// A boss tile may sit only on the terminus — the content half of the terminus question, over
/// hand-authored <see cref="CoreBoard.FromLayout"/> layouts; the topology half (only the terminus
/// may dead-end) lives in <see cref="BoardTests"/>.
/// </summary>
/// <remarks>
/// The rule makes <see cref="MovementEngine.Advance"/>'s tile-keyed boss-exact arm agree with its
/// two identity-keyed arms: without it, a boss tile parked on the far side of a stage boundary
/// would swallow that boundary's Stage Gate and resolve a boss encounter mid-board. Internal seam
/// by necessity: <c>BoardGenerator</c> never produces these shapes, so no command can reach them —
/// <c>FromLayout</c> is the seam an authored-layout loader would call.
/// </remarks>
public sealed class BoardTerminusTests
{
    /// <summary>The fragment that identifies this refusal among <see cref="CoreBoard.FromLayout"/>'s others.</summary>
    private const string BossTileRefusal = "carries the boss tile";

    /// <summary>The dead-end rule's own fragment, asserted absent so the two refusals are shown to be distinguishable.</summary>
    private const string DeadEndRefusal = "has no outgoing edge";

    /// <summary>
    /// The boss tile on the far side of a stage boundary — the shape the boss-exact rule fires on,
    /// where <c>RollDice</c> would skip stage 1's gate.
    /// </summary>
    [Fact]
    public void A_boss_tile_at_a_stage_boundary_that_is_not_the_boss_node_is_refused()
    {
        var a = new BoardNode(new NodeId(0), TileKind.Enemy, 0, 1);
        var impostor = new BoardNode(new NodeId(1), TileKind.Boss, 1, 2); // stage 2, so the boss-exact rule sees a boundary
        var c = new BoardNode(new NodeId(2), TileKind.Enemy, 2, 2);
        var terminus = new BoardNode(new NodeId(3), TileKind.Boss, 3, CoreBoard.BossStage);

        var ex = Should.Throw<ArgumentException>(() => CoreBoard.FromLayout(
            new[] { a, impostor, c, terminus },
            new[]
            {
                new BoardEdge(a.Id, impostor.Id, EdgeKind.Continue),
                new BoardEdge(impostor.Id, c.Id, EdgeKind.Continue),
                new BoardEdge(c.Id, terminus.Id, EdgeKind.Continue),
            },
            new[] { a.Id, impostor.Id, c.Id, terminus.Id },
            Array.Empty<NodeId>()));

        ex.Message.ShouldContain(BossTileRefusal, Case.Sensitive, "steering S2: which rule fired, not merely that the layout was refused.");
        ex.Message.ShouldNotContain(DeadEndRefusal, Case.Sensitive, "the layout has no second dead end, so the topology rule must not be the one answering.");
        ex.Message.ShouldContain(impostor.Id.ToString(), Case.Sensitive, "the refusal must name the offending node.");
        ex.Message.ShouldContain(terminus.Id.ToString(), Case.Sensitive, "and the node it should have been.");
        ex.ParamName.ShouldBe("nodes");
    }

    /// <summary>
    /// The boss tile inside a stage, where the boss-exact rule never looks — a guard written only
    /// against the boundary shape would accept this and let movement walk straight over a boss.
    /// </summary>
    [Fact]
    public void A_boss_tile_inside_a_stage_is_refused_even_though_movement_would_walk_over_it()
    {
        var a = new BoardNode(new NodeId(0), TileKind.Enemy, 0, 1);
        var impostor = new BoardNode(new NodeId(1), TileKind.Boss, 1, 1); // same stage as its predecessor
        var c = new BoardNode(new NodeId(2), TileKind.Enemy, 2, 1);
        var terminus = new BoardNode(new NodeId(3), TileKind.Boss, 3, CoreBoard.BossStage);

        var ex = Should.Throw<ArgumentException>(() => CoreBoard.FromLayout(
            new[] { a, impostor, c, terminus },
            new[]
            {
                new BoardEdge(a.Id, impostor.Id, EdgeKind.Continue),
                new BoardEdge(impostor.Id, c.Id, EdgeKind.Continue),
                new BoardEdge(c.Id, terminus.Id, EdgeKind.Continue),
            },
            new[] { a.Id, impostor.Id, c.Id, terminus.Id },
            Array.Empty<NodeId>()));

        ex.Message.ShouldContain(BossTileRefusal, Case.Sensitive);
        ex.Message.ShouldContain(impostor.Id.ToString(), Case.Sensitive);
        ex.ParamName.ShouldBe("nodes");
    }

    /// <summary>
    /// A boss tile on a fork branch: a branch node is never in the linear index, so a rule that
    /// walked <c>spineByLinearIndex</c> instead of every supplied node would miss it.
    /// </summary>
    [Fact]
    public void A_boss_tile_on_a_fork_branch_is_refused()
    {
        var j = new BoardNode(new NodeId(0), TileKind.Enemy, 0, 1);
        var spine = new BoardNode(new NodeId(1), TileKind.Enemy, 1, 1);
        var rejoin = new BoardNode(new NodeId(2), TileKind.Enemy, 2, 1);
        var terminus = new BoardNode(new NodeId(3), TileKind.Boss, 3, CoreBoard.BossStage);
        var branch = new BoardNode(new NodeId(4), TileKind.Boss, 1, 1); // off the linear index entirely

        var preview = new ForkPreview(ForkLabel.Perilous, new[] { TileKind.Boss });

        var ex = Should.Throw<ArgumentException>(() => CoreBoard.FromLayout(
            new[] { j, spine, rejoin, terminus, branch },
            new[]
            {
                new BoardEdge(j.Id, spine.Id, EdgeKind.Continue),
                new BoardEdge(j.Id, branch.Id, EdgeKind.Branch, preview),
                new BoardEdge(spine.Id, rejoin.Id, EdgeKind.Continue),
                new BoardEdge(branch.Id, rejoin.Id, EdgeKind.Continue),
                new BoardEdge(rejoin.Id, terminus.Id, EdgeKind.Continue),
            },
            new[] { j.Id, spine.Id, rejoin.Id, terminus.Id },
            new[] { j.Id }));

        ex.Message.ShouldContain(BossTileRefusal, Case.Sensitive);
        ex.Message.ShouldContain(branch.Id.ToString(), Case.Sensitive);
        ex.ParamName.ShouldBe("nodes");
    }

    /// <summary>
    /// Exactly one boss tile, still refused because it is not the terminus — a rule reading "at
    /// most one boss tile" would accept this layout. The impostor also carries the terminus's
    /// stage, so a rule keyed on the stage rather than the node would let it through too.
    /// </summary>
    [Fact]
    public void A_boss_tile_that_is_the_only_one_on_the_board_is_still_refused_when_it_is_not_the_terminus()
    {
        var a = new BoardNode(new NodeId(0), TileKind.Enemy, 0, 1);
        var impostor = new BoardNode(new NodeId(1), TileKind.Boss, 1, CoreBoard.BossStage);
        var c = new BoardNode(new NodeId(2), TileKind.Enemy, 2, 3);
        var terminus = new BoardNode(new NodeId(3), TileKind.Enemy, 3, CoreBoard.BossStage);

        var ex = Should.Throw<ArgumentException>(() => CoreBoard.FromLayout(
            new[] { a, impostor, c, terminus },
            new[]
            {
                new BoardEdge(a.Id, impostor.Id, EdgeKind.Continue),
                new BoardEdge(impostor.Id, c.Id, EdgeKind.Continue),
                new BoardEdge(c.Id, terminus.Id, EdgeKind.Continue),
            },
            new[] { a.Id, impostor.Id, c.Id, terminus.Id },
            Array.Empty<NodeId>()));

        ex.Message.ShouldContain(BossTileRefusal, Case.Sensitive);
        ex.Message.ShouldContain(impostor.Id.ToString(), Case.Sensitive);
        ex.Message.ShouldNotContain(DeadEndRefusal, Case.Sensitive);
        ex.ParamName.ShouldBe("nodes");
    }

    /// <summary>
    /// Negative control: a rule that simply refused <see cref="TileKind.Boss"/> anywhere would
    /// reject every real board.
    /// </summary>
    [Fact]
    public void A_layout_whose_only_boss_tile_is_the_terminus_is_accepted()
    {
        var a = new BoardNode(new NodeId(0), TileKind.Enemy, 0, 3);
        var b = new BoardNode(new NodeId(1), TileKind.Campfire, 1, 3);
        var terminus = new BoardNode(new NodeId(2), TileKind.Boss, 2, CoreBoard.BossStage);

        var board = CoreBoard.FromLayout(
            new[] { a, b, terminus },
            new[]
            {
                new BoardEdge(a.Id, b.Id, EdgeKind.Continue),
                new BoardEdge(b.Id, terminus.Id, EdgeKind.Continue),
            },
            new[] { a.Id, b.Id, terminus.Id },
            Array.Empty<NodeId>());

        board.BossNodeId.ShouldBe(terminus.Id);
        board.Node(board.BossNodeId).Tile.ShouldBe(TileKind.Boss);

        // The three answers now coincide, which is the whole point of the rule: the tile-keyed
        // boss-exact arm fires on the same node the identity arms name.
        var result = MovementEngine.Advance(board, b.Id, 4);
        result.Node.ShouldBe(terminus.Id);
        result.ReachedBoss.ShouldBeTrue();
    }

    /// <summary>
    /// Today's behaviour, pinned so changing it is deliberate: the rule is "a boss tile may only be
    /// the terminus", NOT the converse "the terminus must be a boss tile" — the converse cannot be
    /// authored until a mini-boss tile id exists (M4-12).
    /// </summary>
    [Fact]
    public void A_terminus_carrying_an_ordinary_tile_is_still_accepted_and_still_reports_the_boss()
    {
        var a = new BoardNode(new NodeId(0), TileKind.Enemy, 0, 3);
        var terminus = new BoardNode(new NodeId(1), TileKind.Enemy, 1, CoreBoard.BossStage);

        var board = CoreBoard.FromLayout(
            new[] { a, terminus },
            new[] { new BoardEdge(a.Id, terminus.Id, EdgeKind.Continue) },
            new[] { a.Id, terminus.Id },
            Array.Empty<NodeId>());

        board.BossNodeId.ShouldBe(terminus.Id);
        board.Node(board.BossNodeId).Tile.ShouldBe(
            TileKind.Enemy,
            "the content half of the terminus question is open: nothing yet requires a boss-tier tile here.");

        var result = MovementEngine.Advance(board, a.Id, 3);
        result.Node.ShouldBe(terminus.Id);
        result.ReachedBoss.ShouldBeTrue(
            "movement's dead-end and loop-exit arms are positional, so an ordinary terminus is still announced as the boss.");
    }

    /// <summary>
    /// Both rules apply to a mid-board boss tile that also dead-ends. The topology rule answers
    /// first, deliberately: "this node leads nowhere" is the more actionable diagnosis for a layout
    /// that stopped early, and a reader who fixes it gets the content complaint on the next attempt.
    /// </summary>
    [Fact]
    public void A_dangling_mid_board_boss_tile_is_refused_by_the_dead_end_rule_first()
    {
        var a = new BoardNode(new NodeId(0), TileKind.Enemy, 0, 1);
        var impostor = new BoardNode(new NodeId(1), TileKind.Boss, 1, 2); // wrong tile AND leads nowhere
        var terminus = new BoardNode(new NodeId(2), TileKind.Boss, 2, CoreBoard.BossStage);

        var ex = Should.Throw<ArgumentException>(() => CoreBoard.FromLayout(
            new[] { a, impostor, terminus },
            new[] { new BoardEdge(a.Id, impostor.Id, EdgeKind.Continue) },
            new[] { a.Id, impostor.Id, terminus.Id },
            Array.Empty<NodeId>()));

        ex.Message.ShouldContain(DeadEndRefusal, Case.Sensitive);
        ex.Message.ShouldNotContain(BossTileRefusal, Case.Sensitive);
    }
}
