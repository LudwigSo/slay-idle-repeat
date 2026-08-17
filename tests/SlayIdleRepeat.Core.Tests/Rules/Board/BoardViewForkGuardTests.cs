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
/// with <c>ContinueNodeId</c> and <c>BranchNodeId</c> the wrong way round.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Why this file exists, and why it does not go through <c>BoardView.Project</c>.</b>
/// <c>Project</c> takes a <c>RunSnapshot</c> and replays the board out of its seed, so the only
/// board it can ever see is a <c>BoardGenerator</c> one — and the generator emits every junction in
/// the required order, in a single statement. The guard is therefore unreachable from the public
/// door: every case in <see cref="BoardViewTests"/> stays green with it deleted. The producer that
/// CAN lay a junction out any other way is <c>BoardGraph.FromLayout</c>, and the seam that lets a
/// hand-built layout reach the projection is <c>BoardView.ForksOf</c>, internal on
/// <c>GameRules.Execute</c>'s precedent and reachable here under `30` §11.3's one
/// <c>InternalsVisibleTo</c> grant.
/// </para>
/// <para>
/// The stakes are `03` §1.1's fork contract as <c>CHOOSE_FORK</c> answers it: the command names the
/// branch by INDEX, so a view that swapped the two ids would draw one node's preview over the other
/// node's tiles and move the run somewhere the player did not pick — silently, on a board that is
/// otherwise well-formed.
/// </para>
/// <para>
/// One arm of the guard has no case below: <c>edges.Count != 2</c>. <c>ForksOf</c> asks it only of
/// nodes <c>BoardGraph.IsJunction</c> answers true for, and a node is a junction only by being
/// declared one to <c>FromLayout</c>, which already refuses a declared junction that does not have
/// exactly two outgoing edges. Neither producer can reach that arm, so nothing here pretends to.
/// </para>
/// <para>
/// ⚠️ Forks are compared field by field, never by record <c>Equals</c> (steering S17): a
/// synthesized <c>BoardFork.Equals</c> compares <c>BranchIcons</c> by REFERENCE, so it would report
/// two identical projections as different.
/// </para>
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

    /// <summary>The preview a well-formed branch edge carries — the branch's own single tile.</summary>
    private static ForkPreview Preview() =>
        new(ForkLabel.Perilous, new[] { TileKind.Elite });

    /// <summary>
    /// 🔒 `03` §1.1 — <b>the negative control.</b> A hand-built <c>FromLayout</c> board whose
    /// junction IS laid out Continue-then-Branch-with-a-preview projects without complaint, and the
    /// fork it yields names the continuation and the branch entry the right way round.
    /// </summary>
    /// <remarks>
    /// Without this case the three refusals below would all pass against a projection that threw on
    /// every hand-built board — which would pin nothing about the ORDER at all.
    /// </remarks>
    [Fact]
    public void A_well_formed_junction_projects_to_a_fork_naming_the_continuation_and_the_branch()
    {
        var board = Layout(
            new BoardEdge(Junction, Rejoin, EdgeKind.Continue),
            new BoardEdge(Junction, BranchEntry, EdgeKind.Branch, Preview()));

        var forks = BoardView.ForksOf(board, NodeIdOrder());

        var fork = forks.ShouldHaveSingleItem();

        fork.JunctionNodeId.ShouldBe(Junction.Value);
        fork.ContinueNodeId.ShouldBe(
            Rejoin.Value, "the Continue edge is the way that stays on the spine.");
        fork.BranchNodeId.ShouldBe(
            BranchEntry.Value, "the Branch edge is the first node off the spine.");
        fork.BranchLabel.ShouldBe(ForkLabel.Perilous);
        fork.BranchIcons.ShouldBe(new[] { TileKind.Elite });
    }

    /// <summary>
    /// 🔒 `03` §1.1 — a junction whose two edges are ordered <c>Branch</c> then <c>Continue</c> is
    /// refused BY NAME, rather than projected with the two node ids swapped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the shape the guard exists for: both edges are present, both carry a preview — only
    /// the ORDER is wrong, and the order is the whole of what <c>CHOOSE_FORK</c>'s branch index
    /// means.
    /// </para>
    /// <para>
    /// ⚠️ The <c>Continue</c> edge is given a preview too, which no real layout would do. That is
    /// deliberate: with it null, the missing-preview arm below refuses this board as well, and the
    /// case would stay green with the order check deleted — it would pin nothing about the order at
    /// all. Every arm but the one under test is left satisfiable.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_junction_whose_edges_are_ordered_Branch_then_Continue_is_refused_by_name()
    {
        var board = Layout(
            new BoardEdge(Junction, BranchEntry, EdgeKind.Branch, Preview()),
            new BoardEdge(Junction, Rejoin, EdgeKind.Continue, Preview()));

        var ex = Should.Throw<InvalidOperationException>(
            () => BoardView.ForksOf(board, NodeIdOrder()));

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
    /// 🔒 `03` §1.1 — a junction whose second edge is not a <c>Branch</c> edge at all is refused by
    /// name, rather than projected as if the second way out were a fork branch.
    /// </summary>
    /// <remarks>
    /// Distinct from the swapped-order case: the first edge is right, so nothing about the ORDER is
    /// wrong — the second way out simply is not marked as a branch, so there is no branch index for
    /// <c>CHOOSE_FORK</c> to answer. It carries a preview for the same reason the case above does:
    /// so that the edge KIND is the only thing this case can be refused for.
    /// </remarks>
    [Fact]
    public void A_junction_whose_second_edge_is_not_a_branch_is_refused_by_name()
    {
        var board = Layout(
            new BoardEdge(Junction, Rejoin, EdgeKind.Continue),
            new BoardEdge(Junction, BranchEntry, EdgeKind.Continue, Preview()));

        var ex = Should.Throw<InvalidOperationException>(
            () => BoardView.ForksOf(board, NodeIdOrder()));

        ex.Message.ShouldContain(Junction.ToString(), Case.Sensitive);
        ex.Message.ShouldContain(
            "Continue edge then a Branch edge",
            Case.Sensitive,
            "a junction with two Continue edges offers no branch, so the same order-and-kind rule is " +
            "what refuses it.");
    }

    /// <summary>
    /// 🔒 `03` §1.1 — a correctly-ordered junction whose <c>Branch</c> edge carries NO preview is
    /// refused by name. A preview is never derived from the label to fill the gap.
    /// </summary>
    /// <remarks>
    /// The alternative the guard forecloses is the tempting one: the label alone is enough to render
    /// a plausible-looking fork card, and it would promise tiles the branch does not hold. The icons
    /// are the branch's own tiles or the fork cannot be drawn at all.
    /// </remarks>
    [Fact]
    public void A_branch_edge_carrying_no_preview_is_refused_by_name()
    {
        var board = Layout(
            new BoardEdge(Junction, Rejoin, EdgeKind.Continue),
            new BoardEdge(Junction, BranchEntry, EdgeKind.Branch));

        var ex = Should.Throw<InvalidOperationException>(
            () => BoardView.ForksOf(board, NodeIdOrder()));

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
    /// 🔒 `03` §1.1 — the seam is the same code the public door runs: over the real generated board,
    /// <c>ForksOf</c> yields exactly the forks <c>BoardView.Project</c> publishes.
    /// </summary>
    /// <remarks>
    /// Without this, the four cases above would pin a helper that <c>Project</c> could have stopped
    /// calling — a guard proved live on a path nothing walks.
    /// </remarks>
    [Fact]
    public void The_internal_seam_yields_the_same_forks_the_public_projection_publishes()
    {
        var board = BoardGenerator.GenerateBoard(
            ChapterBoardTuning.Read(ShippedHarness.Content, Chapter),
            DeterministicRng.OpenAt(FixedSeed, RngStreams.Board, 0));

        var published = BoardView
            .Project(RunSnapshots.With(chapterId: Chapter, runSeed: FixedSeed), ShippedHarness.Content)
            .Forks;

        var direct = BoardView.ForksOf(board, Reachable(board));

        published.Count.ShouldBeGreaterThan(0, "a board with no fork would make this case vacuous.");
        direct.Length.ShouldBe(published.Count);

        for (var i = 0; i < direct.Length; i++)
        {
            direct[i].JunctionNodeId.ShouldBe(published[i].JunctionNodeId);
            direct[i].ContinueNodeId.ShouldBe(published[i].ContinueNodeId);
            direct[i].BranchNodeId.ShouldBe(published[i].BranchNodeId);
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

    /// <summary>The hand-built board's nodes in node-id order, as the projection walks them.</summary>
    private static NodeId[] NodeIdOrder() => new[] { Start, Junction, Rejoin, Boss, BranchEntry };

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
