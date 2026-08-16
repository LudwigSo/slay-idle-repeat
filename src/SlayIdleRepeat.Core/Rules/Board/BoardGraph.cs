using System.Globalization;

namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>
/// A run's board: a DAG that reads as a mostly-linear track with occasional two-way forks that
/// rejoin. Immutable once built — <see cref="BoardGenerator.GenerateBoard"/> is the procedural
/// producer; <see cref="FromLayout"/> is the seam a future authored-layout loader would call
/// instead, so that path never needs a parallel graph type.
/// </summary>
internal sealed class BoardGraph
{
    /// <summary><see cref="BoardNode.Stage"/> for the boss node — it belongs to no stage.</summary>
    public const int BossStage = 0;

    private static readonly IReadOnlyList<BoardEdge> NoEdges = Array.Empty<BoardEdge>();

    private readonly IReadOnlyDictionary<NodeId, BoardNode> _nodes;
    private readonly IReadOnlyDictionary<NodeId, IReadOnlyList<BoardEdge>> _outgoing;
    private readonly IReadOnlyList<NodeId> _spineByLinearIndex;
    private readonly IReadOnlySet<NodeId> _junctions;

    private BoardGraph(
        IReadOnlyDictionary<NodeId, BoardNode> nodes,
        IReadOnlyDictionary<NodeId, IReadOnlyList<BoardEdge>> outgoing,
        IReadOnlyList<NodeId> spineByLinearIndex,
        IReadOnlySet<NodeId> junctions)
    {
        _nodes = nodes;
        _outgoing = outgoing;
        _spineByLinearIndex = spineByLinearIndex;
        _junctions = junctions;
    }

    /// <summary>The trailhead's first possible landing — the spine node at linear index 0.</summary>
    public NodeId FirstNodeId => _spineByLinearIndex[0];

    /// <summary>
    /// The boss node — always the last entry of the linear index (index 42), and the one node
    /// <see cref="FromLayout"/> lets a layout leave without an outgoing edge. Changing which entry
    /// this picks changes which node that guard exempts.
    /// </summary>
    public NodeId BossNodeId => _spineByLinearIndex[^1];

    /// <summary>Every node this board contains, spine, branch and boss alike.</summary>
    public int NodeCount => _nodes.Count;

    /// <summary>The spine node (or the boss) at a linear index, <c>0..42</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside <c>0..42</c>.</exception>
    public NodeId SpineNode(int linearIndex)
    {
        if (linearIndex < 0 || linearIndex >= _spineByLinearIndex.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(linearIndex), linearIndex,
                $"this board's linear index runs 0..{(_spineByLinearIndex.Count - 1).ToString(CultureInfo.InvariantCulture)}.");
        }

