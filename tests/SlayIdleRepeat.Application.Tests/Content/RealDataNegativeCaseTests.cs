using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// Forty-three single-edit mutations of the <b>real</b> <c>game-data</c>, each of which
/// the validator must reject — at a named code <b>and a named pointer</b>.
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
/// still accepted — because a validator that rejected them would be "fixed" by filling 96 holes
/// with plausible zeroes, which is the single worst outcome this pipeline can have.
/// </para>
/// </remarks>
public sealed class RealDataNegativeCaseTests
{
    /// <summary>
    /// 🔒 Asserts the code <b>and the pointer</b>. Schema rules and cross-file rules are two
    /// independent producers of the same codes — <c>OutOfRange</c> alone comes from roughly twenty
    /// sites, and <see cref="ContentLoader"/> only runs <c>ContentInvariants</c> when schema
    /// validation is clean, so deleting the schema bound a case names hands the case straight to a
    /// different rule at a different pointer and it stays green. Two of these were already
    /// surviving that way: an uncapped ad reward and a 150%-per-level enhance bonus both shipped
    /// green once the bound their name cited was deleted.
    /// </summary>
    private static void Rejects(
        string document, string find, string replaceWith, ContentIssueCode code, string location)
    {
        var issues = ContentLoader.Load(RepoData.SourceWithEdit(document, find, replaceWith)).Issues;

        issues.ShouldContain(i => i.Code == code && i.Location == location,
            $"the rule this case names reports at {location}; another rule reporting {code} elsewhere "
            + "is a different rule, and would leave this one free to stop biting");
    }

    // ═══════════════════════════════════════════════════════ 14 §6 · unknown IDs (1-6)

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

    // ═══════════════════════════════════════════════════════ 14 §6 · missing icons (7-10)

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

    // ═════════════════════════════════════════════════ 14 §6 · out-of-range values (11-17)

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

    // ═══════════════════════════════════════════════ 14 §6 · orphaned references (18-25)

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

    // ══════════════════════════════════════════════════════ 14 §6 · duplicate IDs (26-29)

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

    // ═══════════════════════════════════════════ 🔒 null is not a default — rejection (30)

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
    /// 🔒 <b>R16, the priority.</b> It is the rule that keeps the null-means-unauthorised
    /// convention honest for <c>drops.json</c>: <c>08</c> §3.0a says a <em>null</em> slot
    /// coefficient means "read <c>percentStatsByRarity</c> instead", so the null is the mechanism,
    /// and the two tables are alternatives. Authorising a coefficient without removing its
    /// percent-stat row leaves a stat with two contradicting sources and no way to tell which the
    /// generator used. Nothing committed exercised it.
    /// </summary>
    [Fact]
    public void A_slot_coefficient_that_is_authorised_while_its_percent_stat_row_stays_is_rejected()
    {
        Rejects("tuning/drops.json",
            "\"primaryStat\": \"ASPD\", \"primaryCoef\": null",
            "\"primaryStat\": \"ASPD\", \"primaryCoef\": 0.5",
            ContentIssueCode.UnknownId, "tuning/drops.json#/percentStatsByRarity/ASPD");
    }

    /// <summary>R17 — <c>08</c> §6: the drop bands tile every chapter exactly once.</summary>
    [Fact]
    public void A_drop_band_that_overlaps_the_next_one_is_rejected()
    {
        Rejects("tuning/drops.json",
            "{ \"chapterFrom\": 3, \"chapterTo\": 4,", "{ \"chapterFrom\": 3, \"chapterTo\": 5,",
            ContentIssueCode.DuplicateId, "tuning/drops.json#/dropShareByChapterBand");
    }

    /// <summary>R24 — <c>08</c> §4.2: the enhance ladder's total is its per-level bonus times its length.</summary>
    [Fact]
    public void An_enhance_total_that_stops_agreeing_with_its_per_level_bonus_is_rejected()
    {
        Rejects("tuning/forge.json", "\"totalMultiplierAtMax\": 2.05", "\"totalMultiplierAtMax\": 2.06",
            ContentIssueCode.OutOfRange, "tuning/forge.json#/enhance/totalMultiplierAtMax");
    }

