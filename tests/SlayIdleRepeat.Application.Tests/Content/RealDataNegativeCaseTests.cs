using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// Forty-four single-edit mutations of the real game-data, each of which the validator must
/// reject at a named code and a named pointer — one of them at a named citation instead, because
/// its rule shares both with the rule beside it (see the inventory ceiling cases).
/// </summary>
/// <remarks>
/// Committing these negative cases is the point: a validator whose negative cases are never
/// committed is one nobody can trust after the first refactor. Each case is a single textual edit
/// to one shipped file; <see cref="RepoData.SourceWithEdit"/> throws if its anchor no longer
/// occurs, so a case can't silently stop testing anything. Four positive controls close the file,
/// asserting that the shipped nulls are still accepted.
/// </remarks>
public sealed class RealDataNegativeCaseTests
{
    /// <summary>
    /// Asserts the code and the pointer. OutOfRange alone comes from roughly twenty sites, and
    /// <see cref="ContentLoader"/> only runs ContentInvariants when schema validation is clean, so
    /// deleting the schema bound a case names would hand it to a different rule at a different
    /// pointer and stay green.
    /// </summary>
    private static void Rejects(
        string document, string find, string replaceWith, ContentIssueCode code, string location)
    {
        var issues = ContentLoader.Load(RepoData.SourceWithEdit(document, find, replaceWith)).Issues;

        issues.ShouldContain(i => i.Code == code && i.Location == location,
            $"the rule this case names reports at {location}; another rule reporting {code} elsewhere "
            + "is a different rule, and would leave this one free to stop biting");
    }

    // ═══════════════════════════════════════════════════════ unknown IDs (1-6)

    [Fact]
    public void a_rarity_outside_the_C_to_SS_ladder_is_rejected()
    {
        Rejects("tuning/luck.json",
            "{ \"everyNth\": 160, \"guaranteeRarityAtLeast\": \"SS\" }",
            "{ \"everyNth\": 160, \"guaranteeRarityAtLeast\": \"SSS\" }",
            ContentIssueCode.UnknownId,
            "tuning/luck.json#/chestStandard/hardPity/2/guaranteeRarityAtLeast");
    }

    [Fact]
    public void a_currency_that_is_not_in_the_wallet_is_rejected()
    {
        Rejects("tuning/currencies.json",
            "\"currency\": \"SOUL_SHARDS\", \"price\": 900",
            "\"currency\": \"SOULSHARDS\", \"price\": 900",
            ContentIssueCode.UnknownId,
            "tuning/currencies.json#/shop/dailyTab/staples/0/currency");
    }

    [Fact]
    public void a_dungeon_paying_a_currency_its_schema_does_not_allow_is_rejected()
    {
        Rejects("tuning/dungeons.json", "\"currency\": \"CROWNS\"", "\"currency\": \"GOLD\"",
            ContentIssueCode.UnknownId, "tuning/dungeons.json#/dungeons/2/currency");
    }

    [Fact]
    public void a_status_value_outside_the_three_the_schema_enumerates_is_rejected()
    {
        Rejects("tuning/forge.json", "\"_status\": \"partial\"", "\"_status\": \"draft\"",
            ContentIssueCode.UnknownId, "tuning/forge.json#/_status");
    }

    [Fact]
    public void a_locale_key_that_breaks_the_key_convention_is_rejected()
    {
        Rejects("loc/en.json", "\"loc.currency.gold.name\"", "\"Loc.Currency.Gold.Name\"",
            ContentIssueCode.UnknownId, "loc/en.json#/strings/Loc.Currency.Gold.Name");
    }

    [Fact]
    public void a_property_no_schema_declares_is_rejected()
    {
        Rejects("tuning/dungeons.json",
            "\"_status\": \"transcribed\",", "\"_status\": \"transcribed\", \"_note\": \"x\",",
            ContentIssueCode.UnknownId, "tuning/dungeons.json#/_note");
    }

    // ═══════════════════════════════════════════════════════ missing icons (7-10)

