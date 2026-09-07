namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>An axis-aligned rectangle in canvas units, measured from the canvas's top-left.</summary>
/// <remarks>
/// Not an engine rectangle, and it cannot be one: a presenter source file may not contain the
/// engine's name at all (pinned by <c>PresenterBoundaryRuleTests</c>). The scene converts at the one
/// place it reads a band. That is a small price for a layout that can be measured under a test
/// runner instead of by looking at a screenshot.
/// </remarks>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">How wide.</param>
/// <param name="Height">How tall.</param>
public readonly record struct HomeRect(float X, float Y, float Width, float Height)
{
    /// <summary>The right edge.</summary>
    public float Right => X + Width;

    /// <summary>The bottom edge.</summary>
    public float Bottom => Y + Height;
}

/// <summary>The four insets a handset's unusable edges take, in canvas units.</summary>
/// <param name="Top">The status bar / notch.</param>
/// <param name="Right">The right edge.</param>
/// <param name="Bottom">The home indicator / gesture bar.</param>
/// <param name="Left">The left edge.</param>
public readonly record struct SafeAreaInsets(float Top, float Right, float Bottom, float Left)
{
    /// <summary>A handset with no unusable edge at all.</summary>
    public static SafeAreaInsets None { get; }
}

/// <summary>The three band heights the screen is pinned by. The hero band is what is left.</summary>
/// <param name="TopBarHeight">The top bar's own height, before the top safe inset is added to it.</param>
/// <param name="LaunchHeight">The launch block's height.</param>
/// <param name="TabBarHeight">The tab row's height, before the bottom safe inset is added to it.</param>
public sealed record HomeBandMetrics(float TopBarHeight, float LaunchHeight, float TabBarHeight);

/// <summary>What the top bar packs beside the pills.</summary>
/// <param name="SidePadding">The padding at each end of the bar.</param>
/// <param name="ItemGap">The gap between two neighbouring items in the bar.</param>
/// <param name="AvatarWidth">The avatar's width.</param>
/// <param name="SettingsGlyphWidth">
/// The settings glyph as DRAWN. Its tap target is <see cref="HomeLayout.MinimumTouchTarget"/> or the
/// glyph, whichever is larger — which is the whole reason the two are separate numbers.
/// </param>
public sealed record HomeTopBarMetrics(
    float SidePadding, float ItemGap, float AvatarWidth, float SettingsGlyphWidth);

/// <summary>What one resource pill is built from.</summary>
/// <param name="HorizontalPadding">The padding at each end of the pill.</param>
/// <param name="IconWidth">The resource icon's width.</param>
/// <param name="InnerGap">The gap between the icon, the value and the caption.</param>
/// <param name="ValueCharacterAdvance">How wide one character of the value is.</param>
/// <param name="CaptionCharacterAdvance">How wide one character of the caption is.</param>
public sealed record HomePillMetrics(
    float HorizontalPadding,
    float IconWidth,
    float InnerGap,
    float ValueCharacterAdvance,
    float CaptionCharacterAdvance);

/// <summary>What the launch block stacks.</summary>
/// <param name="SidePadding">The padding at each end of the block.</param>
/// <param name="RowGap">The gap between two of its three rows.</param>
/// <param name="StageCardHeight">The stage card's height.</param>
/// <param name="StartButtonHeight">The primary button's height.</param>
/// <param name="RewardLineHeight">The reward line's height.</param>
/// <param name="ChangeButtonWidth">The Change control as DRAWN — see <see cref="HomeTopBarMetrics.SettingsGlyphWidth"/>.</param>
/// <param name="ChangeButtonHeight">Likewise, as drawn.</param>
public sealed record HomeLaunchMetrics(
    float SidePadding,
    float RowGap,
    float StageCardHeight,
    float StartButtonHeight,
    float RewardLineHeight,
    float ChangeButtonWidth,
    float ChangeButtonHeight);

/// <summary>Every measurement the Home screen is laid out from. Nothing here has a default.</summary>
/// <remarks>
/// ⚠️ <b>Not one of these numbers is decided in this file</b>, for the reason
/// <c>BoardLayoutMetrics</c> records at length: a layout constant baked into the renderer is a
/// design decision hidden where no reviewer looks for it. They arrive from the one <c>[Export]</c>
/// block on <c>Home.tscn</c>, and this type offers no default so nothing can fall back to a number
/// nobody chose.
/// </remarks>
/// <param name="Bands">The three pinned band heights.</param>
/// <param name="TopBar">What the top bar packs.</param>
/// <param name="Pill">What one pill is built from.</param>
/// <param name="Launch">What the launch block stacks.</param>
public sealed record HomeLayoutMetrics(
    HomeBandMetrics Bands,
    HomeTopBarMetrics TopBar,
    HomePillMetrics Pill,
    HomeLaunchMetrics Launch);

