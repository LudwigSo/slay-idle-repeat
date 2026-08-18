using SlayIdleRepeat.Core.Rules.Board;
using Shouldly;
using Xunit;
using CoreBoard = SlayIdleRepeat.Core.Rules.Board.BoardGraph;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

/// <summary>
/// 🔒 <b>What may sit at the end of a board, and what may not sit anywhere else.</b> The content
/// half of the terminus question, over hand-authored <see cref="CoreBoard.FromLayout"/> layouts —
/// the topology half (only the terminus may dead-end) lives in <see cref="BoardTests"/>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b><see cref="MovementEngine.Advance"/> answers "is this the boss?" three different ways, and
/// only two of them are positional.</b> The dead-end arm (a node with no outgoing edge) and the
/// loop-exit arm (the node equals <see cref="CoreBoard.BossNodeId"/>) both ask about identity. The
/// third — the boss-exact rule that fires when a move crosses a stage boundary — asks whether the
/// next node's tile is <see cref="TileKind.Boss"/>, and asks nothing about which node that is. The
/// rule this file pins is what makes the third arm agree with the other two: <b>a boss tile may sit
/// only on the terminus</b>, so the tile-keyed question can no longer be answered "yes" at a node
/// the run has not actually reached.
/// </para>
/// <para>
/// The consequence is not cosmetic. <c>Handlers.RollDice</c> treats a boss report as "arrive, stop
/// the chain, do not gate" — so a boss tile parked on the far side of a stage boundary would swallow
/// that boundary's Stage Gate entirely and resolve a boss encounter in the middle of the board.
/// </para>
/// <para>
/// 🔴 <b>The other half of the question is open, and is deliberately left open here.</b> A terminus
/// carrying an ordinary tile is still accepted, and movement still reports a boss standing on it —
/// pinned below by
/// <see cref="A_terminus_carrying_an_ordinary_tile_is_still_accepted_and_still_reports_the_boss"/>
/// so that tightening it is a decision somebody takes rather than a side effect. It cannot be
/// tightened yet: the design set authors <em>three</em> different things that may end a board — a
/// chapter boss, a mini-boss, and a Resource Dungeon's Guardian, which is explicitly not a boss —
/// against a <see cref="TileKind"/> that has one member for them, and whose set is declared closed.
/// Requiring <see cref="TileKind.Boss"/> would bar the other two before either is built.
/// <see cref="The_tile_ids_the_terminus_ruling_needs_are_still_unrepresentable"/> is the expiry:
/// it fails the day the missing ids become representable, which is the day the tightening becomes
/// statable.
/// </para>
/// <para>
/// <b>What these cases do not close.</b> The shapes below discriminate an identity-keyed rule from
/// the three near-misses worth worrying about — one that merely counts boss tiles, one that keys on
/// the stage instead of the node, and one that walks only the linear index and so never sees a
/// branch. They do <em>not</em> discriminate it from a rule keyed on
/// <see cref="BoardNode.LinearIndex"/> ordering, because every fixture here gives the terminus the
/// highest index. Building one that does would mean authoring a layout whose branch indices
/// contradict the forward-distance mapping — a second malformation, which would make whichever rule
/// fired ambiguous.
/// </para>
/// </remarks>
public sealed class BoardTerminusTests
{
    /// <summary>
    /// The fragment that identifies this refusal rather than one of <see cref="CoreBoard.FromLayout"/>'s
    /// four others. All five answer <c>ParamName</c> <c>"nodes"</c> or a sibling, so steering S2's
    /// "pin which rule fired" has to be carried by the message.
    /// </summary>
    private const string BossTileRefusal = "carries the boss tile";

    /// <summary>The dead-end rule's own fragment, asserted absent so the two refusals are shown to be distinguishable.</summary>
    private const string DeadEndRefusal = "has no outgoing edge";

