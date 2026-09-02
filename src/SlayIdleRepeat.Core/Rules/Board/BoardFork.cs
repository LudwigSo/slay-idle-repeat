namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>A junction and the two ways out of it, with the branch's honest preview.</summary>
/// <remarks>
/// 🔒 <b>The whole branch is named, not just its entry.</b> A screen that draws the board as a
/// place — rather than as a strip of the spine — has to put every branch node somewhere and has to
/// know where the branch comes back, and neither fact is derivable from an entry node alone. The
/// alternative was to let a client infer branch membership from the linear-index coincidence of
/// `03` §1.1 (a branch node shares the index of the spine node at the same forward distance), but
/// that is a property of how <see cref="BoardGenerator"/> lays a board out, not a promise this
/// graph makes, and a client built on it would mislay a branch silently the day generation moved.
/// <para>
/// There is deliberately no <c>BranchNodeId</c> beside <see cref="BranchNodeIds"/>: a second field
/// derived from the first is a second thing to keep true. The entry is
/// <c>BranchNodeIds[0]</c>, and <c>CHOOSE_FORK</c>'s branch index still answers the edge order
/// <see cref="BoardView.ForksOf"/> pins — index 0 continues, index 1 branches.
/// </para>
/// </remarks>
/// <param name="JunctionNodeId">The spine node the choice is made at.</param>
/// <param name="ContinueNodeId">The node reached by staying on the spine.</param>
/// <param name="BranchNodeIds">The branch's own nodes in walk order; the first is the node the branch is entered at.</param>
/// <param name="RejoinNodeId">The spine node the branch's last node leads onto — where the two ways out meet again.</param>
/// <param name="BranchLabel">The bias label the branch's tiles were drawn under.</param>
/// <param name="BranchIcons">The branch's own first tiles in walk order, at most three — never derived from the label.</param>
public sealed record BoardFork(
    int JunctionNodeId,
    int ContinueNodeId,
    IReadOnlyList<int> BranchNodeIds,
    int RejoinNodeId,
    ForkLabel BranchLabel,
    IReadOnlyList<TileKind> BranchIcons);
