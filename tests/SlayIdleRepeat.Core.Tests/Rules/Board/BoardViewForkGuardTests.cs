using Shouldly;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Tests.BalanceHarness;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

/// <summary>
/// 🔒 <b><c>BoardView</c>'s fork-projection guard</b>: a junction that is not laid out
/// <c>Continue</c>-then-<c>Branch</c>-carrying-a-preview is refused by name, rather than published
/// with <c>ContinueNodeId</c> and the branch chain the wrong way round, and a branch that cannot be
/// walked to a rejoin is refused rather than published half-named.
/// </summary>
/// <remarks>
/// Internal seam by necessity: <c>BoardView.Project</c> replays the board from the run seed, so it
/// only ever sees generator boards, whose junctions are always emitted in the required order — the
/// guard is unreachable from the public door. The producer that CAN lay a junction out otherwise is
/// <c>BoardGraph.FromLayout</c>, reaching the projection through the internal <c>BoardView.ForksOf</c>.
/// The stakes: <c>CHOOSE_FORK</c> names the branch by index, so swapped ids would move the run
/// somewhere the player did not pick. The <c>edges.Count != 2</c> arm has no case here because
/// <c>FromLayout</c> already refuses a junction without exactly two edges.
/// </remarks>
public sealed class BoardViewForkGuardTests
{
    /// <summary>The stage every node of the hand-built layout belongs to.</summary>
    private const int Stage1 = 1;

    /// <summary>The chapter and seed the fidelity case reads, matching <see cref="BoardViewTests"/>.</summary>
    private const int Chapter = 1;

    private const ulong FixedSeed = 0x00C0FFEE_00C0FFEEUL;

    private static readonly NodeId Start = new(0);
    private static readonly NodeId Junction = new(1);
    private static readonly NodeId Rejoin = new(2);
    private static readonly NodeId Boss = new(3);
    private static readonly NodeId BranchEntry = new(4);
    private static readonly NodeId BranchSecond = new(5);

    /// <summary>The preview a well-formed branch edge carries — the branch's own single tile.</summary>
    private static ForkPreview Preview() =>
        new(ForkLabel.Perilous, new[] { TileKind.Elite });

    /// <summary>
    /// `03` §1.1 — the negative control: without it, the three refusals below would all pass
    /// against a projection that threw on every hand-built board.
    /// </summary>
    [Fact]
    public void A_well_formed_junction_projects_to_a_fork_naming_the_continuation_and_the_branch()
    {
        var board = Layout(
            new BoardEdge(Junction, Rejoin, EdgeKind.Continue),
            new BoardEdge(Junction, BranchEntry, EdgeKind.Branch, Preview()));

        var forks = BoardView.ForksOf(board, NodeIdOrder(), OnSpine());

        var fork = forks.ShouldHaveSingleItem();

        fork.JunctionNodeId.ShouldBe(Junction.Value);
        fork.ContinueNodeId.ShouldBe(
            Rejoin.Value, "the Continue edge is the way that stays on the spine.");
        fork.BranchNodeIds.ShouldBe(
            new[] { BranchEntry.Value },
            "the branch is named whole and in walk order; the Branch edge reaches its first node.");
        fork.RejoinNodeId.ShouldBe(
            Rejoin.Value,
            "the branch is walked to the first node on the spine it reaches, which is where the two " +
            "ways out meet again.");
        fork.BranchLabel.ShouldBe(ForkLabel.Perilous);
        fork.BranchIcons.ShouldBe(new[] { TileKind.Elite });
    }

