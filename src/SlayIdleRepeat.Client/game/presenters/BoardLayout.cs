using SlayIdleRepeat.Core.Rules.Board;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>A point in the board's own space, in engine units.</summary>
/// <remarks>
/// Not an engine vector, and it cannot be one: a presenter source file may not contain the engine's
/// name at all (`23` §6, pinned by <c>PresenterBoundaryRuleTests</c>). The scene converts at the one
/// place it reads a placement. That is a small price for keeping the whole layout testable without
/// booting anything.
/// </remarks>
/// <param name="X">Across the board, positive to the right of the direction of travel.</param>
/// <param name="Y">Up. Always zero today — `03` §8's tiles are flat pucks, so no elevation is invented.</param>
/// <param name="Z">Along the board, growing negative as the track runs away from the start.</param>
public readonly record struct BoardPoint(float X, float Y, float Z);

/// <summary>The shape numbers a board is laid out with.</summary>
/// <remarks>
/// ⚠️ <b>Not one of these is authored by a design document.</b> `03`, `13` and `15` state how a
/// board must READ — every tile legible at 48 dp, branches drawn beside the spine with a clear join,
/// the hero at roughly 40% of screen height — and state no distance at all. Per steering rule S6 the
/// hole is left open rather than filled with a plausible constant: these arrive from the one
/// <c>[Export]</c> block on <c>Board.tscn</c>, marked as this task's choices and owed to a ruling,
/// and this type deliberately offers NO default so nothing can fall back to a number nobody chose.
/// </remarks>
/// <param name="NodeSpacing">Distance along the track between one node and the next.</param>
/// <param name="WindAmplitude">How far the track wanders either side of its centre line.</param>
/// <param name="WindWavelength">The distance along the track one full wander takes.</param>
/// <param name="BranchOffset">How far a fork's branch is drawn to the side of the spine it leaves.</param>
public sealed record BoardLayoutMetrics(
    float NodeSpacing,
    float WindAmplitude,
    float WindWavelength,
    float BranchOffset);

/// <summary>Where one node stands, and which way the track runs through it.</summary>
/// <param name="NodeId">The node this places, as <see cref="BoardTrackNode.NodeId"/> names it.</param>
/// <param name="Centre">The middle of the node's puck.</param>
/// <param name="HeadingRadians">The direction of travel through the node, for facing and for orienting the path.</param>
/// <param name="OnSpine">Whether this node is on the spine or on a fork's branch.</param>
/// <param name="Stage">The node's stage, so a camera can frame one stage without re-reading the board.</param>
public sealed record BoardNodePlacement(
    int NodeId,
    BoardPoint Centre,
    float HeadingRadians,
    bool OnSpine,
    int Stage);

/// <summary>One drawable run of track between two placed nodes.</summary>
/// <param name="FromNodeId">The node the run leaves.</param>
/// <param name="ToNodeId">The node it arrives at.</param>
/// <param name="OnSpine">False for every run that belongs to a fork's branch, including its rejoin.</param>
public sealed record BoardTrackSegment(int FromNodeId, int ToNodeId, bool OnSpine);

/// <summary>An axis-aligned box in board space — what a camera frames.</summary>
/// <param name="Minimum">The low corner.</param>
/// <param name="Maximum">The high corner.</param>
public readonly record struct BoardExtent(BoardPoint Minimum, BoardPoint Maximum);

/// <summary>
/// Where every node of a board stands in three dimensions, and every run of track between them.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Nothing here knows how big a board is.</b> Node count, stage count, fork count and branch
/// length are all read off the <see cref="BoardView"/> handed in. The two shipped chapters happen to
/// author 43 nodes across three stages, but a chapter authors its own <c>stageLengths</c> and its own
/// fork density (`16` D69), and a regular one is expected to run several times longer. A constant
/// anywhere in this file would be a board size baked into the renderer, and
/// <c>BoardLayoutTests</c> lays out a deliberately long, fork-dense board to keep that honest.
/// </para>
/// <para>
/// The spine runs away from the origin along −Z, wandering in X so the track reads as a trail rather
/// than a ruler. A branch is offset sideways at the SAME distance along the track as the spine node
/// carrying its linear index — which is `03` §1.1's rule ("the k-th node of a branch leaving the
/// spine at node j has i = i(j) + k") expressed as geometry, and it is what makes the two ways out of
/// a junction visibly parallel and visibly rejoin. Always the same side, deterministically: nothing
/// authorises alternating, and a layout that varied would make two screenshots of one board differ.
/// </para>
/// </remarks>
public sealed class BoardLayout
{
    /// <summary>
    /// The id <see cref="Trailhead"/> reports. Never a node of any board: <c>BoardGenerator</c>
    /// numbers from zero, so a negative id cannot collide with one.
    /// </summary>
    public const int TrailheadNodeId = -1;

    private readonly IReadOnlyDictionary<int, BoardNodePlacement> _byNodeId;

