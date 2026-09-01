using Godot;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>What the board camera is looking at.</summary>
public enum BoardCameraMode
{
    /// <summary>Riding behind and above the hero as it walks.</summary>
    Follow,

    /// <summary>Pulled back to frame the whole of the stage the hero is in, tiles still legible.</summary>
    Stage,

    /// <summary>Pulled back to frame the whole board, as shape rather than as something to read.</summary>
    Whole,
}

/// <summary>
/// Rides the board camera behind the hero, and steps it back to the stage and to the whole board.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The script is on the RIG, never on the camera.</b> <c>ScreenStage.Show</c> resolves
/// <c>%Camera</c> as a <see cref="Camera3D"/> and calls <c>MakeCurrent</c> on it; that is the whole
/// handover contract and it is orthogonal to every transform written here. So the camera stays a
/// bare <see cref="Camera3D"/> keeping its name and its scene-unique flag, and only its PARENT
/// changed. A slipped name there makes every handover draw through the previous screen's camera —
/// silent, and it looks like a z-order bug rather than a camera one.
/// </para>
/// <para>
/// Three nodes deep, and each level earns it. The rig carries the world position being looked at, so
/// following is one write a frame. The pitch node carries the look angle, so easing between the
/// three modes is a rotation rather than a look-at re-derived from a moving target. The camera
/// carries only distance, as its own local Z — which is exactly the one number
/// <see cref="BoardFraming.DistanceThatFits"/> returns.
/// </para>
/// <para>
/// 🔒 <b>All three modes are the same three targets fed to one smoother</b>, never three code
/// paths: a position, a pitch and a distance. That is what makes stepping between them free, and
/// what makes it impossible for the way out and the way back to disagree.
/// </para>
/// <para>
/// ⚠️ Every number below is an <c>[Export]</c> and not one of them is authored by a design document
/// — see the block comment on <c>Board.tscn</c>. What IS authored and is therefore derived rather
/// than picked: the hero sits in the band the interface leaves free (`03` §8, `13` §3), and a
/// pull-back fits what it claims to fit at the viewport's real aspect.
/// </para>
/// </remarks>
public partial class BoardCameraRig : Node3D
{
    private const string PitchPath = "Pitch";
    private const string CameraPath = "%Camera";

    /// <summary>How far behind the hero the camera rides.</summary>
    [Export] public float FollowDistance { get; set; } = 9f;

    /// <summary>How far the camera looks down while following, in degrees below the horizon.</summary>
    [Export] public float FollowPitchDegrees { get; set; } = 34f;

    /// <summary>How far it looks down while framing one stage.</summary>
    [Export] public float StagePitchDegrees { get; set; } = 58f;

    /// <summary>How far it looks down while framing the whole board.</summary>
    [Export] public float WholePitchDegrees { get; set; } = 72f;

    /// <summary>How long the follow takes to close half the gap to the hero.</summary>
    [Export] public float FollowHalfLife { get; set; } = 0.14f;

    /// <summary>
    /// How long a pull-back or a return takes to close half its gap. Deliberately slower than the
    /// follow: a step out that snapped would read as a cut rather than as the same board seen wider.
    /// </summary>
    [Export] public float OverviewHalfLife { get; set; } = 0.42f;

    /// <summary>How much room a pull-back leaves around what it frames.</summary>
    [Export] public float OverviewMargin { get; set; } = 1.12f;

    /// <summary>
    /// The band of the screen the hero is framed into, as fractions of screen height from the top.
    /// </summary>
    /// <remarks>
    /// `13` §3 spends the top of the board screen on the HP, gold and stage rows and the bottom on
    /// the action column — whose roll button is required to be the largest thing on the screen and
    /// inside the thumb zone. Framing the hero at the centre of the viewport puts it behind that
    /// button, so the free band is what it is framed into instead. `03` §8's "roughly 40% screen
    /// height" is the middle of this band.
    /// </remarks>
    [Export] public float UsableBandTop { get; set; } = 0.22f;

    /// <inheritdoc cref="UsableBandTop"/>
    [Export] public float UsableBandBottom { get; set; } = 0.58f;

    private Node3D? _pitch;
    private Camera3D? _camera;
    private Node3D? _followTarget;

    private Aabb _board;
    private Aabb _stage;
    private bool _framed;

    /// <summary>Which of the three the camera is heading for.</summary>
    public BoardCameraMode Mode { get; private set; } = BoardCameraMode.Follow;

    /// <inheritdoc/>
    public override void _Ready()
    {
        _pitch = GetNodeOrNull<Node3D>(PitchPath);
        _camera = GetNodeOrNull<Camera3D>(CameraPath);

        if (_pitch is null || _camera is null)
        {
            GD.PushError(
                $"The board's camera rig cannot find its '{PitchPath}' node or the '{CameraPath}' " +
                "camera under it. Without them the camera never moves and the board is drawn from " +
                "wherever the scene left it — which looks like a framing choice rather than a fault.");
        }
    }

    /// <summary>What the follow mode rides. Written every frame of a walk.</summary>
    public void Follows(Node3D? target) => _followTarget = target;

    /// <summary>
    /// The boxes the two pull-backs frame: the whole board, and the stage the hero is in.
    /// </summary>
    /// <remarks>
    /// Handed in rather than computed here, because which stage the hero is in is a fact about the
    /// run and this node knows nothing about runs. Re-handed whenever the hero changes stage.
    /// </remarks>
    public void Frames(Aabb board, Aabb stage)
    {
        _board = board;
        _stage = stage;
        _framed = true;
    }

