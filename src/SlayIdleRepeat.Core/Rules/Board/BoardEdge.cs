namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>One directed edge of a <see cref="Board"/>'s graph. Movement traverses one at a time (`03` §1.1).</summary>
/// <param name="From">The edge's source node.</param>
/// <param name="To">The edge's destination node.</param>
/// <param name="Kind">
/// <see cref="EdgeKind.Branch"/> only for a junction's fork-entry edge; <see cref="EdgeKind.Continue"/>
/// for every other edge, including a junction's own spine continuation.
/// </param>
/// <param name="Preview">
/// Populated only on a <see cref="EdgeKind.Branch"/> edge — the branch's honest preview
/// (`03` §3.1). Null on every <see cref="EdgeKind.Continue"/> edge.
/// </param>
internal sealed record BoardEdge(NodeId From, NodeId To, EdgeKind Kind, ForkPreview? Preview = null);
