using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetManifest.Tests;

/// <summary>
/// The per-row fields the three downstream consumers read: M8-01a keys provenance to the id,
/// M8-06 drives post-processing from delivery size / pivot / atlas, M8-10 emits one placeholder
/// per row.
/// </summary>
public sealed class ArtFieldTests
{
    /// <summary>`15` §C's delivery-size table, transcribed here by hand from the doc.</summary>
    [Theory]
    [InlineData("chr_hero_body_idle", 512, 512)]          // Hero body & gear overlays
    [InlineData("chr_hero_weapon_blade_ss", 512, 512)]
    [InlineData("chr_enemy_frost_brute_attack", 512, 512)] // Standard enemies
    [InlineData("chr_elite_rimefang_warden_idle", 640, 640)] // Elites
    [InlineData("chr_boss_rimehold_phase3", 1024, 1024)]   // Bosses
    [InlineData("pet_stormfang_idle", 256, 256)]           // Pets
    [InlineData("mnt_starhoof_move", 512, 384)]            // Mounts — wider than tall
    [InlineData("tile_icon_treasure", 192, 192)]           // Tile icons
    [InlineData("board_frost_path_curve_l", 256, 256)]     // Board path & decor
    [InlineData("bg_frost_layer2", 1080, 1440)]            // Battle backdrops, per layer
    [InlineData("gear_weapon_staff_ss", 192, 192)]         // Gear icons
    [InlineData("icon_perk_executioner", 128, 128)]        // Perk / talent / status icons
    [InlineData("icon_talent_might_whetstone", 128, 128)]
    [InlineData("icon_status_burn", 128, 128)]
    [InlineData("icon_cur_gold", 96, 96)]                  // Currency icons
    [InlineData("die_face_star", 256, 256)]                // Die faces
    [InlineData("store_app_icon", 1024, 1024)]             // 15 §E21's own table
    public void Delivery_sizes_match_15_section_C(string id, int width, int height)
    {
        var asset = ManifestFiles.Shipped.RequireArt(id);

        asset.RequireDeliverySize().ShouldBe(new PixelSize(width, height));
    }

    /// <summary>
    /// 🔒 Where `15` states no size, the row carries null and stays greppable. Never a borrowed
    /// default — `game-data/README.md` and `16` R6 both forbid filling a hole with a plausible value.
    /// </summary>
    [Theory]
    [InlineData("bg_home", "15 §C's 1080×1440 row is stated for the biome battle backdrops only")]
    [InlineData("bg_arena", "same — §C says nothing about the four scene backgrounds")]
    [InlineData("ui_panel_main_9slice", "15 §C: 'UI panels & frames — variable, 9-slice'")]
    [InlineData("icon_ui_sort", "15 §C has no row for misc UI icons at all")]
    [InlineData("store_wordmark", "delivered as SVG plus PNG at three sizes, so no single size")]
    public void A_size_the_docs_do_not_state_is_null(string id, string why)
    {
        ManifestFiles.Shipped.RequireArt(id).DeliverySize.ShouldBeNull(why);
    }

    /// <summary>
    /// 🔒 And reading one fails loudly rather than defaulting. The pointer is named so a caller
    /// knows which slot is unauthorised, not merely that something was.
    /// </summary>
    [Fact]
    public void Reading_an_unauthorised_delivery_size_throws_instead_of_producing_a_number()
    {
        var asset = ManifestFiles.Shipped.RequireArt("icon_ui_sort");

        var thrown = Should.Throw<InvalidOperationException>(() => asset.RequireDeliverySize());

        thrown.Message.ShouldContain("icon_ui_sort", Case.Sensitive);
        thrown.Message.ShouldContain("15 §C states none", Case.Sensitive);
        thrown.Message.ShouldContain("DSC_MISSING_SIZES", Case.Sensitive);
    }