    [Fact]
    public void a_displayName_that_no_locale_carries_is_rejected()
    {
        Rejects("tuning/currencies.json", "\"loc.currency.gold.name\"", "\"loc.currency.gold.label\"",
            ContentIssueCode.MissingIcon, "tuning/currencies.json#/wallet/0/displayName");
    }

    [Fact]
    public void a_string_deleted_from_one_locale_only_is_rejected()
    {
        Rejects("loc/de.json", "\"loc.currency.gold.name\"", "\"loc.currency.gold.title\"",
            ContentIssueCode.MissingIcon, "tuning/currencies.json#/wallet/0/displayName");
    }

    [Fact]
    public void an_affix_naming_a_string_that_does_not_exist_is_rejected()
    {
        Rejects("tuning/drops.json", "\"loc.affix.crit_chance.name\"", "\"loc.affix.critchance.name\"",
            ContentIssueCode.MissingIcon, "tuning/drops.json#/affixPool/affixes/0/displayName");
    }

    [Fact]
    public void a_dungeon_naming_a_string_that_does_not_exist_is_rejected()
    {
        Rejects("tuning/dungeons.json", "\"loc.dungeon.mint.name\"", "\"loc.dungeon.themint.name\"",
            ContentIssueCode.MissingIcon, "tuning/dungeons.json#/dungeons/2/displayName");
    }

    // ═════════════════════════════════════════════════ out-of-range values (11-17)

    [Fact]
    public void a_percentage_above_its_maximum_is_rejected()
    {
        Rejects("tuning/forge.json", "\"statBonusPerLevel\": 0.07", "\"statBonusPerLevel\": 1.5",
            ContentIssueCode.OutOfRange, "tuning/forge.json#/enhance/statBonusPerLevel");
    }

    [Fact]
    public void an_uncapped_ad_reward_is_rejected_because_12_section_1_forbids_one()
    {
        Rejects("tuning/ads.json",
            "\"id\": \"AD_REVIVE\", \"cap\": 1,",
            "\"id\": \"AD_REVIVE\", \"cap\": 0,",
            ContentIssueCode.OutOfRange, "tuning/ads.json#/inRunPlacements/0/cap");
    }

    [Fact]
    public void a_probability_above_one_is_rejected()
    {
        Rejects("tuning/sim_profiles.json", "\"defaultWatchRate\": 0.3", "\"defaultWatchRate\": 1.2",
            ContentIssueCode.OutOfRange, "tuning/sim_profiles.json#/profiles/5/ads/defaultWatchRate");
    }

    [Fact]
    public void an_enhance_stone_ladder_that_stops_ascending_is_rejected()
    {
        Rejects("tuning/forge.json", "[2, 3, 4, 6, 8, 12,", "[2, 3, 4, 6, 8, 5,",
            ContentIssueCode.OutOfRange, "tuning/forge.json#/enhance/stoneCostPerLevel/5");
    }

    [Fact]
    public void an_expected_power_curve_that_goes_down_is_rejected()
    {
        Rejects("tuning/expected_progression.json", "460000, 800000, 1900000", "460000, 400000, 1900000",
            ContentIssueCode.OutOfRange,
            "tuning/expected_progression.json#/profiles/2/expectedPower/6");
    }

    [Fact]
    public void a_soft_pity_that_sits_above_the_hard_pity_it_protects_is_rejected()
    {
        Rejects("tuning/luck.json", "\"missThreshold\": 15", "\"missThreshold\": 30",
            ContentIssueCode.OutOfRange, "tuning/luck.json#/chestPremium/softPity/missThreshold");
    }

    [Fact]
    public void a_global_ad_cap_that_is_not_the_sum_of_its_placements_is_rejected()
    {
        Rejects("tuning/ads.json", "\"maxInRunImpressionsPerRun\": 17", "\"maxInRunImpressionsPerRun\": 18",
            ContentIssueCode.OutOfRange, "tuning/ads.json#/globalCaps/maxInRunImpressionsPerRun");
    }

    // ═══════════════════════════════════════════════ orphaned references (18-25)

