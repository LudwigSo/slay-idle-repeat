namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>One node of a run's board as the outside world sees it — a read, never a routing handle.</summary>
/// <param name="NodeId">The node's identity, and the value a run's position carries.</param>
/// <param name="Tile">The tile kind this node resolves as.</param>
/// <param name="LinearIndex">
/// Distance along the track in walk order. Not a node identity: a branch node carries the index of
/// the spine node the same forward distance from its junction, so the two share one.
/// </param>
/// <param name="Stage">The chapter stage this node belongs to; the boss belongs to none.</param>
/// <param name="OnSpine">False for a node inside a fork branch.</param>
public sealed record BoardTrackNode(int NodeId, TileKind Tile, int LinearIndex, int Stage, bool OnSpine);