    /// <summary>
    /// `03` §1.1 — a junction whose two edges are ordered <c>Branch</c> then <c>Continue</c> is
    /// refused by name, rather than projected with the two node ids swapped.
    /// </summary>
    /// <remarks>
    /// The <c>Continue</c> edge deliberately carries a preview too: with it null the missing-preview
    /// arm refuses this board as well, and the case would stay green with the order check deleted.
    /// </remarks>
    [Fact]
    public void A_junction_whose_edges_are_ordered_Branch_then_Continue_is_refused_by_name()
    {
        var board = Layout(
            new BoardEdge(Junction, BranchEntry, EdgeKind.Branch, Preview()),
            new BoardEdge(Junction, Rejoin, EdgeKind.Continue, Preview()));

        var ex = Should.Throw<InvalidOperationException>(
            () => BoardView.ForksOf(board, NodeIdOrder(), OnSpine()));

        ex.Message.ShouldContain(
            Junction.ToString(),
            Case.Sensitive,
            "the refusal has to say WHICH junction is malformed — a board carries several, and a " +
            "message that named none would send a reader looking through all of them.");

        ex.Message.ShouldContain(
            "Continue edge then a Branch edge",
            Case.Sensitive,
            "the ORDER rule is the one that fired here, and pinning the substring is what tells this " +
            "case apart from the null-preview refusal below — an assertion on the exception type " +
            "alone would pass on either.");
    }

    /// <summary>
    /// `03` §1.1 — a junction whose second edge is not a <c>Branch</c> edge at all is refused by
    /// name. The edge carries a preview so its KIND is the only thing this case can be refused for.
    /// </summary>
    [Fact]
    public void A_junction_whose_second_edge_is_not_a_branch_is_refused_by_name()
    {
        var board = Layout(
            new BoardEdge(Junction, Rejoin, EdgeKind.Continue),
            new BoardEdge(Junction, BranchEntry, EdgeKind.Continue, Preview()));

        var ex = Should.Throw<InvalidOperationException>(
            () => BoardView.ForksOf(board, NodeIdOrder(), OnSpine()));

        ex.Message.ShouldContain(Junction.ToString(), Case.Sensitive);
        ex.Message.ShouldContain(
            "Continue edge then a Branch edge",
            Case.Sensitive,
            "a junction with two Continue edges offers no branch, so the same order-and-kind rule is " +
            "what refuses it.");
    }

    /// <summary>
    /// `03` §1.1 — a correctly-ordered junction whose <c>Branch</c> edge carries NO preview is
    /// refused by name: a preview derived from the label would promise tiles the branch does not hold.
    /// </summary>
    [Fact]
    public void A_branch_edge_carrying_no_preview_is_refused_by_name()
    {
        var board = Layout(
            new BoardEdge(Junction, Rejoin, EdgeKind.Continue),
            new BoardEdge(Junction, BranchEntry, EdgeKind.Branch));

        var ex = Should.Throw<InvalidOperationException>(
            () => BoardView.ForksOf(board, NodeIdOrder(), OnSpine()));

        ex.Message.ShouldContain(Junction.ToString(), Case.Sensitive);
        ex.Message.ShouldContain(
            "carrying a preview",
            Case.Sensitive,
            "the missing preview is what fired, and this substring is what tells this case apart " +
            "from the two order refusals above.");
        ex.Message.ShouldContain(
            "never derived from the label",
            Case.Sensitive,
            "the refusal states the alternative it forecloses, which is the whole reason it is a " +
            "throw rather than a fallback.");
    }

    /// <summary>
    /// `03` §3 step 4 — a branch node that is itself a junction is refused by name, rather than
    /// projected as a branch whose second choice no fork entry describes.
    /// </summary>
    /// <remarks>
    /// Unreachable from a generated board: <c>BoardGenerator</c> reserves a span per fork so no
    /// junction can land inside another fork's branch. <c>FromLayout</c> does not check it, and a
    /// fork inside a fork would pause a run at a junction this projection publishes no way out of.
    /// </remarks>
    [Fact]
    public void A_branch_node_that_is_itself_a_junction_is_refused_by_name()
    {
        var board = NestedForkLayout();

        var ex = Should.Throw<InvalidOperationException>(
            () => BoardView.ForksOf(board, NestedNodeIdOrder(), OnSpine()));

        ex.Message.ShouldContain(BranchEntry.ToString(), Case.Sensitive);
        ex.Message.ShouldContain(
            "is itself a junction",
            Case.Sensitive,
            "this substring is what tells the nested-fork refusal apart from the never-rejoins one " +
            "below - both are branch-walk failures, and an assertion on the type alone passes on either.");
    }

