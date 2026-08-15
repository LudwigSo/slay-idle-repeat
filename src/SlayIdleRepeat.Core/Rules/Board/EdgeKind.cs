namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>
/// Which of a junction's two outgoing edges this is. Every non-junction node has exactly one
/// outgoing edge, always <see cref="Continue"/>.
/// </summary>
public enum EdgeKind
{
    /// <summary>The spine's own next node, or a branch's internal chain, or a branch's rejoin.</summary>
    Continue,

    /// <summary>A junction's entry into its fork branch. `03` §1.1: this is the edge a <c>CHOOSE_FORK</c> leaves by, when it is not taken.</summary>
    Branch,
}
