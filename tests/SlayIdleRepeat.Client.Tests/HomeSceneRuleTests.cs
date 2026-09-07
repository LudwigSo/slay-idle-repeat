using System.Text.RegularExpressions;
using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The structural facts about the Home scene and the theme it draws through that nothing else can
/// see go wrong.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Read as the text files they are</b>, the way <c>BoardSceneRuleTests</c> reads the board's.
/// No engine, no Node, no scene harness.
/// </para>
/// <para>
/// Each case is a fact whose breakage produces a PICTURE rather than an error: a tile styled by a
/// per-node override that the theme no longer agrees with, a texture path the catalogue does not
/// know, a type variation the theme does not declare and so falls back to the base control's look.
/// </para>
/// </remarks>
public sealed class HomeSceneRuleTests
{
    private const string HomeScene = "src/SlayIdleRepeat.Client/game/scenes/Home.tscn";

    private const string Theme = "src/SlayIdleRepeat.Client/game/theme/SlayTheme.tres";

    /// <summary>The transform an instance carries when nothing frames it.</summary>
    private const string Identity = "Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0)";

    private const string StyleOverride = "theme_override_styles/";

    private static readonly Regex Texture2DResource = new(
        @"^\[ext_resource type=""Texture2D"".*?path=""(?<path>res://[^""]+)""",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex ThemeResource = new(
        @"^\[ext_resource type=""Theme"".*?path=""res://game/theme/SlayTheme\.tres""",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex TypeVariation = new(
        @"theme_type_variation = &""(?<name>[A-Za-z0-9_]+)""",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    // ------------------------------------------------------------- the handover contract

    /// <summary>
    /// 🔒 <c>ScreenStage.Show</c> resolves <c>%Camera</c> as a Camera3D and makes it current. Lose the
    /// flag or the type and every handover onto Home draws through the outgoing screen's camera.
    /// </summary>
    [Fact]
    public void The_home_still_offers_a_scene_unique_Camera3D_for_the_handover_to_claim()
    {
        var camera = SceneText.Node(HomeScene, "Camera").ShouldNotBeNull(
            "Home.tscn declares no single node named 'Camera'. ScreenStage.Show looks it up by the " +
            "unique name '%Camera' and pushes an error when it resolves to nothing.");

        camera.Header.ShouldContain("type=\"Camera3D\"", Case.Sensitive);
        camera.Body.ShouldContain(
            "unique_name_in_owner = true",
            "without the scene-unique flag '%Camera' resolves to nothing and no camera is made current.");
    }

    [Fact]
    public void The_home_still_offers_a_scene_unique_Ui_layer_for_the_handover_to_show()
    {
        var ui = SceneText.Node(HomeScene, "Ui").ShouldNotBeNull();

        ui.Header.ShouldContain("type=\"CanvasLayer\"", Case.Sensitive);
        ui.Body.ShouldContain(
            "unique_name_in_owner = true",
            "ScreenStage shows and hides a screen's interface through '%Ui', because engine " +
            "visibility does not cross the Node3D-to-CanvasLayer seam.");
    }

    [Theory]
    [InlineData("WorldEnvironment")]
    [InlineData("DirectionalLight3D")]
    [InlineData("OmniLight3D")]
    public void The_home_declares_no_node_of_a_kind_only_AppRoot_may_own(string type) =>
        SceneText.Read(HomeScene).ShouldNotContain(
            $"type=\"{type}\"",
            Case.Sensitive,
            $"Home.tscn declares a {type}. AppRoot owns the one environment a viewport can have and " +
            "the lights every screen is lit by; a screen bringing its own decides by tree order which " +
            "applies, and lights the diorama differently from every other screen.");

    // ------------------------------------------------------------------- the diorama

    /// <summary>
    /// Lane 0 moved the hero's framing off <c>Hero.tscn</c>'s model and onto this instance, so every
    /// other screen stopped having to cancel it. A HUD rebuild that recreates the node from the
    /// template loses the override and the diorama sits at identity in the middle of the frame.
    /// </summary>
    [Fact]
    public void The_hero_diorama_keeps_its_framing_on_the_instance()
    {
        var hero = SceneText.Node(HomeScene, "Hero").ShouldNotBeNull(
            "Home.tscn declares no single node named 'Hero', so there is no diorama to frame.");

        hero.Header.ShouldContain(
            "instance=ExtResource(",
            Case.Sensitive,
            "the hero is an instance of Hero.tscn, not a copy of its nodes — a copy stops following " +
            "the hero's own scene the moment either changes.");
        hero.Body.ShouldContain(
            line => line.StartsWith("transform = ", StringComparison.Ordinal),
            "the framing override is a transform on THIS node. Without it the instance sits at " +
            "identity, which is what Hero.tscn was put at precisely so that Home would carry this.");
        hero.Body.ShouldNotContain(
            $"transform = {Identity}",
            "an override that is itself the identity frames nothing: the diorama sits at the origin " +
            "exactly as it would with no override at all.");
    }

    [Fact]
    public void The_home_keeps_its_backdrop_plane() =>
        SceneText.Read(HomeScene).ShouldContain(
            "QuadMesh",
            Case.Sensitive,
            "Home's camera does not move, so the backdrop plane behind the diorama stays where it is " +
            "— unlike the board's, which the travelling camera left behind. Removing it puts AppRoot's " +
            "environment colour behind a hero lit for a darker ground.");

    // ------------------------------------------------------------------- the theme's job

    [Fact]
    public void No_node_overrides_a_stylebox_the_theme_owns() =>
        SceneText.Read(HomeScene).ShouldNotContain(
            StyleOverride,
            Case.Sensitive,
            "a per-node stylebox is a second copy of a tile's look, and it wins over the theme " +
            "silently: retune the theme's HudTile and the one tile with an override keeps the old " +
            "look. The theme owns every stylebox; the scene names a type variation.");

    [Fact]
    public void The_scene_draws_through_the_shared_theme()
    {
        var scene = SceneText.Read(HomeScene);

        ThemeResource.IsMatch(scene).ShouldBeTrue(
            "no Theme ext_resource names SlayTheme.tres, so every type variation the scene uses " +
            "resolves against the engine default theme and draws as a plain grey control.");
        scene.ShouldContain(
            "theme = ExtResource(",
            Case.Sensitive,
            "a theme loaded and assigned to no node is a theme nothing draws through.");
    }

    [Fact]
    public void Every_type_variation_the_scene_uses_is_declared_by_the_theme()
    {
        var used = TypeVariation.Matches(SceneText.Read(HomeScene)).Select(m => m.Groups["name"].Value).Distinct().ToArray();
        var theme = SceneText.Read(Theme);

        used.ShouldNotBeEmpty(
            "the scene names no type variation at all, so the HUD is drawn as bare controls and the " +
            "rule below is stated over nothing.");
        used.ShouldAllBe(
            name => theme.Contains($"{name}/base_type", StringComparison.Ordinal),
            "a variation the theme does not declare is not an error — the control falls back to its " +
            "base type's look, and the one tile styled that way looks like a plain panel next to its " +
            "neighbours.");
    }

    [Theory]
    [InlineData("HudTile")]
    [InlineData("HudValue")]
    [InlineData("HudCaption")]
    [InlineData("HudTitle")]
    [InlineData("PrimaryButton")]
    [InlineData("QuietButton")]
    public void The_theme_declares_each_variation_the_hud_is_built_from(string variation) =>
        SceneText.Read(Theme).ShouldContain(
            $"{variation}/base_type",
            Case.Sensitive,
            $"SlayTheme.tres declares no '{variation}' variation. A scene naming it draws the base " +
            "control's look and a later lane building on the same vocabulary has nothing to build on.");

    // ------------------------------------------------------------------- the textures

    [Fact]
    public void Every_texture_the_scene_loads_is_a_catalogued_icon()
    {
        var loaded = Texture2DResource.Matches(SceneText.Read(HomeScene)).Select(m => m.Groups["path"].Value).ToArray();

        loaded.ShouldNotBeEmpty(
            "the scene loads no Texture2D at all, so the tiles have no icons and the rule below is " +
            "stated over nothing.");
        loaded.ShouldAllBe(
            path => IconCatalogue.All.Contains(path),
            "a texture path the catalogue does not list is one IconCatalogueTests does not check: " +
            "no grid, no scale, no flatness rule. Add the icon to the catalogue, then to the scene.");
    }
    // ============================================================== the run hub's structure

    /// <summary>The five tabs the hub reaches, in the order the reference puts them.</summary>
    /// <remarks>
    /// Named for the systems this build has rather than for the reference's captions: it says
    /// Skills and Relics, and the screens behind them here are Talents and Collection.
    /// </remarks>
    private static readonly string[] Tabs = ["Home", "Gear", "Talents", "Collection", "Shop"];

    private static readonly Regex TabNode = new(
        @"^\[node name=""(?<name>[^""]+)""[^\]]*parent=""[^""]*TabBar""",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex AccentEntry = new(
        @"^(?<key>[A-Za-z0-9_]+/colors/(?:action|energy|gain)_accent) = Color\((?<value>[^)]*)\)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>Every script this screen is drawn by. A colour literal in any of them is the finding.</summary>
    private static readonly string[] HomeScripts =
    [
        "src/SlayIdleRepeat.Client/game/scenes/Home.cs",
        "src/SlayIdleRepeat.Client/game/presenters/HomePresenter.cs",
        "src/SlayIdleRepeat.Client/game/presenters/HomeLayout.cs",
    ];

    /// <summary>
    /// 🔒 Five tabs, exactly the five, and no sixth.
    /// </summary>
    /// <remarks>
    /// Both halves matter and neither implies the other: a scene missing Collection has four tabs
    /// the player can reach and one system they cannot, and a scene with a sixth has a tab leading
    /// to a screen nobody has built. The brief forbids the sixth by name.
    /// </remarks>
    [Fact]
    public void The_tab_bar_carries_the_five_tabs_and_no_sixth()
    {
        var declared = TabNode.Matches(SceneText.Read(HomeScene))
            .Select(match => match.Groups["name"].Value)
            .ToArray();

        declared.ShouldBe(
            Tabs,
            ignoreOrder: false,
            "the tab bar's children ARE the five destinations, in the reference's order. A missing " +
            "one is a system with no way in; a sixth is a way in to a screen that does not exist.");
    }

    /// <summary>
    /// 🔒 One primary button on the screen, and it is the one that starts a run.
    /// </summary>
    /// <remarks>
    /// The variation is what makes a control read as THE action. A second control wearing it makes
    /// the screen ask the player to choose between two things that both look like the only thing.
    /// </remarks>
    [Fact]
    public void The_screen_declares_exactly_one_primary_button() =>
        Occurrences(SceneText.Read(HomeScene), "theme_type_variation = &\"PrimaryButton\"").ShouldBe(
            1,
            "the brief forbids a second primary button in as many words, and the launch block is " +
            "where the one lives.");

    /// <summary>
    /// 🔒 Not one colour is written in a script: every one is a theme entry.
    /// </summary>
    /// <remarks>
    /// A hard-coded colour wins over the theme silently, so retuning the theme leaves exactly the
    /// controls that were coloured in code looking like the old palette. The scan is over the whole
    /// script set at once because the failure is a property of the screen, not of any one file.
    /// </remarks>
    [Theory]
    [InlineData("new Color")]
    [InlineData("Color(")]
    [InlineData("Colors.")]
    public void No_home_script_writes_a_colour_of_its_own(string spelling)
    {
        HomeScripts.Length.ShouldBe(
            3, "a floor: the rule below is stated over the scripts this screen is really drawn by.");

        HomeScripts.ShouldAllBe(
            path => !SceneText.Read(path).Contains(spelling, StringComparison.Ordinal),
            $"a script naming '{spelling}' is deciding a colour where no reviewer of the theme will " +
            "ever see it. The screen names a theme entry; the theme owns the value.");
    }

    /// <summary>
    /// 🔒 The three accent roles this screen needs exist in the theme, and are three DIFFERENT colours.
    /// </summary>
    /// <remarks>
    /// The committed theme is a neutral grey-blue set with no accent hue at all, so all three of
    /// these are new. Distinctness is the discriminating half: three entries pointing at one
    /// existing grey satisfies every "the entry exists" check and draws a refill button, an action
    /// button and a gain flash in the same colour.
    /// </remarks>
    [Fact]
    public void The_theme_declares_three_distinct_accent_colours()
    {
        var accents = AccentEntry.Matches(SceneText.Read(Theme));

        accents.Count.ShouldBe(
            3,
            "the screen needs an action accent for the start button, an energy accent for the refill " +
            "offer and a gain accent for a power increase. Each is a named theme entry, and the " +
            "committed theme has none of them yet.");
        accents.Select(match => match.Groups["value"].Value).Distinct(StringComparer.Ordinal).Count()
            .ShouldBe(
                3,
                "three roles sharing one value is a theme that cannot tell the player which of the " +
                "two buttons they are looking at.");
    }

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        var at = text.IndexOf(needle, StringComparison.Ordinal);

        while (at >= 0)
        {
            count++;
            at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