    /// <summary>
    /// Probe shape 1: the boss tile sits on the far side of a stage boundary. This is the shape the
    /// boss-exact rule fires on — <c>Advance</c> would report a boss encounter at node 1 and
    /// <c>RollDice</c> would skip stage 1's gate.
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
    /// Probe shape 2, and the one that makes the rule's <em>scope</em> measurable rather than just
    /// its existence: the boss tile sits inside a stage, where the boss-exact rule never looks. A
    /// guard written only against the boundary shape would accept this and let movement walk
    /// straight over a boss.
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
    /// Probe shape 3: a boss tile on a fork branch. A branch node is never in the linear index at
    /// all, so a rule that walked <c>spineByLinearIndex</c> instead of every supplied node would
    /// miss it entirely.
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
    /// Probe shape 4, and the one that separates this rule from the cheapest thing that looks like
    /// it: the board holds exactly <b>one</b> boss tile and is still refused, because that tile is
    /// not the terminus. A rule reading "at most one boss tile" would accept this layout — and it is
    /// precisely the layout that leaves the harm live, since movement would then report a boss at
    /// node 1 and stop three steps short of the board's real end.
    /// </summary>
    /// <remarks>
    /// The impostor is also given the stage the terminus holds, which closes the second near-miss:
    /// a rule keyed on the stage rather than on the node would let it through, and a stage is not
    /// what <c>Advance</c>'s identity arms compare against.
    /// </remarks>
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
    /// 🔴 S1 negative control: the ordinary, correct board. A rule that simply refused
    /// <see cref="TileKind.Boss"/> anywhere would reject every real board, and the sweep in
    /// <see cref="BoardTests"/> would be the only thing to notice.
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
    /// 🔴 <b>Today's behaviour, pinned so that changing it is deliberate rather than silent</b> — the
    /// residual arm, on the precedent of <c>SkipDraftTests.A_skip_leaves_all_three_draft_counters_standing</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is <em>also</em> the second negative control, and the sharper of the two: it proves the
    /// rule above is scoped to "a boss tile may only be the terminus" and is <b>not</b> the converse,
    /// "the terminus must be a boss tile". The two are easy to conflate and only one of them is
    /// authored.
    /// </para>
    /// <para>
    /// ⚠️ Whether an ordinary tile may end a board is the open product question this class's remarks
    /// set out. Whoever rules on it changes this test, and that is the intended cost.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// 🔒 <b>The expiry for the open half above</b> (steering S4: a declared exception must fail when
    /// it stops being true, including when it has been satisfied).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reason the terminus's tile kind cannot be required today is exactly this: the two tile
    /// ids the design set authors for a board's ending, beyond the chapter boss, cannot be spoken.
    /// <c>TILE_MINIBOSS</c> is authored as the final node of the tutorial's board — the first
    /// authored board there will be, and one whose validator is specified as binding — and
    /// <c>TILE_CACHE_DUNGEON</c> is authored for the Resource Dungeon profile, whose own terminus is
    /// a Guardian with no authored tile id at all. Neither is in the closed tile set, so neither
    /// parses.
    /// </para>
    /// <para>
    /// The day one of them does parse, whoever added it has decided what a board may end with, and
    /// this assertion fails to make them say so out loud —
    /// <see cref="A_terminus_carrying_an_ordinary_tile_is_still_accepted_and_still_reports_the_boss"/>
    /// is the pin they then have to revisit.
    /// </para>
    /// <para>
    /// The <c>TILE_BOSS</c> leg is the discriminator, not decoration: without it the whole assertion
    /// would also pass against a parser that recognised nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_tile_ids_the_terminus_ruling_needs_are_still_unrepresentable()
    {
        TileKindIds.TryParse("TILE_BOSS", out var boss).ShouldBeTrue("the negative control — a parser that answers false to everything must not pass this test.");
        boss.ShouldBe(TileKind.Boss);

        TileKindIds.TryParse("TILE_MINIBOSS", out _).ShouldBeFalse(
            "the tutorial board's final node is authored as a mini-boss tile. When this id becomes real, the terminus rule above can and must be tightened.");

        TileKindIds.TryParse("TILE_CACHE_DUNGEON", out _).ShouldBeFalse(
            "the Resource Dungeon profile authors this tile, and the same dungeon's terminus is a Guardian that is explicitly not a boss.");

        // The two ids above are the spellings the documents happen to use; the third ending — the
        // Guardian's own tile — has no authored id at all, so no string can stand for it. This is
        // the leg that catches a ruling that lands under any name.
        Enum.GetValues<TileKind>().Length.ShouldBe(
            14,
            "the tile set is declared closed. A new member means somebody has decided what a board may end with, and the pin above is theirs to revisit.");
    }
}
