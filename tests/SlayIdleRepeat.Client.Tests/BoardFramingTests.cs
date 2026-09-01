using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// <see cref="BoardFraming"/> — the arithmetic the board camera frames and follows by.
/// </summary>
/// <remarks>
/// Every case here catches a defect that produces a picture rather than an error. A camera framed
/// off the wrong axis, a follow that lags differently per device, a focus behind the roll button:
/// each one looks deliberate on the screen it was authored against, so none of them is found by
/// looking.
/// </remarks>
public sealed class BoardFramingTests
{
    /// <summary>The client's design viewport: 1080 by 1920, portrait, at the fov Board.tscn carries.</summary>
    private const double PortraitAspect = 1080d / 1920d;
    private const double Fov = 60d;

    /// <summary>
    /// 🔴 A board wider than it is tall is framed off the HORIZONTAL half-angle, which in portrait
    /// is the narrow one.
    /// </summary>
    /// <remarks>
    /// The defect: treating <c>Camera3D.fov</c> as if it governed both axes. It does not — with the
    /// height kept fixed it spreads over the frame's height alone, and at 1080×1920 the width gets
    /// only about 36 degrees against 60. A distance solved from the vertical angle sits far too
    /// close and cuts both ends off a wide board. That failure lands precisely on the overview,
    /// whose entire job is showing the player what the follow camera cannot — so the compliance
    /// mechanism for `16` D67 would fail at the moment it is invoked, and look like a framing choice.
    /// </remarks>
    [Fact]
    public void A_board_wider_than_it_is_tall_is_framed_off_the_horizontal_half_angle()
    {
        const double halfWidth = 20d;
        const double halfHeight = 1d;

        var fitted = BoardFraming.DistanceThatFits(halfWidth, halfHeight, Fov, PortraitAspect, margin: 1d);

        var verticalOnly = halfHeight / Math.Tan(double.DegreesToRadians(Fov) / 2d);

        fitted.ShouldBeGreaterThan(
            verticalOnly * 10d,
            "solving only the vertical angle would put the camera an order of magnitude too close, " +
            "and both ends of the board would be off screen.");

        // The width really does fit at that distance: half the visible width at `fitted` is at
        // least the half-width asked for.
        var visibleHalfWidth = fitted * PortraitAspect * Math.Tan(double.DegreesToRadians(Fov) / 2d);

        visibleHalfWidth.ShouldBeGreaterThanOrEqualTo(halfWidth - 0.0001d);
    }

    /// <summary>
    /// The negative control: a board taller than it is wide is framed off the vertical angle
    /// instead, so the case above is not passing against a function that always takes the width.
    /// </summary>
    [Fact]
    public void A_board_taller_than_it_is_wide_is_framed_off_the_vertical_half_angle()
    {
        const double halfWidth = 1d;
        const double halfHeight = 20d;

        var fitted = BoardFraming.DistanceThatFits(halfWidth, halfHeight, Fov, PortraitAspect, margin: 1d);
        var verticalOnly = halfHeight / Math.Tan(double.DegreesToRadians(Fov) / 2d);

        fitted.ShouldBe(verticalOnly, tolerance: 0.0001d);
    }

    /// <summary>A larger margin sits the camera further back, never closer.</summary>
    /// <remarks>
    /// The defect: a margin divided in rather than multiplied. It reads the same way in the call and
    /// crops harder the more room it is asked to leave.
    /// </remarks>
    [Fact]
    public void A_larger_margin_sits_the_camera_further_back()
    {
        var tight = BoardFraming.DistanceThatFits(10d, 10d, Fov, PortraitAspect, margin: 1d);
        var roomy = BoardFraming.DistanceThatFits(10d, 10d, Fov, PortraitAspect, margin: 1.25d);

        roomy.ShouldBeGreaterThan(tight);
        roomy.ShouldBe(tight * 1.25d, tolerance: 0.0001d);
    }