    [Fact]
    public void a_profile_name_no_behavioural_profile_declares_is_rejected()
    {
        Rejects("tuning/expected_progression.json",
            "\"profile\": \"DungeonOnly\"", "\"profile\": \"DungeonsOnly\"",
            ContentIssueCode.OrphanedReference, "tuning/expected_progression.json#/profiles");
    }

    [Fact]
    public void a_tolerance_ladder_that_is_not_declared_is_rejected()
    {
        Rejects("tuning/expected_progression.json",
            "\"toleranceLadder\": \"plusLapsed\"", "\"toleranceLadder\": \"plusLapsedd\"",
            ContentIssueCode.OrphanedReference,
            "tuning/expected_progression.json#/profiles/2/toleranceLadder");
    }

    [Fact]
    public void an_ad_placement_that_is_not_in_the_catalogue_is_rejected()
    {
        Rejects("tuning/dungeons.json", "\"AD_EXTRA_DUNGEON\"", "\"AD_EXTRA_DUNGEONS\"",
            ContentIssueCode.OrphanedReference, "tuning/dungeons.json#/entries/adPlacementId");
    }

    [Fact]
    public void renaming_an_unlock_gate_orphans_everything_that_names_it()
    {
        Rejects("tuning/progression.json", "\"DUNGEONS\":", "\"DUNGEON_ACCESS\":",
            ContentIssueCode.OrphanedReference, "tuning/guilds.json#/quests/pool/5/gatedOn");
    }

    [Fact]
    public void an_assertion_grading_a_profile_that_does_not_exist_is_rejected()
    {
        Rejects("tuning/sim_thresholds.json",
            "\"profile\": \"NoAds_Core\", \"maxEnergyBlockedDayShare\"",
            "\"profile\": \"NoAds_Core2\", \"maxEnergyBlockedDayShare\"",
            ContentIssueCode.OrphanedReference, "tuning/sim_thresholds.json#/coreEconomy/A7/profile");
    }

    [Fact]
    public void a_mirrored_forge_cost_that_stops_agreeing_with_luck_is_rejected()
    {
        Rejects("tuning/forge.json", "\"C\": 100, \"B\": 300", "\"C\": 150, \"B\": 300",
            ContentIssueCode.OrphanedReference, "tuning/forge.json#/reforge/costByRarity/C");
    }

    [Fact]
    public void an_ad_bundle_that_stops_being_worth_its_Crown_equivalence_is_rejected()
    {
        Rejects("tuning/currencies.json", "\"crownsPerUnit\": 25", "\"crownsPerUnit\": 26",
            ContentIssueCode.OrphanedReference,
            "tuning/ads.json#/rewardScaling/baseValue/AD_ENHANCE_STONES");
    }

    [Fact]
    public void a_dungeon_paying_a_currency_the_same_file_says_it_never_pays_is_rejected()
    {
        Rejects("tuning/dungeons.json", "\"MERGE_DUST\",", "\"CROWNS\",",
            ContentIssueCode.OrphanedReference, "tuning/dungeons.json#/dungeons/2/currency");
    }

    // ══════════════════════════════════════════════════════ duplicate IDs (26-29)

    [Fact]
    public void the_same_wallet_currency_declared_twice_is_rejected()
    {
        Rejects("tuning/currencies.json", "\"id\": \"ENERGY\"", "\"id\": \"CROWNS\"",
            ContentIssueCode.DuplicateId, "tuning/currencies.json#/wallet");
    }

    [Fact]
    public void the_same_ad_placement_id_in_both_arrays_is_rejected()
    {
        Rejects("tuning/ads.json", "\"id\": \"AD_MERGE_DUST\"", "\"id\": \"AD_REVIVE\"",
            ContentIssueCode.DuplicateId,
            "tuning/ads.json#/inRunPlacements/0/id, tuning/ads.json#/metaPlacements/7/id");
    }

