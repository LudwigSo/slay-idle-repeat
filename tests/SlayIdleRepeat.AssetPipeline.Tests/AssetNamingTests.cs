using Shouldly;
using SlayIdleRepeat.AssetManifest;
using SlayIdleRepeat.AssetPipeline.Qa;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// `15` §D1: <c>{category}_{subcategory}_{id}[_{variant}][_{state}].png</c>, snake_case — the
/// grammar half of Part F item 10.
/// </summary>
public sealed class AssetNamingTests
{
    /// <summary>
    /// Every example `15` §D1 prints under its grammar, verbatim.
    /// </summary>
    /// <remarks>
    /// 🔒 The doc's own examples, not a set chosen to suit the implementation. Two of them are the
    /// awkward ones and both are deliberate: <c>ui_panel_main_9slice.png</c> has a segment starting
    /// with a digit, and <c>chr_hero_weapon_blade_s.png</c> has five segments where
    /// <c>pet_stormfang_idle.png</c> has three — so a rule about segment counts would reject the
    /// doc.
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

    /// <summary>Malformed names and the `15` §D1 rule each one breaks.</summary>
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

    /// <summary>
    /// 🔒 Steering rule S2. "Not a §D1 name" is four defects with four fixes, and a reviewer holding
    /// a batch of rejected files needs to know which one they have.
    /// </summary>
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

    /// <summary>
    /// 🔒 Driven off a real shipped row, so the grammar is tested against the register rather than
    /// only against the doc's sixteen examples. M8-10 delivers 942 files named this way.
    /// </summary>
    [Fact]
    public void Validate_accepts_a_shipped_manifest_rows_id_with_the_png_extension()
    {
        var row = ManifestRows.Require(ManifestRows.BiomeScopedCharacter);

        var result = AssetNaming.Validate(row.Id + AssetNaming.PngExtension, row.Id);

        result.Rejection.ShouldBe(AssetNameRejection.None);
        result.Stem.ShouldBe(row.Id);
    }

    /// <summary>
    /// 🔒 The whole shipped register, not one row of it. A grammar that accepted the doc's sixteen
    /// examples and rejected fifty real ids would be a grammar nobody could ship against, and this
    /// is the case that would say so.
    /// </summary>
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
    /// 🔒 The prefix list is M8-09's and there is not a second one. This asserts the dependency
    /// rather than assuming it: a prefix added to <see cref="ManifestValidator.IdPrefixes"/> must
    /// become nameable here without an edit to this project.
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

    /// <summary>
    /// 🔒 An id whose prefix `15` §D1 does not declare is a loud failure, not a category of one. A
    /// silently accepted prefix would give the silhouette registry a bucket nothing else ever joins,
    /// and every asset in it would be maximally distinguishable forever.
    /// </summary>
    [Fact]
    public void CategoryOf_fails_loudly_for_a_prefix_15_D1_does_not_declare()
    {
        var exception = Should.Throw<ArgumentException>(
            () => AssetNaming.CategoryOf("sprite_cur_crown"));

        exception.Message.ShouldContain("sprite", Case.Sensitive);
    }
}
