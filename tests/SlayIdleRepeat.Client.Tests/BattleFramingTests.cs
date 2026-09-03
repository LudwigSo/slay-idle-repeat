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
    public void Frame_fits_the_eight_corners_projected_onto_the_yawed_cameras_right_and_up()
    {
        const double halfX = 4d;
        const double height = 2d;
        const double halfZ = 1d;

        var frame = BattleFraming.Frame(Box(halfX, height, halfZ), Camera, Fov, PortraitAspect);

        var yaw = double.DegreesToRadians(Math.Abs(Camera.YawDegrees));
        var pitch = double.DegreesToRadians(Camera.PitchDegrees);

        // right = (cos yaw, 0, −sin yaw); up = (−sin pitch·sin yaw, cos pitch, −sin pitch·cos yaw).
        // The half-extent of a box's corners along an axis is the sum of |axis| times each half-size.
        var halfWidth = (halfX * Math.Cos(yaw)) + (halfZ * Math.Sin(yaw));
        var halfHeight = (halfX * Math.Sin(pitch) * Math.Sin(yaw))
                         + (height / 2 * Math.Cos(pitch))
                         + (halfZ * Math.Sin(pitch) * Math.Cos(yaw));

        frame.Distance.ShouldBe(
            BoardFraming.DistanceThatFits(halfWidth, halfHeight, Fov, PortraitAspect, Camera.Margin),
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
    public void Frame_lets_a_stages_depth_widen_the_picture_only_through_the_yaw()
    {
        var deep = Box(halfX: 1, height: 1, halfZ: 4);

        var straightOn = BattleFraming.Frame(deep, Camera with { YawDegrees = 0 }, Fov, PortraitAspect);
        var yawed = BattleFraming.Frame(deep, Camera, Fov, PortraitAspect);

        var yaw = double.DegreesToRadians(Math.Abs(Camera.YawDegrees));

        // Width governs both in portrait: the projected half-width is 1 straight on and
        // cos yaw + 4·sin yaw (≈ 2.76 at 28°) yawed, so the yawed camera sits that much further back.
        straightOn.Distance.ShouldBeGreaterThan(0d);
        yawed.Distance.ShouldBe(straightOn.Distance * (Math.Cos(yaw) + (4 * Math.Sin(yaw))), 1e-6);
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

    private static StageFootprint Footprint(float x, float z, double height, bool present = true) =>
        new(new StagePoint(x, z), height, present);
}
