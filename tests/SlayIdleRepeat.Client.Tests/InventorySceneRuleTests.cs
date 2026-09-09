using System.Globalization;
using System.Text.RegularExpressions;
using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The structural facts about the Gear scene, its two part scenes and the theme they draw through
/// that nothing else can see go wrong. Read as the text files they are, the way <c>HomeSceneRuleTests</c>
/// reads Home's.
/// </summary>
public sealed class InventorySceneRuleTests
{
    private const string InventoryScene = "src/SlayIdleRepeat.Client/game/scenes/Inventory.tscn";
    private const string SlotTileScene = "src/SlayIdleRepeat.Client/game/scenes/SlotTile.tscn";
    private const string GearCellScene = "src/SlayIdleRepeat.Client/game/scenes/GearCell.tscn";
    private const string Theme = "src/SlayIdleRepeat.Client/game/theme/SlayTheme.tres";

    private const string Identity = "Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0)";
    private const string StyleOverride = "theme_override_styles/";

    /// <summary>The five bands' fixed palette, as the design brief states it.</summary>
    private static readonly (string Role, string Hex)[] RarityPalette =
    [
        ("rarity_c", "#9AA5B1"),
        ("rarity_b", "#4CAF50"),
        ("rarity_a", "#3B82F6"),
        ("rarity_s", "#F5A623"),
        ("rarity_ss", "#C13BE8"),
    ];

    private static readonly string[] Scenes = [InventoryScene, SlotTileScene, GearCellScene];

    private static readonly string[] Tabs = ["Home", "Gear", "Talents", "Collection", "Shop"];

    /// <summary>Every script this screen is drawn by. A colour literal in any of them is the finding.</summary>
    private static readonly string[] InventoryScripts =
    [
        "src/SlayIdleRepeat.Client/game/scenes/Inventory.cs",
        "src/SlayIdleRepeat.Client/game/scenes/InventoryHandover.cs",
        "src/SlayIdleRepeat.Client/game/presenters/InventoryPresenter.cs",
        "src/SlayIdleRepeat.Client/game/presenters/IconCatalogue.Gear.cs",
    ];

    private const int InventoryScriptFloor = 4;

    /// <summary>The sheet's three footers, one per page, each of which carries exactly one primary action.</summary>
    private static readonly string[] SheetFooters = ["DetailsActions", "EnhanceActions", "MergeActions"];

