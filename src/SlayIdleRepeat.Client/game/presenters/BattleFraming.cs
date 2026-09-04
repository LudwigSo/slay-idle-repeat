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

/// <summary>
/// Where the camera looks and how far back it sits to frame a stage. The look-at point is the box's
/// centre moved along the camera's right axis by <see cref="HorizontalShift"/> and along its up axis
/// by <see cref="VerticalShift"/>; the camera sits <see cref="Distance"/> behind that point along its
/// own forward axis.
/// </summary>
/// <param name="Distance">How far back from the look-at point, along the camera's forward axis.</param>
/// <param name="HorizontalShift">How far along the camera's right axis the look-at point sits from the box's centre.</param>
/// <param name="VerticalShift">
/// How far along the camera's up axis the look-at point sits from the box's centre. Positive moves
/// the look-at point UP the screen, and so the picture DOWN it: a band above centre asks for a
/// negative shift.
/// </param>
public readonly record struct StageFrame(double Distance, double HorizontalShift, double VerticalShift);

/// <summary>One actor as the framing sees it: where it rests on the floor, how tall and how wide it stands, and whether it is on the stage.</summary>
/// <param name="Rest">The actor's rest point, where the layout stood it.</param>
/// <param name="Height">How tall the actor's model stands, so its plate anchor can be counted in.</param>
/// <param name="Present">Whether the actor is on the stage at all; one that is not takes no room.</param>
/// <param name="HalfWidth">The body's half extent across the floor, so the box reaches past the rest point to the body's edge.</param>
public readonly record struct StageFootprint(StagePoint Rest, double Height, bool Present, double HalfWidth);

/// <summary>
/// The arithmetic the battle camera frames by, on top of <see cref="BoardFraming"/>. Each of the
/// box's eight corners, projected onto the yawed, pitched camera's right and up axes, is fitted by
/// <see cref="BoardFraming.DistanceThatFits"/> at its own depth along the camera's forward axis, and
/// the furthest-back answer wins. Fitting alone only guarantees inclusion: under a pitched, yawed
/// view the near corners project far from the far ones, so the picture's mass sits toward the near
/// side and low. The look-at point is therefore moved off the box's centre until the projected
/// picture is centred across the screen and its vertical middle sits at the middle of the band the
/// interface leaves free, and the distance is fitted again about the moved point.
/// </summary>
public static class BattleFraming
{
    private const int Corners = 8;

    // Each pass centres the projected picture exactly for the current distance, then re-fits the
    // distance about the moved look-at point; the re-fit disturbs the centring only by the change in
    // distance over the reach, so the residual shrinks quadratically and four passes land well
    // inside a millionth of the screen. The distance is always the last thing computed, so the
    // margin holds exactly about the point returned.
    private const int CentringPasses = 4;

    /// <summary>
    /// The box the present actors stand inside: every rest point across the floor, padded by the
    /// body's half width either side, from the floor up to the plate anchor above the tallest of
    /// them. An empty stage is the zero box.
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
            var halfWidth = footprint.HalfWidth;
            var top = footprint.Height + plateClearance;

            bounds = first
                ? new StageBounds(x - halfWidth, 0d, z - halfWidth, x + halfWidth, top, z + halfWidth)
                : new StageBounds(
                    Math.Min(bounds.MinX, x - halfWidth), 0d, Math.Min(bounds.MinZ, z - halfWidth),
                    Math.Max(bounds.MaxX, x + halfWidth), Math.Max(bounds.MaxY, top), Math.Max(bounds.MaxZ, z + halfWidth));
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

        // Every corner as the camera sees it, relative to the box's centre: across the screen, up
        // it, and along the camera's forward axis. Depth does not change when the look-at point
        // moves across or up, so it is fixed here once.
        Span<double> across = stackalloc double[Corners];
        Span<double> rise = stackalloc double[Corners];
        Span<double> depth = stackalloc double[Corners];