    /// <summary>Steps to the next mode: follow, then the stage, then the whole board, then follow.</summary>
    public void Step() =>
        Mode = Mode switch
        {
            BoardCameraMode.Follow => BoardCameraMode.Stage,
            BoardCameraMode.Stage => BoardCameraMode.Whole,
            _ => BoardCameraMode.Follow,
        };

    /// <summary>Returns to following the hero. Every control that moves the run calls this first.</summary>
    public void Follow() => Mode = BoardCameraMode.Follow;

    /// <summary>Arrives at the current mode's framing at once, with no ease.</summary>
    /// <remarks>
    /// For entering the screen and for resuming it. A board resumed after another screen handed back
    /// may be looking at a run that moved while the player was elsewhere, and easing across that gap
    /// would animate a journey the player did not take and did not see.
    /// </remarks>
    public void Snap()
    {
        if (_pitch is null || _camera is null)
        {
            return;
        }

        var (position, pitchDegrees, distance) = TargetFraming();

        GlobalPosition = position;
        _pitch.Rotation = new Vector3(Mathf.DegToRad(-pitchDegrees), _pitch.Rotation.Y, _pitch.Rotation.Z);
        _camera.Position = new Vector3(_camera.Position.X, _camera.Position.Y, distance);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Gated on visibility because a hidden board keeps processing — <c>Board.LeaveToHome</c>'s own
    /// remarks record it — and there is no reason to keep easing a camera nobody is looking at.
    /// </remarks>
    public override void _Process(double delta)
    {
        if (_pitch is null || _camera is null || !IsVisibleInTree())
        {
            return;
        }

        var (position, pitchDegrees, distance) = TargetFraming();

        var halfLife = Mode == BoardCameraMode.Follow ? FollowHalfLife : OverviewHalfLife;
        var remaining = (float)BoardFraming.Damp(Math.Max(halfLife, 0.001f), delta);

        GlobalPosition = position + ((GlobalPosition - position) * remaining);

        var pitch = Mathf.DegToRad(-pitchDegrees);
        _pitch.Rotation = new Vector3(
            pitch + ((_pitch.Rotation.X - pitch) * remaining), _pitch.Rotation.Y, _pitch.Rotation.Z);

        _camera.Position = new Vector3(
            _camera.Position.X,
            _camera.Position.Y,
            distance + ((_camera.Position.Z - distance) * remaining));
    }

    /// <summary>Where the rig, the pitch and the camera are heading, for the mode it is in.</summary>
    private (Vector3 Position, float PitchDegrees, float Distance) TargetFraming()
    {
        if (Mode == BoardCameraMode.Follow || !_framed)
        {
            var target = _followTarget is { } hero && IsInstanceValid(hero)
                ? hero.GlobalPosition
                : GlobalPosition;

            return (Lifted(target, FollowDistance, FollowPitchDegrees), FollowPitchDegrees, FollowDistance);
        }

        var box = Mode == BoardCameraMode.Stage ? _stage : _board;
        var pitchDegrees = Mode == BoardCameraMode.Stage ? StagePitchDegrees : WholePitchDegrees;
        var distance = FitDistance(box, pitchDegrees);

        return (Lifted(box.GetCenter(), distance, pitchDegrees), pitchDegrees, distance);
    }

    /// <summary>
    /// The framed point, moved along the screen's up axis so it lands in the band the interface
    /// leaves free rather than behind the roll button.
    /// </summary>
    private Vector3 Lifted(Vector3 target, float distance, float pitchDegrees)
    {
        if (_camera is not { } camera)
        {
            return target;
        }

        var shift = (float)BoardFraming.VerticalShiftFor(
            UsableBandTop, UsableBandBottom, distance, camera.Fov);

        // Along the screen's up axis rather than the world's, so the shift stays correct as the
        // pitch changes between the three modes. At a steep pitch the screen's up is mostly the
        // board's forward, which is exactly right: looking down, "higher on screen" is "further on".
        var pitch = Mathf.DegToRad(-pitchDegrees);
        var screenUp = new Vector3(0f, Mathf.Cos(pitch), -Mathf.Sin(pitch));

        return target + (screenUp * shift);
    }

    /// <summary>
    /// How far back the camera must sit to fit a box, at the viewport's real aspect.
    /// </summary>
    /// <remarks>
    /// 🔴 The box is measured in the plane the camera looks at, which at a steep pitch is mostly the
    /// board's own ground plane — so its screen height comes from the board's DEPTH foreshortened by
    /// the pitch, not from its (zero) height. A fit that used the box's Y extent would treat a flat
    /// board as having no height at all and fly the camera into it.
    /// </remarks>
    private float FitDistance(Aabb box, float pitchDegrees)
    {
        if (_camera is not { } camera)
        {
            return FollowDistance;
        }

        var pitch = Mathf.DegToRad(pitchDegrees);
        var halfWidth = Math.Max(box.Size.X, 0.001f) / 2f;
        var halfHeight = Math.Max(
            ((box.Size.Z * Mathf.Sin(pitch)) + (box.Size.Y * Mathf.Cos(pitch))) / 2f, 0.001f);

        var viewport = GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
        var aspect = viewport.Y > 0f ? viewport.X / viewport.Y : 1f;

        return (float)BoardFraming.DistanceThatFits(
            halfWidth, halfHeight, camera.Fov, aspect, OverviewMargin);
    }
}
