using FluentAssertions;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// Thirty single-edit mutations of the <b>real</b> <c>SlayIdleRepeat.Data</c>, each of which the
/// validator must reject.
/// </summary>
/// <remarks>
/// <para>
/// M0-10 ran a set like this as throwaways while authoring the data. Committing them is the point:
/// a validator whose negative cases were never committed is a validator nobody can trust after the
/// first refactor — every rule in it could have quietly stopped biting and every run would still
/// be green.
/// </para>
/// <para>
/// Each case is a single textual edit to one shipped file, so the mutation is legible next to the
/// data it corrupts. <see cref="RepoData.SourceWithEdit"/> throws when its anchor no longer occurs,
/// which turns "the data moved and this case silently stopped testing anything" into a failure.
/// </para>
/// <para>
/// 🔒 Four <b>positive controls</b> close the file. They assert that the shipped <c>null</c>s are
/// still accepted — because a validator that rejected them would be "fixed" by filling 98 holes
/// with plausible zeroes, which is the single worst outcome this pipeline can have.
/// </para>
/// </remarks>
public sealed class RealDataNegativeCaseTests
{
    private static IReadOnlyList<ContentIssue> After(string document, string find, string replaceWith) =>
        ContentLoader.Load(RepoData.SourceWithEdit(document, find, replaceWith)).Issues;

    // ═══════════════════════════════════════════════════════ 14 §6 · unknown IDs (1-6)

    [Fact]
    public void N01_a_rarity_outside_the_C_to_SS_ladder_is_rejected()
    {
        After("tuning/luck.json", "\"guaranteeRarityAtLeast\": \"SS\"", "\"guaranteeRarityAtLeast\": \"SSS\"")
            .Should().Contain(i => i.Code == ContentIssueCode.UnknownId);
    }

    [Fact]
    public void N02_a_currency_that_is_not_in_the_wallet_is_rejected()
    {
        After("tuning/currencies.json", "\"currency\": \"SOUL_SHARDS\"", "\"currency\": \"SOULSHARDS\"")
            .Should().Contain(i => i.Code == ContentIssueCode.UnknownId ||
                                   i.Code == ContentIssueCode.OrphanedReference);
    }

    [Fact]
    public void N03_a_dungeon_paying_a_currency_its_schema_does_not_allow_is_rejected()
    {
        After("tuning/dungeons.json", "\"currency\": \"CROWNS\"", "\"currency\": \"GOLD\"")
            .Should().Contain(i => i.Code == ContentIssueCode.UnknownId);
    }

    [Fact]
    public void N04_a_status_value_outside_the_three_the_schema_enumerates_is_rejected()
    {
        After("tuning/forge.json", "\"_status\": \"partial\"", "\"_status\": \"draft\"")
            .Should().Contain(i => i.Code == ContentIssueCode.UnknownId);
    }

    [Fact]
    public void N05_a_locale_key_that_breaks_the_key_convention_is_rejected()
    {
        After("loc/en.json", "\"loc.currency.gold.name\"", "\"Loc.Currency.Gold.Name\"")
            .Should().Contain(i => i.Code == ContentIssueCode.UnknownId);
    }

    [Fact]
    public void N06_a_property_no_schema_declares_is_rejected()
    {
        After("tuning/dungeons.json", "\"_status\": \"transcribed\",", "\"_status\": \"transcribed\", \"_note\": \"x\",")
            .Should().Contain(i => i.Code == ContentIssueCode.UnknownId);
    }

    // ═══════════════════════════════════════════════════════ 14 §6 · missing icons (7-10)

    [Fact]
    public void N07_a_displayName_that_no_locale_carries_is_rejected()
    {
        After("tuning/currencies.json", "\"loc.currency.gold.name\"", "\"loc.currency.gold.label\"")
            .Should().Contain(i => i.Code == ContentIssueCode.MissingIcon);
    }

    [Fact]
    public void N08_a_string_deleted_from_one_locale_only_is_rejected()
    {
        After("loc/de.json", "\"loc.currency.gold.name\"", "\"loc.currency.gold.title\"")
            .Should().Contain(i => i.Code == ContentIssueCode.MissingIcon);
    }

