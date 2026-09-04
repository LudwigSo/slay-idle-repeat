using Godot;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>What the world dresses its actors with, beyond where they stand.</summary>
/// <param name="HeroFacingYawDegrees">How far the hero's model is turned inside its rig to look along the rig's −Z.</param>
/// <param name="HeroHeight">How tall the hero stands, so its plate can sit above it.</param>
/// <param name="PlateClearance">How far above an actor's head its plate anchor sits.</param>
/// <param name="PulseSwell">How much a wind-up swells the actor at its peak, as a fraction of its size.</param>
public sealed record ActorDressing(
    float HeroFacingYawDegrees, float HeroHeight, float PlateClearance, float PulseSwell);

/// <summary>
/// The battle stage as a place: a rig per actor standing where <see cref="BattleStageLayout"/> puts
/// it, posed each frame the way <see cref="BattleChoreography"/> says.
/// </summary>
/// <remarks>
/// <para>
/// Each rig is three nodes: the root carries the rest position and the facing, so its −Z looks
/// across the stage; <c>Motion</c> under it carries the frame's pose, so a displacement is a local
/// position, a tip is a rotation about the rig's own side axis and a squash is a scale; and the
/// model under that is the hero scene or an <see cref="Enemy"/>. Built per roster at runtime, which
/// is the one sanctioned reason for a node tree made in code rather than in a scene.
/// </para>
/// <para>
/// 🔒 Nothing here knows how many actors a fight fields or which side is which — every count and
/// side comes off the roster, and every distance off the metrics handed in.
/// </para>
/// </remarks>
public partial class BattleWorld : Node3D
{
    private const string ActorsPath = "Actors";
    private const string MotionName = "Motion";

    private Node3D? _actors;
    private readonly List<ActorRig> _rigs = [];
    private readonly Dictionary<byte, ActorRig> _rigById = new();

    private float _plateClearance;
    private float _pulseSwell;

    /// <summary>
    /// The box the actors on the stage stand inside, as <see cref="BattleFraming.BoundsOf"/> draws it
    /// over every rig's rest point, height and presence.
    /// </summary>
    public StageBounds StageBounds
    {
        get
        {
            var footprints = new StageFootprint[_rigs.Count];

            for (var index = 0; index < _rigs.Count; index++)
            {
                var rig = _rigs[index];

                footprints[index] = new StageFootprint(rig.Rest, rig.Height, rig.Shown);
            }

            return BattleFraming.BoundsOf(footprints, _plateClearance);
        }
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        _actors = GetNodeOrNull<Node3D>(ActorsPath);

        if (_actors is null)
        {
            GD.PushError(
                $"The battle's 3D world is missing '{ActorsPath}', so no actor can be stood on the " +
                "stage and the fight would play out on an empty floor with a working interface over it.");
        }
    }

    /// <summary>
    /// Stands every actor of a fight on the stage. Called once per fight, off a roster the fight
    /// does not change.
    /// </summary>
    /// <param name="actors">The roster.</param>
    /// <param name="biome">The chapter's art set, which picks each enemy's model; null dresses every enemy as Unknown.</param>
    /// <param name="metrics">The distances the stage is laid out with.</param>
    /// <param name="dressing">What the actors are dressed with beyond where they stand.</param>
    /// <exception cref="ArgumentNullException">The roster, the metrics or the dressing is null.</exception>
    public void Build(
        IReadOnlyList<ReplayActor> actors, string? biome, BattleStageMetrics metrics, ActorDressing dressing)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(dressing);

        if (_actors is not { } stage)
        {
            return;
        }

        Clear(stage);

        _plateClearance = dressing.PlateClearance;
        _pulseSwell = dressing.PulseSwell;

        var heroScene = GD.Load<PackedScene>(Hero.ScenePath);
        var enemyScene = GD.Load<PackedScene>(Enemy.ScenePath);

        if (heroScene is null || enemyScene is null)
        {
            GD.PushError(
                $"The battle stage cannot be dressed: no template could be loaded from '{Hero.ScenePath}' " +
                $"or '{Enemy.ScenePath}'. The fight plays out on an empty floor.");

            return;
        }

        var placements = BattleStageLayout.Place(actors, metrics);

