using Godot;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// Resolves the real safe-area insets from the display server and applies them to a screen's
/// margin container.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 One resolution shared by every screen that draws to the edge, rather than one per screen.
/// <c>AppRoot.tscn</c>'s margins are a static worst-case guess and its own remarks warn against
/// reading them as a measurement; a second screen that copied those numbers would look like it had
/// asked when it had not. This is the asking.
/// </para>
/// <para>
/// 🔒 The display server answers in physical screen pixels while a scene is laid out in canvas
/// units, so each inset is scaled by the ratio the stretch mode is already applying.
/// </para>
/// <para>
/// ⚠️ A platform with no cutouts reports the whole window as safe, and a headless or not-yet-sized
/// window reports nothing usable at all. The second case keeps the margins the scene was authored
/// with rather than collapsing them to zero — an unanswered query is not a measurement of no inset.
/// </para>
/// </remarks>
internal static class SafeAreaInsets
{
    private const string MarginLeftConstant = "margin_left";
    private const string MarginTopConstant = "margin_top";
    private const string MarginRightConstant = "margin_right";
    private const string MarginBottomConstant = "margin_bottom";

    /// <summary>
    /// The narrowest gap between the screen edge and anything drawn, in canvas units — roughly
    /// 16 dp across the supported density range. A resolved inset smaller than this is widened to
    /// it: a cutout-free edge is not a reason to put text against the glass.
    /// </summary>
    private const int DesignGutter = 48;

    /// <summary>
    /// The most of one axis a single resolved inset may take. A display server answering in a
    /// coordinate space this game did not anticipate has to degrade to a wide margin, never to a
    /// content rect with no room left inside it to draw.
    /// </summary>
    private const float MaxInsetShare = 0.25f;

    /// <summary>Writes the resolved insets onto a screen's outermost margin container.</summary>
    /// <param name="margins">The container everything drawn sits inside.</param>
    /// <param name="canvas">The viewport rect's size, in canvas units.</param>
    /// <exception cref="ArgumentNullException"><paramref name="margins"/> is null.</exception>
    internal static void ApplyTo(MarginContainer margins, Vector2 canvas)
    {
        ArgumentNullException.ThrowIfNull(margins);

        if (Resolve(canvas) is not { } insets)
        {
            return;
        }

        margins.AddThemeConstantOverride(MarginLeftConstant, (int)insets.Left);
        margins.AddThemeConstantOverride(MarginTopConstant, (int)insets.Top);
        margins.AddThemeConstantOverride(MarginRightConstant, (int)insets.Right);
        margins.AddThemeConstantOverride(MarginBottomConstant, (int)insets.Bottom);
    }

    /// <summary>
    /// The resolved insets in canvas units, or <c>null</c> when the display server answered nothing
    /// a measurement could be taken from.
    /// </summary>
    /// <remarks>
    /// 🔒 The same resolution <see cref="ApplyTo"/> applies, handed back as NUMBERS — because a
    /// screen laid out by <c>HomeLayout</c> needs the insets themselves rather than a container with
    /// them written on it: the inset decides which band grows, and that is arithmetic done away from
    /// the engine. One reading, two callers; a second query would be a second answer to drift from.
    /// </remarks>
    /// <param name="canvas">The viewport rect's size, in canvas units.</param>
    internal static Presenters.SafeAreaInsets? Resolve(Vector2 canvas)
    {
        var window = DisplayServer.WindowGetSize();
        var safeArea = DisplayServer.GetDisplaySafeArea();

        if (window.X <= 0 || window.Y <= 0 || safeArea.Size.X <= 0 || safeArea.Size.Y <= 0 ||
            canvas.X <= 0 || canvas.Y <= 0)
        {
            return null;
        }

        // The display server answers in SCREEN coordinates, and the window is only ever part of one
        // screen. Left unshifted, a window that does not sit at the desktop's origin resolves an
        // inset measured from somebody else's corner — on a second monitor, one wider than the whole
        // canvas.
        var origin = DisplayServer.WindowGetPosition();

        var horizontal = canvas.X / window.X;
        var vertical = canvas.Y / window.Y;

        return new Presenters.SafeAreaInsets(
            Inset((safeArea.Position.Y - origin.Y) * vertical, canvas.Y),
            Inset((window.X - (safeArea.End.X - origin.X)) * horizontal, canvas.X),
            Inset((window.Y - (safeArea.End.Y - origin.Y)) * vertical, canvas.Y),
            Inset((safeArea.Position.X - origin.X) * horizontal, canvas.X));
    }

    /// <summary>
    /// One resolved inset in canvas units: never narrower than the design gutter, and never wide
    /// enough that the pair of them could close over the content between them.
    /// </summary>
    private static int Inset(float canvasUnits, float axis)
    {
        var ceiling = Mathf.Max(DesignGutter, Mathf.RoundToInt(axis * MaxInsetShare));

        return Math.Clamp(Mathf.RoundToInt(canvasUnits), DesignGutter, ceiling);
    }
}