    [Fact]
    public void the_same_guild_quest_id_twice_is_rejected()
    {
        Rejects("tuning/guilds.json", "\"id\": \"GQ_STARS\"", "\"id\": \"GQ_ENEMIES\"",
            ContentIssueCode.DuplicateId,
            "tuning/guilds.json#/quests/pool/0/id, tuning/guilds.json#/quests/pool/14/id");
    }

    [Fact]
    public void a_duplicated_object_key_is_rejected_even_though_every_parser_would_swallow_it()
    {
        Rejects("tuning/currencies.json",
            "\"crownsPerUnit\": 6,", "\"crownsPerUnit\": 6, \"crownsPerUnit\": 3,",
            ContentIssueCode.DuplicateKey,
            "tuning/currencies.json#/shop/materialsTab/MERGE_DUST/crownsPerUnit");
    }

    // ═══════════════════════════════════════════ null is not a default — rejection (30)

    [Fact]
    public void a_null_where_the_schema_does_not_permit_one_is_rejected()
    {
        Rejects("tuning/forge.json", "\"statBonusPerLevel\": 0.07", "\"statBonusPerLevel\": null",
            ContentIssueCode.SchemaViolation, "tuning/forge.json#/enhance/statBonusPerLevel");
    }

    [Fact]
    public void A_null_ad_cap_is_rejected_because_ads_json_says_outright_that_a_null_cap_is_not_legal()
    {
        Rejects("tuning/ads.json", "\"cap\": 4,", "\"cap\": null,",
            ContentIssueCode.SchemaViolation, "tuning/ads.json#/metaPlacements/0/cap");
    }

    // ═══════════════════ DeclaredRules shapes that no committed case reached before (31-43)

    /// <summary>
    /// R16: a null slot coefficient means "read percentStatsByRarity instead" — the null is the
    /// mechanism, and the two tables are alternatives. Authorising a coefficient without removing
    /// its percent-stat row leaves two contradicting sources with no way to tell which was used.
    /// </summary>
    [Fact]
    public void A_slot_coefficient_that_is_authorised_while_its_percent_stat_row_stays_is_rejected()
    {
        Rejects("tuning/drops.json",
            "\"primaryStat\": \"ASPD\", \"primaryCoef\": null",
            "\"primaryStat\": \"ASPD\", \"primaryCoef\": 0.5",
            ContentIssueCode.UnknownId, "tuning/drops.json#/percentStatsByRarity/ASPD");
    }

    /// <summary>R17: the drop bands tile every chapter exactly once.</summary>
    [Fact]
    public void A_drop_band_that_overlaps_the_next_one_is_rejected()
    {
        Rejects("tuning/drops.json",
            "{ \"chapterFrom\": 3, \"chapterTo\": 4,", "{ \"chapterFrom\": 3, \"chapterTo\": 5,",
            ContentIssueCode.DuplicateId, "tuning/drops.json#/dropShareByChapterBand");
    }

    /// <summary>R24: the enhance ladder's total is its per-level bonus times its length.</summary>
    [Fact]
    public void An_enhance_total_that_stops_agreeing_with_its_per_level_bonus_is_rejected()
    {
        Rejects("tuning/forge.json", "\"totalMultiplierAtMax\": 2.05", "\"totalMultiplierAtMax\": 2.06",
            ContentIssueCode.OutOfRange, "tuning/forge.json#/enhance/totalMultiplierAtMax");
    }

    /// <summary>
    /// R24's sibling: the per-level success ladder is the band endpoints spread evenly, so the two
    /// separately authored statements of one ramp may not drift.
    /// </summary>
    /// <remarks>
    /// 🔒 The mutation is the tidy-looking one a transcriber would actually make — 0.4375 rounded
    /// to 0.44 — rather than an obviously absurd figure, because a rule that only caught nonsense
    /// would leave the real failure mode (a hand-rounded ladder that reads as a deliberate re-tune)
    /// green.
    /// </remarks>
    [Fact]
    public void A_success_ladder_rung_that_stops_matching_its_bands_ramp_is_rejected()
    {
        Rejects("tuning/forge.json", "0.5, 0.4375, 0.375", "0.5, 0.44, 0.375",
            ContentIssueCode.OutOfRange, "tuning/forge.json#/enhance/perLevelSuccessRate/11");
    }