    /// <summary>R7 — <c>21</c> §5.4: the three ad-behaviour groups partition the catalogue exactly.</summary>
    [Fact]
    public void An_ad_placement_that_falls_out_of_every_behaviour_group_is_rejected()
    {
        Rejects("tuning/sim_profiles.json", "\"AD_DOUBLE_QUEST\",", "\"AD_DOUBLE_QUESTS\",",
            ContentIssueCode.OrphanedReference, "tuning/sim_profiles.json#/adPlacementGroups");
    }

    /// <summary>R13 — <c>29</c> §4: the par table is its own default fill.</summary>
    [Fact]
    public void A_par_power_cell_that_stops_matching_the_default_fill_is_rejected()
    {
        Rejects("tuning/par_power.json",
            "{ \"chapter\": 1, \"NORMAL\": 1000,", "{ \"chapter\": 1, \"NORMAL\": 1100,",
            ContentIssueCode.OutOfRange, "tuning/par_power.json#/parPower/0/NORMAL");
    }

    /// <summary>R28 — <c>10</c> §4.2: the daily shop draw has to be satisfiable.</summary>
    [Fact]
    public void A_daily_shop_draw_larger_than_its_pool_is_rejected()
    {
        Rejects("tuning/currencies.json", "\"rotatingOffersPerDay\": 6", "\"rotatingOffersPerDay\": 60",
            ContentIssueCode.OutOfRange, "tuning/currencies.json#/shop/dailyTab/rotatingOffersPerDay");
    }

    /// <summary>R29 — <c>27</c> §3.1: the guild quest pool has to be drawable.</summary>
    [Fact]
    public void A_guild_quest_draw_larger_than_its_pool_is_rejected()
    {
        Rejects("tuning/guilds.json", "\"perDay\": 3,", "\"perDay\": 30,",
            ContentIssueCode.OutOfRange, "tuning/guilds.json#/quests/perDay");
    }

    /// <summary><c>Derives</c> — <c>10</c> §4: the ladder and the capacity it reaches are one fact.</summary>
    [Fact]
    public void An_inventory_capacity_that_its_own_ladder_cannot_reach_is_rejected()
    {
        Rejects("tuning/forge.json",
            "\"maxCapacityReachableFromLadder\": 320", "\"maxCapacityReachableFromLadder\": 321",
            ContentIssueCode.OrphanedReference, "tuning/forge.json#/inventory/maxCapacityReachableFromLadder");
    }

    /// <summary><c>CountEquals</c> — <c>10</c> §4: one ladder price per purchase step.</summary>
    [Fact]
    public void A_ladder_with_more_prices_than_purchase_steps_is_rejected()
    {
        Rejects("tuning/currencies.json",
            "\"inventoryExpansionMaxPurchases\": 10", "\"inventoryExpansionMaxPurchases\": 9",
            ContentIssueCode.OutOfRange, "tuning/currencies.json#/crowns/inventoryExpansionLadder");
    }

    /// <summary><c>SharesSumTo</c> — <c>26</c> §3: the calendar's two archetype shares are a partition.</summary>
    [Fact]
    public void An_event_calendar_whose_two_shares_stop_summing_to_one_is_rejected()
    {
        Rejects("tuning/events.json", "\"chapterEventShare\": 0.75", "\"chapterEventShare\": 0.7",
            ContentIssueCode.OutOfRange, "tuning/events.json#/calendar/chapterEventShare");
    }

    /// <summary><c>KeysResolveIn</c> — <c>12</c> §4: the ad-placement catalogue is closed.</summary>
    [Fact]
    public void A_reward_value_keyed_by_a_placement_that_does_not_exist_is_rejected()
    {
        Rejects("tuning/ads.json",
            "\"AD_CAMPFIRE_HEAL\": { \"healPctMaxHp\": 0.3 }",
            "\"AD_CAMPFIRE_HEALL\": { \"healPctMaxHp\": 0.3 }",
            ContentIssueCode.OrphanedReference, "tuning/ads.json#/placementRewardValues/AD_CAMPFIRE_HEALL");
    }

    /// <summary><c>ItemsResolveIn</c> — <c>24</c> §5: Focus applies only to classified gear sources.</summary>
    [Fact]
    public void A_focus_class_that_luck_json_does_not_classify_is_rejected()
    {
        Rejects("tuning/luck.json", "\"CHEST_APEX\", \"DROP_RUN\"]", "\"CHEST_APEX\", \"DROP_RUNS\"]",
            ContentIssueCode.OrphanedReference, "tuning/luck.json#/focus/appliesToClasses");
    }