    private static readonly Regex Texture2DResource = new(
        @"^\[ext_resource type=""Texture2D"".*?path=""(?<path>res://[^""]+)""",
        RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    private static readonly Regex ThemeResource = new(
        @"^\[ext_resource type=""Theme"".*?path=""res://game/theme/SlayTheme\.tres""",
        RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    private static readonly Regex TypeVariation = new(
        @"theme_type_variation = &""(?<name>[A-Za-z0-9_]+)""",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    private static readonly Regex TabNode = new(
        @"^\[node name=""(?<name>[^""]+)""[^\]]*parent=""[^""]*TabBar""",
        RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    private static readonly Regex CaptionedControl = new(
        @"^\[node name=""(?<name>[A-Za-z]+)"" type=""(?<type>Label|Button)""[^\]]*\]\r?\n(?<block>(?:[^\[\r\n].*\r?\n)*)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    private static readonly Regex AnyNode = new(
        @"^\[node name=""(?<name>[^""]+)"" type=""(?<type>[A-Za-z0-9]+)""(?<header>[^\]]*)\]\r?\n(?<block>(?:[^\[\r\n].*\r?\n)*)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    private static readonly Regex RootMinimumSize = new(
        @"^custom_minimum_size = Vector2\((?<width>[0-9.]+), (?<height>[0-9.]+)\)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    private static readonly Regex AccentEntry = new(
        @"^SlayAccents/colors/(?<role>[a-z_]+) = Color\((?<r>[0-9.]+), (?<g>[0-9.]+), (?<b>[0-9.]+), [0-9.]+\)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    private static readonly Regex HexColour = new(
        @"#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\b",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    // ------------------------------------------------------------- the handover contract

    [Fact]
    public void The_screen_offers_a_scene_unique_Camera3D_and_Ui_layer_for_the_handover()
    {
        var camera = SceneText.Node(InventoryScene, "Camera").ShouldNotBeNull(
            "ScreenStage.Show looks the camera up by '%Camera' and pushes an error when that names nothing.");

        camera.Header.ShouldContain("type=\"Camera3D\"", Case.Sensitive);
        camera.Body.ShouldContain("unique_name_in_owner = true");

        var ui = SceneText.Node(InventoryScene, "Ui").ShouldNotBeNull();

        ui.Header.ShouldContain("type=\"CanvasLayer\"", Case.Sensitive);
        ui.Body.ShouldContain(
            "unique_name_in_owner = true",
            "ScreenStage shows and hides the interface through '%Ui', because visibility does not cross " +
            "the Node3D-to-CanvasLayer seam.");
    }

    [Fact]
    public void The_overlay_root_passes_every_pointer_event_no_band_above_it_took() =>
        SceneText.Node(InventoryScene, "Screen").ShouldNotBeNull().Body.ShouldContain(
            "mouse_filter = 2",
            "the overlay root defaults to STOP and would swallow every tap the bands did not take — " +
            "including the ones meant for the slot tiles over the diorama.");

    [Theory]
    [InlineData("WorldEnvironment")]
    [InlineData("DirectionalLight3D")]
    [InlineData("OmniLight3D")]
    public void The_screen_declares_no_node_of_a_kind_only_AppRoot_may_own(string type) =>
        SceneText.Read(InventoryScene).ShouldNotContain(
            $"type=\"{type}\"", Case.Sensitive,
            $"a {type} on a screen lights and grounds the diorama differently from every other screen.");

    [Fact]
    public void The_hero_diorama_keeps_its_framing_on_the_instance()
    {
        var hero = SceneText.Node(InventoryScene, "Hero").ShouldNotBeNull();

        hero.Header.ShouldContain("instance=ExtResource(", Case.Sensitive, "the hero is an instance of Hero.tscn, not a copy.");
        hero.Body.ShouldContain(line => line.StartsWith("transform = ", StringComparison.Ordinal));
        hero.Body.ShouldNotContain(
            $"transform = {Identity}",
            "an identity override frames nothing; Hero.tscn sits at identity precisely so each screen frames its own.");
    }

    // ------------------------------------------------------------------- the theme's job

    [Theory]
    [InlineData(InventoryScene)]
    [InlineData(SlotTileScene)]
    [InlineData(GearCellScene)]
    public void No_node_overrides_a_stylebox_the_theme_owns(string scene) =>
        SceneText.Read(scene).ShouldNotContain(
            StyleOverride, Case.Sensitive,
            "a per-node stylebox wins over the theme silently; the band's tint is written at render time " +
            "over the theme's face, never authored as a second face in the scene.");

    [Fact]
    public void The_scene_draws_through_the_shared_theme()
    {
        var scene = SceneText.Read(InventoryScene);

        ThemeResource.IsMatch(scene).ShouldBeTrue("no Theme ext_resource names SlayTheme.tres.");
        scene.ShouldContain("theme = ExtResource(", Case.Sensitive, "a theme loaded and assigned to no node is a theme nothing draws through.");
    }

    [Theory]
    [InlineData(InventoryScene)]
    [InlineData(SlotTileScene)]
    [InlineData(GearCellScene)]
    public void Every_type_variation_the_scene_uses_is_declared_by_the_theme(string scene)
    {
        var used = TypeVariation.Matches(SceneText.Read(scene)).Select(m => m.Groups["name"].Value).Distinct().ToArray();
        var theme = SceneText.Read(Theme);

        used.ShouldNotBeEmpty("the scene names no type variation at all, so the rule below is stated over nothing.");
        used.ShouldAllBe(
            name => theme.Contains($"{name}/base_type", StringComparison.Ordinal),
            "a variation the theme does not declare falls back to the base control's look, silently.");
    }

    [Theory]
    [InlineData("SlotTile")]
    [InlineData("GearCell")]
    [InlineData("GearDeck")]
    [InlineData("GearSheet")]
    [InlineData("GearModal")]
    [InlineData("GearScrim")]
    [InlineData("GearGem")]
    [InlineData("DangerButton")]
    [InlineData("WalletChip")]
    public void The_theme_declares_each_variation_the_gear_screen_is_built_from(string variation) =>
        SceneText.Read(Theme).ShouldContain(
            $"{variation}/base_type", Case.Sensitive,
            $"SlayTheme.tres declares no '{variation}' variation, and the screen names it.");

    /// <summary>
    /// 🔒 The five bands' colours are the fixed palette, to the rounding the resource format allows.
    /// </summary>
    /// <remarks>
    /// Rarity colour is a signal the whole game trains players to read, so a near-miss hex is a
    /// finding rather than a nitpick. Compared numerically rather than by string, because the theme
    /// writes floats and the brief writes hex.
    /// </remarks>
    [Fact]
    public void The_theme_carries_the_fixed_rarity_palette_by_role()
    {
        var accents = AccentEntry.Matches(SceneText.Read(Theme))
            .ToDictionary(
                m => m.Groups["role"].Value,
                m => (R: Parse(m.Groups["r"].Value), G: Parse(m.Groups["g"].Value), B: Parse(m.Groups["b"].Value)),
                StringComparer.Ordinal);

        foreach (var (role, hex) in RarityPalette)
        {
            accents.ShouldContainKey(role, "the screen reads this band's tint by this role, and an absent role draws black.");

            var (r, g, b) = accents[role];
            var expected = (R: Channel(hex, 1), G: Channel(hex, 3), B: Channel(hex, 5));

            Math.Abs(r - expected.R).ShouldBeLessThan(0.002, $"{role} red is off the palette's {hex}");
            Math.Abs(g - expected.G).ShouldBeLessThan(0.002, $"{role} green is off the palette's {hex}");
            Math.Abs(b - expected.B).ShouldBeLessThan(0.002, $"{role} blue is off the palette's {hex}");
        }

        accents.ShouldContainKey("loss_accent", "a loss is drawn in its own accent beside the arrow and the sign.");
        accents.ShouldContainKey("lock_accent", "a lock and a warning are drawn in their own accent.");
    }

    // ------------------------------------------------------------------- the textures

    [Fact]
    public void Every_texture_the_scene_loads_is_a_catalogued_icon()
    {
        var loaded = Texture2DResource.Matches(SceneText.Read(InventoryScene)).Select(m => m.Groups["path"].Value).ToArray();

        loaded.ShouldNotBeEmpty("the wallet chips load no icon at all, so the rule below is stated over nothing.");
        loaded.ShouldAllBe(
            path => IconCatalogue.All.Contains(path),
            "a texture path the catalogue does not list is one IconCatalogueTests does not check.");
    }

    [Fact]
    public void Every_slot_and_mark_glyph_is_a_file_the_checkout_holds()
    {
        var paths = Enum.GetValues<Core.Primitives.GearSlot>().Select(IconCatalogue.PathOf)
            .Concat(Enum.GetValues<GearMark>().Select(IconCatalogue.PathOf))
            .ToArray();

        paths.Length.ShouldBe(10, "six slots and four marks.");

        foreach (var path in paths)
        {
            var onDisk = Path.Combine(RepoPaths.RepositoryRoot, "src/SlayIdleRepeat.Client", path["res://".Length..]);

            File.Exists(onDisk).ShouldBeTrue($"'{path}' has no file behind it; the glyph draws as nothing.");
            File.Exists(onDisk + ".import").ShouldBeTrue($"'{path}' has no import sidecar, so the editor imports it at its defaults.");
            File.ReadAllText(onDisk + ".import").ShouldContain("svg/scale=2.0", Case.Sensitive);
            File.ReadAllText(onDisk).ShouldContain("viewBox=\"0 0 96 96\"", Case.Sensitive, "one grid for every HUD glyph.");
            IconCatalogue.All.ShouldContain(path, "a glyph the catalogue's All does not list fails the scene rule for a texture that IS catalogued.");
        }
    }

    // ------------------------------------------------------------------- the controls

    [Theory]
    [InlineData(InventoryScene)]
    [InlineData(SlotTileScene)]
    [InlineData(GearCellScene)]
    public void Every_control_that_draws_text_says_what_it_does_when_the_text_does_not_fit(string scene)
    {
        var controls = CaptionedControl.Matches(SceneText.Read(scene))
            .Select(m => (Name: m.Groups["name"].Value, Block: m.Groups["block"].Value))
            .ToArray();

        controls.ShouldNotBeEmpty("the scene declares no Label or Button, so the rule below is stated over nothing.");

        controls.Where(c => !c.Block.Contains("autowrap_mode = ", StringComparison.Ordinal) &&
                            !c.Block.Contains("text_overrun_behavior = ", StringComparison.Ordinal))
            .Select(c => c.Name)
            .ShouldBeEmpty(
                "each of these reports its whole caption as a minimum width the containers above it must " +
                "honour, and a German caption a third longer grows the row off the canvas.");
    }

    /// <summary>The Gear scene's text controls, counted, so the rule above cannot go quiet as the scene grows.</summary>
    [Fact]
    public void The_gear_scene_declares_the_text_controls_the_screen_is_made_of() =>
        CaptionedControl.Matches(SceneText.Read(InventoryScene)).Count.ShouldBeGreaterThanOrEqualTo(
            50, "a floor under the overflow rule: the screen has this many captions and buttons at least.");

    /// <summary>🔒 A slot tile and a bag cell are tap targets, and are authored as ones.</summary>
    [Theory]
    [InlineData(SlotTileScene, "SlotTile")]
    [InlineData(GearCellScene, "GearCell")]
    public void A_tile_and_a_cell_are_at_least_a_tap_target_square_and_their_children_pass_the_pointer(
        string scene, string root)
    {
        var text = SceneText.Read(scene);
        var authored = RootMinimumSize.Match(text);

        authored.Success.ShouldBeTrue($"{root}.tscn states no minimum size, so the target is whatever its glyph needs.");
        Parse(authored.Groups["width"].Value).ShouldBeGreaterThanOrEqualTo(HomeLayout.MinimumTouchTarget);
        Parse(authored.Groups["height"].Value).ShouldBeGreaterThanOrEqualTo(HomeLayout.MinimumTouchTarget);

        var nodes = AnyNode.Matches(text).Select(m => (Name: m.Groups["name"].Value, Header: m.Groups["header"].Value, Block: m.Groups["block"].Value)).ToArray();

        nodes[0].Name.ShouldBe(root);
        nodes.Skip(1).Where(n => !n.Block.Contains("mouse_filter = 2", StringComparison.Ordinal))
            .Select(n => n.Name)
            .ShouldBeEmpty("a child that takes the pointer steals the press from the button under it.");
    }

    [Fact]
    public void Every_button_authored_in_the_gear_scene_is_at_least_a_tap_target_tall()
    {
        var short_ = AnyNode.Matches(SceneText.Read(InventoryScene))
            .Where(m => m.Groups["type"].Value == "Button")
            .Select(m => (Name: m.Groups["name"].Value, Block: m.Groups["block"].Value))
            .Where(b => !b.Name.Equals("Scrim", StringComparison.Ordinal))
            .Where(b =>
            {
                var size = RootMinimumSize.Match(b.Block);

                return !size.Success || Parse(size.Groups["height"].Value) < HomeLayout.MinimumTouchTarget;
            })
            .Select(b => b.Name)
            .ToArray();

        short_.ShouldBeEmpty("48 dp at this canvas's 3x scale is the floor every control on Home is padded to.");
    }

    [Fact]
    public void Each_sheet_page_carries_exactly_one_primary_button()
    {
        var text = SceneText.Read(InventoryScene);

        foreach (var footer in SheetFooters)
        {
            var primaries = AnyNode.Matches(text)
                .Where(m => m.Groups["header"].Value.Contains($"Footer/{footer}", StringComparison.Ordinal))
                .Count(m => m.Groups["block"].Value.Contains("theme_type_variation = &\"PrimaryButton\"", StringComparison.Ordinal));

            primaries.ShouldBe(1, $"{footer} is one page's footer and one page has one thing that looks like THE action.");
        }

        AnyNode.Matches(text)
            .Where(m => m.Groups["header"].Value.Contains("SheetPages/", StringComparison.Ordinal))
            .Count(m => m.Groups["type"].Value == "Button")
            .ShouldBe(0, "a button inside the scrolling page can scroll out of reach; every action lives in the footer.");

        Occurrences(text, "theme_type_variation = &\"DangerButton\"").ShouldBe(
            2, "the two buttons that destroy — the batch bar's request and the confirmation's answer — and no third.");
    }

    [Fact]
    public void The_tab_bar_carries_Homes_five_tabs_in_Homes_order() =>
        TabNode.Matches(SceneText.Read(InventoryScene)).Select(m => m.Groups["name"].Value)
            .ShouldBe(Tabs, ignoreOrder: false, "the bar is the same bar on both screens, or it is two bars.");

    // ------------------------------------------------------------------- the scripts

    [Theory]
    [InlineData("Color(")]
    [InlineData("Colors.")]
    [InlineData("Color.From")]
    public void No_gear_script_writes_a_colour_of_its_own(string spelling)
    {
        InventoryScripts.Length.ShouldBe(InventoryScriptFloor, "a floor: the rule is stated over the scripts the screen is drawn by.");

        InventoryScripts.ShouldAllBe(
            path => !SceneText.Read(path).Contains(spelling, StringComparison.Ordinal),
            $"a script naming '{spelling}' decides a colour where no reviewer of the theme will see it; the " +
            "five rarity tints and the two accents are SlayAccents roles.");
    }

    [Fact]
    public void No_gear_script_writes_a_colour_as_a_hex_string() =>
        InventoryScripts.ShouldAllBe(
            path => !HexColour.IsMatch(SceneText.Read(path)),
            "a hex colour in a script is the theme's value copied where no reviewer of the theme will look.");

    private static float Parse(string value) => float.Parse(value, CultureInfo.InvariantCulture);

    private static double Channel(string hex, int at) => int.Parse(hex.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;

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
