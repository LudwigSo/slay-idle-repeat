namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>A junction and the two ways out of it, with the branch's honest preview.</summary>
/// <param name="JunctionNodeId">The spine node the choice is made at.</param>
/// <param name="ContinueNodeId">The node reached by staying on the spine.</param>
/// <param name="BranchNodeId">The first node of the branch.</param>
/// <param name="BranchLabel">The bias label the branch's tiles were drawn under.</param>
/// <param name="BranchIcons">The branch's own first tiles in walk order, at most three — never derived from the label.</param>
public sealed record BoardFork(
    int JunctionNodeId,
    int ContinueNodeId,
    int BranchNodeId,
    ForkLabel BranchLabel,
    IReadOnlyList<TileKind> BranchIcons);