    /// <summary>A bigger board is framed from further away.</summary>
    [Fact]
    public void A_bigger_board_is_framed_from_further_away()
    {
        BoardFraming.DistanceThatFits(40d, 40d, Fov, PortraitAspect, margin: 1d)
            .ShouldBeGreaterThan(BoardFraming.DistanceThatFits(10d, 10d, Fov, PortraitAspect, margin: 1d));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(180d)]
    [InlineData(double.NaN)]
    public void A_field_of_view_outside_its_range_is_refused(double fov) =>
        Should.Throw<ArgumentOutOfRangeException>(
            () => BoardFraming.DistanceThatFits(1d, 1d, fov, PortraitAspect, margin: 1d));

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    public void A_non_positive_aspect_or_margin_is_refused(double bad)
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => BoardFraming.DistanceThatFits(1d, 1d, Fov, bad, margin: 1d));

        Should.Throw<ArgumentOutOfRangeException>(
            () => BoardFraming.DistanceThatFits(1d, 1d, Fov, PortraitAspect, bad));
    }

    /// <summary>
    /// 🔒 One long frame closes exactly as much of a gap as several short ones summing to the same
    /// time.
    /// </summary>
    /// <remarks>
    /// The defect: <c>Lerp(current, target, delta * stiffness)</c>, which is the shape everyone
    /// reaches for. It follows visibly looser at thirty frames than at sixty — so the camera behaves
    /// differently on a handset than on the desktop it was tuned on — and at a low enough frame rate
    /// <c>delta * stiffness</c> passes 1 and the camera overshoots the thing it is following.
    /// </remarks>
    [Fact]
    public void One_long_frame_closes_the_same_fraction_as_several_short_ones()
    {
        const double halfLife = 0.2d;

        var oneFrame = BoardFraming.Damp(halfLife, 0.6d);

        var manyFrames = 1d;

        foreach (var _ in Enumerable.Range(0, 12))
        {
            manyFrames *= BoardFraming.Damp(halfLife, 0.05d);
        }

        oneFrame.ShouldBe(manyFrames, tolerance: 1e-9d);
    }

    /// <summary>
    /// Exactly one half-life leaves exactly half the gap — a half-life read as a time constant
    /// would leave about 0.37 of it, a follow nearly twice as tight as the number says.
    /// </summary>
    [Fact]
    public void One_half_life_leaves_exactly_half_the_gap()
    {
        BoardFraming.Damp(0.25d, 0.25d).ShouldBe(0.5d, tolerance: 1e-12d);
        BoardFraming.Damp(0.25d, 0.5d).ShouldBe(0.25d, tolerance: 1e-12d);
    }

    /// <summary>A frame that took no time closes nothing.</summary>
    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    public void A_frame_that_took_no_time_closes_nothing(double delta) =>
        BoardFraming.Damp(0.2d, delta).ShouldBe(1d);

    [Fact]
    public void A_non_positive_half_life_is_refused() =>
        Should.Throw<ArgumentOutOfRangeException>(() => BoardFraming.Damp(0d, 0.1d));

    /// <summary>
    /// The negative control for the shift: a free band centred on the screen needs no shift at all.
    /// Without it, the case below would pass against a function that always returned something.
    /// </summary>
    [Fact]
    public void A_free_band_centred_on_the_screen_needs_no_shift() =>
        BoardFraming.VerticalShiftFor(0.3d, 0.7d, distance: 10d, Fov).ShouldBe(0d, tolerance: 1e-9d);

    /// <summary>
    /// A free band sitting above centre lifts the framed point — which is what keeps the hero out
    /// from behind the roll button.
    /// </summary>
    /// <remarks>
    /// The defect is a sign error, and it is invisible in code: it puts the hero exactly as far
    /// wrong in the other direction, behind the 340-unit roll button `13` §3 requires to be the
    /// largest thing on the screen and in the thumb zone.
    /// </remarks>
    [Fact]
    public void A_free_band_above_centre_lifts_the_framed_point()
    {
        // `13` §3's board: the HP, gold and stage rows across the top, the action column across the
        // bottom. The band the hero can be seen in sits above the middle of the screen.
        var shift = BoardFraming.VerticalShiftFor(0.20d, 0.60d, distance: 10d, Fov);

        shift.ShouldBeGreaterThan(0d, "the free band is above centre, so the framed point moves up.");
    }

    /// <summary>The other direction, so the case above cannot be passing on a constant.</summary>
    [Fact]
    public void A_free_band_below_centre_lowers_the_framed_point() =>
        BoardFraming.VerticalShiftFor(0.40d, 0.80d, distance: 10d, Fov).ShouldBeLessThan(0d);

    /// <summary>
    /// The shift scales with how much of the world the camera can see, so the same band holds the
    /// hero in the same place whether the camera is close in or pulled back.
    /// </summary>
    [Fact]
    public void The_shift_scales_with_how_much_the_camera_can_see()
    {
        var near = BoardFraming.VerticalShiftFor(0.20d, 0.60d, distance: 10d, Fov);
        var far = BoardFraming.VerticalShiftFor(0.20d, 0.60d, distance: 30d, Fov);

        far.ShouldBe(near * 3d, tolerance: 1e-9d);
    }

    [Fact]
    public void An_inverted_free_band_is_refused() =>
        Should.Throw<ArgumentOutOfRangeException>(
            () => BoardFraming.VerticalShiftFor(0.7d, 0.3d, distance: 10d, Fov));
}
