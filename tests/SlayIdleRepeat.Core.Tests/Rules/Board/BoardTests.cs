using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using Shouldly;
using Xunit;
using CoreBoard = SlayIdleRepeat.Core.Rules.Board.BoardGraph;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

/// <summary>Tests <see cref="CoreBoard.FromLayout"/>, the seam a future authored-layout loader would call directly, bypassing <see cref="BoardGenerator"/>.</summary>
public sealed class BoardTests
{
    private static (BoardNode a, BoardNode b) TwoNodes() =>
        (new BoardNode(new NodeId(0), TileKind.Enemy, 0, 1), new BoardNode(new NodeId(1), TileKind.Boss, 1, CoreBoard.BossStage));

    [Fact]
    public void A_minimal_two_node_layout_round_trips()
    {
        var (a, b) = TwoNodes();
        var edge = new BoardEdge(a.Id, b.Id, EdgeKind.Continue);

        var board = CoreBoard.FromLayout(new[] { a, b }, new[] { edge }, new[] { a.Id, b.Id }, Array.Empty<NodeId>());

        board.NodeCount.ShouldBe(2);
        board.FirstNodeId.ShouldBe(a.Id);
        board.BossNodeId.ShouldBe(b.Id);
        board.OutgoingEdges(a.Id).ShouldHaveSingleItem();
        board.OutgoingEdges(b.Id).ShouldBeEmpty();
        board.IsJunction(a.Id).ShouldBeFalse();
    }

    [Fact]
    public void An_edge_referencing_an_unknown_node_is_rejected()
    {
        var (a, b) = TwoNodes();
        var stray = new BoardEdge(a.Id, new NodeId(99), EdgeKind.Continue);

        var ex = Should.Throw<ArgumentException>(() =>
            CoreBoard.FromLayout(new[] { a, b }, new[] { stray }, new[] { a.Id, b.Id }, Array.Empty<NodeId>()));

        ex.ParamName.ShouldBe("edges");
    }

    [Fact]
    public void A_linear_index_entry_referencing_an_unknown_node_is_rejected()
    {
        var (a, b) = TwoNodes();

        var ex = Should.Throw<ArgumentException>(() =>
            CoreBoard.FromLayout(new[] { a, b }, Array.Empty<BoardEdge>(), new[] { a.Id, new NodeId(99) }, Array.Empty<NodeId>()));

        ex.ParamName.ShouldBe("spineByLinearIndex");
    }

    [Fact]
    public void A_junction_without_exactly_two_outgoing_edges_is_rejected()
    {
        var (a, b) = TwoNodes();
        var edge = new BoardEdge(a.Id, b.Id, EdgeKind.Continue);

        var ex = Should.Throw<ArgumentException>(() =>
            CoreBoard.FromLayout(new[] { a, b }, new[] { edge }, new[] { a.Id, b.Id }, new[] { a.Id }));

        ex.ParamName.ShouldBe("junctionIds");
    }

    [Fact]
    public void An_empty_linear_index_is_rejected()
    {
        var (a, _) = TwoNodes();

        Should.Throw<ArgumentException>(() =>
            CoreBoard.FromLayout(new[] { a }, Array.Empty<BoardEdge>(), Array.Empty<NodeId>(), Array.Empty<NodeId>()));
    }

    [Fact]
    public void Looking_up_an_unknown_node_throws()
    {
        var (a, b) = TwoNodes();
        var edge = new BoardEdge(a.Id, b.Id, EdgeKind.Continue);
        var board = CoreBoard.FromLayout(new[] { a, b }, new[] { edge }, new[] { a.Id, b.Id }, Array.Empty<NodeId>());

        Should.Throw<KeyNotFoundException>(() => board.Node(new NodeId(999)));
        Should.Throw<KeyNotFoundException>(() => board.OutgoingEdges(new NodeId(999)));
    }

    // ------------------------------------------------------------------------------------------
    // Only the boss node may dead-end.
    //
    // MovementEngine.Advance ends movement and reports ReachedBoss the moment a node has no
    // outgoing edge, while the same method's fall-through return asks whether it is standing on
    // BossNodeId. The two readings agree only because this constructor guarantees they describe
    // the same node — so a board with any other dead end would report a boss encounter at a node
    // that is not the boss. 03 §1 authors the topology this enforces: every fork branch rejoins
    // the spine, so the boss is the run's single terminus.
    // ------------------------------------------------------------------------------------------

    /// <summary>Probe shape 1: a break in the spine itself — a node stops the walk before the boss.</summary>
    [Fact]
    public void A_spine_node_with_no_outgoing_edge_is_refused()
    {
        var a = new BoardNode(new NodeId(0), TileKind.Enemy, 0, 1);
        var b = new BoardNode(new NodeId(1), TileKind.Enemy, 1, 1); // never leads anywhere
        var boss = new BoardNode(new NodeId(2), TileKind.Boss, 2, CoreBoard.BossStage);

        var ex = Should.Throw<ArgumentException>(() => CoreBoard.FromLayout(
            new[] { a, b, boss },
            new[] { new BoardEdge(a.Id, b.Id, EdgeKind.Continue) },
            new[] { a.Id, b.Id, boss.Id },
            Array.Empty<NodeId>()));

        // Which rule fired, not merely that the board was refused (steering S2): "nodes" is this
        // refusal's own parameter — the phantom-edge rule answers "edges", the junction-arity rule
        // "junctionIds", the linear-index rule "spineByLinearIndex".
        ex.ParamName.ShouldBe("nodes");
        ex.Message.ShouldContain(b.Id.ToString(), Case.Sensitive, "the refusal must name the offending node.");
    }