    /// <summary>`15` §C: "Pivot | Characters: bottom-center. Icons: center."</summary>
    [Theory]
    [InlineData("E2", "bottom-center")]
    [InlineData("E3", "bottom-center")]
    [InlineData("E4", "bottom-center")]
    [InlineData("E5", "bottom-center")]
    [InlineData("E6", "bottom-center")]
    [InlineData("E7", "bottom-center")]
    [InlineData("E8", "center")]
    [InlineData("E11", "center")]
    [InlineData("E12", "center")]
    [InlineData("E13", "center")]
    [InlineData("E14", "center")]
    [InlineData("E15", "center")]
    [InlineData("E20", "center")]
    public void Character_and_icon_sections_carry_the_pivot_15_section_C_states(
        string sectionId, string pivot)
    {
        var rows = ManifestFiles.Shipped.ArtInSection(sectionId).ToArray();

        rows.ShouldNotBeEmpty($"{sectionId} must have rows for this rule to mean anything");
        rows.ShouldAllBe(a => a.Pivot == pivot);
    }

    /// <summary>
    /// 🔒 `15` §C names exactly two pivot classes. Everything else is null — a board tile, a
    /// backdrop, a 9-slice panel and a die face are none of "character" or "icon", and guessing
    /// one would be inventing a value the doc withholds.
    /// </summary>
    [Theory]
    [InlineData("E9")]
    [InlineData("E10")]
    [InlineData("E16")]
    [InlineData("E17")]
    [InlineData("E19")]
    [InlineData("E21")]
    public void Sections_15_section_C_gives_no_pivot_for_carry_null(string sectionId)
    {
        var rows = ManifestFiles.Shipped.ArtInSection(sectionId).ToArray();

        rows.ShouldNotBeEmpty();
        rows.ShouldAllBe(a => a.Pivot == null);
    }

    /// <summary>`15` §D2's atlas grouping, per section.</summary>
    [Theory]
    [InlineData("E2", "atlas_hero")]
    [InlineData("E6", "atlas_pets")]
    [InlineData("E7", "atlas_mounts")]
    [InlineData("E11", "atlas_icons_gear")]
    [InlineData("E12", "atlas_icons_perks")]
    [InlineData("E13", "atlas_icons_perks")]
    [InlineData("E14", "atlas_icons_perks")]
    [InlineData("E15", "atlas_ui")]
    [InlineData("E16", "atlas_dice")]
    [InlineData("E17", "atlas_ui")]
    [InlineData("E19", "atlas_vfx")]
    public void Sections_pack_into_the_atlas_15_section_D2_assigns(string sectionId, string atlas)
    {
        var rows = ManifestFiles.Shipped.ArtInSection(sectionId).ToArray();

        rows.ShouldNotBeEmpty();
        rows.ShouldAllBe(a => a.Atlas == atlas);
    }

    /// <summary>Biome-scoped sections pack into their chapter's atlas (`15` §D2).</summary>
    [Theory]
    [InlineData("E3")]
    [InlineData("E4")]
    [InlineData("E5")]
    [InlineData("E9")]
    public void Biome_scoped_sections_pack_into_their_chapters_biome_atlas(string sectionId)
    {
        var rows = ManifestFiles.Shipped.ArtInSection(sectionId).ToArray();
        var byKey = ManifestFiles.Shipped.Art.Biomes.ToDictionary(b => b.Key, StringComparer.Ordinal);

        rows.ShouldNotBeEmpty();
        rows.ShouldAllBe(a => a.Atlas == $"atlas_biome_{byKey[a.Biome!].Chapter}");
    }

    /// <summary>
    /// 🔒 `15` §D2 assigns no atlas to backgrounds ("not atlased"), and names none for misc UI
    /// icons, store art or the shared tile icons. Those carry null rather than a guess — see the
    /// DSC_TILE_ATLAS and DSC_UNASSIGNED_ATLASES records.
    /// </summary>
    [Theory]
    [InlineData("E8")]
    [InlineData("E10")]
    [InlineData("E20")]
    [InlineData("E21")]
    public void Sections_15_section_D2_assigns_no_atlas_to_carry_null(string sectionId)
    {
        var rows = ManifestFiles.Shipped.ArtInSection(sectionId).ToArray();

        rows.ShouldNotBeEmpty();
        rows.ShouldAllBe(a => a.Atlas == null);
    }

