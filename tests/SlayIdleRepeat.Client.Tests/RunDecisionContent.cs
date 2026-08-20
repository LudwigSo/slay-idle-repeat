using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The three run-decision screens' string keys, and the content sets their cases run against.
/// </summary>
/// <remarks>
/// <para>
/// One holder for the Perk Draft, the run Shop and the Campfire / Shrine, because they are one
/// screen group reached from one tile each and composed against one string catalogue. Splitting
/// them would put three readers of the shipped locale in three files.
/// </para>
/// <para>
/// Fixture values are the key with a marker in front, so every string is unique, obviously not the
/// shipped copy, and impossible for a hard-coded literal to match by accident.
/// </para>
/// </remarks>
internal static class RunDecisionContent
{
    // ---- Perk Draft ---------------------------------------------------------------------------

    internal const string DraftTitleNameKey = "loc.perk_draft.title.name";
    internal const string DraftAdFourthOptionNameKey = "loc.perk_draft.ad_fourth_option.name";
    internal const string DraftSynergyLabelKey = "loc.perk_draft.synergy.label";
    internal const string DraftRerollCostLabelKey = "loc.perk_draft.reroll_cost.label";
    internal const string DraftSkipRewardLabelKey = "loc.perk_draft.skip_reward.label";
    internal const string DraftLegendaryPityLabelKey = "loc.perk_draft.legendary_pity.label";
    internal const string DraftQualityFloorLabelKey = "loc.perk_draft.quality_floor.label";
    internal const string DraftUpgradeFamineLabelKey = "loc.perk_draft.upgrade_famine.label";
    internal const string DraftUpgradeBadgeKey = "loc.perk_draft.upgrade.badge";
    internal const string DraftRerollActionKey = "loc.perk_draft.reroll.action";
    internal const string DraftAdRerollActionKey = "loc.perk_draft.ad_reroll.action";
    internal const string DraftSkipActionKey = "loc.perk_draft.skip.action";
    internal const string DraftAdRerollBlockKey = "loc.perk_draft.ad_reroll_deferred.block";
    internal const string DraftAdFourthOptionBlockKey = "loc.perk_draft.ad_fourth_option_deferred.block";
    internal const string DraftFreeRerollBlockKey = "loc.perk_draft.free_reroll_unbuilt.block";
    internal const string DraftGuaranteeNotDueBlockKey = "loc.perk_draft.guarantee_not_due.block";
    internal const string DraftLoadingStatusKey = "loc.perk_draft.loading.status";
    internal const string DraftNoDraftStatusKey = "loc.perk_draft.no_draft.status";
    internal const string DraftRunMissingStatusKey = "loc.perk_draft.run_missing.status";
    internal const string DraftReadUnavailableStatusKey = "loc.perk_draft.read_unavailable.status";
    internal const string DraftCardsUnavailableStatusKey = "loc.perk_draft.cards_unavailable.status";
    internal const string DraftEffectNumbersUnavailableStatusKey =
        "loc.perk_draft.effect_numbers_unavailable.status";

    internal const string DraftRefusedStatusKey = "loc.perk_draft.refused.status";
    internal const string DraftRerollUnaffordableStatusKey = "loc.perk_draft.reroll_unaffordable.status";
    internal const string DraftHostUnavailableStatusKey = "loc.perk_draft.host_unavailable.status";

    // ---- run Shop -----------------------------------------------------------------------------

    internal const string ShopTitleNameKey = "loc.shop.title.name";
    internal const string ShopLeaveActionKey = "loc.shop.leave.action";
    internal const string ShopBuyActionKey = "loc.shop.buy.action";
    internal const string ShopRefreshActionKey = "loc.shop.refresh.action";
    internal const string ShopGoldLabelKey = "loc.shop.gold.label";
    internal const string ShopPerkSlotNameKey = "loc.shop.slot.perk.name";
    internal const string ShopConsumableSlotNameKey = "loc.shop.slot.consumable.name";
    internal const string ShopRunBuffSlotNameKey = "loc.shop.slot.run_buff.name";
    internal const string ShopHealSlotNameKey = "loc.shop.slot.heal.name";
    internal const string ShopSoldLabelKey = "loc.shop.sold.label";
    internal const string ShopUnaffordableLabelKey = "loc.shop.unaffordable.label";
    internal const string ShopEmptySlotLabelKey = "loc.shop.empty_slot.label";
    internal const string ShopRefreshSpentBlockKey = "loc.shop.refresh_spent.block";
    internal const string ShopOfferUnavailableStatusKey = "loc.shop.offer_unavailable.status";
    internal const string ShopUnaffordableStatusKey = "loc.shop.unaffordable.status";
    internal const string ShopLoadingStatusKey = "loc.shop.loading.status";
    internal const string ShopRunMissingStatusKey = "loc.shop.run_missing.status";
    internal const string ShopNotAtAShopStatusKey = "loc.shop.not_at_a_shop.status";
    internal const string ShopReadUnavailableStatusKey = "loc.shop.read_unavailable.status";
    internal const string ShopRefusedStatusKey = "loc.shop.refused.status";
    internal const string ShopHostUnavailableStatusKey = "loc.shop.host_unavailable.status";

