using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// <c>HomeLayout</c> — the Home screen's measurable claims: four bands that tile the canvas, three
/// of them pinned and one taking the remainder, safe-area insets that grow bands rather than shrink
/// targets, every tap target at 48 dp, and three pills that still fit at their longest values.
/// </summary>
/// <remarks>
/// <para>
/// No engine anywhere: these are the claims that cannot be checked by looking at a screenshot, and a
/// screenshot is the only other place they could be checked at all.
/// </para>
/// <para>
/// 🔒 <b>The two canvases are the two shipping aspect ratios</b>, 9:16 and 9:20 — the project is
/// portrait-locked to that range. A layout asserted at one of them only would let a pinned viewport
/// height pass, which is exactly the mistake the residual rule exists to prevent.
/// </para>
/// </remarks>
public sealed class HomeLayoutTests
{
    private const float CanvasWidth = 1080f;

    /// <summary>9:16 — the short shipping profile, and the canvas the project declares.</summary>
    private const float ShortCanvasHeight = 1920f;

    /// <summary>9:20 — the tall shipping profile.</summary>
    private const float TallCanvasHeight = 2400f;

    // The reference's logical numbers at the canvas's 3x scale: top bar 64, launch 156, tab bar 66.
    private const float TopBarHeight = 192f;
    private const float LaunchHeight = 468f;
    private const float TabBarHeight = 198f;

    /// <summary>What the three pinned bands take together, leaving the hero band the rest.</summary>
    private const float PinnedTotal = TopBarHeight + LaunchHeight + TabBarHeight;

    private const float SidePadding = 36f;
    private const float ItemGap = 18f;
    private const float AvatarWidth = 108f;

    /// <summary>The settings glyph as the reference draws it: 30 logical, well under a tap target.</summary>
    private const float SettingsGlyphWidth = 90f;

    /// <summary>
    /// The gaps the top bar spends: avatar-pill, pill-pill, pill-pill, pill-settings.
    /// </summary>
    private const float TopBarGapCount = 4f;

    /// <summary>The energy pill's value at a full starting bar — the longest it can be.</summary>
    private const string LongestEnergyValue = "120/120";

    /// <summary>The regeneration countdown, in the shape the reference shows it.</summary>
    private const string LongestEnergyCaption = "4:12";

    private static readonly HomeLayoutMetrics Metrics = new(
        new HomeBandMetrics(TopBarHeight, LaunchHeight, TabBarHeight),
        new HomeTopBarMetrics(SidePadding, ItemGap, AvatarWidth, SettingsGlyphWidth),
        // ⚠️ The two advances are this fixture's stated per-character budget, not a measured face:
        // nothing in the design authors a font metric. They are what the scene's exported numbers
        // will be checked against, and the case below is what says whether the shipped face fits.
        new HomePillMetrics(
            HorizontalPadding: 27f, IconWidth: 48f, InnerGap: 15f,
            ValueCharacterAdvance: 15f, CaptionCharacterAdvance: 12f),
        new HomeLaunchMetrics(
            SidePadding: 42f,
            RowGap: 30f,
            StageCardHeight: 156f,
            StartButtonHeight: 168f,
            RewardLineHeight: 78f,
            ChangeButtonWidth: 96f,
            ChangeButtonHeight: 78f));

    // ------------------------------------------------------------------------ the four bands

    /// <summary>The four bands tile the canvas: each starts where the last ended, and nothing is left over.</summary>
    [Theory]
    [InlineData(ShortCanvasHeight)]
    [InlineData(TallCanvasHeight)]
    public void For_stacks_the_four_bands_with_no_gap_and_no_overlap(float canvasHeight)
    {
        var layout = Layout(canvasHeight);

        layout.TopBar.Y.ShouldBe(0f, "the top bar paints from the very top, safe inset included.");
        layout.Hero.Y.ShouldBe(layout.TopBar.Bottom, "a gap here is a stripe of background between two bands.");
        layout.Launch.Y.ShouldBe(layout.Hero.Bottom, "an overlap here draws the launch block over the diorama.");
        layout.TabBar.Y.ShouldBe(layout.Launch.Bottom);
        layout.TabBar.Bottom.ShouldBe(
            canvasHeight,
            "the last band ends at the bottom of the canvas; anything less leaves a strip nothing paints.");
    }