        for (var corner = 0; corner < Corners; corner++)
        {
            var offset = new Axis(
                (corner & 1) == 0 ? -halfSize.X : halfSize.X,
                (corner & 2) == 0 ? -halfSize.Y : halfSize.Y,
                (corner & 4) == 0 ? -halfSize.Z : halfSize.Z);

            across[corner] = Dot(offset, right);
            rise[corner] = Dot(offset, up);
            depth[corner] = Dot(offset, forward);
        }

        // Godot's field of view is the vertical one under the default keep-height aspect.
        var tanVertical = Math.Tan(double.DegreesToRadians(verticalFovDegrees) / 2d);
        var tanHorizontal = aspect * tanVertical;

        // The band's centre in normalised screen coordinates, up positive: a band centred above the
        // screen's middle is a positive target, and the picture's middle is moved up to it.
        var bandCentre = 1d - (camera.UsableBandTop + camera.UsableBandBottom);

        var horizontalShift = 0d;
        var verticalShift = 0d;
        var distance = DistanceFor(across, rise, depth, horizontalShift, verticalShift, camera, verticalFovDegrees, aspect);

        for (var pass = 0; pass < CentringPasses; pass++)
        {
            if (!Recentre(across, depth, distance, tanHorizontal, 0d, ref horizontalShift) ||
                !Recentre(rise, depth, distance, tanVertical, bandCentre, ref verticalShift))
            {
                break;
            }

            distance = DistanceFor(across, rise, depth, horizontalShift, verticalShift, camera, verticalFovDegrees, aspect);
        }

        return new StageFrame(distance, horizontalShift, verticalShift);
    }

    /// <summary>
    /// How far back the look-at point the camera must sit for every corner to fit: each corner is
    /// fitted for its own projection about the look-at point, pulled forward by how much nearer than
    /// the centre it lies along the forward axis, and whichever asks for the most wins.
    /// </summary>
    private static double DistanceFor(
        ReadOnlySpan<double> across,
        ReadOnlySpan<double> rise,
        ReadOnlySpan<double> depth,
        double horizontalShift,
        double verticalShift,
        BattleCameraMetrics camera,
        double verticalFovDegrees,
        double aspect)
    {
        var distance = double.NegativeInfinity;

        for (var corner = 0; corner < Corners; corner++)
        {
            var fromLookAt = BoardFraming.DistanceThatFits(
                Math.Abs(across[corner] - horizontalShift),
                Math.Abs(rise[corner] - verticalShift),
                verticalFovDegrees,
                aspect,
                camera.Margin);

            distance = Math.Max(distance, fromLookAt - depth[corner]);
        }

        return distance;
    }

    /// <summary>
    /// Moves the look-at point along one screen axis so the middle of the projected picture lands on
    /// <paramref name="target"/>, in normalised screen coordinates. One Newton step on the two extreme
    /// corners: each projection slides by its own reach, so the step that puts the two extremes'
    /// middle on the target is exact while they stay the extremes. False when a corner sits at or
    /// behind the camera, where no projection exists.
    /// </summary>
    private static bool Recentre(
        ReadOnlySpan<double> components,
        ReadOnlySpan<double> depth,
        double distance,
        double tangent,
        double target,
        ref double shift)
    {
        var max = double.NegativeInfinity;
        var min = double.PositiveInfinity;
        var slopeAtMax = 0d;
        var slopeAtMin = 0d;

        for (var corner = 0; corner < Corners; corner++)
        {
            var reach = distance + depth[corner];

            if (reach <= 0d)
            {
                return false;
            }

            var slope = 1d / (reach * tangent);
            var projected = (components[corner] - shift) * slope;

            if (projected > max)
            {
                max = projected;
                slopeAtMax = slope;
            }

            if (projected < min)
            {
                min = projected;
                slopeAtMin = slope;
            }
        }

        shift += (max + min - (2d * target)) / (slopeAtMax + slopeAtMin);

        return true;
    }

    private static double Dot(Axis a, Axis b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private readonly record struct Axis(double X, double Y, double Z);
}
