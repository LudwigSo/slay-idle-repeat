using Shouldly;
using SlayIdleRepeat.AssetManifest;
using SlayIdleRepeat.AssetPipeline.Qa;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>Grammar: <c>{category}_{subcategory}_{id}[_{variant}][_{state}].png</c>, snake_case.</summary>
public sealed class AssetNamingTests
{
    /// <remarks>
    /// Includes deliberately awkward cases: a segment starting with a digit
    /// (<c>ui_panel_main_9slice.png</c>) and a five-segment name (<c>chr_hero_weapon_blade_s.png</c>)
    /// so a rule based on segment counts would fail here.
    /// </remarks>
    public static TheoryData<string> Doc15D1Examples() => new()
    {
        "chr_hero_body_idle.png",
        "chr_hero_weapon_blade_s.png",
        "chr_enemy_frost_brute_attack.png",
        "chr_boss_rimehold_phase3.png",
        "pet_stormfang_idle.png",
        "mnt_starhoof_move.png",
        "tile_icon_treasure.png",
        "board_frost_path_curve_l.png",
        "gear_weapon_staff_ss.png",
        "icon_perk_executioner.png",
        "icon_talent_might_whetstone.png",
        "icon_status_burn.png",
        "ui_panel_main_9slice.png",
        "bg_frost_layer2.png",
        "vfx_crit_burst_sheet.png",
        "die_face_star_default.png",
    };

    public static TheoryData<string, AssetNameRejection> MalformedNames() => new()
    {
        { "IconCurCrown.png", AssetNameRejection.NotSnakeCase },
        { "icon_cur_Crown.png", AssetNameRejection.NotSnakeCase },
        { "icon-cur-crown.png", AssetNameRejection.NotSnakeCase },
        { "icon_cur_crown", AssetNameRejection.MissingPngExtension },
        { "icon_cur_crown.webp", AssetNameRejection.MissingPngExtension },
        { "sprite_cur_crown.png", AssetNameRejection.UnknownCategoryPrefix },
        { "icon_cur_gem.png", AssetNameRejection.StemDoesNotMatchManifestId },
    };

    [Theory]
    [MemberData(nameof(Doc15D1Examples))]
    public void Validate_accepts_every_name_15_D1_prints_as_an_example(string fileName)
    {
        var stem = fileName[..^AssetNaming.PngExtension.Length];

        var result = AssetNaming.Validate(fileName, stem);

        result.Rejection.ShouldBe(AssetNameRejection.None);
        result.IsValid.ShouldBeTrue();
        result.Stem.ShouldBe(stem);
    }

    /// <summary>A rejection names which of the four naming defects it is, not just "invalid".</summary>
    [Theory]
    [MemberData(nameof(MalformedNames))]
    public void Validate_rejects_a_malformed_name_naming_the_D1_rule_it_broke(
        string fileName, AssetNameRejection expected)
    {
        var result = AssetNaming.Validate(fileName, ManifestRows.NonBiomeUiIcon);

        result.Rejection.ShouldBe(expected);
        result.IsValid.ShouldBeFalse();
        result.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Validate_accepts_a_shipped_manifest_rows_id_with_the_png_extension()
    {
        var row = ManifestRows.Require(ManifestRows.BiomeScopedCharacter);

        var result = AssetNaming.Validate(row.Id + AssetNaming.PngExtension, row.Id);

        result.Rejection.ShouldBe(AssetNameRejection.None);
        result.Stem.ShouldBe(row.Id);
    }

    [Fact]
    public void Validate_accepts_every_id_in_the_shipped_register()
    {
        var rows = PipelineFiles.Shipped.Art.Assets;
        rows.Count.ShouldBeGreaterThan(900, "an empty register would make this case vacuous");

        var rejected = rows
            .Select(row => AssetNaming.Validate(row.Id + AssetNaming.PngExtension, row.Id))
            .Where(result => !result.IsValid)
            .ToArray();

        rejected.ShouldBeEmpty();
    }

    /// <summary>
    /// A prefix added to <see cref="ManifestValidator.IdPrefixes"/> must become nameable here
    /// without an edit to this project.
    /// </summary>
    [Fact]
    public void Validate_accepts_every_category_prefix_M8_09_declares()
    {
        var prefixes = ManifestValidator.IdPrefixes.OrderBy(p => p, StringComparer.Ordinal).ToArray();
        prefixes.Length.ShouldBe(12);

        var rejected = prefixes
            .Select(prefix => AssetNaming.Validate($"{prefix}_sub_thing.png", $"{prefix}_sub_thing"))
            .Where(result => !result.IsValid)
            .ToArray();

        rejected.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("icon_cur_crown", "icon")]
    [InlineData("chr_enemy_astral_brute_idle", "chr")]
    [InlineData("board_frost_path_curve_l", "board")]
    public void CategoryOf_returns_the_D1_category_prefix(string assetId, string expected)
    {
        AssetNaming.CategoryOf(assetId).ShouldBe(expected);
    }

    /// <summary>An undeclared prefix must fail loudly, not silently join its own singleton bucket.</summary>
    [Fact]
    public void CategoryOf_fails_loudly_for_a_prefix_15_D1_does_not_declare()
    {
        var exception = Should.Throw<ArgumentException>(
            () => AssetNaming.CategoryOf("sprite_cur_crown"));

        exception.Message.ShouldContain("sprite", Case.Sensitive);
    }
}
