using Godot;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Rules.Board;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// The board as a place: a puck for every node, a run of track between every pair, the gate marks,
/// and the hero standing on one of them.
/// </summary>
/// <remarks>
/// <para>
/// A node of its own rather than more of <c>Board.cs</c>, which is already the longest file in the
/// client and is a driving adapter over the presenter — nothing in it should have to know what a
/// mesh is. A <see cref="Node3D"/> rather than a static helper on purpose: <c>SceneBoundaryRuleTests</c>
/// governs every <c>Godot.Node</c> subclass mechanically, while a static would need a hand-kept line
/// in its name list and would be invisible the day it moved.
/// </para>
/// <para>
/// 🔒 <b>Nothing here knows how big a board is.</b> Everything is sized off the
/// <see cref="BoardLayout"/> handed in. The shipped chapters author 43 nodes, and a regular chapter
/// is expected to run several times longer with several times the forks.
/// </para>
/// <para>
/// 🔒 <b>The pucks and the runs of track are drawn as multimeshes, and that is a design decision
/// rather than an optimisation deferred.</b> At 43 nodes, individual mesh instances would have been
/// fine; at 150 with dense forks they are four hundred draw calls on the Mobile renderer, on a
/// phone. Grouping by palette colour gets the whole board into roughly ten, whatever length it is.
/// <c>BoardTile.tscn</c> and <c>BoardPathSegment.tscn</c> still exist and are still `15` §E9's
/// swap-in seam — they supply the mesh and the marks — so the batching does not erase the seam.
/// </para>
/// </remarks>
public partial class BoardWorld : Node3D
{
    private const string TileScenePath = "res://game/scenes/BoardTile.tscn";
    private const string SegmentScenePath = "res://game/scenes/BoardPathSegment.tscn";

    private const string TilePuckPath = "Puck";
    private const string TileGatePath = "Gate";
    private const string SegmentRibbonPath = "Ribbon";

    private const string TrackPath = "Track";
    private const string HeroRigPath = "HeroRig";
    private const string HeroPivotPath = "HeroRig/HeroPivot";

    /// <summary>
    /// How far <c>Hero.tscn</c>'s own origin sits above the hero's feet, in that scene's units.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Measured, not chosen.</b> <c>Hero.tscn</c> drops its <c>Model</c> child to
    /// <c>y = -1.72</c> at a scale of <c>1.32</c>, and <c>chr_hero_rogue.glb</c>'s own POSITION
    /// accessor puts its lowest vertex at <c>y = -0.004</c> — the model's origin is at the feet, as
    /// `15` §C1 requires. So the feet sit at <c>-1.72 + (-0.004 x 1.32) = -1.7253</c> below the
    /// scene's origin, and lifting the hero by that much puts it standing on something rather than
    /// buried to the waist in it.
    /// <para>
    /// That framing belongs to Home, which is the only other screen that instances the hero, and
    /// <c>Hero.cs</c>'s own remarks make it part of the hero's contract — so it is countered here
    /// rather than edited there. Moving it onto Home's own instance override is the cleaner fix and
    /// is a change of its own, with its own visual regression to check.
    /// </para>
    /// </remarks>
    private const float HeroOriginAboveFeet = 1.7253f;

    /// <summary>
    /// The yaw <c>Hero.tscn</c> bakes into its <c>Model</c> child, in degrees.
    /// </summary>
    /// <remarks>
    /// Also Home's framing, and countered for the same reason. Read off the same transform: its X
    /// basis is <c>(1.22388, 0, -0.49449)</c>, which at length 1.32 is a yaw of 22 degrees.
    /// </remarks>
    private const float HeroModelYawDegrees = 22f;

    /// <summary>The colour a node with a tile kind this build has no colour for is drawn in.</summary>
    private static readonly Color UnknownTileColour = new(0.24f, 0.25f, 0.30f);

    private Node3D? _track;
    private Node3D? _heroRig;
    private Node3D? _heroPivot;

    private BoardLayout? _layout;
    private PackedScene? _tileScene;
    private Mesh? _puckMesh;
    private Mesh? _ribbonMesh;

    /// <summary>How thick a puck is, so the hero can be stood on top of one rather than inside it.</summary>
    private float _puckHeight;

    /// <summary>What the camera follows: the hero's own node, moving as it hops.</summary>
    public Node3D? HeroAnchor => _heroRig;

    /// <summary>Whether a board has been built into this world.</summary>
    public bool IsBuilt => _layout is not null;

    /// <summary>The box the whole board sits inside.</summary>
    public Aabb BoardBounds { get; private set; }

