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

    /// <summary>The insets, with any negative edge read as no inset at all.</summary>
    /// <remarks>
    /// A display server that answers a wider window than screen reports a negative edge, and a
    /// negative inset would <em>shrink</em> the band it lands in rather than grow it — which is the
    /// one thing an inset may never do.
    /// </remarks>
    internal SafeAreaInsets Nonnegative() => new(
        Math.Max(0f, Top), Math.Max(0f, Right), Math.Max(0f, Bottom), Math.Max(0f, Left));
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

    /// <summary>
    /// The gaps the top bar spends: avatar to pill, pill to pill, pill to pill, pill to settings.
    /// </summary>
    private const float TopBarGaps = 4f;

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

        var safe = insets.Nonnegative();
        var bands = metrics.Bands;

        // The row the five tabs are tappable in, never smaller than a thumb. The BAND grows by the
        // gesture bar underneath it; the row above it keeps its own height, which is the whole
        // difference between a tab bar that survives a tall home indicator and one that does not.
        var tabRowHeight = Math.Max(bands.TabBarHeight, MinimumTouchTarget);

        var topBarHeight = bands.TopBarHeight + safe.Top;
        var tabBarHeight = tabRowHeight + safe.Bottom;
        var heroHeight = canvasHeight - topBarHeight - bands.LaunchHeight - tabBarHeight;

        if (heroHeight <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(canvasHeight),
                canvasHeight,
                "the three pinned bands and the safe area take the whole canvas, so there is no " +
                "height left for the hero band. Refused rather than clamped to zero: a screen with " +
                "no diorama and no explanation is not a state this layout may answer.");
        }

        var topBar = new HomeRect(0f, 0f, canvasWidth, topBarHeight);
        var topBarContent = new HomeRect(0f, safe.Top, canvasWidth, bands.TopBarHeight);
        var hero = new HomeRect(0f, topBar.Bottom, canvasWidth, heroHeight);
        var launch = new HomeRect(0f, hero.Bottom, canvasWidth, bands.LaunchHeight);
        var tabBar = new HomeRect(0f, launch.Bottom, canvasWidth, tabBarHeight);

        return new HomeLayout(
            topBar,
            topBarContent,
            MeasureSettings(topBarContent, metrics.TopBar),
            MeasurePillRow(canvasWidth, metrics.TopBar),
            hero,
            launch,
            MeasureStageCard(launch, metrics.Launch),
            MeasureChange(launch, metrics.Launch),
            MeasureStart(launch, metrics.Launch),
            MeasureReward(launch, metrics.Launch),
            tabBar,
            new HomeRect(0f, tabBar.Y, canvasWidth, tabRowHeight));
    }

    /// <summary>The settings control's tap target, at the right end of the bar's content.</summary>
    private static HomeRect MeasureSettings(HomeRect content, HomeTopBarMetrics topBar)
    {
        var side = Target(topBar.SettingsGlyphWidth);

        return new HomeRect(
            content.Right - topBar.SidePadding - side,
            content.Y + ((content.Height - side) / 2f),
            side,
            side);
    }

    /// <summary>
    /// What the three pills have between the avatar and the settings target: the canvas less the
    /// bar's own chrome.
    /// </summary>
    /// <remarks>
    /// 🔒 The settings TARGET rather than its glyph. Reserving the drawn glyph leaves the pills
    /// overlapping the part of the target a thumb that missed will land on.
    /// </remarks>
    private static float MeasurePillRow(float canvasWidth, HomeTopBarMetrics topBar) =>
        canvasWidth
        - (2f * topBar.SidePadding)
        - topBar.AvatarWidth
        - Target(topBar.SettingsGlyphWidth)
        - (TopBarGaps * topBar.ItemGap);

    private static HomeRect MeasureStageCard(HomeRect launch, HomeLaunchMetrics metrics) =>
        new(
            launch.X + metrics.SidePadding,
            launch.Y,
            launch.Width - (2f * metrics.SidePadding),
            metrics.StageCardHeight);

    private static HomeRect MeasureChange(HomeRect launch, HomeLaunchMetrics metrics)
    {
        var card = MeasureStageCard(launch, metrics);
        var width = Target(metrics.ChangeButtonWidth);
        var height = Target(metrics.ChangeButtonHeight);

        return new HomeRect(
            card.Right - width,
            card.Y + ((card.Height - height) / 2f),
            width,
            height);
    }

    private static HomeRect MeasureStart(HomeRect launch, HomeLaunchMetrics metrics)
    {
        var card = MeasureStageCard(launch, metrics);

        return new HomeRect(
            card.X, card.Bottom + metrics.RowGap, card.Width, metrics.StartButtonHeight);
    }

    private static HomeRect MeasureReward(HomeRect launch, HomeLaunchMetrics metrics)
    {
        var button = MeasureStart(launch, metrics);

        return new HomeRect(
            button.X, button.Bottom + metrics.RowGap, button.Width, metrics.RewardLineHeight);
    }

    /// <summary>One drawn extent as the target it is tapped through.</summary>
    private static float Target(float drawn) => Math.Max(drawn, MinimumTouchTarget);

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
        ArgumentOutOfRangeException.ThrowIfNegative(valueCharacters);
        ArgumentOutOfRangeException.ThrowIfNegative(captionCharacters);

        var width =
            (2f * metrics.HorizontalPadding)
            + metrics.IconWidth
            + metrics.InnerGap
            + (valueCharacters * metrics.ValueCharacterAdvance);

        // The caption costs its own characters AND the gap that separates it from the value, and a
        // pill with no caption pays for neither: the Energy pill hides its countdown at full, and a
        // width that charged for it anyway would reserve room for a caption nobody can see.
        return captionCharacters == 0
            ? width
            : width + metrics.InnerGap + (captionCharacters * metrics.CaptionCharacterAdvance);
    }
}
