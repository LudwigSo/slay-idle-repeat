namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>An axis-aligned box in stage space — what the battle camera frames.</summary>
public readonly record struct StageBounds(
    double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ);

/// <summary>The battle camera's own numbers. Handed in by the scene; nothing here defaults them.</summary>
/// <param name="YawDegrees">The rig's turn about the vertical axis, for the three-quarter view.</param>
/// <param name="PitchDegrees">How far the camera looks down.</param>
/// <param name="Margin">A multiplier on the fitted distance, so the stage does not touch the frame edge.</param>
/// <param name="UsableBandTop">Where the band the interface leaves free starts, as a fraction of screen height from the top.</param>
/// <param name="UsableBandBottom">Where that band ends, same units.</param>
/// <param name="HalfLife">How long the follow takes to close half a gap, for <see cref="BoardFraming.Damp"/>.</param>
public sealed record BattleCameraMetrics(
    double YawDegrees,
    double PitchDegrees,
    double Margin,
    double UsableBandTop,
    double UsableBandBottom,
    double HalfLife);

/// <summary>Where the camera sits to frame a stage.</summary>
/// <param name="Distance">How far back from the box's centre, along the camera's own forward axis.</param>
/// <param name="VerticalShift">How far up the camera's own up axis the framed point moves to sit in the free band.</param>
public readonly record struct StageFrame(double Distance, double VerticalShift);

/// <summary>
/// The arithmetic the battle camera frames by, on top of <see cref="BoardFraming"/>: the box's eight
/// corners projected onto the yawed, pitched camera's right and up axes give the half-extents
/// <see cref="BoardFraming.DistanceThatFits"/> solves for, and the band lift is
/// <see cref="BoardFraming.VerticalShiftFor"/> at that distance.
/// </summary>
public static class BattleFraming
{
    /// <summary>Frames a box for a camera of this field of view and aspect.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="camera"/> is null.</exception>
    public static StageFrame Frame(
        StageBounds bounds, BattleCameraMetrics camera, double verticalFovDegrees, double aspect)
    {
        ArgumentNullException.ThrowIfNull(camera);

        var yaw = double.DegreesToRadians(camera.YawDegrees);
        var pitch = double.DegreesToRadians(camera.PitchDegrees);

        var right = new Axis(Math.Cos(yaw), 0d, -Math.Sin(yaw));
        var up = new Axis(-Math.Sin(pitch) * Math.Sin(yaw), Math.Cos(pitch), -Math.Sin(pitch) * Math.Cos(yaw));

        var halfSize = new Axis(
            (bounds.MaxX - bounds.MinX) / 2d,
            (bounds.MaxY - bounds.MinY) / 2d,
            (bounds.MaxZ - bounds.MinZ) / 2d);

        var distance = BoardFraming.DistanceThatFits(
            HalfExtentAlong(right, halfSize),
            HalfExtentAlong(up, halfSize),
            verticalFovDegrees,
            aspect,
            camera.Margin);
        var verticalShift = BoardFraming.VerticalShiftFor(
            camera.UsableBandTop, camera.UsableBandBottom, distance, verticalFovDegrees);

        return new StageFrame(distance, verticalShift);
    }

    /// <summary>
    /// How far the box's corners reach along an axis, either side of its centre. Of the eight, the
    /// corner whose signs match the axis's reaches furthest, and its projection is this sum.
    /// </summary>
    private static double HalfExtentAlong(Axis axis, Axis halfSize) =>
        (Math.Abs(axis.X) * halfSize.X) + (Math.Abs(axis.Y) * halfSize.Y) + (Math.Abs(axis.Z) * halfSize.Z);

    private readonly record struct Axis(double X, double Y, double Z);
}