    private BoardLayout(
        IReadOnlyList<BoardNodePlacement> placements,
        IReadOnlyList<BoardTrackSegment> segments,
        IReadOnlyDictionary<int, BoardNodePlacement> byNodeId,
        BoardExtent extent,
        IReadOnlyDictionary<int, BoardExtent> extentByStage)
    {
        Placements = placements;
        Segments = segments;
        _byNodeId = byNodeId;
        Extent = extent;
        ExtentByStage = extentByStage;
    }

    /// <summary>Every node the board holds, exactly once, in the order <see cref="BoardView.Nodes"/> gives them.</summary>
    public IReadOnlyList<BoardNodePlacement> Placements { get; }

    /// <summary>Every drawable run: each spine adjacency, and each fork's junction-branch-rejoin chain.</summary>
    public IReadOnlyList<BoardTrackSegment> Segments { get; }

    /// <summary>The box every placement sits inside.</summary>
    public BoardExtent Extent { get; }

    /// <summary>The box each stage's own placements sit inside, keyed by stage.</summary>
    public IReadOnlyDictionary<int, BoardExtent> ExtentByStage { get; }

    /// <summary>
    /// Where the hero stands before its first roll: one step short of the board's first node.
    /// </summary>
    /// <remarks>
    /// 🔒 `03` §1.1's trailhead is not a tile — nothing resolves there and the run holds it as
    /// position −1 — but it is somewhere the hero visibly IS, and `03` §1.1 says the token is drawn
    /// at the head of the track. Without a point for it the board comes up with no hero on it at
    /// all, and the first thing a new player sees is a board they are not on.
    /// <para>
    /// Extrapolated along the same centre line rather than placed at the origin, so the first roll
    /// hops onto the board from the direction the board runs.
    /// </para>
    /// </remarks>
    public BoardNodePlacement Trailhead { get; private set; } = null!;

    /// <summary>Lays a board out.</summary>
    /// <param name="board">The projected board. Every node it holds gets exactly one placement.</param>
    /// <param name="metrics">The distances to lay it out with.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static BoardLayout Of(BoardView board, BoardLayoutMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(metrics);

        var byNodeId = new Dictionary<int, BoardNodePlacement>(board.Nodes.Count);
        var segments = new List<BoardTrackSegment>();

        PlaceSpine(board, metrics, byNodeId, segments);
        PlaceBranches(board, metrics, byNodeId, segments);

        // Ordered the way BoardView.Nodes is, so a caller can zip the two without a lookup. Any node
        // the walk above missed would fail here rather than quietly leave a hole in the board.
        var placements = new BoardNodePlacement[board.Nodes.Count];

        for (var i = 0; i < board.Nodes.Count; i++)
        {
            var nodeId = board.Nodes[i].NodeId;

            placements[i] = byNodeId.TryGetValue(nodeId, out var placement)
                ? placement
                : throw new InvalidOperationException(
                    $"node {nodeId} is on the board and was never placed. Every node the run can " +
                    "stand on needs somewhere to stand: the hero would be drawn at the origin, on " +
                    "no tile at all, for as long as the run stood there.");
        }

