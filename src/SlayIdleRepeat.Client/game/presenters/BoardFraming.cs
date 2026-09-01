namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>
/// The arithmetic a board camera frames and follows by: how far back to sit to fit something, how
/// much of a gap to close in a frame, and how far to shift a focus so the interface does not cover it.
/// </summary>
/// <remarks>
/// Engine-free so the three things here that can be silently wrong are testable: the portrait field
/// of view (see <see cref="DistanceThatFits"/>), frame-rate dependence (see <see cref="Damp"/>), and
/// the sign of the shift that keeps the hero out from behind the roll button. None of the three
/// announces itself — each produces a picture that looks deliberate and is wrong.
/// </remarks>
public static class BoardFraming
{
    /// <summary>
    /// How far back a camera must sit for a box of this half-size to fit inside its frustum.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The vertical field of view does not govern both axes, and in portrait it is the
    /// generous one.</b> A perspective camera keeping its height fixed spreads
    /// <paramref name="verticalFovDegrees"/> over the frame's height and only
    /// <c>2·atan(aspect·tan(fov/2))</c> over its width — at 1080×1920 that is about 36 degrees
    /// against 60. A distance computed from the vertical angle alone therefore fits a WIDE board
    /// nowhere near the frame, and it fails at exactly the moment the framing is asked for: the
    /// overview whose whole job is showing what the follow camera cannot. So both axes are solved
    /// and the further of the two wins.
    /// </remarks>
    /// <param name="halfWidth">Half the box's extent across the screen.</param>
    /// <param name="halfHeight">Half its extent up the screen.</param>
    /// <param name="verticalFovDegrees">The camera's field of view, which is the VERTICAL one. Must be in (0, 180).</param>
    /// <param name="aspect">Viewport width divided by height. Must be positive.</param>
    /// <param name="margin">A multiplier on the result, so the box does not touch the frame edge. Must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">The field of view, the aspect or the margin is outside its range.</exception>
    public static double DistanceThatFits(
        double halfWidth, double halfHeight, double verticalFovDegrees, double aspect, double margin)
    {
        if (!double.IsFinite(verticalFovDegrees) || verticalFovDegrees is <= 0d or >= 180d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(verticalFovDegrees), verticalFovDegrees, "a field of view lies strictly between 0 and 180 degrees.");
        }

        if (!double.IsFinite(aspect) || aspect <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(aspect), aspect, "an aspect ratio is positive.");
        }

        if (!double.IsFinite(margin) || margin <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(margin), margin, "a framing margin is positive.");
        }

        var verticalHalfAngle = double.DegreesToRadians(verticalFovDegrees) / 2d;
        var horizontalHalfAngle = Math.Atan(aspect * Math.Tan(verticalHalfAngle));

        var forHeight = Math.Abs(halfHeight) / Math.Tan(verticalHalfAngle);
        var forWidth = Math.Abs(halfWidth) / Math.Tan(horizontalHalfAngle);

        return Math.Max(forHeight, forWidth) * margin;
    }

    /// <summary>
    /// The fraction of a gap still remaining after <paramref name="deltaSeconds"/>, for a smoother
    /// that closes half the distance every <paramref name="halfLifeSeconds"/>.
    /// </summary>
    /// <remarks>
    /// 🔒 Used as <c>current = target + (current - target) * Damp(halfLife, delta)</c>. The obvious
    /// alternative — <c>Lerp(current, target, delta * stiffness)</c> — is frame-rate DEPENDENT: it
    /// follows visibly looser on a thirty-frame handset than on the sixty-frame desktop it was
    /// authored against, and at a low enough frame rate <c>delta * stiffness</c> passes 1 and the
    /// camera overshoots its target. This form has neither failure and costs one <c>Pow</c>.
    /// </remarks>
    /// <param name="halfLifeSeconds">How long the smoother takes to close half the gap. Must be positive.</param>
    /// <param name="deltaSeconds">Seconds elapsed. A negative or non-finite value closes nothing.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="halfLifeSeconds"/> is not positive.</exception>
    public static double Damp(double halfLifeSeconds, double deltaSeconds)
    {
        if (!double.IsFinite(halfLifeSeconds) || halfLifeSeconds <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(halfLifeSeconds), halfLifeSeconds,
                "a half-life is positive — at zero the camera would snap, which is what Snap is for.");
        }

        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0d)
        {
            return 1d;
        }

        return Math.Pow(0.5d, deltaSeconds / halfLifeSeconds);
    }

    /// <summary>
    /// How far up the camera's own up axis a framed point must move so it lands in the middle of the
    /// band the interface leaves free, rather than in the middle of the screen.
    /// </summary>
    /// <remarks>
    /// The board's overlay is not a thin frame: `13` §3 spends the top of the screen on the HP,
    /// gold and stage rows and the bottom on the roll button — the largest interactive element on
    /// the screen by design, and deliberately in the thumb zone. Framing the hero at the centre puts
    /// it behind that button. Positive means the framed point must move UP the screen, which happens
    /// when the free band sits above centre.
    /// </remarks>
    /// <param name="usableTopFraction">Where the free band starts, as a fraction of screen height from the top.</param>
    /// <param name="usableBottomFraction">Where it ends, same units. Must not be above the top.</param>
    /// <param name="distance">How far the camera is from the framed point.</param>
    /// <param name="verticalFovDegrees">The camera's vertical field of view. Must be in (0, 180).</param>
    /// <exception cref="ArgumentOutOfRangeException">The band is inverted, or the field of view is outside its range.</exception>
    public static double VerticalShiftFor(
        double usableTopFraction, double usableBottomFraction, double distance, double verticalFovDegrees)
    {
        if (!double.IsFinite(verticalFovDegrees) || verticalFovDegrees is <= 0d or >= 180d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(verticalFovDegrees), verticalFovDegrees, "a field of view lies strictly between 0 and 180 degrees.");
        }

        if (usableBottomFraction < usableTopFraction)
        {
            throw new ArgumentOutOfRangeException(
                nameof(usableBottomFraction), usableBottomFraction,
                "the free band's bottom is measured downward from the top of the screen, so it cannot " +
                "sit above the band's top.");
        }

        var visibleHeight = 2d * distance * Math.Tan(double.DegreesToRadians(verticalFovDegrees) / 2d);
        var bandCentreFromTop = (usableTopFraction + usableBottomFraction) / 2d;

        // 0.5 is the centre of the screen. A band centred above it needs the framed point lifted.
        return (0.5d - bandCentreFromTop) * visibleHeight;
    }
}