    [Fact]
    public void N09_an_affix_naming_a_string_that_does_not_exist_is_rejected()
    {
        After("tuning/drops.json", "\"loc.affix.crit_chance.name\"", "\"loc.affix.critchance.name\"")
            .Should().Contain(i => i.Code == ContentIssueCode.MissingIcon);
    }

    [Fact]
    public void N10_a_dungeon_naming_a_string_that_does_not_exist_is_rejected()
    {
        After("tuning/dungeons.json", "\"loc.dungeon.mint.name\"", "\"loc.dungeon.themint.name\"")
            .Should().Contain(i => i.Code == ContentIssueCode.MissingIcon);
    }

    // ═════════════════════════════════════════════════ 14 §6 · out-of-range values (11-17)

    [Fact]
    public void N11_a_percentage_above_its_maximum_is_rejected()
    {
        After("tuning/forge.json", "\"statBonusPerLevel\": 0.07", "\"statBonusPerLevel\": 1.5")
            .Should().Contain(i => i.Code == ContentIssueCode.OutOfRange);
    }

    [Fact]
    public void N12_an_uncapped_ad_reward_is_rejected_because_12_section_1_forbids_one()
    {
        After("tuning/ads.json", "\"cap\": 1,", "\"cap\": 0,")
            .Should().Contain(i => i.Code == ContentIssueCode.OutOfRange);
    }

    [Fact]
    public void N13_a_probability_above_one_is_rejected()
    {
        After("tuning/sim_profiles.json", "\"defaultWatchRate\": 0.5", "\"defaultWatchRate\": 1.2")
            .Should().Contain(i => i.Code == ContentIssueCode.OutOfRange);
    }

    [Fact]
    public void N14_an_enhance_stone_ladder_that_stops_ascending_is_rejected()
    {
        After("tuning/forge.json", "[2, 3, 4, 6, 8, 12,", "[2, 3, 4, 6, 8, 5,")
            .Should().Contain(i => i.Code == ContentIssueCode.OutOfRange);
    }

    [Fact]
    public void N15_an_expected_power_curve_that_goes_down_is_rejected()
    {
        After("tuning/expected_progression.json", "950000, 2400000", "450000, 2400000")
            .Should().Contain(i => i.Code == ContentIssueCode.OutOfRange);
    }

    [Fact]
    public void N16_a_soft_pity_that_sits_above_the_hard_pity_it_protects_is_rejected()
    {
        After("tuning/luck.json", "\"missThreshold\": 15", "\"missThreshold\": 30")
            .Should().Contain(i => i.Code == ContentIssueCode.OutOfRange);
    }

    [Fact]
    public void N17_a_global_ad_cap_that_is_not_the_sum_of_its_placements_is_rejected()
    {
        After("tuning/ads.json", "\"maxInRunImpressionsPerRun\": 17", "\"maxInRunImpressionsPerRun\": 18")
            .Should().Contain(i => i.Code == ContentIssueCode.OutOfRange);
    }

    // ═══════════════════════════════════════════════ 14 §6 · orphaned references (18-25)

    [Fact]
    public void N18_a_profile_name_no_behavioural_profile_declares_is_rejected()
    {
        After("tuning/expected_progression.json", "\"profile\": \"DungeonOnly\"", "\"profile\": \"DungeonsOnly\"")
            .Should().Contain(i => i.Code == ContentIssueCode.OrphanedReference);
    }

    [Fact]
    public void N19_a_tolerance_ladder_that_is_not_declared_is_rejected()
    {
        After("tuning/expected_progression.json", "\"toleranceLadder\": \"wide\"", "\"toleranceLadder\": \"widest\"")
            .Should().Contain(i => i.Code == ContentIssueCode.OrphanedReference);
    }

    [Fact]
    public void N20_an_ad_placement_that_is_not_in_the_catalogue_is_rejected()
    {
        After("tuning/dungeons.json", "\"AD_EXTRA_DUNGEON\"", "\"AD_EXTRA_DUNGEONS\"")
            .Should().Contain(i => i.Code == ContentIssueCode.OrphanedReference);
    }

    [Fact]
    public void N21_renaming_an_unlock_gate_orphans_everything_that_names_it()
    {
        After("tuning/progression.json", "\"DUNGEONS\":", "\"DUNGEON_ACCESS\":")
            .Should().Contain(i => i.Code == ContentIssueCode.OrphanedReference);
    }