    /// <summary>
    /// The same rule at the OTHER end of a band: an endpoint that stops matching the rung it
    /// authors. The interior mutation above would pass for a rule that only checked endpoints, and
    /// this one would pass for a rule that only checked interiors.
    /// </summary>
    [Fact]
    public void A_success_ladder_endpoint_that_stops_matching_its_band_is_rejected()
    {
        Rejects("tuning/forge.json", "0.85, 0.8, 0.75", "0.86, 0.8, 0.75",
            ContentIssueCode.OutOfRange, "tuning/forge.json#/enhance/perLevelSuccessRate/5");
    }

    /// <summary>R7: the three ad-behaviour groups partition the catalogue exactly.</summary>
    [Fact]
    public void An_ad_placement_that_falls_out_of_every_behaviour_group_is_rejected()
    {
        Rejects("tuning/sim_profiles.json", "\"AD_DOUBLE_QUEST\",", "\"AD_DOUBLE_QUESTS\",",
            ContentIssueCode.OrphanedReference, "tuning/sim_profiles.json#/adPlacementGroups");
    }

    /// <summary>R13: the par table is its own default fill.</summary>
    [Fact]
    public void A_par_power_cell_that_stops_matching_the_default_fill_is_rejected()
    {
        Rejects("tuning/par_power.json",
            "{ \"chapter\": 1, \"NORMAL\": 1000,", "{ \"chapter\": 1, \"NORMAL\": 1100,",
            ContentIssueCode.OutOfRange, "tuning/par_power.json#/parPower/0/NORMAL");
    }

    /// <summary>R28: the daily shop draw has to be satisfiable.</summary>
    [Fact]
    public void A_daily_shop_draw_larger_than_its_pool_is_rejected()
    {
        Rejects("tuning/currencies.json", "\"rotatingOffersPerDay\": 6", "\"rotatingOffersPerDay\": 60",
            ContentIssueCode.OutOfRange, "tuning/currencies.json#/shop/dailyTab/rotatingOffersPerDay");
    }

    /// <summary>R29: the guild quest pool has to be drawable.</summary>
    [Fact]
    public void A_guild_quest_draw_larger_than_its_pool_is_rejected()
    {
        Rejects("tuning/guilds.json", "\"perDay\": 3,", "\"perDay\": 30,",
            ContentIssueCode.OutOfRange, "tuning/guilds.json#/quests/perDay");
    }

    /// <summary>
    /// StaysBelow: the deferred expansion ladder stays out of reach of the flat ceiling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 1200 is the exact reach of the shipped ladder (<c>1000 + 10 × 20</c>) — the state the
    /// pre-ruling documents were in and the state the old <c>Derives</c> rule <em>demanded</em>. The
    /// M4 retro ruling of 2026-08-17 refuses it: a ceiling on the ladder's reach makes the last
    /// purchase buyable, and no command sells one.
    /// </para>
    /// <para>
    /// 🔴 <b>Asserted by CITATION, not by code and pointer, and that is not decoration.</b> The
    /// flatness rule beside it reports the same code at the same pointer for this edit, because
    /// 1200 is also not the base — with positive purchase counts and step sizes, every ceiling the
    /// ladder can reach is also a ceiling that is not the base, so the ladder rule's trigger set is
    /// a strict SUBSET of the flatness rule's and no data edit can separate them. What it adds is
    /// the diagnosis, so the diagnosis is what this pins: written as code-and-pointer, weakening the
    /// ladder rule to <c>&gt;</c> left this case green, which is how the subsumption was found.
    /// </para>
    /// </remarks>
    [Fact]
    public void An_inventory_ceiling_that_the_deferred_ladder_reaches_is_rejected()
    {
        var issues = ContentLoader
            .Load(RepoData.SourceWithEdit(
                "tuning/forge.json", "\"maxCapacity\": 1000", "\"maxCapacity\": 1200"))
            .Issues;

        issues.ShouldContain(
            issue => issue.Code == ContentIssueCode.OrphanedReference &&
                     issue.Location == "tuning/forge.json#/inventory/maxCapacity" &&
                     issue.Message.Contains("deferred ladder", StringComparison.Ordinal),
            "the LADDER rule must be one of the rules that fires on a ceiling standing exactly on " +
            "the ladder's reach. The flatness rule reports the same code at the same pointer for " +
            "this edit, so a case stated only over those two would pass with the ladder rule " +
            "switched off entirely — and the reader of the failure would be told the ceiling is not " +
            "the base rather than that somebody just made an expansion buyable.");
    }

