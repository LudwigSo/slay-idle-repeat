using Godot;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// Gives one button its text colour in every draw state it has, rather than in the one state a
/// single override reaches.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>A button does not fall back to <c>font_color</c>.</b> The engine picks the font colour by
/// draw mode, and each mode has its own default: measured off a real button in a headless run, the
/// default <c>font_pressed_color</c> is opaque white and the default <c>font_disabled_color</c> is a
/// HALF-TRANSPARENT grey. So a control given only <c>font_color</c> — or, as both primary actions
/// on these screens were, given none at all — draws at the engine's colour rather than at the one
/// the screen decided for it, in exactly the states it spends most of its life in. A disabled
/// primary action is the ordinary case here, not the edge one.
/// </para>
/// <para>
/// 🔒 <b>Two colours, because "cannot be used" is the one state that means something different.</b>
/// The other four all say the control is live and only differ by what the finger is doing, so they
/// take one colour between them. Unavailable takes the palette's quiet secondary — the same grey the
/// captions and the status lines are drawn in — and deliberately NOT the amber a refused chapter
/// takes: on these screens amber means the ladder said no, and an action that is merely waiting for
/// a read has not been refused anything.
/// </para>
/// <para>
/// ⚠️ Every colour written through here is a per-node override, and the whole file is debt owed to
/// M8-03's UI kit rather than a naming scheme of its own. It exists so the debt is paid in ONE
/// place: three of the four buttons across these two screens are drawn from code, and a second copy
/// of this list of state names is how one of them silently keeps an engine default.
/// </para>
/// <para>
/// ⚠️ This is not a theme and must not grow into one. It writes overrides onto a control it is
/// handed; it holds no styleboxes, no fonts, no variations and no palette of its own, and it decides
/// nothing about which colour a caller should pass.
/// </para>
/// </remarks>
internal static class ButtonTextColours
{
    private const string FontColourOverride = "font_color";
    private const string PressedFontColourOverride = "font_pressed_color";
    private const string HoverFontColourOverride = "font_hover_color";
    private const string HoverPressedFontColourOverride = "font_hover_pressed_color";
    private const string DisabledFontColourOverride = "font_disabled_color";

    /// <summary>Draws one button's text in the same colour whatever state it is in.</summary>
    /// <remarks>
    /// For a control whose colour already carries its own message — a chapter the ladder refuses
    /// stays amber whether it is disabled or not, and a chapter whose gating is not yet known keeps
    /// the live colour for the same reason. Being unusable is not the news in either case.
    /// </remarks>
    /// <param name="button">The control to draw.</param>
    /// <param name="colour">What its text says about itself.</param>
    /// <exception cref="ArgumentNullException"><paramref name="button"/> is null.</exception>
    internal static void ApplyTo(Button button, Color colour) => ApplyTo(button, colour, colour);

    /// <summary>Draws one button's text, with a second colour for the state it cannot be used in.</summary>
    /// <param name="button">The control to draw.</param>
    /// <param name="usable">What its text is drawn in while there is something it can do.</param>
    /// <param name="unusable">And while there is not.</param>
    /// <exception cref="ArgumentNullException"><paramref name="button"/> is null.</exception>
    internal static void ApplyTo(Button button, Color usable, Color unusable)
    {
        ArgumentNullException.ThrowIfNull(button);

        button.AddThemeColorOverride(FontColourOverride, usable);
        button.AddThemeColorOverride(PressedFontColourOverride, usable);
        button.AddThemeColorOverride(HoverFontColourOverride, usable);
        button.AddThemeColorOverride(HoverPressedFontColourOverride, usable);
        button.AddThemeColorOverride(DisabledFontColourOverride, unusable);
    }
}
