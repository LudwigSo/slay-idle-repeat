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

    // The shipped numbers: the band the interface leaves free runs from 14% to 70% of the screen's
    // height, so its centre sits at 42% from the top — normalised, with up positive, +0.16.
    private static readonly BattleCameraMetrics Shipped = new(
        YawDegrees: -28, PitchDegrees: 16, Margin: 1.15, UsableBandTop: 0.14, UsableBandBottom: 0.7, HalfLife: 0.35);

    // The shipped one-on-one against the elite: the hero at x −2.4, z −0.3 with a body 0.55 either
    // side of its rest point, the elite at x +2.4, z +0.3 with a body 0.9 either side, the elite's
    // 3.41 plus the 0.35 plate clearance on top.
    private static readonly StageBounds EliteFight = new(
        MinX: -2.4 - 0.55, MinY: 0, MinZ: -0.3 - 0.55, MaxX: 2.4 + 0.9, MaxY: 3.41 + 0.35, MaxZ: 0.3 + 0.9);

    [Fact]
    public void Frame_straight_on_and_level_centres_a_symmetric_stage_with_no_shift_when_the_band_is_centred()
    {
        const double halfX = 4d;
        const double height = 2d;
        const double halfZ = 1d;
        var level = Camera with { YawDegrees = 0, PitchDegrees = 0, UsableBandTop = 0.25, UsableBandBottom = 0.75 };

        var frame = BattleFraming.Frame(Box(halfX, height, halfZ), level, Fov, PortraitAspect);

        // Straight on, the camera at +Z looks along −Z: the four corners on the box's +Z face are
        // halfZ nearer than the centre and share its projection, so they push the camera back by
        // halfZ. The picture is symmetric about the centre and the band is centred on the screen,
        // so nothing moves the look-at point.
        frame.Distance.ShouldBe(
            BoardFraming.DistanceThatFits(halfX, height / 2, Fov, PortraitAspect, level.Margin) + halfZ,
            1e-9);
        frame.HorizontalShift.ShouldBe(0d, 1e-9);
        frame.VerticalShift.ShouldBe(0d, 1e-9);
    }

    [Fact]
    public void Frame_lowers_the_look_at_point_to_lift_the_picture_into_a_band_above_centre()
    {
        const double halfX = 4d;
        const double height = 2d;
        var level = Camera with { YawDegrees = 0, PitchDegrees = 0, UsableBandTop = 0.14, UsableBandBottom = 0.7 };
        var tanVertical = Math.Tan(double.DegreesToRadians(Fov) / 2);
        var tanHorizontal = PortraitAspect * tanVertical;

        // The band's centre at 42% from the top is +0.16 up the normalised screen. A flat stage, so
        // every corner shares the look-at point's reach and the lift has a closed form.
        const double bandCentre = 1 - (0.14 + 0.7);
        var distance = BoardFraming.DistanceThatFits(halfX, height / 2, Fov, PortraitAspect, level.Margin);

        // Preconditions of the closed form: the width governs the distance, both before the shift
        // and after it has moved the top corner further from the look-at point.
        distance.ShouldBe(level.Margin * halfX / tanHorizontal, 1e-9, "the wide stage is fitted by its width in portrait.");
        (level.Margin * ((height / 2) + (bandCentre * distance * tanVertical)) / tanVertical)
            .ShouldBeLessThan(distance, "the shifted height still asks for less than the width does.");

        var frame = BattleFraming.Frame(Box(halfX, height, halfZ: 0), level, Fov, PortraitAspect);

        // Moving the look-at point by s along the camera's up moves every projection by
        // −s / (distance · tan(fov/2)); landing the picture's middle at +0.16 therefore takes
        // s = −0.16 · distance · tan(fov/2): the look-at point goes DOWN so the picture goes UP.
        frame.VerticalShift.ShouldBeLessThan(0d, "the free band sits above centre, so the look-at point drops.");
        frame.VerticalShift.ShouldBe(-bandCentre * distance * tanVertical, 1e-9);
        frame.HorizontalShift.ShouldBe(0d, 1e-9);
        frame.Distance.ShouldBe(distance, 1e-9);
    }

    [Fact]
    public void Frame_centres_the_projected_corners_of_a_yawed_stage_in_the_band()
    {
        const double bandCentre = 1 - (0.14 + 0.7);
        var withinMargin = 1 / Shipped.Margin;

        var frame = BattleFraming.Frame(EliteFight, Shipped, Fov, PortraitAspect);

        var projected = Project(EliteFight, frame, Shipped);
        var maxX = projected.Max(point => point.X);
        var minX = projected.Min(point => point.X);
        var maxY = projected.Max(point => point.Y);
        var minY = projected.Min(point => point.Y);

        // Centred: as much picture right of the screen's middle as left of it, and the vertical
        // middle on the band's centre rather than the screen's.
        maxX.ShouldBe(-minX, 1e-6, "the picture must be centred across the screen.");
        ((maxY + minY) / 2).ShouldBe(bandCentre, 1e-6, "the picture's middle must sit at the band's centre.");

        // Padded: the margin still holds about the look-at point after the centring.
        foreach (var (x, y) in projected)
        {
            Math.Abs(x).ShouldBeLessThanOrEqualTo(withinMargin + 1e-9, "every corner keeps the margin across the screen.");
            y.ShouldBeInRange(bandCentre - withinMargin - 1e-9, bandCentre + withinMargin + 1e-9);
        }

        // The camera stands on the hero's side, so the near hero corners spread furthest across the
        // screen and lowest down it: the look-at point moves toward the hero and down.
        frame.HorizontalShift.ShouldBeLessThan(0d, "the look-at point moves toward the near, hero side.");
        frame.VerticalShift.ShouldBeLessThan(0d, "the look-at point drops so the picture lifts into the band.");
    }

    [Fact]
    public void Frame_sits_the_camera_nearer_once_the_stage_is_centred_than_the_uncentred_fit_did()
    {
        var frame = BattleFraming.Frame(EliteFight, Shipped, Fov, PortraitAspect);

        // The fit about the box's own centre: each corner fitted for its own projection, less its
        // depth beyond the centre, the furthest-back answer winning.
        var (right, up, forward) = Axes(Shipped);
        var uncentred = double.NegativeInfinity;

        foreach (var corner in Corners(EliteFight))
        {
            var fromCentre = BoardFraming.DistanceThatFits(
                Math.Abs(Dot(corner, right)), Math.Abs(Dot(corner, up)), Fov, PortraitAspect, Shipped.Margin);

            uncentred = Math.Max(uncentred, fromCentre - Dot(corner, forward));
        }

        // Centring never asks for more room than the box's centre did, and for this box it asks for
        // strictly less: the near hero corner governed the uncentred fit while the far side wasted
        // room, and moving the look-at point toward the hero trades that corner's demand for the
        // far corner's cheaper one.
        frame.Distance.ShouldBeLessThanOrEqualTo(uncentred);
        frame.Distance.ShouldBeLessThan(uncentred, "the extents were asymmetric about the centre.");
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
        var deep = Box(halfX: 1, height: 1, halfZ: 4);

        var straightOn = BattleFraming.Frame(deep, Camera with { YawDegrees = 0 }, Fov, PortraitAspect);
        var yawed = BattleFraming.Frame(deep, Camera, Fov, PortraitAspect);

        // Straight on, the depth only brings the near face closer; yawed, it also spreads the
        // corners across the frame, and the camera sits well further back for it.
        straightOn.Distance.ShouldBeGreaterThan(0d);
        yawed.Distance.ShouldBeGreaterThan(straightOn.Distance);
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
    public void BoundsOf_pads_each_footprint_by_its_body_half_width()
    {
        var bounds = BattleFraming.BoundsOf(
            [Footprint(-3, 0, height: 2, halfWidth: 0.5), Footprint(3, 1, height: 2, halfWidth: 1.25)],
            plateClearance: 0.5);

        bounds.ShouldBe(
            new StageBounds(MinX: -3.5, MinY: 0, MinZ: -0.5, MaxX: 4.25, MaxY: 2.5, MaxZ: 2.25),
            "a rest point is the middle of a body, not its edge: each side of the box reaches out by " +
            "the half width of the actor standing at it, or that actor's body crosses the frame's edge.");
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

    /// <summary>The eight corners of a box, as offsets from its centre.</summary>
    private static IEnumerable<Axis> Corners(StageBounds bounds)
    {
        var halfX = (bounds.MaxX - bounds.MinX) / 2;
        var halfY = (bounds.MaxY - bounds.MinY) / 2;
        var halfZ = (bounds.MaxZ - bounds.MinZ) / 2;

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

    /// <summary>
    /// Every corner of the box on the normalised screen, up positive, under the frame's contract:
    /// the look-at point is the box's centre moved by the shifts along the camera's right and up,
    /// and the camera sits the distance behind it along its forward.
    /// </summary>
    private static List<(double X, double Y)> Project(StageBounds bounds, StageFrame frame, BattleCameraMetrics camera)
    {
        var (right, up, forward) = Axes(camera);
        var tanVertical = Math.Tan(double.DegreesToRadians(Fov) / 2);
        var tanHorizontal = PortraitAspect * tanVertical;
        var points = new List<(double X, double Y)>();

        foreach (var corner in Corners(bounds))
        {
            var reach = frame.Distance + Dot(corner, forward);

            points.Add((
                (Dot(corner, right) - frame.HorizontalShift) / (reach * tanHorizontal),
                (Dot(corner, up) - frame.VerticalShift) / (reach * tanVertical)));
        }

        return points;
    }

    private static double Dot(Axis a, Axis b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private readonly record struct Axis(double X, double Y, double Z);

    private static StageFootprint Footprint(
        float x, float z, double height, bool present = true, double halfWidth = 0) =>
        new(new StagePoint(x, z), height, present, halfWidth);
}