        return _spineByLinearIndex[linearIndex];
    }

    /// <summary>Looks up a node's data.</summary>
    /// <exception cref="KeyNotFoundException">No node with this id exists on this board.</exception>
    public BoardNode Node(NodeId id)
    {
        if (_nodes.TryGetValue(id, out var node))
        {
            return node;
        }

        throw new KeyNotFoundException($"{id} is not a node on this board.");
    }

    /// <summary>
    /// A node's outgoing edges — empty for the boss node, exactly one for every ordinary node,
    /// exactly two (<see cref="EdgeKind.Continue"/> then <see cref="EdgeKind.Branch"/>) for a
    /// junction.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No node with this id exists on this board.</exception>
    public IReadOnlyList<BoardEdge> OutgoingEdges(NodeId id)
    {
        if (!_nodes.ContainsKey(id))
        {
            throw new KeyNotFoundException($"{id} is not a node on this board.");
        }

        return _outgoing.TryGetValue(id, out var edges) ? edges : NoEdges;
    }

    /// <summary>
    /// Whether a node is a junction — a node with two outgoing edges, where movement pauses for
    /// <c>CHOOSE_FORK</c> only when it must leave it.
    /// </summary>
    public bool IsJunction(NodeId id) => _junctions.Contains(id);

    /// <summary>Builds a board directly from an already-decided layout, bypassing generation entirely.</summary>
    /// <remarks>
    /// Refuses a layout in which any node other than the boss — the last entry of
    /// <paramref name="spineByLinearIndex"/>, which is what <see cref="BossNodeId"/> returns — has
    /// no outgoing edge. <see cref="MovementEngine.Advance"/> reads "this node has no outgoing
    /// edge" as "the boss was reached", so a second dead end anywhere would have it announce a boss
    /// encounter at a node that is not the boss — a malformed board must fail loudly here instead.
    /// Every fork branch rejoins the spine, so the boss is a well-formed board's single terminus.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// The spine index is empty, a node referenced by an edge or by <paramref name="spineByLinearIndex"/>
    /// is not in <paramref name="nodes"/>, a junction id is not a node with exactly two outgoing edges,
    /// or a node other than the boss node has no outgoing edge.
    /// </exception>
    public static BoardGraph FromLayout(
        IReadOnlyList<BoardNode> nodes,
        IReadOnlyList<BoardEdge> edges,
        IReadOnlyList<NodeId> spineByLinearIndex,
        IReadOnlyCollection<NodeId> junctionIds)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(spineByLinearIndex);
        ArgumentNullException.ThrowIfNull(junctionIds);

        if (spineByLinearIndex.Count == 0)
        {
            throw new ArgumentException("a board needs at least one linear-index entry (the boss).", nameof(spineByLinearIndex));
        }

        var nodeMap = new Dictionary<NodeId, BoardNode>(nodes.Count);
        foreach (var node in nodes)
        {
            nodeMap[node.Id] = node;
        }

        foreach (var id in spineByLinearIndex)
        {
            if (!nodeMap.ContainsKey(id))
            {
                throw new ArgumentException($"{id} is listed in the linear index but is not one of the supplied nodes.", nameof(spineByLinearIndex));
            }
        }

        var outgoing = new Dictionary<NodeId, List<BoardEdge>>();
        foreach (var edge in edges)
        {
            if (!nodeMap.ContainsKey(edge.From) || !nodeMap.ContainsKey(edge.To))
            {
                throw new ArgumentException($"edge {edge.From} -> {edge.To} references a node that is not supplied.", nameof(edges));
            }

            if (!outgoing.TryGetValue(edge.From, out var list))
            {
                list = new List<BoardEdge>();
                outgoing[edge.From] = list;
            }

            list.Add(edge);
        }

        var junctionSet = new HashSet<NodeId>(junctionIds);
        foreach (var junction in junctionSet)
        {
            if (!nodeMap.ContainsKey(junction))
            {
                throw new ArgumentException($"{junction} is marked as a junction but is not one of the supplied nodes.", nameof(junctionIds));
            }

            if (!outgoing.TryGetValue(junction, out var junctionEdges) || junctionEdges.Count != 2)
            {
                throw new ArgumentException($"{junction} is marked as a junction, so 03 §1.1 requires exactly two outgoing edges; it has {(outgoing.TryGetValue(junction, out var e) ? e.Count : 0).ToString(CultureInfo.InvariantCulture)}.", nameof(junctionIds));
            }
        }

        var bossNodeId = spineByLinearIndex[^1];
        foreach (var node in nodes)
        {
            if (node.Id.Equals(bossNodeId) ||
                (outgoing.TryGetValue(node.Id, out var nodeEdges) && nodeEdges.Count > 0))
            {
                continue;
            }

            throw new ArgumentException(
                $"{node.Id} has no outgoing edge, but only the boss node ({bossNodeId}) may end the board; " +
                "every other node leads somewhere, and a fork branch rejoins the spine.",
                nameof(nodes));
        }

        var outgoingReadOnly = outgoing.ToDictionary(
            kv => kv.Key, IReadOnlyList<BoardEdge> (kv) => kv.Value);

        return new BoardGraph(nodeMap, outgoingReadOnly, spineByLinearIndex.ToArray(), junctionSet);
    }
}