    /// <summary>
    /// <c>MirrorsFieldsOf</c> — <c>29</c> §2.2: the standard dummy is the reference opponent
    /// promoted to a live actor, so every field the model reads has to agree.
    /// </summary>
    [Fact]
    public void A_standard_dummy_that_stops_agreeing_with_the_reference_opponent_is_rejected()
    {
        Rejects("tuning/calibration_builds.json", "\"def\": 1500,", "\"def\": 1501,",
            ContentIssueCode.OrphanedReference, "tuning/power_model.json#/referenceOpponent/def");
    }

    // ═══════════════════════════ 🔒 positive controls — these nulls MUST still be accepted

    /// <summary>
    /// 🔒 Pins the <em>population</em> of unauthorised holes, not a sample of it.
    /// </summary>
    /// <remarks>
    /// ⚠️ The count was <b>96</b> at M0-09, not the 98 that brief states. Counted three ways — this
    /// loader over the snapshot, a JSON walk over <c>tuning/*.json</c>, and per file — the shipped
    /// data held 96 JSON nulls, all in <c>tuning/</c> and none in <c>loc/</c>: guilds 32,
    /// drops 25, power_model 13, events 7, progression 5, luck 4, sim_profiles 4, currencies 2,
    /// forge 2, beasts 1, calibration_builds 1. The brief was off by two; this records what is
    /// actually there, because a guarded number that does not match the data guards nothing.
    /// <para>
    /// ⚠️ <b>M2-11 opened the first two holes outside <c>tuning/</c>, deliberately, and they are the
    /// reason this number is now 98.</b> Both are in <c>content/enemies/enemies.json</c> and both are
    /// `05` §6 declining to authorise a value:
    /// </para>
    /// <list type="bullet">
    /// <item><c>onHit/casterBiomeStatus/4/maxStacks</c> — `05` §6.1a states a stack count for five
    /// of its eight <c>CASTER</c> rows and defers the rest to `05`'s status catalogue, which fixes
    /// <c>BLEED</c> as non-stacking and says <em>nothing at all</em> about <c>FREEZE</c>. Chapter 5
    /// is that row.</item>
    /// <item><c>elites/modifiers/6/curseId</c> — `05` §6.2 says <c>CURSED</c> <em>"applies a
    /// run-scoped curse"</em> and names none; <c>content/curses/</c> is empty.</item>
    /// </list>
    /// <para>
    /// Both have a <c>Require…</c> accessor in <c>Core</c> that throws by name if anything tries to
    /// use them, and both are asserted individually in <c>EnemiesDataTests</c>. Filling either is a
    /// design decision that changes this number in the same commit — which is what this guard is for.
    /// </para>
    /// <para>
    /// ⚠️ <b>M2-10 opened the third, and it is the reason this number is now 99.</b>
    /// <c>content/statuses.json#/statuses/8/decayCurve</c> — `05` §5 says <c>RAGE</c> is <em>"+X%
    /// ATK, <b>decays over D s</b>"</em> and states no curve, not linear, not stepped, not
    /// exponential; no boss script, perk row or on-hit row in the content set authors one either.
    /// A plausible linear ramp would be a balance decision invented by the implementer and invisible
    /// afterwards, so <c>RAGE</c> ships holding its full potency for its duration — the only shape
    /// `18` §6 can express — and the missing decay is greppable rather than absent. It is the only
    /// row in that file carrying the key, <c>StatusDefinition.RequireDecayCurve</c> throws by name if
    /// anything tries to use it, and the obligation expires by itself through
    /// <c>SubjectSetFloorTests.Pending</c>'s <c>StatusDecayCurve</c> entry.
    /// </para>
    /// <para>
    /// ⚠️ <b>M3-07 opened 176 holes, all in <c>content/perks/perks.json</c>, and they are the reason
    /// this number is now 275.</b> Every one is a canonical DSL token `18` names explicitly, not an
    /// undecided value: <b>149</b> are <c>condition: null</c> — `18` §4's own worked example writes
    /// it that way for "ungated", and §4 states no default, so authoring one here would be
    /// manufacturing a rule the DSL does not have. <b>21</b> are <c>valueScale.cap: null</c> — `18`
    /// §1.1 fixes <c>null</c> as "uncapped" (<c>PK_HOARD</c>'s own worked example). <b>6</b> are
    /// <c>trigger: null</c>, all on <c>PK_GAMBLER</c>'s two per-tier sibling effects — `18` §10.1's
    /// <c>RANDOM_OUTCOME</c> extension has the outcome table's own trigger fire the draw, and its
    /// winning row's effect fires only through that reference, never independently, so the sibling
    /// carries no trigger of its own (the same shape `18` §9.1 and §7.7 already establish for
    /// <c>CP_GLASS_HEART</c> and a pet's aura). None of the three needs a <c>Require…</c> accessor:
    /// unlike the four holes above, nothing in <c>Core</c> can ever legitimately ask "what was this
    /// undecided value" of a token the vocabulary itself defines as null.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_shipped_data_set_still_carries_exactly_its_275_unauthorised_holes()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        CountUnauthorised(snapshot).ShouldBe(275,
            "game-data/README.md: null means the design docs do not authorise a value " +
            "here. Sampling four pointers would leave 92 holes free to be filled with plausible " +
            "zeroes — the outcome this pipeline exists to prevent. Filling one is a design " +
            "decision, made deliberately, changing this number in the same commit.");
    }

    /// <summary>
    /// 🔒 The same population, <b>per file</b>. A total of 98 cannot see a <em>compensating</em>
    /// change — one hole filled in <c>guilds.json</c> and one opened in <c>drops.json</c> nets to
    /// zero, and the filled one is precisely the design decision this suite exists to make
    /// deliberate. The breakdown was already written down in the remark above; asserting it costs
    /// nothing and closes the hole in the guard.
    /// </summary>
    [Theory]
    [InlineData("tuning/guilds.json", 32)]
    [InlineData("tuning/drops.json", 25)]
    [InlineData("tuning/power_model.json", 13)]
    [InlineData("tuning/events.json", 7)]
    [InlineData("tuning/progression.json", 5)]
    [InlineData("tuning/luck.json", 4)]
    [InlineData("tuning/sim_profiles.json", 4)]
    [InlineData("tuning/currencies.json", 2)]
    [InlineData("tuning/forge.json", 2)]
    [InlineData("tuning/beasts.json", 1)]
    [InlineData("tuning/calibration_builds.json", 1)]
    [InlineData("tuning/ads.json", 0)]
    [InlineData("tuning/dungeons.json", 0)]
    [InlineData("tuning/expected_progression.json", 0)]
    [InlineData("tuning/par_power.json", 0)]
    [InlineData("tuning/sim_thresholds.json", 0)]
    [InlineData("loc/en.json", 0)]
    [InlineData("loc/de.json", 0)]

    // M2-11 — the first two holes outside tuning/. 05 §6.1a authorises no stack count for the
    // FREEZE row and 05 §6.2 names no curse for CURSED; see the remarks on the total above.
    [InlineData("content/enemies/enemies.json", 2)]
    [InlineData("content/combat_caps.json", 0)]

    // M2-10 — the third hole outside tuning/. 05 §5 says RAGE decays over D s and states no curve;
    // see the remarks on the total above.
    [InlineData("content/statuses.json", 1)]

    // M2-13 — the eight boss scripts and the FTUE row, and NO hole, which is worth a line rather
    // than a silence. Where 17 authorises nothing, the boss data omits the key instead of writing
    // null: a boss that does not summon carries no adds fraction, a mechanic that needs no wind-up
    // carries no telegraphSeconds, and the FTUE row alone carries the two fixed inputs. The two
    // holes 17 §7 and §8 really do leave — Piston Slam's DEF penetration and the sporeling-death
    // heal — are mechanics the DSL cannot express at all, so there is no key to write null INTO;
    // they are recorded in the affected scripts' own _doc.
    [InlineData("content/bosses/bosses.json", 0)]

    // M3-07 — 176 holes, all canonical DSL tokens `18` names explicitly (condition: null for
    // ungated, valueScale.cap: null for uncapped, trigger: null on RANDOM_OUTCOME's siblings); see
    // the remarks on the total above.
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
    [InlineData("tuning/forge.json#/enhance/perLevelSuccessRate")]
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