    /// <inheritdoc/>
    public override void _Ready()
    {
        _track = GetNodeOrNull<Node3D>(TrackPath);
        _heroRig = GetNodeOrNull<Node3D>(HeroRigPath);
        _heroPivot = GetNodeOrNull<Node3D>(HeroPivotPath);

        if (_track is null || _heroRig is null || _heroPivot is null)
        {
            GD.PushError(
                $"The board's 3D world is missing '{TrackPath}', '{HeroRigPath}' or " +
                $"'{HeroPivotPath}'. Nothing of the board can be drawn without them, and the screen " +
                "would come up as an empty dark frame with a working interface over it.");
        }
    }

    /// <summary>The box one stage sits inside, or the whole board's for a stage this board has none of.</summary>
    public Aabb StageBounds(int? stage)
    {
        if (_layout is not { } layout || stage is not { } number)
        {
            return BoardBounds;
        }

        return layout.ExtentOfStage(number) is { } extent ? Box(extent) : BoardBounds;
    }

    /// <summary>
    /// Draws a board. Called once per run — a board cannot change while the run that generated it
    /// is alive, and rebuilding one every render would write a few hundred transforms a frame.
    /// </summary>
    /// <param name="board">The projected board.</param>
    /// <param name="metrics">The distances to lay it out with.</param>
    /// <param name="colourOf">The colour one tile kind is drawn in — Board.cs's palette, unmoved.</param>
    /// <param name="scale">How large the hero is drawn on the board.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public void Build(
        BoardView board, BoardLayoutMetrics metrics, Func<TileKind, Color> colourOf, float scale)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(colourOf);

        if (_track is not { } track)
        {
            return;
        }

        Clear(track);
        _layout = null;

        if (!LoadTemplates())
        {
            return;
        }

        var layout = BoardLayout.Of(board, metrics);
        var kindByNodeId = board.Nodes.ToDictionary(node => node.NodeId, node => node.Tile);

        DrawSegments(track, layout);
        DrawPucks(track, layout, kindByNodeId, colourOf);
        DrawGateMarks(track, layout, kindByNodeId);

        _layout = layout;
        BoardBounds = Box(layout.Extent);

        ScaleHero(scale);
    }

    /// <summary>Puts the hero on a node with no motion at all.</summary>
    public void PlaceHero(int? nodeId)
    {
        if (_heroRig is not { } rig)
        {
            return;
        }

        rig.Visible = nodeId is not null && _layout is not null;

        if (Placement(nodeId) is not { } placement)
        {
            return;
        }

        rig.Position = StandingPosition(placement.Centre);
        rig.Rotation = new Vector3(0f, placement.HeadingRadians, 0f);
    }

    /// <summary>
    /// Draws the hero part-way through a hop, on an arc between two nodes.
    /// </summary>
    /// <remarks>
    /// An arc rather than a slide, because the hero mesh is unrigged and has no clip of any kind —
    /// <c>hero_export.py</c> exports with animations off, and no skeleton or animation player exists
    /// anywhere in this build. A hop per node is what a board-game piece does anyway, and it is the
    /// one motion that reads as deliberate rather than as a walk cycle that failed to load. One hop
    /// per node the run traversed, so the number on the die is readable from the motion.
    /// </remarks>
    /// <param name="fromNodeId">The node the hop left.</param>
    /// <param name="toNodeId">The node it is arriving at.</param>
    /// <param name="progress">How far through the hop, 0 to 1.</param>
    /// <param name="arcHeight">How high the hop rises at its midpoint.</param>
    public void HopHero(int? fromNodeId, int toNodeId, float progress, float arcHeight)
    {
        if (_heroRig is not { } rig ||
            Placement(fromNodeId) is not { } from ||
            Placement(toNodeId) is not { } to)
        {
            PlaceHero(toNodeId);

            return;
        }

        var t = Math.Clamp(progress, 0f, 1f);
        var start = StandingPosition(from.Centre);
        var end = StandingPosition(to.Centre);

        rig.Visible = true;
        rig.Position = start.Lerp(end, t) + (Vector3.Up * (4f * arcHeight * t * (1f - t)));

        // Turned toward the node being arrived at rather than snapped on landing: a snap reads as a
        // glitch on a mesh with no animation to cover it.
        rig.Rotation = new Vector3(
            0f, Mathf.LerpAngle(from.HeadingRadians, to.HeadingRadians, t), 0f);
    }

    /// <summary>The stage the node the hero is drawn on belongs to, or null when it is on none.</summary>
    public int? StageOf(int? nodeId) => Placement(nodeId)?.Stage;

    private BoardNodePlacement? Placement(int? nodeId) =>
        _layout is { } layout && nodeId is { } id ? layout.Placement(id) : null;

    /// <summary>Where a hero standing on a puck's centre actually stands: on its top face.</summary>
    private Vector3 StandingPosition(BoardPoint centre) =>
        new(centre.X, centre.Y + (_puckHeight / 2f), centre.Z);

    /// <summary>
    /// Sizes the hero for the board and cancels the framing <c>Hero.tscn</c> carries for Home.
    /// </summary>
    private void ScaleHero(float scale)
    {
        if (_heroPivot is not { } pivot)
        {
            return;
        }

        pivot.Scale = new Vector3(scale, scale, scale);
        pivot.Rotation = new Vector3(0f, Mathf.DegToRad(-HeroModelYawDegrees), 0f);
        pivot.Position = new Vector3(0f, HeroOriginAboveFeet * scale, 0f);
    }

    /// <summary>
    /// Reads the meshes out of the two template scenes, and says so loudly when it cannot.
    /// </summary>
    /// <remarks>
    /// <c>GD.Load</c> answers null rather than throwing when a resource is missing or its import
    /// cannot be read, so an unnamed null reference is all a caller gets unless the scene is named
    /// here. The same reason <c>TrackNode.tscn</c>'s load was named before it.
    /// </remarks>
    private bool LoadTemplates()
    {
        _tileScene ??= GD.Load<PackedScene>(TileScenePath);
        var segmentScene = GD.Load<PackedScene>(SegmentScenePath);

        if (_tileScene is null || segmentScene is null)
        {
            GD.PushError(
                $"The board cannot be drawn: no template could be loaded from '{TileScenePath}' or " +
                $"'{SegmentScenePath}'. The whole board goes with them, including the mark on the " +
                "nodes the run may not walk past.");

            return false;
        }

        if (_puckMesh is null)
        {
            using var tile = _tileScene.Instantiate<Node3D>();
            var puck = tile.GetNodeOrNull<MeshInstance3D>(TilePuckPath);

            if (puck?.Mesh is null)
            {
                GD.PushError(
                    $"The board's tile template '{TileScenePath}' carries no '{TilePuckPath}' mesh, " +
                    "so there is nothing to draw a node of the board with.");

                return false;
            }

            _puckMesh = puck.Mesh;
            _puckHeight = puck.Mesh.GetAabb().Size.Y;
        }

        if (_ribbonMesh is null)
        {
            using var segment = segmentScene.Instantiate<Node3D>();
            var ribbon = segment.GetNodeOrNull<MeshInstance3D>(SegmentRibbonPath);

            if (ribbon?.Mesh is null)
            {
                GD.PushError(
                    $"The board's path template '{SegmentScenePath}' carries no " +
                    $"'{SegmentRibbonPath}' mesh, so the tiles would be drawn with nothing joining " +
                    "them and the board would read as a scatter rather than as a track.");

                return false;
            }

            _ribbonMesh = ribbon.Mesh;
        }

        return true;
    }

    /// <summary>One multimesh per palette colour: the whole board's tiles in a handful of draw calls.</summary>
    private void DrawPucks(
        Node3D track,
        BoardLayout layout,
        IReadOnlyDictionary<int, TileKind> kindByNodeId,
        Func<TileKind, Color> colourOf)
    {
        var byColour = new Dictionary<Color, List<Transform3D>>();

        foreach (var placement in layout.Placements)
        {
            var colour = kindByNodeId.TryGetValue(placement.NodeId, out var kind)
                ? colourOf(kind)
                : UnknownTileColour;

            if (!byColour.TryGetValue(colour, out var transforms))
            {
                transforms = new List<Transform3D>();
                byColour[colour] = transforms;
            }

            transforms.Add(new Transform3D(Basis.Identity, Vector(placement.Centre)));
        }

        foreach (var (colour, transforms) in byColour)
        {
            track.AddChild(Batch(_puckMesh!, colour, transforms));
        }
    }

    /// <summary>
    /// Two multimeshes: the spine's runs of track, and every fork branch's, tinted apart.
    /// </summary>
    /// <remarks>
    /// The tint plus <see cref="BoardLayout"/>'s sideways offset is how `03` §8's "side by side with
    /// a clear join, never as ambiguous crossing lines" is met — the branch runs beside the spine at
    /// a visibly different shade and visibly returns to it.
    /// </remarks>
    private void DrawSegments(Node3D track, BoardLayout layout)
    {
        var spine = new List<Transform3D>();
        var branch = new List<Transform3D>();

        foreach (var segment in layout.Segments)
        {
            if (layout.Placement(segment.FromNodeId) is not { } from ||
                layout.Placement(segment.ToNodeId) is not { } to)
            {
                continue;
            }

            (segment.OnSpine ? spine : branch).Add(Ribbon(from.Centre, to.Centre));
        }

        if (spine.Count > 0)
        {
            track.AddChild(Batch(_ribbonMesh!, SpinePathColour, spine));
        }

        if (branch.Count > 0)
        {
            track.AddChild(Batch(_ribbonMesh!, BranchPathColour, branch));
        }
    }

    /// <summary>
    /// Instances the tile template on its own for the one or two nodes that carry the gate mark.
    /// </summary>
    /// <remarks>
    /// Individually rather than batched, because there are one or two of them on a board of any
    /// length and the mark is several meshes rather than one. The puck itself is hidden: the
    /// multimesh already drew it.
    /// </remarks>
    private void DrawGateMarks(
        Node3D track, BoardLayout layout, IReadOnlyDictionary<int, TileKind> kindByNodeId)
    {
        foreach (var placement in layout.Placements)
        {
            if (!kindByNodeId.TryGetValue(placement.NodeId, out var kind) || kind != TileKind.MiniBoss)
            {
                continue;
            }

            var tile = _tileScene!.Instantiate<Node3D>();
            var puck = tile.GetNodeOrNull<MeshInstance3D>(TilePuckPath);
            var gate = tile.GetNodeOrNull<Node3D>(TileGatePath);

            if (gate is null)
            {
                GD.PushError(
                    $"The board's tile template '{TileScenePath}' carries no '{TileGatePath}' child, " +
                    "so no node can be marked as one the run may not walk past. The board is drawn " +
                    "without the mark.");

                tile.QueueFree();

                return;
            }

            if (puck is not null)
            {
                puck.Visible = false;
            }

            gate.Visible = true;
            tile.Position = Vector(placement.Centre);
            tile.Rotation = new Vector3(0f, placement.HeadingRadians, 0f);

            track.AddChild(tile);
        }
    }

    /// <summary>The colour the spine's runs of track are drawn in.</summary>
    private static readonly Color SpinePathColour = new(0.19f, 0.20f, 0.25f);

    /// <summary>The colour a fork branch's runs of track are drawn in — lighter, so the two read apart.</summary>
    private static readonly Color BranchPathColour = new(0.30f, 0.31f, 0.38f);

    /// <summary>A ribbon stretched to span two placements, flat on the ground between them.</summary>
    private static Transform3D Ribbon(BoardPoint from, BoardPoint to)
    {
        var start = Vector(from);
        var end = Vector(to);
        var span = end - start;
        var length = span.Length();

        // Degenerate spans do happen: a layout with zero spacing is a legal thing to author. Drawn
        // as nothing rather than as a NaN-oriented ribbon.
        if (length <= 0.0001f)
        {
            return new Transform3D(Basis.Identity.Scaled(Vector3.Zero), start);
        }

        var basis = new Basis(new Vector3(0f, 1f, 0f), Mathf.Atan2(span.X, span.Z))
            .Scaled(new Vector3(RibbonWidth, RibbonThickness, length));

        return new Transform3D(basis, start.Lerp(end, 0.5f) + (Vector3.Down * RibbonSink));
    }

    /// <summary>How wide a run of track is drawn.</summary>
    private const float RibbonWidth = 0.34f;

    /// <summary>How thick it is drawn.</summary>
    private const float RibbonThickness = 0.05f;

    /// <summary>How far below a puck's middle the track sits, so the pucks read as standing on it.</summary>
    private const float RibbonSink = 0.06f;

    /// <summary>One multimesh instance holding every transform that shares a colour.</summary>
    private static MultiMeshInstance3D Batch(Mesh mesh, Color colour, IReadOnlyList<Transform3D> transforms)
    {
        var multi = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = transforms.Count,
        };

        for (var i = 0; i < transforms.Count; i++)
        {
            multi.SetInstanceTransform(i, transforms[i]);
        }

        return new MultiMeshInstance3D
        {
            Multimesh = multi,

            // Unshaded, for the three reasons BoardTile.tscn states: the palette was chosen as drawn
            // colour, the contrast suite measures authored albedo, and this build has no probe and
            // no sky behind a lit material.
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = colour,
            },
        };
    }

    private static Vector3 Vector(BoardPoint point) => new(point.X, point.Y, point.Z);

    private static Aabb Box(BoardExtent extent)
    {
        var minimum = Vector(extent.Minimum);

        return new Aabb(minimum, Vector(extent.Maximum) - minimum);
    }

    /// <summary>Empties a container now rather than at the end of the frame.</summary>
    /// <remarks>
    /// The same discipline <c>Board.cs</c> already uses on its containers, for the same reason:
    /// <c>QueueFree</c> alone defers removal, so a child freed and a child added in one frame are
    /// both in the tree and both drawn until it ends.
    /// </remarks>
    private static void Clear(Node container)
    {
        foreach (var child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }
}
