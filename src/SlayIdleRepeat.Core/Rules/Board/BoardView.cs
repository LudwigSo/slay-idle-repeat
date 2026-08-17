using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;

namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>
/// A run's board, projected into read-only records the Board screen can draw. The board itself is
/// never persisted — it regenerates from the run seed — so this is the only way anything outside
/// <c>Core</c> can see a tile track or a fork preview at all.
/// </summary>
/// <remarks>
/// The projection replays the layout through <see cref="BoardResolution.Replay"/>, the same helper
/// the movement handlers resolve a run's board with, so the track a player is shown is the track the
/// run is walked along. It hands out no graph, no edge and no draw stream: a caller can read where a
/// run stands and what lies ahead, and can neither generate a board nor route one.
/// </remarks>
public sealed class BoardView
{
    private readonly IReadOnlyDictionary<int, BoardTrackNode> _byNodeId;

    private BoardView(
        IReadOnlyList<BoardTrackNode> spine,
        IReadOnlyList<BoardTrackNode> nodes,
        IReadOnlyList<BoardFork> forks,
        IReadOnlyDictionary<int, BoardTrackNode> byNodeId,
        int position,
        int? pendingForkJunctionPosition)
    {
        Spine = spine;
        Nodes = nodes;
        Forks = forks;
        _byNodeId = byNodeId;
        StandingOn = Node(position);
        PendingFork = pendingForkJunctionPosition is { } junction
            ? forks.FirstOrDefault(f => f.JunctionNodeId == junction)
            : null;
    }

    /// <summary>The main track in walk order: entry <c>i</c> is linear index <c>i</c>, boss last.</summary>
    public IReadOnlyList<BoardTrackNode> Spine { get; }

    /// <summary>Every node the run can reach, spine and branch alike, ordered by node id.</summary>
    public IReadOnlyList<BoardTrackNode> Nodes { get; }

    /// <summary>One entry per junction, ordered by junction node id.</summary>
    public IReadOnlyList<BoardFork> Forks { get; }

    /// <summary>The node the run stands on, or <c>null</c> while it sits at the trailhead.</summary>
    public BoardTrackNode? StandingOn { get; }

    /// <summary>
    /// The fork the run is paused at waiting for a choice, or <c>null</c> when nothing is paused —
    /// or when the paused position is no junction of this board.
    /// </summary>
    public BoardFork? PendingFork { get; }

    /// <summary>Projects <paramref name="run"/>'s board out of its seed and its chapter's content.</summary>
    /// <param name="run">The run whose board is being drawn.</param>
    /// <param name="content">The loaded content set, read for the run's chapter.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static BoardView Project(RunSnapshot run, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(content);

        var board = BoardResolution.Replay(ChapterBoardTuning.Read(content, run.ChapterId), run.RunSeed);

        var spineIds = SpineIds(board);
        var onSpine = new HashSet<NodeId>(spineIds);
        var reachable = ReachableInNodeIdOrder(board);

        var nodes = new BoardTrackNode[reachable.Count];
        var byNodeId = new Dictionary<int, BoardTrackNode>(reachable.Count);

        for (var i = 0; i < reachable.Count; i++)
        {
            var source = board.Node(reachable[i]);
            var node = new BoardTrackNode(
                source.Id.Value, source.Tile, source.LinearIndex, source.Stage, onSpine.Contains(source.Id));

            nodes[i] = node;
            byNodeId[node.NodeId] = node;
        }

        var spine = new BoardTrackNode[spineIds.Count];
        for (var linearIndex = 0; linearIndex < spineIds.Count; linearIndex++)
        {
            spine[linearIndex] = byNodeId[spineIds[linearIndex].Value];
        }

        return new BoardView(
            Array.AsReadOnly(spine),
            Array.AsReadOnly(nodes),
            Array.AsReadOnly(ForksOf(board, reachable)),
            byNodeId,
            run.Position,
            run.PendingForkJunctionPosition);
    }

    /// <summary>The node with this id, or <c>null</c> when this board holds no such node.</summary>
    public BoardTrackNode? Node(int nodeId) =>
        _byNodeId.TryGetValue(nodeId, out var node) ? node : null;

    private static IReadOnlyList<NodeId> SpineIds(BoardGraph board)
    {
        var ids = new NodeId[board.SpineLength];

        for (var linearIndex = 0; linearIndex < ids.Length; linearIndex++)
        {
            ids[linearIndex] = board.SpineNode(linearIndex);
        }

        return ids;
    }

    /// <summary>
    /// Every node a run can arrive at, found by walking forward from the first node — the same
    /// traversal movement makes, so an unreachable node cannot appear on a drawn track.
    /// </summary>
    private static IReadOnlyList<NodeId> ReachableInNodeIdOrder(BoardGraph board)
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

        var ordered = seen.ToArray();
        Array.Sort(ordered, (left, right) => left.Value.CompareTo(right.Value));

        return ordered;
    }

    private static BoardFork[] ForksOf(BoardGraph board, IReadOnlyList<NodeId> nodeIdOrder)
    {
        var forks = new List<BoardFork>();

        foreach (var id in nodeIdOrder)
        {
            if (!board.IsJunction(id))
            {
                continue;
            }

            var edges = board.OutgoingEdges(id);
            var branch = edges[1];
            var preview = branch.Preview!;

            forks.Add(new BoardFork(
                id.Value,
                edges[0].To.Value,
                branch.To.Value,
                preview.Label,
                Array.AsReadOnly(preview.Icons.ToArray())));
        }

        return forks.ToArray();
    }
}