    // ---- Campfire / Shrine --------------------------------------------------------------------

    internal const string CampfireTitleNameKey = "loc.campfire.title.name";
    internal const string CampfireShrineTitleNameKey = "loc.campfire.shrine_title.name";
    internal const string CampfireShrineBuffsLabelKey = "loc.campfire.shrine_buffs.label";
    internal const string CampfireRestActionKey = "loc.campfire.rest.action";
    internal const string CampfireUpgradePerkActionKey = "loc.campfire.upgrade_perk.action";
    internal const string CampfireContinueActionKey = "loc.campfire.continue.action";
    internal const string CampfireUpgradePerkNoneBlockKey = "loc.campfire.upgrade_perk_none.block";
    internal const string CampfireTakeActionKey = "loc.campfire.take.action";
    internal const string CampfireCleanseActionKey = "loc.campfire.cleanse.action";
    internal const string CampfireShrineChooseLabelKey = "loc.campfire.shrine_choose.label";
    internal const string CampfireLoadingStatusKey = "loc.campfire.loading.status";
    internal const string CampfireRunMissingStatusKey = "loc.campfire.run_missing.status";
    internal const string CampfireNotAtACampfireStatusKey = "loc.campfire.not_at_a_campfire.status";
    internal const string CampfireReadUnavailableStatusKey = "loc.campfire.read_unavailable.status";
    internal const string CampfireShrineUnavailableStatusKey = "loc.campfire.shrine_unavailable.status";
    internal const string CampfireRefusedStatusKey = "loc.campfire.refused.status";
    internal const string CampfireHostUnavailableStatusKey = "loc.campfire.host_unavailable.status";

    private static readonly ContentVersion FixtureStamp =
        ContentVersion.FromHex(new string('d', ContentVersion.HexLength));

    internal const string InventoryTitleNameKey = "loc.inventory.title.name";
    internal const string InventoryCapacityLabelKey = "loc.inventory.capacity.label";
    internal const string InventoryHeldLabelKey = "loc.inventory.held.label";
    internal const string InventoryEquippedBadgeKey = "loc.inventory.equipped.badge";
    internal const string InventoryEquipActionKey = "loc.inventory.equip.action";
    internal const string InventoryCloseActionKey = "loc.inventory.close.action";
    internal const string InventoryHeldNotEquippableBlockKey = "loc.inventory.held_not_equippable.block";
    internal const string InventoryLoadingStatusKey = "loc.inventory.loading.status";
    internal const string InventoryEmptyStatusKey = "loc.inventory.empty.status";
    internal const string InventoryUnavailableStatusKey = "loc.inventory.unavailable.status";
    internal const string InventoryRefusedStatusKey = "loc.inventory.refused.status";
    internal const string InventoryHostUnavailableStatusKey = "loc.inventory.host_unavailable.status";

    // ---- run end: the death offer and the tally (S13 / S14) -----------------------------------

    internal const string RunEndDefeatedNameKey = "loc.run_end.defeated.name";
    internal const string RunEndVictoryNameKey = "loc.run_end.victory.name";
    internal const string RunEndAbandonedNameKey = "loc.run_end.abandoned.name";
    internal const string RunEndBankedLabelKey = "loc.run_end.banked.label";
    internal const string RunEndPayoutLabelKey = "loc.run_end.payout.label";
    internal const string RunEndLegendXpLabelKey = "loc.run_end.legend_xp.label";
    internal const string RunEndFloorItemsLabelKey = "loc.run_end.floor_items.label";
    internal const string RunEndEliteMercyLabelKey = "loc.run_end.elite_mercy.label";
    internal const string RunEndBossMercyLabelKey = "loc.run_end.boss_mercy.label";
    internal const string RunEndReviveActionKey = "loc.run_end.revive.action";
    internal const string RunEndFinishActionKey = "loc.run_end.finish.action";
    internal const string RunEndReviveNeedsPlusBlockKey = "loc.run_end.revive_needs_plus.block";
    internal const string RunEndReviveSpentBlockKey = "loc.run_end.revive_spent.block";
    internal const string RunEndDeathCostsRewardsBlockKey = "loc.run_end.death_costs_rewards.block";
    internal const string RunEndLoadingStatusKey = "loc.run_end.loading.status";
    internal const string RunEndRunMissingStatusKey = "loc.run_end.run_missing.status";
    internal const string RunEndUnavailableStatusKey = "loc.run_end.unavailable.status";
    internal const string RunEndRefusedStatusKey = "loc.run_end.refused.status";
    internal const string RunEndHostUnavailableStatusKey = "loc.run_end.host_unavailable.status";