    /// <summary>🔒 After the O8 ruling, `atlas_vfx` has members but none of them uncut.</summary>
    [Fact]
    public void Atlas_vfx_still_exists_in_section_D2_but_has_no_contents_left()
    {
        var vfx = ManifestFiles.Shipped.Art.Atlases.Single(a => a.Id == "atlas_vfx");

        vfx.AssetCount.ShouldBe(32);
        vfx.UncutAssetCount.ShouldBe(0);
        ManifestFiles.Shipped.AtlasMembers("atlas_vfx").ShouldBeEmpty();
    }

    /// <summary>Every other atlas still has contents — the rule above is not vacuously true of all.</summary>
    [Fact]
    public void Every_atlas_other_than_vfx_has_uncut_contents()
    {
        var populated = ManifestFiles.Shipped.Art.Atlases
            .Where(a => a.Id != "atlas_vfx")
            .ToArray();

        populated.Length.ShouldBe(8);
        populated.ShouldAllBe(a => a.UncutAssetCount > 0);
    }

    /// <summary>`15` §A5's eight biomes, their chapters and their locked base hue.</summary>
    [Theory]
    [InlineData(1, "greenwood", "Greenwood Vale", "#5FBF5F")]
    [InlineData(2, "mire", "Ashen Mire", "#6B5A8E")]
    [InlineData(3, "crypt", "Sunken Crypt", "#4A6E7A")]
    [InlineData(4, "ember", "Emberpeak", "#C4462A")]
    [InlineData(5, "frost", "Frostbound Reach", "#7EC8E8")]
    [InlineData(6, "clockwork", "Clockwork Vaults", "#C89A4A")]
    [InlineData(7, "bloom", "Bloom of Decay", "#D46BA8")]
    [InlineData(8, "astral", "Astral Spire", "#7A5AD8")]
    public void The_biome_palettes_match_15_section_A5(
        int chapter, string key, string displayName, string baseHue)
    {
        var biome = ManifestFiles.Shipped.Art.Biomes.Single(b => b.Chapter == chapter);

        biome.Key.ShouldBe(key);
        biome.DisplayName.ShouldBe(displayName);
        biome.Palette.Base.ShouldBe(baseHue);
        biome.Palette.Hues.Count.ShouldBe(6, "15 §A5 locks six hues per biome");
    }

    /// <summary>`15` §A5's rarity colours, identical across all biomes.</summary>
    [Theory]
    [InlineData("C", "#9AA5B1")]
    [InlineData("B", "#4CAF50")]
    [InlineData("A", "#3B82F6")]
    [InlineData("S", "#F5A623")]
    [InlineData("SS", "#C13BE8")]
    public void The_rarity_colours_match_15_section_A5(string code, string colour)
    {
        ManifestFiles.Shipped.Art.Rarities.Single(r => r.Code == code).Colour.ShouldBe(colour);
    }

    /// <summary>
    /// Subject descriptors are transcribed verbatim from the doc, including the ones `22` owns.
    /// </summary>
    [Theory]
    [InlineData("chr_boss_thornmaw_idle",
        "a colossal carnivorous flower with a fanged maw, thick thorned vines for arms, glowing yellow pollen, rooted in mossy stone")]
    [InlineData("pet_dicebeast_idle",
        "a small creature whose whole body is a glowing golden six-sided die, with tiny legs and big eyes")]
    [InlineData("icon_perk_singularity", "many small symbols spiralling into one bright point")]
    [InlineData("icon_talent_fortune_the_sixth_star",
        "a die's six face replaced by a radiant golden star")]
    [InlineData("tile_icon_dice_forge", "a golden die on a tiny anvil")]
    [InlineData("icon_cur_soulshard", "a glowing violet crystal shard")]
    public void Subject_descriptors_are_transcribed_verbatim(string id, string subject)
    {
        ManifestFiles.Shipped.RequireArt(id).Subject.ShouldBe(subject);
    }