        for (var index = 0; index < actors.Count; index++)
        {
            var rig = Stand(stage, actors[index], placements[index], biome, dressing, heroScene, enemyScene);

            _rigs.Add(rig);
            _rigById[rig.ActorId] = rig;
        }
    }

    /// <summary>Draws every actor in the pose the choreography has it in this frame.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="choreography"/> is null.</exception>
    public void Pose(BattleChoreography choreography)
    {
        ArgumentNullException.ThrowIfNull(choreography);

        for (var index = 0; index < _rigs.Count; index++)
        {
            var rig = _rigs[index];

            Apply(rig, choreography.PoseOf(rig.ActorId));
        }
    }

    /// <summary>
    /// Where an actor's plate hangs: above its head by the clearance, following its body. Null for
    /// an actor the stage does not hold or one that is not present on it.
    /// </summary>
    public Vector3? AnchorOf(byte actorId)
    {
        if (!_rigById.TryGetValue(actorId, out var rig) || !rig.Shown)
        {
            return null;
        }

        return rig.Motion.GlobalPosition + (Vector3.Up * (rig.Height + _plateClearance));
    }

    private static ActorRig Stand(
        Node3D stage,
        ReplayActor actor,
        StagePlacement placement,
        string? biome,
        ActorDressing dressing,
        PackedScene heroScene,
        PackedScene enemyScene)
    {
        var facing = placement.Facing == StageFacing.PositiveX ? Vector3.Right : Vector3.Left;

        // Yawed so the root's own −Z looks along the facing, the way every Node3D looks: an advance
        // toward the counterpart is then mostly along local −Z and a side-step along local X.
        var root = new Node3D
        {
            Position = new Vector3(placement.Position.X, 0f, placement.Position.Z),
            Rotation = new Vector3(0f, Mathf.Atan2(-facing.X, -facing.Z), 0f),
        };

        var motion = new Node3D { Name = MotionName };

        root.AddChild(motion);
        stage.AddChild(root);

        float height;

        if (actor.Side == ReplaySide.Hero)
        {
            var hero = heroScene.Instantiate<Node3D>();

            hero.RotationDegrees = new Vector3(0f, dressing.HeroFacingYawDegrees, 0f);
            motion.AddChild(hero);

            height = dressing.HeroHeight;
        }
        else
        {
            var enemy = enemyScene.Instantiate<Enemy>();

            motion.AddChild(enemy);
            enemy.Dress(ModelFor(actor, biome));

            height = enemy.Height;
        }

        return new ActorRig(actor.ActorId, root, motion, placement.Position, facing, height);
    }

    /// <summary>The catalogue's model for an actor, or null with the reason pushed.</summary>
    private static EnemyModel? ModelFor(ReplayActor actor, string? biome)
    {
        var model = EnemyModelCatalogue.For(biome, actor.Identity);

        if (model is null)
        {
            GD.PushError(
                $"No enemy model for actor {actor.ActorId} with identity '{actor.Identity ?? "none"}' " +
                $"in biome '{biome ?? "none"}', so it is drawn as a bare capsule named Unknown.");
        }

        return model;
    }

    /// <remarks>Writes the nodes only when the pose moved: an actor at rest costs the frame nothing.</remarks>
    private void Apply(ActorRig rig, in ActorPose pose)
    {
        if (rig.Posed == pose)
        {
            return;
        }

        rig.Posed = pose;

        if (rig.Shown != pose.Present)
        {
            rig.Shown = pose.Present;
            rig.Root.Visible = pose.Present;
        }

        if (!pose.Present)
        {
            return;
        }

        var toward = pose.Toward is { } counterpart && _rigById.TryGetValue(counterpart, out var other)
            ? Direction(rig, other)
            : rig.Facing;
        var side = new Vector3(-toward.Z, 0f, toward.X);

        var offset = (toward * (float)pose.Advance) +
                     (side * (float)pose.SideStep) +
                     (Vector3.Up * (float)(pose.Lift - (pose.Sink * rig.Height)));

        rig.Motion.Position = rig.ToLocal * offset;

        var swell = (float)(pose.Scale * (1d + (pose.Pulse * _pulseSwell)));
        var squash = (float)pose.Squash;

        // A squash keeps the body's volume: what comes off the height goes half each onto the width
        // and the depth. The floor of Epsilon keeps an entering actor's basis invertible at scale 0.
        var scale = new Vector3(
            Math.Max(swell * (1f + (squash / 2f)), Mathf.Epsilon),
            Math.Max(swell * (1f - squash), Mathf.Epsilon),
            Math.Max(swell * (1f + (squash / 2f)), Mathf.Epsilon));

        rig.Motion.Basis =
            Basis.FromEuler(new Vector3(Mathf.DegToRad((float)pose.TipDegrees), 0f, 0f)) * Basis.FromScale(scale);
    }

    /// <summary>The unit direction across the floor from one actor's rest point to another's.</summary>
    private static Vector3 Direction(ActorRig from, ActorRig to)
    {
        var across = to.Root.Position - from.Root.Position;

        across.Y = 0f;

        return across.LengthSquared() > 0f ? across.Normalized() : from.Facing;
    }

    private void Clear(Node3D stage)
    {
        foreach (var child in stage.GetChildren())
        {
            stage.RemoveChild(child);
            child.QueueFree();
        }

        _rigs.Clear();
        _rigById.Clear();
    }

    /// <summary>One actor's three nodes and the few facts about it the pose and the framing need.</summary>
    private sealed class ActorRig(
        byte actorId, Node3D root, Node3D motion, StagePoint rest, Vector3 facing, float height)
    {
        internal byte ActorId { get; } = actorId;

        internal Node3D Root { get; } = root;

        internal Node3D Motion { get; } = motion;

        /// <summary>Where the layout stood the actor, as the layout said it rather than read back off the node.</summary>
        internal StagePoint Rest { get; } = rest;

        internal Vector3 Facing { get; } = facing;

        internal float Height { get; } = height;

        /// <summary>The root's basis inverted, once: a world-space displacement becomes the motion node's local one through it.</summary>
        internal Basis ToLocal { get; } = root.Basis.Inverse();

        /// <summary>Whether the root is visible, kept here so a frame need not ask the node.</summary>
        internal bool Shown { get; set; } = true;

        /// <summary>The pose last written to the nodes, or null before the first.</summary>
        internal ActorPose? Posed { get; set; }
    }
}
