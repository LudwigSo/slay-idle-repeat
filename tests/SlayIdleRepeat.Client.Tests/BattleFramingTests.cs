using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

public sealed class BattleFramingTests
{
    private const double PortraitAspect = 1080d / 1920d;
    private const double Fov = 60d;

    // The yaw and pitch the spec names; the margin and band are not the shipped ones.
    private static readonly BattleCameraMetrics Camera = new(
        YawDegrees: -28, PitchDegrees: 16, Margin: 1.2, UsableBandTop: 0.2, UsableBandBottom: 0.6, HalfLife: 0.3);

    [Fact]
    public void Frame_fits_each_of_the_eight_corners_at_its_own_depth_behind_the_yawed_camera()
    {
        const double halfX = 4d;
        const double height = 2d;
        const double halfZ = 1d;

        var frame = BattleFraming.Frame(Box(halfX, height, halfZ), Camera, Fov, PortraitAspect);

        var (right, up, forward) = Axes(Camera);
        var expected = double.NegativeInfinity;

        // Each corner needs the centre-based fit for its own projection, less its depth beyond the
        // centre along the camera's forward axis; the corner that asks for the most sets the distance.
        foreach (var corner in Corners(halfX, height / 2, halfZ))
        {
            var fromCentre = BoardFraming.DistanceThatFits(
                Math.Abs(Dot(corner, right)), Math.Abs(Dot(corner, up)), Fov, PortraitAspect, Camera.Margin);

            expected = Math.Max(expected, fromCentre - Dot(corner, forward));
        }

        frame.Distance.ShouldBe(expected, 1e-9);
    }

    [Fact]
    public void Frame_straight_on_and_level_sits_half_the_stages_depth_behind_the_centre_based_fit()
    {
        const double halfX = 4d;
        const double height = 2d;
        const double halfZ = 1d;

        var frame = BattleFraming.Frame(
            Box(halfX, height, halfZ), Camera with { YawDegrees = 0, PitchDegrees = 0 }, Fov, PortraitAspect);

        // Straight on, the camera at +Z looks along −Z: the four corners on the box's +Z face are
        // halfZ nearer than the centre and share its projection, so they push the camera back by halfZ.
        frame.Distance.ShouldBe(
            BoardFraming.DistanceThatFits(halfX, height / 2, Fov, PortraitAspect, Camera.Margin) + halfZ,
            1e-9);
    }

    [Fact]
    public void Frame_sits_the_camera_farther_back_for_a_taller_stage()
    {
        var shorter = BattleFraming.Frame(Box(2, 1, 1), Camera, Fov, PortraitAspect);
        var taller = BattleFraming.Frame(Box(2, 8, 1), Camera, Fov, PortraitAspect);

        taller.Distance.ShouldBeGreaterThan(shorter.Distance);
    }

    [Fact]
    public void Frame_sits_the_camera_farther_back_for_a_wider_stage()
    {
        var narrower = BattleFraming.Frame(Box(2, 1, 1), Camera, Fov, PortraitAspect);
        var wider = BattleFraming.Frame(Box(8, 1, 1), Camera, Fov, PortraitAspect);

        wider.Distance.ShouldBeGreaterThan(narrower.Distance);
    }

    [Fact]
    public void Frame_lets_a_stages_depth_widen_the_picture_through_the_yaw()
    {
        const double halfX = 1d;
        const double height = 1d;
        const double halfZ = 4d;
        var deep = Box(halfX, height, halfZ);

        var straightOn = BattleFraming.Frame(deep, Camera with { YawDegrees = 0 }, Fov, PortraitAspect);
        var yawed = BattleFraming.Frame(deep, Camera, Fov, PortraitAspect);

        var (right, up, forward) = Axes(Camera);
        var expected = double.NegativeInfinity;

        foreach (var corner in Corners(halfX, height / 2, halfZ))
        {
            var fromCentre = BoardFraming.DistanceThatFits(
                Math.Abs(Dot(corner, right)), Math.Abs(Dot(corner, up)), Fov, PortraitAspect, Camera.Margin);

            expected = Math.Max(expected, fromCentre - Dot(corner, forward));
        }

        // Straight on, the depth only brings the near face closer; yawed, it also spreads the
        // corners across the frame, and the camera sits well further back for it.
        straightOn.Distance.ShouldBeGreaterThan(0d);
        yawed.Distance.ShouldBeGreaterThan(straightOn.Distance);
        yawed.Distance.ShouldBe(expected, 1e-9);
    }