    /// <summary>
    /// Probe shape 2: a fork branch that never rejoins the spine — the malformation 03 §1's
    /// "a fork branch ... rejoins the spine" rules out, and the one a real layout would hit.
    /// </summary>
    [Fact]
    public void A_fork_branch_that_never_rejoins_the_spine_is_refused()
    {
        var j = new BoardNode(new NodeId(0), TileKind.Enemy, 0, 1);
        var n1 = new BoardNode(new NodeId(1), TileKind.Enemy, 1, 1);
        var boss = new BoardNode(new NodeId(2), TileKind.Boss, 2, CoreBoard.BossStage);
        var b0 = new BoardNode(new NodeId(3), TileKind.Shrine, 1, 1); // branch entry, rejoins nothing

        var preview = new ForkPreview(ForkLabel.Sheltered, new[] { TileKind.Shrine });

        var ex = Should.Throw<ArgumentException>(() => CoreBoard.FromLayout(
            new[] { j, n1, boss, b0 },
            new[]
            {
                new BoardEdge(j.Id, n1.Id, EdgeKind.Continue),
                new BoardEdge(j.Id, b0.Id, EdgeKind.Branch, preview),
                new BoardEdge(n1.Id, boss.Id, EdgeKind.Continue),
            },
            new[] { j.Id, n1.Id, boss.Id },
            new[] { j.Id }));

        ex.ParamName.ShouldBe("nodes");
        ex.Message.ShouldContain(b0.Id.ToString(), Case.Sensitive, "the refusal must name the branch node that dead-ends.");
    }

    /// <summary>
    /// 🔴 S1 negative control: the guard must <em>accept</em> a legitimately dangling boss node.
    /// A guard that simply refused every dead end would reject every real board.
    /// </summary>
    [Fact]
    public void A_layout_whose_only_dead_end_is_the_boss_node_is_accepted()
    {
        var n0 = new BoardNode(new NodeId(0), TileKind.Enemy, 0, 1);
        var j = new BoardNode(new NodeId(1), TileKind.Enemy, 1, 1);
        var n2 = new BoardNode(new NodeId(2), TileKind.Enemy, 2, 1);
        var n3 = new BoardNode(new NodeId(3), TileKind.Enemy, 3, 1);
        var boss = new BoardNode(new NodeId(4), TileKind.Boss, 4, CoreBoard.BossStage);
        var b0 = new BoardNode(new NodeId(5), TileKind.Shrine, 2, 1);
        var b1 = new BoardNode(new NodeId(6), TileKind.Treasure, 3, 1);

        var preview = new ForkPreview(ForkLabel.Sheltered, new[] { TileKind.Shrine, TileKind.Treasure });

        var board = CoreBoard.FromLayout(
            new[] { n0, j, n2, n3, boss, b0, b1 },
            new[]
            {
                new BoardEdge(n0.Id, j.Id, EdgeKind.Continue),
                new BoardEdge(j.Id, n2.Id, EdgeKind.Continue),
                new BoardEdge(j.Id, b0.Id, EdgeKind.Branch, preview),
                new BoardEdge(n2.Id, n3.Id, EdgeKind.Continue),
                new BoardEdge(b0.Id, b1.Id, EdgeKind.Continue),
                new BoardEdge(b1.Id, n3.Id, EdgeKind.Continue), // rejoin
                new BoardEdge(n3.Id, boss.Id, EdgeKind.Continue),
            },
            new[] { n0.Id, j.Id, n2.Id, n3.Id, boss.Id },
            new[] { j.Id });

        DeadEnds(board).ShouldBe(new[] { board.BossNodeId });
        board.BossNodeId.ShouldBe(boss.Id);
    }

    /// <summary>
    /// 🔒 <see cref="BoardGenerator"/> must keep producing boards this guard accepts. Swept over
    /// real generated boards rather than reasoned about: six chapter configurations — the two
    /// shipped chapters, a tiny even-weighted one, two degenerate weight tables that force the
    /// redraw-exhaustion and C7-injection fallbacks, and one stressing stage geometry — times
    /// twenty seeds.
    /// </summary>
    /// <remarks>
    /// The dead-end set is re-derived by walking the graph through the public API, not read back
    /// off the constructor that just enforced it, so the sweep is evidence rather than a tautology.
    /// </remarks>
    [Fact]
    public void Every_generated_board_across_chapters_and_seeds_dead_ends_only_at_the_boss()
    {
        var configs = SweepConfigs();
        var swept = 0;

        foreach (var config in configs)
        {
            foreach (var seed in SweepSeeds)
            {
                var board = BoardGenerator.GenerateBoard(config, new DeterministicRng(seed, RngStreams.Board));

                var reachable = Reachable(board);
                reachable.Count.ShouldBe(
                    board.NodeCount,
                    $"chapter {config.ChapterId} seed {seed}: every node must be reachable from the first node.");

                DeadEnds(board).ShouldBe(
                    new[] { board.BossNodeId },
                    $"chapter {config.ChapterId} seed {seed}: the boss node is the run's only terminus.");

                board.Node(board.BossNodeId).Tile.ShouldBe(TileKind.Boss, $"chapter {config.ChapterId} seed {seed}");

                swept++;
            }
        }

        // Steering S3: a sweep whose subject set silently empties passes forever.
        swept.ShouldBe(configs.Count * SweepSeeds.Length);
        swept.ShouldBeGreaterThanOrEqualTo(100, "the sweep is only evidence if it actually generated boards.");
    }

