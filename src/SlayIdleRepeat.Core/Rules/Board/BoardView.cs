using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;

namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>
/// A run's board, projected into read-only records the Board screen can draw. The board is never
/// persisted — it regenerates from the run seed through <see cref="BoardResolution.Replay"/>, the
/// same helper the movement handlers resolve it with — so this is the only way anything outside
/// <c>Core</c> can see a tile track or a fork preview at all.
/// </summary>
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
    /// The fork the run is paused at waiting for a choice — <c>null</c> when nothing is paused, or
    /// when the paused position is no junction of this board.
    /// </summary>
    public BoardFork? PendingFork { get; }

    /// <summary>Projects <paramref name="run"/>'s board out of its seed and its chapter's content.</summary>
    /// <param name="run">The run whose board is being drawn.</param>
    /// <param name="content">The loaded content set, read for the run's chapter.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="MissingContentException"><paramref name="content"/> declares no such chapter.</exception>
    public static BoardView Project(RunSnapshot run, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(content);

        var board = BoardResolution.Replay(ChapterBoardTuning.Read(content, run.ChapterId), run.RunSeed);

        var spineIds = SpineIds(board);
        var onSpine = new HashSet<NodeId>(spineIds);
        var reachable = ReachableInNodeIdOrder(board);

        var nodes = new BoardTrackNode[reachable.Length];
        var byNodeId = new Dictionary<int, BoardTrackNode>(reachable.Length);

        for (var i = 0; i < reachable.Length; i++)
        {
            var source = board.Node(reachable[i]);
            var node = new BoardTrackNode(
                source.Id.Value, source.Tile, source.LinearIndex, source.Stage, onSpine.Contains(source.Id));

            nodes[i] = node;
            byNodeId[node.NodeId] = node;
        }

        var spine = new BoardTrackNode[spineIds.Length];
        for (var linearIndex = 0; linearIndex < spineIds.Length; linearIndex++)
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

    private static NodeId[] SpineIds(BoardGraph board)
    {
        var ids = new NodeId[board.SpineLength];

        for (var linearIndex = 0; linearIndex < ids.Length; linearIndex++)
        {
            ids[linearIndex] = board.SpineNode(linearIndex);
        }

        return ids;
    }

    /// <summary>Every node a run can arrive at, walked forward from the first node the way movement walks it.</summary>
    private static NodeId[] ReachableInNodeIdOrder(BoardGraph board)
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

    /// <summary>
    /// Projects one <see cref="BoardFork"/> per junction, and refuses a junction laid out any other
    /// way than Continue-then-Branch-with-a-preview.
    /// </summary>
    /// <remarks>
    /// Internal rather than private, and not a second entry point: <see cref="Project"/> can only
    /// ever see a <see cref="BoardGenerator"/> board — it replays one out of the run seed — so the
    /// refusal below is unreachable from the public door and would be silently deletable. The one
    /// producer that CAN lay a junction out wrongly is <see cref="BoardGraph.FromLayout"/>, and the
    /// only caller that can hand this a hand-built layout is the domain suite through
    /// <c>InternalsVisibleTo</c>. <c>GameRules.Execute</c> is the same seam for the same reason.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A junction is not laid out Continue-then-Branch, or its Branch edge carries no preview.</exception>
    internal static BoardFork[] ForksOf(BoardGraph board, NodeId[] nodeIdOrder)
    {
        var forks = new List<BoardFork>();

        foreach (var id in nodeIdOrder)
        {
            if (!board.IsJunction(id))
            {
                continue;
            }

            var edges = board.OutgoingEdges(id);

            if (edges.Count != 2 ||
                edges[0].Kind != EdgeKind.Continue ||
                edges[1] is not { Kind: EdgeKind.Branch, Preview: { } preview } branch)
            {
                throw new InvalidOperationException(
                    $"junction {id} must offer a Continue edge then a Branch edge carrying a preview — " +
                    "the order CHOOSE_FORK's branch index answers a fork by, so a junction laid out any " +
                    "other way would have this view name one node and the command move the run to the " +
                    "other. BoardGenerator emits that order; BoardGraph.FromLayout does not check it. A " +
                    "preview is never derived from the label to fill the gap: the icons are the branch's " +
                    "own tiles or the fork cannot be drawn at all.");
            }

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