    [Fact]
    public void Frame_sits_the_camera_back_far_enough_for_the_corner_nearest_to_it()
    {
        // The shipped one-on-one: the hero at x −2.4 and the enemy at x +2.4, the hero's 2.22 plus
        // the 0.35 plate clearance on top, the rest points 0.3 either side of the centre line.
        const double halfX = 2.4;
        const double height = 2.57;
        const double halfZ = 0.3;
        var shipped = Camera with { Margin = 1.15 };

        var frame = BattleFraming.Frame(Box(halfX, height, halfZ), shipped, Fov, PortraitAspect);

        var (right, up, forward) = Axes(shipped);
        var tanVertical = Math.Tan(double.DegreesToRadians(Fov) / 2);
        var tanHorizontal = PortraitAspect * tanVertical;

        foreach (var corner in Corners(halfX, height / 2, halfZ))
        {
            var reach = frame.Distance + Dot(corner, forward);

            Math.Abs(Dot(corner, right)).ShouldBeLessThanOrEqualTo(
                (tanHorizontal * reach / shipped.Margin) + 1e-9,
                $"the corner at {corner} must sit inside the frustum's width with the margin applied.");
            Math.Abs(Dot(corner, up)).ShouldBeLessThanOrEqualTo(
                (tanVertical * reach / shipped.Margin) + 1e-9,
                $"the corner at {corner} must sit inside the frustum's height with the margin applied.");
        }

        // Negative control: the centre-based fit — every corner treated as if it stood at the
        // centre's depth — leaves the corner nearest the camera outside the frustum. The camera is
        // on the hero's side, above and in front of the stage, so that is the hero's top front corner.
        var centreBased = BoardFraming.DistanceThatFits(
            (halfX * Math.Abs(right.X)) + (halfZ * Math.Abs(right.Z)),
            (halfX * Math.Abs(up.X)) + (height / 2 * Math.Abs(up.Y)) + (halfZ * Math.Abs(up.Z)),
            Fov, PortraitAspect, shipped.Margin);
        var nearest = new Axis(-halfX, height / 2, halfZ);

        Dot(nearest, forward).ShouldBeLessThan(0d, "the hero's top front corner is nearer than the centre.");
        Math.Abs(Dot(nearest, right)).ShouldBeGreaterThan(
            tanHorizontal * (centreBased + Dot(nearest, forward)) / shipped.Margin,
            "the centre-based fit would crop the hero, which is the defect this case discriminates.");
        frame.Distance.ShouldBeGreaterThan(centreBased);
    }

    [Fact]
    public void Frame_lifts_the_framed_point_into_the_band_the_interface_leaves_free()
    {
        var frame = BattleFraming.Frame(Box(4, 2, 1), Camera, Fov, PortraitAspect);

        frame.VerticalShift.ShouldBeGreaterThan(0d, "the free band sits above centre, so the stage moves up.");
        frame.VerticalShift.ShouldBe(
            BoardFraming.VerticalShiftFor(Camera.UsableBandTop, Camera.UsableBandBottom, frame.Distance, Fov),
            1e-9);
    }

    [Fact]
    public void BoundsOf_spans_the_rest_points_across_the_floor_and_tops_out_at_the_tallest_plate_anchor()
    {
        var bounds = BattleFraming.BoundsOf(
            [Footprint(-3, 0, height: 2.25), Footprint(3, -1.5f, height: 1.75), Footprint(3.5f, 1.5f, height: 4.5)],
            plateClearance: 0.5);

        bounds.ShouldBe(
            new StageBounds(MinX: -3, MinY: 0, MinZ: -1.5, MaxX: 3.5, MaxY: 5, MaxZ: 1.5),
            "the floor is the bottom, the tallest actor's head plus the clearance is the top, and the " +
            "rest points bound the sides — a box taken off one actor or without the clearance would " +
            "crop a plate.");
    }

    [Fact]
    public void BoundsOf_leaves_an_actor_that_is_off_the_stage_out_of_the_box()
    {
        var bounds = BattleFraming.BoundsOf(
            [Footprint(-2, 0, height: 2), Footprint(2, 0, height: 2), Footprint(6, 3, height: 8, present: false)],
            plateClearance: 0.5);

        bounds.ShouldBe(
            new StageBounds(MinX: -2, MinY: 0, MinZ: 0, MaxX: 2, MaxY: 2.5, MaxZ: 0),
            "a summon that has not entered yet would otherwise pull the camera back for an empty patch of floor.");
    }

    [Fact]
    public void BoundsOf_answers_the_zero_box_for_a_stage_with_nobody_on_it() =>
        BattleFraming.BoundsOf([Footprint(4, 4, height: 4, present: false)], plateClearance: 0.5)
            .ShouldBe(default(StageBounds));

    private static StageBounds Box(double halfX, double height, double halfZ) =>
        new(-halfX, 0, -halfZ, halfX, height, halfZ);

    // The rig yaws about Y, the pitch node pitches about X, and the camera sits at +Z on the pitch
    // node looking along −Z: right = (cos yaw, 0, −sin yaw), up = (−sin pitch·sin yaw, cos pitch,
    // −sin pitch·cos yaw), forward = (−cos pitch·sin yaw, −sin pitch, −cos pitch·cos yaw).
    private static (Axis Right, Axis Up, Axis Forward) Axes(BattleCameraMetrics camera)
    {
        var yaw = double.DegreesToRadians(camera.YawDegrees);
        var pitch = double.DegreesToRadians(camera.PitchDegrees);

        return (
            new Axis(Math.Cos(yaw), 0, -Math.Sin(yaw)),
            new Axis(-Math.Sin(pitch) * Math.Sin(yaw), Math.Cos(pitch), -Math.Sin(pitch) * Math.Cos(yaw)),
            new Axis(-Math.Cos(pitch) * Math.Sin(yaw), -Math.Sin(pitch), -Math.Cos(pitch) * Math.Cos(yaw)));
    }

    /// <summary>The eight corners of a box centred on the origin, as offsets from its centre.</summary>
    private static IEnumerable<Axis> Corners(double halfX, double halfY, double halfZ)
    {
        foreach (var x in new[] { -halfX, halfX })
        {
            foreach (var y in new[] { -halfY, halfY })
            {
                foreach (var z in new[] { -halfZ, halfZ })
                {
                    yield return new Axis(x, y, z);
                }
            }
        }
    }

    private static double Dot(Axis a, Axis b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private readonly record struct Axis(double X, double Y, double Z);

    private static StageFootprint Footprint(float x, float z, double height, bool present = true) =>
        new(new StagePoint(x, z), height, present);
}