    private static readonly ulong[] SweepSeeds =
    {
        1UL, 2UL, 3UL, 42UL, 1337UL, 99999UL, 0xC0FFEEUL, 0xDEADBEEFUL, 123456789UL, 987654321UL,
        1111UL, 2222UL, 3333UL, 4444UL, 5555UL, 6666UL, 7777UL, 8888UL, 9999UL, 10101UL,
    };

    private static IReadOnlyList<ChapterBoardConfig> SweepConfigs() => new[]
    {
        BoardFixtures.ChapterOneConfig(),
        ShippedChapterTwoConfig(),
        BoardFixtures.TinyConfig(),
        SingleKindConfig(chapterId: 81, TileKind.Portal),
        SingleKindConfig(chapterId: 82, TileKind.Curse),
        UnevenStageGeometryConfig(),
    };

    /// <summary>Chapter 2's shipped board numbers — its curse/elite weights climb where chapter 1's do not.</summary>
    private static ChapterBoardConfig ShippedChapterTwoConfig() => ChapterBoardConfig.From(
        chapterId: 2,
        stageLengths: new[] { 12, 14, 16 },
        eliteCount: new[] { 1, 2, 2 },
        tileWeights: new[]
        {
            SweepWeights(empty: 6, curse: 9, elite: 7),
            SweepWeights(empty: 4, curse: 10, elite: 8),
            SweepWeights(empty: 2, curse: 11, elite: 9),
        },
        bossId: "BOSS_GULGROT");

    /// <summary>
    /// A table the weighted draw can only answer one way, so every other tile on the board came
    /// from a constraint fallback or a mandatory placement — the paths a happy-path config never
    /// walks.
    /// </summary>
    private static ChapterBoardConfig SingleKindConfig(int chapterId, TileKind only) => ChapterBoardConfig.From(
        chapterId: chapterId,
        stageLengths: new[] { 12, 12, 12 },
        eliteCount: new[] { 0, 0, 0 },
        tileWeights: new[]
        {
            new Dictionary<TileKind, double> { [only] = 100.0 },
            new Dictionary<TileKind, double> { [only] = 100.0 },
            new Dictionary<TileKind, double> { [only] = 100.0 },
        },
        bossId: "BOSS_TEST");

    /// <summary>
    /// Stage lengths at both ends of what the fork window allows: 8 leaves room for a single fork
    /// candidate, 20 for several, 9 puts stage 3's campfire and its fork window close together.
    /// </summary>
    private static ChapterBoardConfig UnevenStageGeometryConfig() => ChapterBoardConfig.From(
        chapterId: 83,
        stageLengths: new[] { 8, 20, 9 },
        eliteCount: new[] { 1, 2, 2 },
        tileWeights: new[] { SweepWeights(), SweepWeights(), SweepWeights() },
        bossId: "BOSS_TEST");

    private static Dictionary<TileKind, double> SweepWeights(
        double empty = 12, double curse = 6, double elite = 4) => new()
    {
        [TileKind.Enemy] = 34,
        [TileKind.Empty] = empty,
        [TileKind.Shrine] = 9,
        [TileKind.Treasure] = 8,
        [TileKind.Event] = 8,
        [TileKind.Minigame] = 7,
        [TileKind.Curse] = curse,
        [TileKind.Shop] = 5,
        [TileKind.Elite] = elite,
        [TileKind.Cache] = 3,
        [TileKind.Portal] = 2,
        [TileKind.DiceForge] = 2,
    };

    /// <summary>Every node reachable from <see cref="CoreBoard.FirstNodeId"/>, walked through the public API.</summary>
    private static IReadOnlyCollection<NodeId> Reachable(CoreBoard board)
    {
        var seen = new HashSet<NodeId> { board.FirstNodeId };
        var pending = new Stack<NodeId>();
        pending.Push(board.FirstNodeId);

        while (pending.Count > 0)
        {
            foreach (var edge in board.OutgoingEdges(pending.Pop()))
            {
                if (seen.Add(edge.To))
                {
                    pending.Push(edge.To);
                }
            }
        }

        return seen;
    }

    private static NodeId[] DeadEnds(CoreBoard board) =>
        Reachable(board).Where(id => board.OutgoingEdges(id).Count == 0).OrderBy(id => id.Value).ToArray();
}