    /// <summary>
    /// 🔒 The Soul Shards line's name, which is <c>tuning/currencies.json</c>'s and NOT this
    /// screen's.
    /// </summary>
    /// <remarks>
    /// Listed here because the fixture has to carry it for the row to resolve at all, exactly as
    /// <see cref="ShrineBuffNameKeys"/> is. The run-end document points at this key rather than
    /// authoring a second one, so a fixture that invented a <c>loc.run_end.soul_shards.*</c> would be
    /// proving the screen against a key the content set does not have.
    /// </remarks>
    internal const string CurrencySoulShardsNameKey = "loc.currency.soul_shards.name";

    /// <summary>Every string key the run-end screen (S13 / S14) renders.</summary>
    internal static IReadOnlyList<string> RunEndKeys { get; } =
    [
        RunEndDefeatedNameKey, RunEndVictoryNameKey, RunEndAbandonedNameKey,
        RunEndBankedLabelKey, RunEndPayoutLabelKey,
        RunEndLegendXpLabelKey, CurrencySoulShardsNameKey,
        RunEndFloorItemsLabelKey, RunEndEliteMercyLabelKey, RunEndBossMercyLabelKey,
        RunEndReviveActionKey, RunEndFinishActionKey,
        RunEndReviveNeedsPlusBlockKey, RunEndReviveSpentBlockKey, RunEndDeathCostsRewardsBlockKey,
        RunEndLoadingStatusKey, RunEndRunMissingStatusKey, RunEndUnavailableStatusKey,
        RunEndRefusedStatusKey, RunEndHostUnavailableStatusKey,
    ];

    /// <summary>Every string key the Inventory screen (S16) renders.</summary>
    internal static IReadOnlyList<string> InventoryKeys { get; } =
    [
        InventoryTitleNameKey,
        InventoryCapacityLabelKey, InventoryHeldLabelKey,
        InventoryEquippedBadgeKey,
        InventoryEquipActionKey, InventoryCloseActionKey,
        InventoryHeldNotEquippableBlockKey,
        InventoryLoadingStatusKey, InventoryEmptyStatusKey, InventoryUnavailableStatusKey,
        InventoryRefusedStatusKey, InventoryHostUnavailableStatusKey,
    ];

    /// <summary>Every string key the Perk Draft screen renders.</summary>
    internal static IReadOnlyList<string> DraftKeys { get; } =
    [
        DraftTitleNameKey, DraftAdFourthOptionNameKey,
        DraftSynergyLabelKey, DraftRerollCostLabelKey, DraftSkipRewardLabelKey,
        DraftLegendaryPityLabelKey, DraftQualityFloorLabelKey, DraftUpgradeFamineLabelKey,
        DraftUpgradeBadgeKey,
        DraftRerollActionKey, DraftAdRerollActionKey, DraftSkipActionKey,
        DraftAdRerollBlockKey, DraftAdFourthOptionBlockKey, DraftFreeRerollBlockKey,
        DraftGuaranteeNotDueBlockKey,
        DraftLoadingStatusKey, DraftNoDraftStatusKey, DraftRunMissingStatusKey,
        DraftReadUnavailableStatusKey, DraftCardsUnavailableStatusKey,
        DraftEffectNumbersUnavailableStatusKey, DraftRefusedStatusKey,
        DraftRerollUnaffordableStatusKey, DraftHostUnavailableStatusKey,
    ];

    /// <summary>Every string key the run Shop screen renders.</summary>
    internal static IReadOnlyList<string> ShopKeys { get; } =
    [
        ShopTitleNameKey, ShopLeaveActionKey, ShopBuyActionKey, ShopRefreshActionKey,
        ShopGoldLabelKey,
        ShopPerkSlotNameKey, ShopConsumableSlotNameKey, ShopRunBuffSlotNameKey, ShopHealSlotNameKey,
        ShopSoldLabelKey, ShopUnaffordableLabelKey, ShopEmptySlotLabelKey, ShopRefreshSpentBlockKey,
        ShopLoadingStatusKey, ShopRunMissingStatusKey, ShopNotAtAShopStatusKey,
        ShopReadUnavailableStatusKey, ShopOfferUnavailableStatusKey, ShopRefusedStatusKey,
        ShopUnaffordableStatusKey, ShopHostUnavailableStatusKey,
    ];

