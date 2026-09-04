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

/// <summary>One actor as the framing sees it: where it rests on the floor, how tall it stands, and whether it is on the stage.</summary>
/// <param name="Rest">The actor's rest point, where the layout stood it.</param>
/// <param name="Height">How tall the actor's model stands, so its plate anchor can be counted in.</param>
/// <param name="Present">Whether the actor is on the stage at all; one that is not takes no room.</param>
public readonly record struct StageFootprint(StagePoint Rest, double Height, bool Present);

/// <summary>
/// The arithmetic the battle camera frames by, on top of <see cref="BoardFraming"/>: each of the box's
/// eight corners, projected onto the yawed, pitched camera's right and up axes, is fitted by
/// <see cref="BoardFraming.DistanceThatFits"/> at its own depth along the camera's forward axis, the
/// furthest-back answer wins, and the band lift is <see cref="BoardFraming.VerticalShiftFor"/> at
/// that distance.
/// </summary>
public static class BattleFraming
{
    /// <summary>
    /// The box the present actors stand inside: every rest point across the floor, from the floor up
    /// to the plate anchor above the tallest of them. An empty stage is the zero box.
    /// </summary>
    /// <param name="footprints">Every actor on the roster, present or not.</param>
    /// <param name="plateClearance">How far above an actor's head its plate anchor sits.</param>
    /// <exception cref="ArgumentNullException"><paramref name="footprints"/> is null.</exception>
    public static StageBounds BoundsOf(IReadOnlyList<StageFootprint> footprints, double plateClearance)
    {
        ArgumentNullException.ThrowIfNull(footprints);

        var bounds = default(StageBounds);
        var first = true;

        for (var index = 0; index < footprints.Count; index++)
        {
            var footprint = footprints[index];

            if (!footprint.Present)
            {
                continue;
            }

            var x = (double)footprint.Rest.X;
            var z = (double)footprint.Rest.Z;
            var top = footprint.Height + plateClearance;

            bounds = first
                ? new StageBounds(x, 0d, z, x, top, z)
                : new StageBounds(
                    Math.Min(bounds.MinX, x), 0d, Math.Min(bounds.MinZ, z),
                    Math.Max(bounds.MaxX, x), Math.Max(bounds.MaxY, top), Math.Max(bounds.MaxZ, z));
            first = false;
        }

        return bounds;
    }

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
        var forward = new Axis(-Math.Cos(pitch) * Math.Sin(yaw), -Math.Sin(pitch), -Math.Cos(pitch) * Math.Cos(yaw));

        var halfSize = new Axis(
            (bounds.MaxX - bounds.MinX) / 2d,
            (bounds.MaxY - bounds.MinY) / 2d,
            (bounds.MaxZ - bounds.MinZ) / 2d);

        // A corner nearer the camera than the box's centre sits where the frustum is narrower, so
        // each is fitted at its own depth: the distance the centre needs for that corner's
        // projection, pulled forward by how much nearer the corner is. Whichever corner asks for
        // the most wins, even if every corner asks for a negative distance.
        var distance = double.NegativeInfinity;

        for (var corner = 0; corner < 8; corner++)
        {
            var offset = new Axis(
                (corner & 1) == 0 ? -halfSize.X : halfSize.X,
                (corner & 2) == 0 ? -halfSize.Y : halfSize.Y,
                (corner & 4) == 0 ? -halfSize.Z : halfSize.Z);

            var fromCentre = BoardFraming.DistanceThatFits(
                Math.Abs(Dot(offset, right)),
                Math.Abs(Dot(offset, up)),
                verticalFovDegrees,
                aspect,
                camera.Margin);

            distance = Math.Max(distance, fromCentre - Dot(offset, forward));
        }

        var verticalShift = BoardFraming.VerticalShiftFor(
            camera.UsableBandTop, camera.UsableBandBottom, distance, verticalFovDegrees);

        return new StageFrame(distance, verticalShift);
    }

    private static double Dot(Axis a, Axis b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private readonly record struct Axis(double X, double Y, double Z);
}