    /// <summary>
    /// 🔒 Three heights are pinned and the hero band is what is left — which is the layout's one rule.
    /// </summary>
    /// <remarks>
    /// 1062 at 9:16 and 1542 at 9:20, both being the canvas minus the 858 the three pinned bands
    /// take. A layout that pinned the viewport instead would answer the same number twice, and the
    /// second row is what catches it.
    /// </remarks>
    [Theory]
    [InlineData(ShortCanvasHeight, 1062f)]
    [InlineData(TallCanvasHeight, 1542f)]
    public void For_pins_three_bands_and_gives_the_hero_band_the_residual(float canvasHeight, float heroHeight)
    {
        var layout = Layout(canvasHeight);

        layout.TopBar.Height.ShouldBe(TopBarHeight);
        layout.Launch.Height.ShouldBe(LaunchHeight);
        layout.TabBar.Height.ShouldBe(TabBarHeight);
        layout.Hero.Height.ShouldBe(
            heroHeight,
            $"the canvas is {canvasHeight} and the three pinned bands take {PinnedTotal}.");
    }

    /// <summary>A canvas too short for the pinned bands is refused, never drawn with a negative band.</summary>
    [Fact]
    public void For_refuses_a_canvas_too_short_for_the_three_pinned_bands() =>
        Should.Throw<ArgumentOutOfRangeException>(
            () => HomeLayout.For(CanvasWidth, PinnedTotal - 1f, SafeAreaInsets.None, Metrics),
            "a negative residual clamped to zero is a screen with no diorama and no explanation.");

    // -------------------------------------------------------------------- the safe area

    /// <summary>The top inset lands INSIDE the top bar: the band grows, its content moves down.</summary>
    [Fact]
    public void For_grows_the_top_bar_by_the_top_inset_and_moves_its_content_below_it()
    {
        const float topInset = 132f;

        var layout = HomeLayout.For(
            CanvasWidth, ShortCanvasHeight, new SafeAreaInsets(topInset, 0f, 0f, 0f), Metrics);

        layout.TopBar.Y.ShouldBe(0f, "the bar paints behind the status bar rather than starting below it.");
        layout.TopBar.Height.ShouldBe(TopBarHeight + topInset);
        layout.TopBarContent.Y.ShouldBe(topInset, "nothing may be drawn under the notch.");
        layout.TopBarContent.Height.ShouldBe(TopBarHeight);
    }

    /// <summary>The bottom inset lands BELOW the tab row: the band grows, the targets do not move.</summary>
    [Fact]
    public void For_grows_the_tab_bar_by_the_bottom_inset_and_leaves_the_tab_row_alone()
    {
        const float bottomInset = 96f;

        var layout = HomeLayout.For(
            CanvasWidth, ShortCanvasHeight, new SafeAreaInsets(0f, 0f, bottomInset, 0f), Metrics);

        layout.TabBar.Height.ShouldBe(TabBarHeight + bottomInset);
        layout.TabRow.Height.ShouldBe(TabBarHeight);
        layout.TabRow.Bottom.ShouldBe(
            ShortCanvasHeight - bottomInset,
            "the tabs sit above the gesture bar; a row running under it is a row the handset swallows.");
    }

    /// <summary>
    /// 🔒 An absurd bottom inset grows the band and never eats the row.
    /// </summary>
    /// <remarks>
    /// The failure this catches is a tab bar of a FIXED total height that subtracts the inset from
    /// itself: it looks right on a handset with a small gesture bar and shrinks the five targets
    /// below a thumb on one without.
    /// </remarks>
    [Fact]
    public void For_never_shrinks_the_tab_row_below_a_touch_target_however_tall_the_bottom_inset()
    {
        var layout = HomeLayout.For(
            CanvasWidth, TallCanvasHeight, new SafeAreaInsets(0f, 0f, 400f, 0f), Metrics);

        layout.TabRow.Height.ShouldBeGreaterThanOrEqualTo(HomeLayout.MinimumTouchTarget);
    }

    // ------------------------------------------------------------------- the tap targets

