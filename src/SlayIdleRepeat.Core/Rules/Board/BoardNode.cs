namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>One node of a <see cref="Board"/>'s graph.</summary>
/// <param name="Id">This node's opaque graph identity.</param>
/// <param name="Tile">Which of `03` §2's 14 tile kinds this node resolves as.</param>
/// <param name="LinearIndex">
/// `03` §1.1's linear node index: <c>0..41</c> for spine nodes in walk order, <c>42</c> for the
/// boss. A branch node's index equals the index the spine node at the same forward distance from
/// its junction would have — <see cref="Board.IsJunction"/> plus <see cref="Board.OutgoingEdges"/>
/// tell a spine node from a branch node sharing the same index; this field alone does not.
/// <see cref="Rules.Combat.Enemies"/>'s <c>EnemyPower(i)</c> (`02` §4.3) reads this value, and
/// branch/spine pay identical power at equal forward progress by construction.
/// </param>
/// <param name="Stage">
/// <c>1..3</c> for a node that belongs to one of the chapter's three stages (a branch node
/// belongs to the stage its junction belongs to); <see cref="Board.BossStage"/> (<c>0</c>) for the
/// boss node, which is not part of any stage.
/// </param>
public sealed record BoardNode(NodeId Id, TileKind Tile, int LinearIndex, int Stage);
