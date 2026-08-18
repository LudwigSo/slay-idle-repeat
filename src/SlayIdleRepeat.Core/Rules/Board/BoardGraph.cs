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
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This board names the boss three different ways, and only <see cref="BoardGenerator"/>
    /// makes them agree.</b> Identity is this member; <see cref="MovementEngine"/>'s boss-exact rule
    /// asks whether the next node's tile is <see cref="TileKind.Boss"/>; stage arithmetic
    /// (<see cref="EnemyPowerFormula"/>, <c>Run</c>'s stage validation) asks whether the stage is
    /// <see cref="BossStage"/>. Each answers a different question — what the tile resolves as, which
    /// node it is, what its stage multiplier is — so collapsing them would push a tile-kind concern
    /// into graph identity rather than simplify anything. The generator emits all three in one
    /// statement, so a generated board cannot separate them.
    /// </para>
    /// <para>
    /// <see cref="FromLayout"/> now makes all three agree on <em>which node</em> they are talking
    /// about: only this node may dangle, and only this node may carry <see cref="TileKind.Boss"/>.
    /// So the tile-keyed reading can no longer answer "yes" at a node that is not this one, and the
    /// <c>ReachedBoss</c> clauses in <c>Handlers.RollDice</c> and <c>Handlers.ChooseFork</c> are
    /// redundant on every board that exists rather than merely on every generated one.
    /// </para>
    /// <para>
    /// ⚠️ What is still <b>not</b> settled is the converse: nothing requires this node to carry
    /// <see cref="TileKind.Boss"/> at all, so a layout may still end on an ordinary tile and have
    /// movement announce a boss standing on it. That is a content question with no decidable answer
    /// yet — the tile set has one member for the three different things the design authors as a
    /// board's ending — and <c>BoardTerminusTests</c> pins today's behaviour and carries the expiry.
    /// </para>
    /// </remarks>
    public NodeId BossNodeId => _spineByLinearIndex[^1];

    /// <summary>Every node this board contains, spine, branch and boss alike.</summary>
    public int NodeCount => _nodes.Count;

    /// <summary>How many linear indices this board has — every spine node plus the boss.</summary>
    internal int SpineLength => _spineByLinearIndex.Count;

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

    /// <summary>
    /// Whether a node is the last one of its stage: it has at least one outgoing edge, every one of
    /// them leaves the node's stage, and none of them is the boss node. The Stage Gate is a property
    /// of the node a run comes to rest on, so this asks nothing about how it got there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// False for the boss node (no outgoing edges) and for the last node of the final stage (its one
    /// edge leads to the boss, which belongs to no stage) — a chapter therefore has one fewer gate
    /// than it has stages.
    /// </para>
    /// <para>
    /// Quantified over <em>every</em> outgoing edge rather than over a single one, because a node
    /// that still offers a way deeper into its own stage has not ended it. On a generated board that
    /// arm is unreachable — <see cref="BoardGenerator"/> keeps every junction well before a stage's
    /// last node — but <see cref="FromLayout"/> is a public seam and a layout that put a fork there
    /// would otherwise gate a run that had not finished the stage.
    /// </para>
    /// </remarks>
    /// <exception cref="KeyNotFoundException">No node with this id exists on this board.</exception>
    public bool IsStageEndNode(NodeId id)
    {
        var edges = OutgoingEdges(id);

        if (edges.Count == 0)
        {
            return false;
        }

        var stage = Node(id).Stage;

        // Indexed rather than foreach: the edge list is interface-typed, so a foreach would heap-
        // allocate an enumerator on every landing this is asked about.
        for (var i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];

            if (edge.To.Equals(BossNodeId) || Node(edge.To).Stage == stage)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Builds a board directly from an already-decided layout, bypassing generation entirely.</summary>
    /// <remarks>
    /// Refuses a layout in which any node other than the boss — the last entry of
    /// <paramref name="spineByLinearIndex"/>, which is what <see cref="BossNodeId"/> returns — has
    /// no outgoing edge. <see cref="MovementEngine.Advance"/> reads "this node has no outgoing
    /// edge" as "the boss was reached", so a second dead end anywhere would have it announce a boss
    /// encounter at a node that is not the boss — a malformed board must fail loudly here instead.
    /// Every fork branch rejoins the spine, so the boss is a well-formed board's single terminus.
    /// <para>
    /// Refuses, for the same reason by a different route, any node other than the boss that carries
    /// <see cref="TileKind.Boss"/>. <see cref="MovementEngine.Advance"/>'s boss-exact rule reads the
    /// <em>tile</em> of the node a stage-crossing step would land on, not its identity, so a boss
    /// tile parked anywhere else makes movement stop early, report a boss, and swallow the stage
    /// gate that node's boundary owed. The check is safe for every generated board without
    /// sampling: <see cref="ChapterBoardConfig"/> refuses a boss weight in every stage table, no
    /// fallback or injection produces one, and <see cref="BoardGenerator"/> writes the boss tile in
    /// exactly one statement, onto the last linear index.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// The spine index is empty, a node referenced by an edge or by <paramref name="spineByLinearIndex"/>
    /// is not in <paramref name="nodes"/>, a junction id is not a node with exactly two outgoing edges,
    /// a node other than the boss node has no outgoing edge, or a node other than the boss node
    /// carries <see cref="TileKind.Boss"/>.
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

        // Deliberately a second pass rather than folded into the one above: a node that both
        // dead-ends and carries the boss tile gets the topology complaint, which is the more
        // actionable of the two for a layout that simply stopped early.
        foreach (var node in nodes)
        {
            if (node.Tile != TileKind.Boss || node.Id.Equals(bossNodeId))
            {
                continue;
            }

            throw new ArgumentException(
                $"{node.Id} carries the boss tile, but this board's boss node is {bossNodeId}; " +
                "a board holds exactly one boss tile and it sits at the end, or movement announces " +
                "a boss encounter at a node the run has not reached and the stage gate before it " +
                "never fires.",
                nameof(nodes));
        }

        var outgoingReadOnly = outgoing.ToDictionary(
            kv => kv.Key, IReadOnlyList<BoardEdge> (kv) => kv.Value);

        return new BoardGraph(nodeMap, outgoingReadOnly, spineByLinearIndex.ToArray(), junctionSet);
    }
}