    /// <summary>
    /// Every control the layout places is at least 48 dp on both axes — the settings glyph and the
    /// Change control included, which the reference draws at 30 and 34 logical.
    /// </summary>
    [Fact]
    public void Every_tap_target_the_layout_places_is_at_least_the_minimum()
    {
        var layout = Layout(ShortCanvasHeight);

        var targets = new[]
        {
            ("settings", layout.SettingsButton),
            ("change", layout.ChangeButton),
            ("start", layout.StartButton),
            ("tab row", layout.TabRow),
        };

        targets.Length.ShouldBe(4, "a floor: the rule below is stated over four real targets.");
        targets.ShouldAllBe(
            target => target.Item2.Width >= HomeLayout.MinimumTouchTarget
                && target.Item2.Height >= HomeLayout.MinimumTouchTarget,
            "a control drawn smaller than 48 dp is padded to a 48 dp target. The settings glyph is " +
            "30 logical and the Change control 34: both are drawn as designed and both must be " +
            "tappable by a thumb that missed.");
    }

    // ---------------------------------------------------------------------- the pill row

    /// <summary>
    /// The three pills get the canvas minus the bar's own chrome: two side paddings, the avatar, the
    /// settings TARGET and the four gaps between the five items.
    /// </summary>
    /// <remarks>
    /// The settings target rather than its glyph, and that is the discriminating part: reserving the
    /// 90-unit glyph leaves 54 units of the pill row underneath a control that will take a tap.
    /// </remarks>
    [Fact]
    public void For_leaves_the_pills_the_canvas_minus_the_top_bars_own_chrome()
    {
        var expected = CanvasWidth
            - (2f * SidePadding)
            - AvatarWidth
            - HomeLayout.MinimumTouchTarget
            - (TopBarGapCount * ItemGap);

        Layout(ShortCanvasHeight).PillRowWidth.ShouldBe(
            expected,
            "1080 less 2x36 of padding, the 108 avatar, the 144 settings target and 4x18 of gaps.");
    }

    /// <summary>
    /// 🔒 The three pills fit at the longest values the brief names: 999,999 Crowns, a full Energy
    /// bar with its countdown, and 1.2M power.
    /// </summary>
    /// <remarks>
    /// The value strings come from <see cref="PlayerNumber"/> rather than from literals, so this is
    /// the rendering the screen will actually draw: 999,999 Crowns is shortened and 1.2M power is
    /// shortened, and a case that transcribed "999,999" would be measuring a string no pill shows.
    /// </remarks>
    [Fact]
    public void The_three_pills_fit_their_longest_values_in_the_row_they_are_given()
    {
        var crowns = PlayerNumber.Abbreviated(999_999L);
        var power = PlayerNumber.Abbreviated(1_200_000L);

        var total =
            HomeLayout.PillWidth(crowns.Length, 0, Metrics.Pill)
            + HomeLayout.PillWidth(LongestEnergyValue.Length, LongestEnergyCaption.Length, Metrics.Pill)
            + HomeLayout.PillWidth(power.Length, 0, Metrics.Pill);

        total.ShouldBeLessThanOrEqualTo(
            Layout(ShortCanvasHeight).PillRowWidth,
            $"'{crowns}', '{LongestEnergyValue} {LongestEnergyCaption}' and '{power}' are the widest " +
            "the three pills get, and a row that cannot hold them pushes one off the screen edge.");
    }

    /// <summary>A pill with a caption is wider than the same pill without one, by the caption and its gap.</summary>
    /// <remarks>
    /// The counterfactual: a width that ignored the caption would answer the same number twice, and
    /// the Energy pill would clip its countdown the moment it stopped being full.
    /// </remarks>
    [Fact]
    public void PillWidth_charges_for_the_caption_only_when_there_is_one()
    {
        var withCaption = HomeLayout.PillWidth(
            LongestEnergyValue.Length, LongestEnergyCaption.Length, Metrics.Pill);
        var without = HomeLayout.PillWidth(LongestEnergyValue.Length, 0, Metrics.Pill);

        (withCaption - without).ShouldBe(
            Metrics.Pill.InnerGap + (LongestEnergyCaption.Length * Metrics.Pill.CaptionCharacterAdvance),
            "the caption costs its own characters plus the one gap that separates it from the value.");
    }

    private static HomeLayout Layout(float canvasHeight) =>
        HomeLayout.For(CanvasWidth, canvasHeight, SafeAreaInsets.None, Metrics);
}