    /// <summary>
    /// `03` §3 step 4 — a branch that never reaches the spine again is refused by name. Every
    /// branch rejoins; one that does not would have this view publish a fork whose two ways out
    /// never meet.
    /// </summary>
    /// <remarks>
    /// The shape is a loop rather than a dangling chain, because <c>FromLayout</c> already refuses a
    /// node with no outgoing edge - a chain that simply stopped would never reach this walk.
    /// </remarks>
    [Fact]
    public void A_branch_that_never_returns_to_the_spine_is_refused_by_name()
    {
        var board = LoopingBranchLayout();

        var ex = Should.Throw<InvalidOperationException>(
            () => BoardView.ForksOf(board, NestedNodeIdOrder(), OnSpine()));

        ex.Message.ShouldContain(Junction.ToString(), Case.Sensitive);
        ex.Message.ShouldContain(
            "never returns to the spine",
            Case.Sensitive,
            "the rejoin rule is what fired, and the substring is what separates this from the " +
            "nested-fork refusal above.");
    }

    /// <summary>
    /// The seam is the same code the public door runs: over a real generated board, <c>ForksOf</c>
    /// yields exactly the forks <c>BoardView.Project</c> publishes — otherwise the cases above
    /// would pin a helper <c>Project</c> could have stopped calling.
    /// </summary>
    [Fact]
    public void The_internal_seam_yields_the_same_forks_the_public_projection_publishes()
    {
        var board = BoardGenerator.GenerateBoard(
            ChapterBoardTuning.Read(ShippedHarness.Content, Chapter),
            DeterministicRng.OpenAt(FixedSeed, RngStreams.Board, 0));

        var published = BoardView
            .Project(RunSnapshots.With(chapterId: Chapter, runSeed: FixedSeed), ShippedHarness.Content)
            .Forks;

        var direct = BoardView.ForksOf(board, Reachable(board), Spine(board));

        published.Count.ShouldBeGreaterThan(0, "a board with no fork would make this case vacuous.");
        direct.Length.ShouldBe(published.Count);

        for (var i = 0; i < direct.Length; i++)
        {
            direct[i].JunctionNodeId.ShouldBe(published[i].JunctionNodeId);
            direct[i].ContinueNodeId.ShouldBe(published[i].ContinueNodeId);
            direct[i].BranchNodeIds.ShouldBe(published[i].BranchNodeIds);
            direct[i].RejoinNodeId.ShouldBe(published[i].RejoinNodeId);
            direct[i].BranchLabel.ShouldBe(published[i].BranchLabel);
            direct[i].BranchIcons.ShouldBe(published[i].BranchIcons);
        }
    }

    // ----------------------------------------------------------------------------------------
    // Fixtures.
    // ----------------------------------------------------------------------------------------

    /// <summary>
    /// The one hand-built board every refusal case varies: spine <c>0 → 1 → 2 → boss</c> with node
    /// 1 a junction, and a one-node branch at 4 that rejoins onto node 2.
    /// </summary>
    /// <remarks>
    /// The junction's two edges are the parameters, in the order they are handed over — the order
    /// <c>FromLayout</c> preserves and the projection reads.
    /// </remarks>
    private static BoardGraph Layout(BoardEdge first, BoardEdge second) => BoardGraph.FromLayout(
        new[]
        {
            new BoardNode(Start, TileKind.Enemy, 0, Stage1),
            new BoardNode(Junction, TileKind.Empty, 1, Stage1),
            new BoardNode(Rejoin, TileKind.Enemy, 2, Stage1),
            new BoardNode(Boss, TileKind.Boss, 3, BoardGraph.BossStage),
            new BoardNode(BranchEntry, TileKind.Elite, 2, Stage1),
        },
        new[]
        {
            new BoardEdge(Start, Junction, EdgeKind.Continue),
            first,
            second,
            new BoardEdge(BranchEntry, Rejoin, EdgeKind.Continue),
            new BoardEdge(Rejoin, Boss, EdgeKind.Continue),
        },
        new[] { Start, Junction, Rejoin, Boss },
        new[] { Junction });