        return new BoardLayout(
            placements,
            segments,
            byNodeId,
            ExtentOf(placements),
            ExtentsByStage(placements))
        {
            // Node index −1: the same arithmetic every other placement uses, one step back.
            Trailhead = new BoardNodePlacement(
                TrailheadNodeId,
                CentreLine(-1, metrics),
                HeadingAlongCentreLine(-1, board.Spine.Count, metrics),
                OnSpine: true,
                board.Spine[0].Stage),
        };
    }

    /// <summary>Where a node stands, or <c>null</c> when this layout holds no such node.</summary>
    public BoardNodePlacement? Placement(int nodeId) =>
        _byNodeId.TryGetValue(nodeId, out var placement) ? placement : null;

    /// <summary>The box one stage's placements sit inside, or <c>null</c> for a stage this board has none of.</summary>
    public BoardExtent? ExtentOfStage(int stage) =>
        ExtentByStage.TryGetValue(stage, out var extent) ? extent : null;

    private static void PlaceSpine(
        BoardView board,
        BoardLayoutMetrics metrics,
        Dictionary<int, BoardNodePlacement> byNodeId,
        List<BoardTrackSegment> segments)
    {
        for (var linearIndex = 0; linearIndex < board.Spine.Count; linearIndex++)
        {
            var node = board.Spine[linearIndex];

            byNodeId[node.NodeId] = new BoardNodePlacement(
                node.NodeId,
                CentreLine(linearIndex, metrics),
                HeadingAlongCentreLine(linearIndex, board.Spine.Count, metrics),
                OnSpine: true,
                node.Stage);

            if (linearIndex > 0)
            {
                segments.Add(new BoardTrackSegment(
                    board.Spine[linearIndex - 1].NodeId, node.NodeId, OnSpine: true));
            }
        }
    }

    private static void PlaceBranches(
        BoardView board,
        BoardLayoutMetrics metrics,
        Dictionary<int, BoardNodePlacement> byNodeId,
        List<BoardTrackSegment> segments)
    {
        foreach (var fork in board.Forks)
        {
            var junction = board.Node(fork.JunctionNodeId);

            if (junction is null)
            {
                continue;
            }

            var previousNodeId = fork.JunctionNodeId;

            for (var k = 0; k < fork.BranchNodeIds.Count; k++)
            {
                var branchNodeId = fork.BranchNodeIds[k];
                var branchNode = board.Node(branchNodeId);

                // `03` §1.1 as geometry: the k-th branch node stands the same distance along the
                // track as the spine node one forward step further on, offset to the side. The two
                // ways out of a junction are therefore parallel and equally long, which is what
                // makes a fork read as a risk choice rather than as a shortcut.
                var alongTrack = junction.LinearIndex + k + 1;

                byNodeId[branchNodeId] = new BoardNodePlacement(
                    branchNodeId,
                    Offset(CentreLine(alongTrack, metrics), LeftNormal(alongTrack, metrics), metrics.BranchOffset),
                    HeadingAlongCentreLine(alongTrack, board.Spine.Count, metrics),
                    OnSpine: false,
                    branchNode?.Stage ?? junction.Stage);

                segments.Add(new BoardTrackSegment(previousNodeId, branchNodeId, OnSpine: false));
                previousNodeId = branchNodeId;
            }

            // The rejoin is a spine node and already stands somewhere — that is the whole reason
            // BoardFork names it. Only the run of track back onto it is this fork's to draw.
            segments.Add(new BoardTrackSegment(previousNodeId, fork.RejoinNodeId, OnSpine: false));
        }
    }

    /// <summary>The centre of the track at a distance along it, measured in node steps.</summary>
    private static BoardPoint CentreLine(int alongTrack, BoardLayoutMetrics metrics)
    {
        var distance = alongTrack * metrics.NodeSpacing;

        return new BoardPoint(Wander(distance, metrics), 0f, -distance);
    }

    private static float Wander(float distance, BoardLayoutMetrics metrics) =>
        metrics.WindWavelength == 0f
            ? 0f
            : metrics.WindAmplitude * MathF.Sin(2f * MathF.PI * distance / metrics.WindWavelength);

    /// <summary>
    /// The direction of travel through a node, as the chord to the next one — and for the last node,
    /// the chord from the previous one, so the boss faces the way the run arrived rather than nowhere.
    /// </summary>
    private static float HeadingAlongCentreLine(int alongTrack, int spineLength, BoardLayoutMetrics metrics)
    {
        var (from, to) = alongTrack + 1 < spineLength
            ? (alongTrack, alongTrack + 1)
            : (Math.Max(0, alongTrack - 1), alongTrack);

        if (from == to)
        {
            return 0f;
        }

        var start = CentreLine(from, metrics);
        var end = CentreLine(to, metrics);

        return MathF.Atan2(end.X - start.X, end.Z - start.Z);
    }

    /// <summary>The unit vector ninety degrees to the left of the direction of travel.</summary>
    private static BoardPoint LeftNormal(int alongTrack, BoardLayoutMetrics metrics)
    {
        var heading = HeadingAlongCentreLine(alongTrack, int.MaxValue, metrics);

        return new BoardPoint(MathF.Cos(heading), 0f, -MathF.Sin(heading));
    }

    private static BoardPoint Offset(BoardPoint point, BoardPoint direction, float distance) =>
        new(point.X + (direction.X * distance),
            point.Y + (direction.Y * distance),
            point.Z + (direction.Z * distance));

    private static BoardExtent ExtentOf(IReadOnlyList<BoardNodePlacement> placements)
    {
        if (placements.Count == 0)
        {
            return new BoardExtent(default, default);
        }

        var minimum = placements[0].Centre;
        var maximum = placements[0].Centre;

        for (var i = 1; i < placements.Count; i++)
        {
            var centre = placements[i].Centre;

            minimum = new BoardPoint(
                MathF.Min(minimum.X, centre.X), MathF.Min(minimum.Y, centre.Y), MathF.Min(minimum.Z, centre.Z));
            maximum = new BoardPoint(
                MathF.Max(maximum.X, centre.X), MathF.Max(maximum.Y, centre.Y), MathF.Max(maximum.Z, centre.Z));
        }

        return new BoardExtent(minimum, maximum);
    }

    /// <summary>
    /// One extent per stage the board actually holds — never per stage a constant says it holds.
    /// </summary>
    private static IReadOnlyDictionary<int, BoardExtent> ExtentsByStage(
        IReadOnlyList<BoardNodePlacement> placements)
    {
        var byStage = new Dictionary<int, List<BoardNodePlacement>>();

        foreach (var placement in placements)
        {
            if (!byStage.TryGetValue(placement.Stage, out var stagePlacements))
            {
                stagePlacements = new List<BoardNodePlacement>();
                byStage[placement.Stage] = stagePlacements;
            }

            stagePlacements.Add(placement);
        }

        return byStage.ToDictionary(entry => entry.Key, entry => ExtentOf(entry.Value));
    }
}