    /// <summary>Mirrors: capacity is flat, so the ceiling is the base.</summary>
    /// <remarks>
    /// 1100 is BELOW the ladder's 1200 reach on purpose: the rule above is satisfied by it, so only
    /// the flatness rule can be firing here. That asymmetry is the whole relationship between the two
    /// — flatness catches everything the ladder rule catches and more, and the ladder rule exists for
    /// the diagnosis and for the day capacity stops being flat.
    /// </remarks>
    [Fact]
    public void An_inventory_ceiling_that_is_not_the_base_is_rejected()
    {
        Rejects("tuning/forge.json",
            "\"maxCapacity\": 1000", "\"maxCapacity\": 1100",
            ContentIssueCode.OrphanedReference, "tuning/forge.json#/inventory/maxCapacity");
    }

    /// <summary>CountEquals: one ladder price per purchase step.</summary>
    [Fact]
    public void A_ladder_with_more_prices_than_purchase_steps_is_rejected()
    {
        Rejects("tuning/currencies.json",
            "\"inventoryExpansionMaxPurchases\": 10", "\"inventoryExpansionMaxPurchases\": 9",
            ContentIssueCode.OutOfRange, "tuning/currencies.json#/crowns/inventoryExpansionLadder");
    }

    /// <summary>SharesSumTo: the calendar's two archetype shares are a partition.</summary>
    [Fact]
    public void An_event_calendar_whose_two_shares_stop_summing_to_one_is_rejected()
    {
        Rejects("tuning/events.json", "\"chapterEventShare\": 0.75", "\"chapterEventShare\": 0.7",
            ContentIssueCode.OutOfRange, "tuning/events.json#/calendar/chapterEventShare");
    }

    /// <summary>KeysResolveIn: the ad-placement catalogue is closed.</summary>
    [Fact]
    public void A_reward_value_keyed_by_a_placement_that_does_not_exist_is_rejected()
    {
        Rejects("tuning/ads.json",
            "\"AD_CAMPFIRE_HEAL\": { \"healPctMaxHp\": 0.3 }",
            "\"AD_CAMPFIRE_HEALL\": { \"healPctMaxHp\": 0.3 }",
            ContentIssueCode.OrphanedReference, "tuning/ads.json#/placementRewardValues/AD_CAMPFIRE_HEALL");
    }

    /// <summary>ItemsResolveIn: Focus applies only to classified gear sources.</summary>
    [Fact]
    public void A_focus_class_that_luck_json_does_not_classify_is_rejected()
    {
        Rejects("tuning/luck.json", "\"CHEST_APEX\", \"DROP_RUN\"]", "\"CHEST_APEX\", \"DROP_RUNS\"]",
            ContentIssueCode.OrphanedReference, "tuning/luck.json#/focus/appliesToClasses");
    }

    /// <summary>MirrorsFieldsOf: the standard dummy is the reference opponent promoted to a live actor, so every field the model reads has to agree.</summary>
    [Fact]
    public void A_standard_dummy_that_stops_agreeing_with_the_reference_opponent_is_rejected()
    {
        Rejects("tuning/calibration_builds.json", "\"def\": 1500,", "\"def\": 1501,",
            ContentIssueCode.OrphanedReference, "tuning/power_model.json#/referenceOpponent/def");
    }

    // ═══════════════════════════ positive controls — these nulls MUST still be accepted