/// <summary>
/// Where every band and every tap target of the Home screen sits, for one canvas size and one set
/// of safe-area insets.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Pure C#, so the screen's measurable claims can be measured.</b> "The top bar and the tab bar
/// are pinned and only the viewport flexes", "no band overlaps its neighbour", "every tap target is
/// at least 48 dp" and "the three pills still fit at their longest values" are all arithmetic — and
/// arithmetic checked by looking at a screenshot is arithmetic nobody checks again. Following the
/// committed precedent of <see cref="BoardLayout"/>, <see cref="BattleStageLayout"/> and
/// <see cref="BattleFraming"/>.
/// </para>
/// <para>
/// 🔒 <b>The hero band is the residual, and that is the layout's one rule.</b> Three heights are
/// pinned and the fourth is whatever is left, so a taller handset gives its extra height to the
/// diorama and a shorter one takes it away — the alternative, a pinned viewport, pushes the primary
/// action off the bottom of a 9:20 screen.
/// </para>
/// </remarks>
/// <param name="TopBar">The top bar's whole band, top safe inset included — what it paints.</param>
/// <param name="TopBarContent">The part of the top bar that may hold anything: the band below the top safe inset.</param>
/// <param name="SettingsButton">The settings control's TAP target, never smaller than <see cref="HomeLayout.MinimumTouchTarget"/>.</param>
/// <param name="PillRowWidth">How much width the three pills have between the avatar and the settings target.</param>
/// <param name="Hero">The hero viewport: whatever height the other three bands leave.</param>
/// <param name="Launch">The launch block's whole band.</param>
/// <param name="StageCard">The stage card inside the launch block.</param>
/// <param name="ChangeButton">The Change control's TAP target, never smaller than <see cref="HomeLayout.MinimumTouchTarget"/>.</param>
/// <param name="StartButton">The one primary button.</param>
/// <param name="RewardLine">The reward line under the button.</param>
/// <param name="TabBar">The tab bar's whole band, bottom safe inset included — what it paints.</param>
/// <param name="TabRow">
/// The strip of the tab bar the tabs are actually tappable in: the band minus the bottom safe inset,
/// so a handset with a tall gesture bar grows the band and never shrinks the targets.
/// </param>
public sealed record HomeLayout(
    HomeRect TopBar,
    HomeRect TopBarContent,
    HomeRect SettingsButton,
    float PillRowWidth,
    HomeRect Hero,
    HomeRect Launch,
    HomeRect StageCard,
    HomeRect ChangeButton,
    HomeRect StartButton,
    HomeRect RewardLine,
    HomeRect TabBar,
    HomeRect TabRow)
{
    /// <summary>
    /// The smallest a tap target may be, in canvas units: 48 logical at the canvas's 3x scale.
    /// </summary>
    public const float MinimumTouchTarget = 144f;

    /// <summary>Lays the screen out for one canvas and one set of insets.</summary>
    /// <param name="canvasWidth">The canvas width, in canvas units.</param>
    /// <param name="canvasHeight">The canvas height, in canvas units.</param>
    /// <param name="insets">The handset's unusable edges.</param>
    /// <param name="metrics">Every measurement the layout is built from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="metrics"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The canvas is not big enough for the three pinned bands, so there is no hero band to give the
    /// remainder to. Refused rather than clamped: a negative residual drawn as zero is a screen with
    /// no diorama and no explanation.
    /// </exception>
    public static HomeLayout For(
        float canvasWidth, float canvasHeight, SafeAreaInsets insets, HomeLayoutMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        throw new NotImplementedException(
            "HomeLayout.For is a Phase 1 skeleton: the tests stating the band geometry, the " +
            "safe-area handling and the tap-target floor are written and red. Phase 3 implements it.");
    }

    /// <summary>How wide one pill is, at a given number of value and caption characters.</summary>
    /// <param name="valueCharacters">Characters in the value, as the screen will render it.</param>
    /// <param name="captionCharacters">
    /// Characters in the caption, or zero when the pill has none — which is what the Crowns and
    /// Power pills always are, and what the Energy pill is at full.
    /// </param>
    /// <param name="metrics">What a pill is built from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="metrics"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Either count is negative.</exception>
    public static float PillWidth(int valueCharacters, int captionCharacters, HomePillMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        throw new NotImplementedException(
            "HomeLayout.PillWidth is a Phase 1 skeleton: the test stating that the three pills fit " +
            "at their longest values is written and red. Phase 3 implements it.");
    }
}
