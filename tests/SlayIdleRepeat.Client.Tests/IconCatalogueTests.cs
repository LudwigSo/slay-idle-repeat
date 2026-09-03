using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The HUD's icons as the files they are: one path per icon, every path a real file the checkout
/// holds, drawn flat on the 96-unit grid and imported at the scale a 3 cu/dp canvas needs.
/// </summary>
/// <remarks>
/// Read off disk the way <c>BoardSceneRuleTests</c> reads the scenes — no engine, no import, no
/// texture loaded. A missing file, a missing sidecar or a gradient in an SVG produces a blank tile
/// or a blurry one rather than an error, and nothing else in the build can see it.
/// </remarks>
public sealed class IconCatalogueTests
{
    private const string ClientProject = "src/SlayIdleRepeat.Client";

    private const string ResourceScheme = "res://";

    private const string ImportSidecar = ".import";

    private const string ImportedAtDoubleScale = "svg/scale=2.0";

    private const string HudGrid = "viewBox=\"0 0 96 96\"";

    public static TheoryData<HudIcon> EveryIcon()
    {
        var data = new TheoryData<HudIcon>();

        foreach (var icon in Enum.GetValues<HudIcon>())
        {
            data.Add(icon);
        }

        return data;
    }

    /// <summary>Every icon crossed with every construct a flat icon may not carry.</summary>
    public static TheoryData<HudIcon, string> EveryIconAndForbiddenConstruct()
    {
        var data = new TheoryData<HudIcon, string>();

        foreach (var icon in Enum.GetValues<HudIcon>())
        {
            data.Add(icon, "<text");
            data.Add(icon, "<image");
            data.Add(icon, "Gradient");
            data.Add(icon, "data:");
        }

        return data;
    }

    /// <summary>
    /// 🔒 The mapping itself, because the scene binds a tile to a path and nothing else checks that
    /// the Energy tile's path is the energy glyph rather than the crowns one.
    /// </summary>
    [Theory]
    [InlineData(HudIcon.Energy, "hud_energy.svg")]
    [InlineData(HudIcon.EnergyReserve, "hud_energy_reserve.svg")]
    [InlineData(HudIcon.Crowns, "hud_crowns.svg")]
    [InlineData(HudIcon.SoulShards, "hud_soul_shards.svg")]
    [InlineData(HudIcon.Gold, "hud_gold.svg")]
    [InlineData(HudIcon.Power, "hud_power.svg")]
    [InlineData(HudIcon.Chapter, "hud_chapter.svg")]
    [InlineData(HudIcon.Hp, "hud_hp.svg")]
    public void PathOf_names_the_authored_file_for_each_icon(HudIcon icon, string fileName) =>
        IconCatalogue.PathOf(icon).ShouldBe(
            IconCatalogue.Directory + fileName,
            "the scene loads the tile's texture by this path. Two icons swapped here draw the wrong " +
            "glyph on a tile whose caption says otherwise, and no other check can see it.");

    [Theory]
    [MemberData(nameof(EveryIcon))]
    public void PathOf_names_a_file_the_checkout_holds(HudIcon icon) =>
        File.Exists(OnDisk(IconCatalogue.PathOf(icon))).ShouldBeTrue(
            $"'{IconCatalogue.PathOf(icon)}' is a path with no file behind it. The engine loads a " +
            "missing texture as nothing and the tile draws without its icon, silently.");

    /// <summary>
    /// The SVG importer rasterises at the source's own pixel size by default — 96 px for a glyph
    /// drawn at 288 canvas units on a 3 cu/dp canvas, which is a blurry icon on every handset.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryIcon))]
    public void Every_icon_is_imported_at_double_scale(HudIcon icon)
    {
        var sidecar = OnDisk(IconCatalogue.PathOf(icon)) + ImportSidecar;

        File.Exists(sidecar).ShouldBeTrue(
            $"'{IconCatalogue.PathOf(icon)}' has no committed .import sidecar, so the editor writes one " +
            "at its defaults on first open and the scale below is whatever the default is.");
        File.ReadAllText(sidecar).ShouldContain(
            ImportedAtDoubleScale,
            Case.Sensitive,
            "the sidecar has to carry the scale explicitly; at the importer's default a 96-unit glyph " +
            "is rasterised at 96 px and upscaled three times on the canvas.");
    }

    [Theory]
    [MemberData(nameof(EveryIcon))]
    public void Every_icon_is_drawn_on_the_96_unit_grid(HudIcon icon) =>
        File.ReadAllText(OnDisk(IconCatalogue.PathOf(icon))).ShouldContain(
            HudGrid,
            Case.Sensitive,
            "one grid for every HUD glyph, so the importer's one scale puts them all at the same " +
            "size and a tile row does not have one icon a third larger than its neighbours.");

    /// <summary>
    /// Flat: one accent fill and a shared outline, which is what survives being drawn at 32 dp over a
    /// dark tile. Text is a font dependency the importer does not have, an embedded raster is a
    /// blurry one, and a gradient is a second colour system nothing else on the screen uses.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryIconAndForbiddenConstruct))]
    public void No_icon_carries_text_a_raster_or_a_gradient(HudIcon icon, string forbidden) =>
        File.ReadAllText(OnDisk(IconCatalogue.PathOf(icon))).ShouldNotContain(
            forbidden,
            Case.Sensitive,
            $"'{IconCatalogue.PathOf(icon)}' carries '{forbidden}'. The HUD's icons are flat vector " +
            "glyphs — one accent fill, one shared outline — and this is a construct a flat glyph has " +
            "no use for.");

    /// <summary>
    /// The floor under the scene rule that every texture the Home scene loads is a catalogued icon:
    /// the catalogue has to list every icon, or a scene loading a real one fails that rule for a
    /// missing row here.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryIcon))]
    public void All_lists_the_path_of_each_hud_icon(HudIcon icon) =>
        IconCatalogue.All.ShouldContain(
            IconCatalogue.PathOf(icon),
            $"{icon}'s path is missing from All, so a scene loading it fails the rule that every " +
            "texture is catalogued — for an icon that IS.");

    [Fact]
    public void All_lists_at_least_the_eight_hud_icons() =>
        IconCatalogue.All.Count.ShouldBeGreaterThanOrEqualTo(
            Enum.GetValues<HudIcon>().Length,
            "eight HUD icons at least, and a later lane's status half on top — never fewer than the " +
            "HUD alone declares. A list shorter than the enum has dropped one, and the theory above " +
            "says which.");

    [Fact]
    public void All_holds_no_path_twice() =>
        IconCatalogue.All.ShouldBeUnique(
            "a path listed twice is two enum members mapped to one file, and one of the two tiles is " +
            "drawing the other's glyph.");

    private static string OnDisk(string resourcePath)
    {
        resourcePath.ShouldStartWith(
            ResourceScheme,
            Case.Sensitive,
            "every icon path is a res:// path, which is the only kind the scene can load.");

        return Path.Combine(RepoPaths.RepositoryRoot, ClientProject, resourcePath[ResourceScheme.Length..]);
    }
}