    /// <summary>Every string key the Campfire / Shrine screen renders, buff names aside.</summary>
    internal static IReadOnlyList<string> CampfireKeys { get; } =
    [
        CampfireTitleNameKey, CampfireShrineTitleNameKey, CampfireShrineBuffsLabelKey,
        CampfireRestActionKey, CampfireUpgradePerkActionKey,
        CampfireContinueActionKey,
        CampfireUpgradePerkNoneBlockKey, CampfireTakeActionKey, CampfireCleanseActionKey,
        CampfireShrineChooseLabelKey,
        CampfireLoadingStatusKey, CampfireRunMissingStatusKey, CampfireNotAtACampfireStatusKey,
        CampfireReadUnavailableStatusKey, CampfireShrineUnavailableStatusKey,
        CampfireRefusedStatusKey, CampfireHostUnavailableStatusKey,
    ];

    /// <summary>
    /// The ten shrine buff name keys the shrine arm draws its rows through.
    /// </summary>
    /// <remarks>
    /// 🔒 These belong to <c>tuning/currencies.json</c>, which names them as the buff pool's own
    /// display names — so they are already referenced and already translated, and the campfire
    /// document must not claim them a second time. They are listed here because the fixture has to
    /// carry them for the shrine arm to resolve a row's name at all.
    /// </remarks>
    internal static IReadOnlyList<string> ShrineBuffNameKeys { get; } =
    [
        "loc.shrine.atk.name", "loc.shrine.hp.name", "loc.shrine.aspd.name", "loc.shrine.crit.name",
        "loc.shrine.def.name", "loc.shrine.ls.name", "loc.shrine.dr.name", "loc.shrine.thorn.name",
        "loc.shrine.gold.name", "loc.shrine.heal.name",
    ];

    /// <summary>
    /// The shipped English locale, for the cases whose claim is about the AUTHORED strings.
    /// </summary>
    /// <remarks>
    /// 🔒 The distinctness claims on these three screens cannot be made against a fixture at all.
    /// Every fixture value below is derived from its own key, so any set of distinct keys gives a
    /// set of distinct values by construction and a fixture-based version of "these four sentences
    /// differ" could never fail whatever anyone wrote in <c>en.json</c>. The claim being made is
    /// about what a player reads, and only the authored strings are that.
    /// </remarks>
    internal static IReadOnlyDictionary<string, string> ShippedEnglish { get; } = ReadShippedEnglish();

    /// <summary>The English fixture value for a key.</summary>
    internal static string EnglishValueOf(string key) => "FIXTURE " + key;

    /// <summary>The German fixture value for a key — a marker, not a translation.</summary>
    internal static string GermanValueOf(string key) => "FIXTURE-DE " + key;

    /// <summary>A catalogue answering in English over a content set.</summary>
    internal static LocaleStringCatalogue Catalogue(ContentSnapshot content) =>
        new(content, ScreenContent.English);

    /// <summary>A catalogue answering in English over every string these three screens need.</summary>
    internal static LocaleStringCatalogue Catalogue() => Catalogue(Strings());

    /// <summary>A content set carrying all three screens' strings and nothing else.</summary>
    /// <remarks>
    /// Deliberately no perk catalogue and no shrine buff pool: the projections those need are the
    /// rules layer's, and a case about what this screen SAYS should not have to author 46 perks to
    /// ask. The cases that are about a projection arrange their own content.
    /// </remarks>
    internal static ContentSnapshot Strings() => new(FixtureStamp, [.. Locales()]);

    /// <summary>The authored English sentence behind a key, or null when nothing authors one.</summary>
    internal static string? Authored(string key) =>
        ShippedEnglish.TryGetValue(key, out var sentence) ? sentence : null;

    private static IReadOnlyList<ContentDocument> Locales()
    {
        var keys = DraftKeys
            .Concat(ShopKeys)
            .Concat(CampfireKeys)
            .Concat(ShrineBuffNameKeys)
            .Concat(InventoryKeys)
            .Concat(RunEndKeys)
            .ToArray();

        return
        [
            Locale("loc/en.json", ScreenContent.English, keys, EnglishValueOf),
            Locale("loc/de.json", ScreenContent.German, keys, GermanValueOf),
        ];
    }

    private static ContentDocument Locale(
        string path, string localeTag, IReadOnlyList<string> keys, Func<string, string> valueOf) =>
        new(path, ContentValue.Object(
        [
            new KeyValuePair<string, ContentValue>("_locale", ContentValue.Text(localeTag)),
            new KeyValuePair<string, ContentValue>("strings", ContentValue.Object(
                keys.Select(k =>
                    new KeyValuePair<string, ContentValue>(k, ContentValue.Text(valueOf(k)))))),
        ]));

    /// <summary>Reads the authored English strings straight off the checkout.</summary>
    private static IReadOnlyDictionary<string, string> ReadShippedEnglish()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepoPaths.ContentDataRoot, "loc", "en.json")));

        return document.RootElement.GetProperty("strings")
                       .EnumerateObject()
                       .ToDictionary(m => m.Name, m => m.Value.GetString() ?? "", StringComparer.Ordinal);
    }
}