    [Fact]
    public void N22_an_assertion_grading_a_profile_that_does_not_exist_is_rejected()
    {
        After("tuning/sim_thresholds.json", "\"profile\": \"NoAds_Core\"", "\"profile\": \"NoAds_Core2\"")
            .Should().Contain(i => i.Code == ContentIssueCode.OrphanedReference);
    }

    [Fact]
    public void N23_a_mirrored_forge_cost_that_stops_agreeing_with_luck_is_rejected()
    {
        After("tuning/forge.json", "\"C\": 100, \"B\": 300", "\"C\": 150, \"B\": 300")
            .Should().Contain(i => i.Code == ContentIssueCode.OrphanedReference);
    }

    [Fact]
    public void N24_an_ad_bundle_that_stops_being_worth_its_Crown_equivalence_is_rejected()
    {
        After("tuning/currencies.json", "\"crownsPerUnit\": 25", "\"crownsPerUnit\": 26")
            .Should().Contain(i => i.Code == ContentIssueCode.OrphanedReference);
    }

    [Fact]
    public void N25_a_dungeon_paying_a_currency_the_same_file_says_it_never_pays_is_rejected()
    {
        After("tuning/dungeons.json", "\"MERGE_DUST\",", "\"CROWNS\",")
            .Should().Contain(i => i.Code == ContentIssueCode.OrphanedReference);
    }

    // ══════════════════════════════════════════════════════ 14 §6 · duplicate IDs (26-29)

    [Fact]
    public void N26_the_same_wallet_currency_declared_twice_is_rejected()
    {
        After("tuning/currencies.json", "\"id\": \"ENERGY\"", "\"id\": \"CROWNS\"")
            .Should().Contain(i => i.Code == ContentIssueCode.DuplicateId);
    }

    [Fact]
    public void N27_the_same_ad_placement_id_in_both_arrays_is_rejected()
    {
        After("tuning/ads.json", "\"id\": \"AD_MERGE_DUST\"", "\"id\": \"AD_REVIVE\"")
            .Should().Contain(i => i.Code == ContentIssueCode.DuplicateId);
    }

    [Fact]
    public void N28_the_same_guild_quest_id_twice_is_rejected()
    {
        After("tuning/guilds.json", "\"id\": \"GQ_STARS\"", "\"id\": \"GQ_ENEMIES\"")
            .Should().Contain(i => i.Code == ContentIssueCode.DuplicateId);
    }

    [Fact]
    public void N29_a_duplicated_object_key_is_rejected_even_though_every_parser_would_swallow_it()
    {
        After("tuning/currencies.json", "\"crownsPerUnit\": 6,", "\"crownsPerUnit\": 6, \"crownsPerUnit\": 3,")
            .Should().Contain(i => i.Code == ContentIssueCode.DuplicateKey);
    }

    // ═══════════════════════════════════════════ 🔒 null is not a default — rejection (30)

    [Fact]
    public void N30_a_null_where_the_schema_does_not_permit_one_is_rejected()
    {
        After("tuning/forge.json", "\"statBonusPerLevel\": 0.07", "\"statBonusPerLevel\": null")
            .Should().Contain(i => i.Code == ContentIssueCode.SchemaViolation);
    }

    [Fact]
    public void A_null_ad_cap_is_rejected_because_ads_json_says_outright_that_a_null_cap_is_not_legal()
    {
        After("tuning/ads.json", "\"cap\": 4,", "\"cap\": null,")
            .Should().Contain(i => i.Code == ContentIssueCode.SchemaViolation);
    }

    // ═══════════════════════════ 🔒 positive controls — these nulls MUST still be accepted

    [Theory]
    [InlineData("tuning/power_model.json#/kPower")]
    [InlineData("tuning/forge.json#/enhance/perLevelSuccessRate")]
    [InlineData("tuning/luck.json#/chestApex/softPity")]
    [InlineData("tuning/drops.json#/slotCoefficients/4/primaryCoef")]
    public void A_shipped_unauthorised_hole_stays_unauthorised_and_is_never_filled_with_a_zero(string reference)
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.Read(reference).IsUnauthorised.Should().BeTrue(
            "SlayIdleRepeat.Data/README.md: null means the design docs do not authorise a value " +
            "here. A validator that rejected these would be 'fixed' by filling 98 holes with " +
            "plausible zeroes, which is the worst outcome this pipeline can have");
    }
}