    /// <summary>Pins the population of unauthorised holes, not a sample of it.</summary>
    /// <remarks>
    /// Verified three independent ways (this loader over the snapshot, a JSON walk over
    /// <c>tuning/*.json</c>, and per file). Most holes are deferred design decisions with a
    /// <c>Require…</c> accessor in <c>Core</c> that throws by name if anything tries to use them —
    /// filling one is a design decision that must change this number in the same commit. The
    /// perks.json holes are different: they are canonical DSL tokens the effect vocabulary defines
    /// as null on purpose (<c>condition: null</c> for "ungated", <c>valueScale.cap: null</c> for
    /// "uncapped", <c>trigger: null</c> for an outcome-table sibling effect with no trigger of its
    /// own), so nothing can ever legitimately ask what their "undecided value" was. See the
    /// per-file breakdown in the theory below.
    /// </remarks>
    [Fact]
    public void The_shipped_data_set_still_carries_exactly_its_278_unauthorised_holes()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        CountUnauthorised(snapshot).ShouldBe(278,
            "game-data/README.md: null means the design docs do not authorise a value " +
            "here. Sampling four pointers would leave 92 holes free to be filled with plausible " +
            "zeroes — the outcome this pipeline exists to prevent. Filling one is a design " +
            "decision, made deliberately, changing this number in the same commit.");
    }

    /// <summary>
    /// The same population, per file. A single total can't see a compensating change — one hole
    /// filled in <c>guilds.json</c> and one opened in <c>drops.json</c> nets to zero, hiding the
    /// design decision this suite exists to make deliberate.
    /// </summary>
    [Theory]
    [InlineData("tuning/guilds.json", 32)]

    // drops.json: 25 until M4-03, which filled the eleven affix ids the design docs had left
    // unnamed. That was an IDENTIFIER decision under a kickoff ruling — the three authored ids fix
    // an AFX_<STAT> convention and the other eleven follow it — not a number invented for a hole,
    // and the schema now requires the id rather than allowing null so a row cannot lose one again.
    // Every affix RANGE remains exactly as authored.
    //
    // 14 until M4-16, which OPENED two: every affix row now names the stat it writes and the bucket
    // it writes through, and thirteen of the fourteen resolve to a stat that exists. The fourteenth
    // is the damage-vs-Elites affix, which is conditional damage — the stat block has no conditional
    // bucket, and a target-gated standing effect throws during re-aggregation rather than reading
    // false — so both of its keys are null and neither is required to be. Opening a hole is the same
    // deliberate act as filling one and moves this number the same way.
    [InlineData("tuning/drops.json", 16)]
    [InlineData("tuning/power_model.json", 13)]
    [InlineData("tuning/events.json", 7)]
    [InlineData("tuning/progression.json", 5)]
    [InlineData("tuning/luck.json", 4)]
    [InlineData("tuning/sim_profiles.json", 4)]
    [InlineData("tuning/currencies.json", 2)]
    // forge.json: 2 until M4-04, which discharged enhance/perLevelSuccessRate. The interpolation
    // between the two published band endpoints was the document's own ramp notation rather than an
    // undecided number, so the ladder is a function of values already authored — a READING
    // decision, not a number invented for a hole. The one left is merge/dustSubstituteCost/SS,
    // which is an authored n/a: nothing merges out of the top rung, so there is no substitution to
    // price and never will be.
    [InlineData("tuning/forge.json", 1)]
    [InlineData("tuning/beasts.json", 1)]
    [InlineData("tuning/calibration_builds.json", 1)]
    [InlineData("tuning/ads.json", 0)]
    [InlineData("tuning/dungeons.json", 0)]
    [InlineData("tuning/expected_progression.json", 0)]
    [InlineData("tuning/par_power.json", 0)]
    [InlineData("tuning/sim_thresholds.json", 0)]
    [InlineData("loc/en.json", 0)]
    [InlineData("loc/de.json", 0)]

    // enemies.json: no stack count authorised for the FREEZE row, and CURSED names no curse
    // (content/curses/ is empty). Both have a Require… accessor that throws by name if used.
    [InlineData("content/enemies/enemies.json", 2)]
    [InlineData("content/combat_caps.json", 0)]

    // statuses.json: RAGE's decay curve is deliberately unauthored (no shape is stated), so it
    // ships holding full potency for its duration rather than an invented ramp.
    [InlineData("content/statuses.json", 1)]

    // Chapter 1's unlockCondition is null because chapter.schema.json says outright it has no
    // prerequisite — the one hole that is not a deferred design decision.
    [InlineData("content/chapters/CH_01_GREENWOOD_VALE.json", 1)]
    [InlineData("content/chapters/CH_02_ASHEN_MIRE.json", 0)]
    [InlineData("content/curses/curses.json", 0)]

    // gear.json: the 24 base items are a complete transcription of 08 §1's slot/family grid, so
    // there is nothing in it the design docs leave unauthorised. Listed at zero rather than omitted,
    // because a file with no row here is a file this theory does not watch at all.
    [InlineData("content/gear/gear.json", 0)]

    // sets.json: new in M4-16, and its twelve holes are the two kinds this file's header
    // distinguishes. SEVEN are deferred design decisions — of the four sets' twelve breakpoints,
    // five are a standing stat modifier or a heal on a kill and are authored in full, while the
    // other seven each need something that does not exist: pets (three of them), a conditional
    // damage bucket, the Star die face (two), or a magnitude the design set never wrote down. Each
    // of those carries its owner and its reason in GearAuthoringGapRegisterTests, whose second arm
    // fails when that owner ships. The remaining FIVE are perks.json's kind: `condition: null` is
    // the effect vocabulary's canonical "ungated", one per authored effect, and nothing can ever
    // legitimately ask what its undecided value was.
    [InlineData("content/sets/sets.json", 12)]

    // Bosses carry zero holes: where the design authorises nothing, the boss data omits the key
    // instead of writing null (a boss with no summons carries no adds fraction, and so on). The
    // two mechanics the DSL genuinely cannot express have no key to hold a null and are recorded
    // in the affected scripts' own _doc instead.
    [InlineData("content/bosses/bosses.json", 0)]

    // perks.json: 176 holes, all canonical DSL tokens the effect vocabulary defines as null on
    // purpose (condition: null for ungated, valueScale.cap: null for uncapped, trigger: null on
    // RANDOM_OUTCOME's sibling effects).
    [InlineData("content/perks/perks.json", 176)]
    public void Each_shipped_file_carries_exactly_the_unauthorised_holes_it_is_recorded_as_carrying(
        string documentPath, int expected)
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        Count(snapshot.GetDocument(documentPath).Root).ShouldBe(expected);
    }

    private static int CountUnauthorised(Core.Content.ContentSnapshot snapshot) =>
        snapshot.DocumentPaths.Sum(path => Count(snapshot.GetDocument(path).Root));

    private static int Count(Core.Content.ContentValue value) => value.Kind switch
    {
        Core.Content.ContentValueKind.Unauthorised => 1,
        Core.Content.ContentValueKind.Array => value.Items.Sum(Count),
        Core.Content.ContentValueKind.Object => value.MemberNames.Sum(name =>
        {
            value.TryGetMember(name, out var member);
            return Count(member!);
        }),
        _ => 0,
    };

    [Theory]
    [InlineData("tuning/power_model.json#/kPower")]
    [InlineData("tuning/forge.json#/merge/dustSubstituteCost/SS")]
    [InlineData("tuning/luck.json#/chestApex/softPity")]
    [InlineData("tuning/drops.json#/slotCoefficients/4/primaryCoef")]
    public void A_shipped_unauthorised_hole_stays_unauthorised_and_is_never_filled_with_a_zero(string reference)
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.Read(reference).IsUnauthorised.ShouldBeTrue(
            "game-data/README.md: null means the design docs do not authorise a value " +
            "here. A validator that rejected these would be 'fixed' by filling 96 holes with " +
            "plausible zeroes, which is the worst outcome this pipeline can have");
    }
}
