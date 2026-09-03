using Godot;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// Frames the battle stage from a fixed three-quarter view, and eases to the framing when the stage
/// changes shape.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The script is on the RIG, never on the camera</b> — the same arrangement as
/// <see cref="BoardCameraRig"/>, for the same reason. <c>ScreenStage.Show</c> resolves
/// <c>%Camera</c> as a bare <see cref="Camera3D"/> and makes it current; the rig yaws, its
/// <c>Pitch</c> child tilts, and the camera under both only sits back along its own Z — which is
/// exactly the one distance <see cref="BattleFraming.Frame"/> returns.
/// </para>
/// <para>
/// ⚠️ Every number below is an <c>[Export]</c> assigned in <c>BattleReplay.tscn</c>, under the
/// banner that says why. What is derived rather than picked: the distance that fits the stage at
/// the viewport's real aspect, and the lift that puts it in the band the interface leaves free.
/// </para>
/// </remarks>
public partial class BattleCameraRig : Node3D
{
    private const string PitchPath = "Pitch";
    private const string CameraPath = "%Camera";

    /// <summary>The rig's turn about the vertical, for the three-quarter view over the hero's shoulder.</summary>
    [Export] public float YawDegrees { get; set; } = -28f;

    /// <summary>How far the camera looks down, in degrees below the horizon.</summary>
    [Export] public float PitchDegrees { get; set; } = 16f;

    /// <summary>How much room the framing leaves around the stage.</summary>
    [Export] public float Margin { get; set; } = 1.15f;

    /// <summary>Where the band the interface leaves free starts, as a fraction of screen height from the top.</summary>
    [Export] public float UsableBandTop { get; set; } = 0.14f;

    /// <inheritdoc cref="UsableBandTop"/>
    [Export] public float UsableBandBottom { get; set; } = 0.7f;

    /// <summary>How long a re-framing takes to close half its gap.</summary>
    [Export] public float HalfLife { get; set; } = 0.35f;

    private Node3D? _pitch;
    private Camera3D? _camera;
    private BattleCameraMetrics? _metrics;

    private StageBounds _bounds;
    private bool _framed;

    /// <inheritdoc/>
    public override void _Ready()
    {
        _pitch = GetNodeOrNull<Node3D>(PitchPath);
        _camera = GetNodeOrNull<Camera3D>(CameraPath);

        if (_pitch is null || _camera is null)
        {
            GD.PushError(
                $"The battle's camera rig cannot find its '{PitchPath}' node or the '{CameraPath}' " +
                "camera under it. Without them the stage is drawn from wherever the scene left the " +
                "camera, which looks like a framing choice rather than a fault.");

            return;
        }

        _metrics = new BattleCameraMetrics(
            YawDegrees, PitchDegrees, Margin, UsableBandTop, UsableBandBottom, HalfLife);

        RotationDegrees = new Vector3(0f, YawDegrees, 0f);
        _pitch.RotationDegrees = new Vector3(-PitchDegrees, 0f, 0f);
    }

    /// <summary>The box the camera frames. Re-handed whenever an actor enters the stage.</summary>
    public void Frames(StageBounds bounds)
    {
        _bounds = bounds;
        _framed = true;
    }

    /// <summary>Arrives at the framing at once, with no ease — for the first frame of a fight.</summary>
    public void Snap()
    {
        if (_camera is not { } camera || Target() is not { } target)
        {
            return;
        }

        GlobalPosition = target.Position;
        camera.Position = new Vector3(camera.Position.X, camera.Position.Y, target.Distance);
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_camera is not { } camera || !IsVisibleInTree() || Target() is not { } target)
        {
            return;
        }

        var remaining = (float)BoardFraming.Damp(Math.Max(HalfLife, Mathf.Epsilon), delta);

        GlobalPosition = target.Position + ((GlobalPosition - target.Position) * remaining);
        camera.Position = new Vector3(
            camera.Position.X,
            camera.Position.Y,
            target.Distance + ((camera.Position.Z - target.Distance) * remaining));
    }

    /// <summary>Where the rig is heading and how far back the camera sits, for the framed box.</summary>
    private (Vector3 Position, float Distance)? Target()
    {
        if (!_framed || _pitch is not { } pitch || _camera is not { } camera || _metrics is not { } metrics)
        {
            return null;
        }

        var viewport = GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
        var aspect = viewport.Y > 0f ? viewport.X / viewport.Y : 1f;

        var frame = BattleFraming.Frame(_bounds, metrics, camera.Fov, aspect);

        var centre = new Vector3(
            (float)(_bounds.MinX + _bounds.MaxX) / 2f,
            (float)(_bounds.MinY + _bounds.MaxY) / 2f,
            (float)(_bounds.MinZ + _bounds.MaxZ) / 2f);

        // Along the screen's up axis rather than the world's, which is what the pitch node's own Y
        // is once the rig has yawed and it has tilted.
        var screenUp = pitch.GlobalBasis.Y;

        return (centre + (screenUp * (float)frame.VerticalShift), (float)frame.Distance);
    }
}