    /// <summary>
    /// The same spine, with the branch entry turned into a junction of its own: it continues onto
    /// the rejoin and branches again onto a second off-spine node.
    /// </summary>
    private static BoardGraph NestedForkLayout() => BoardGraph.FromLayout(
        NestedNodes(),
        new[]
        {
            new BoardEdge(Start, Junction, EdgeKind.Continue),
            new BoardEdge(Junction, Rejoin, EdgeKind.Continue),
            new BoardEdge(Junction, BranchEntry, EdgeKind.Branch, Preview()),
            new BoardEdge(BranchEntry, Rejoin, EdgeKind.Continue),
            new BoardEdge(BranchEntry, BranchSecond, EdgeKind.Branch, Preview()),
            new BoardEdge(BranchSecond, Rejoin, EdgeKind.Continue),
            new BoardEdge(Rejoin, Boss, EdgeKind.Continue),
        },
        new[] { Start, Junction, Rejoin, Boss },
        new[] { Junction, BranchEntry });

    /// <summary>The same spine, with a two-node branch that loops back on itself instead of rejoining.</summary>
    private static BoardGraph LoopingBranchLayout() => BoardGraph.FromLayout(
        NestedNodes(),
        new[]
        {
            new BoardEdge(Start, Junction, EdgeKind.Continue),
            new BoardEdge(Junction, Rejoin, EdgeKind.Continue),
            new BoardEdge(Junction, BranchEntry, EdgeKind.Branch, Preview()),
            new BoardEdge(BranchEntry, BranchSecond, EdgeKind.Continue),
            new BoardEdge(BranchSecond, BranchEntry, EdgeKind.Continue),
            new BoardEdge(Rejoin, Boss, EdgeKind.Continue),
        },
        new[] { Start, Junction, Rejoin, Boss },
        new[] { Junction });

    /// <summary>The refusal layouts' nodes: the four-node spine plus two off-spine branch nodes.</summary>
    private static BoardNode[] NestedNodes() => new[]
    {
        new BoardNode(Start, TileKind.Enemy, 0, Stage1),
        new BoardNode(Junction, TileKind.Empty, 1, Stage1),
        new BoardNode(Rejoin, TileKind.Enemy, 2, Stage1),
        new BoardNode(Boss, TileKind.Boss, 3, BoardGraph.BossStage),
        new BoardNode(BranchEntry, TileKind.Elite, 2, Stage1),
        new BoardNode(BranchSecond, TileKind.Curse, 2, Stage1),
    };

    /// <summary>The hand-built board's nodes in node-id order, as the projection walks them.</summary>
    private static NodeId[] NodeIdOrder() => new[] { Start, Junction, Rejoin, Boss, BranchEntry };

    /// <summary>The refusal layouts' nodes in node-id order.</summary>
    private static NodeId[] NestedNodeIdOrder() =>
        new[] { Start, Junction, Rejoin, Boss, BranchEntry, BranchSecond };

    /// <summary>The hand-built board's spine, which is what tells the branch walk where to stop.</summary>
    private static IReadOnlySet<NodeId> OnSpine() =>
        new HashSet<NodeId> { Start, Junction, Rejoin, Boss };

    /// <summary>A generated board's spine, by linear index.</summary>
    private static IReadOnlySet<NodeId> Spine(BoardGraph board)
    {
        var spine = new HashSet<NodeId>();

        for (var linearIndex = 0; linearIndex < board.SpineLength; linearIndex++)
        {
            spine.Add(board.SpineNode(linearIndex));
        }

        return spine;
    }

    /// <summary>Every node reachable from a board's first node, in node-id order.</summary>
    private static NodeId[] Reachable(BoardGraph board)
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

        return seen.OrderBy(id => id.Value).ToArray();
    }
}