    /// <summary>
    /// 🔒 `15` §E11 gives no descriptor for the boots, ring and amulet families — §E2's family
    /// table covers only the twelve weapon/helmet/armor ones. Those 60 rows keep subject:null
    /// rather than echoing the family name back as though it were a prompt.
    /// </summary>
    [Fact]
    public void Gear_families_the_docs_never_describe_carry_a_null_subject()
    {
        var gear = ManifestFiles.Shipped.ArtInSection("E11").ToArray();
        gear.Length.ShouldBe(120);

        var described = gear.Where(a => a.Subject is not null).ToArray();
        var undescribed = gear.Where(a => a.Subject is null).ToArray();

        described.Length.ShouldBe(60);
        described.Select(a => a.Extra["gearSlot"]).Distinct()
            .ShouldBe(["weapon", "helmet", "armor"], ignoreOrder: true);
        undescribed.Select(a => a.Extra["gearSlot"]).Distinct()
            .ShouldBe(["boots", "ring", "amulet"], ignoreOrder: true);
    }

    /// <summary>
    /// 🔒 §E20 names each misc icon but describes none. The name is not a descriptor, so subject
    /// stays null instead of echoing displayName — otherwise a prompt pipeline would generate art
    /// from the string "bug report".
    /// </summary>
    [Fact]
    public void Misc_ui_icons_carry_a_name_but_no_invented_subject()
    {
        var icons = ManifestFiles.Shipped.ArtInSection("E20").ToArray();

        icons.Length.ShouldBe(49);
        icons.ShouldAllBe(a => a.Subject == null);
        icons.ShouldAllBe(a => a.Extra.ContainsKey("displayName"));
    }

    /// <summary>Every id is unique — three later tasks key their records to it.</summary>
    [Fact]
    public void Every_asset_id_across_both_manifests_is_unique()
    {
        var ids = ManifestFiles.Shipped.AllIds;

        ids.Count.ShouldBe(974 + 106);
        ids.Distinct(StringComparer.Ordinal).Count().ShouldBe(ids.Count);
    }

    /// <summary>`15` §D1's own worked examples must all resolve against the register.</summary>
    [Theory]
    [InlineData("chr_hero_body_idle")]
    [InlineData("chr_hero_weapon_blade_s")]
    [InlineData("chr_enemy_frost_brute_attack")]
    [InlineData("chr_boss_rimehold_phase3")]
    [InlineData("pet_stormfang_idle")]
    [InlineData("mnt_starhoof_move")]
    [InlineData("tile_icon_treasure")]
    [InlineData("board_frost_path_curve_l")]
    [InlineData("gear_weapon_staff_ss")]
    [InlineData("icon_perk_executioner")]
    [InlineData("icon_talent_might_whetstone")]
    [InlineData("icon_status_burn")]
    [InlineData("ui_panel_main_9slice")]
    [InlineData("bg_frost_layer2")]
    public void Every_worked_example_in_15_section_D1_resolves_to_a_row(string id)
    {
        ManifestFiles.Shipped.RequireArt(id).Id.ShouldBe(id);
    }

    /// <summary>
    /// The one §D1 example that does NOT resolve, and why: `die_face_star_default.png` carries a
    /// `_default` variant suffix left over from the die-skin system D14 cut. §E16's ids have none.
    /// </summary>
    [Fact]
    public void The_die_skin_leftover_in_15_section_D1_is_recorded_rather_than_honoured()
    {
        ManifestFiles.Shipped.FindArt("die_face_star_default").ShouldBeNull();
        ManifestFiles.Shipped.FindArt("die_face_star").ShouldNotBeNull();

        ManifestFiles.Shipped.Art.Discrepancies
            .ShouldContain(d => d.Id == "DSC_DIE_VARIANT_SUFFIX");
    }

    /// <summary>An unknown id fails loudly and says how big the register is.</summary>
    [Fact]
    public void Requiring_an_unknown_asset_throws_and_names_the_id()
    {
        var thrown = Should.Throw<KeyNotFoundException>(
            () => ManifestFiles.Shipped.RequireArt("chr_hero_body_moonwalk"));

        thrown.Message.ShouldContain("chr_hero_body_moonwalk", Case.Sensitive);
        thrown.Message.ShouldContain("974", Case.Sensitive);
    }
}
