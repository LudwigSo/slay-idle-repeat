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
    internal const string DraftUpgradeBadgeKey = "loc.perk_draft.upgrade.badge";
    internal const string DraftRerollActionKey = "loc.perk_draft.reroll.action";
    internal const string DraftAdRerollActionKey = "loc.perk_draft.ad_reroll.action";
    internal const string DraftSkipActionKey = "loc.perk_draft.skip.action";
    internal const string DraftAdRerollBlockKey = "loc.perk_draft.ad_reroll_deferred.block";
    internal const string DraftAdFourthOptionBlockKey = "loc.perk_draft.ad_fourth_option_deferred.block";
    internal const string DraftFreeRerollBlockKey = "loc.perk_draft.free_reroll_unbuilt.block";
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
    internal const string ShopNothingStockedBlockKey = "loc.shop.nothing_stocked.block";
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
    internal const string CampfireRerollChargesActionKey = "loc.campfire.reroll_charges.action";
    internal const string CampfireContinueActionKey = "loc.campfire.continue.action";
    internal const string CampfireUpgradePerkBlockKey = "loc.campfire.upgrade_perk_untracked.block";
    internal const string CampfireRerollChargesBlockKey = "loc.campfire.reroll_charges_untracked.block";
    internal const string CampfireShrineChoiceBlockKey = "loc.campfire.shrine_choice_absent.block";
    internal const string CampfireShrineCleanseBlockKey = "loc.campfire.shrine_cleanse_absent.block";
    internal const string CampfireLoadingStatusKey = "loc.campfire.loading.status";
    internal const string CampfireRunMissingStatusKey = "loc.campfire.run_missing.status";
    internal const string CampfireNotAtACampfireStatusKey = "loc.campfire.not_at_a_campfire.status";
    internal const string CampfireReadUnavailableStatusKey = "loc.campfire.read_unavailable.status";
    internal const string CampfireShrineUnavailableStatusKey = "loc.campfire.shrine_unavailable.status";
    internal const string CampfireRefusedStatusKey = "loc.campfire.refused.status";
    internal const string CampfireHostUnavailableStatusKey = "loc.campfire.host_unavailable.status";

    private static readonly ContentVersion FixtureStamp =
        ContentVersion.FromHex(new string('d', ContentVersion.HexLength));

    /// <summary>Every string key the Perk Draft screen renders.</summary>
    internal static IReadOnlyList<string> DraftKeys { get; } =
    [
        DraftTitleNameKey, DraftAdFourthOptionNameKey,
        DraftSynergyLabelKey, DraftRerollCostLabelKey, DraftSkipRewardLabelKey,
        DraftUpgradeBadgeKey,
        DraftRerollActionKey, DraftAdRerollActionKey, DraftSkipActionKey,
        DraftAdRerollBlockKey, DraftAdFourthOptionBlockKey, DraftFreeRerollBlockKey,
        DraftLoadingStatusKey, DraftNoDraftStatusKey, DraftRunMissingStatusKey,
        DraftReadUnavailableStatusKey, DraftCardsUnavailableStatusKey,
        DraftEffectNumbersUnavailableStatusKey, DraftRefusedStatusKey,
        DraftRerollUnaffordableStatusKey, DraftHostUnavailableStatusKey,
    ];

    /// <summary>Every string key the run Shop screen renders.</summary>
    internal static IReadOnlyList<string> ShopKeys { get; } =
    [
        ShopTitleNameKey, ShopLeaveActionKey, ShopNothingStockedBlockKey,
        ShopLoadingStatusKey, ShopRunMissingStatusKey, ShopNotAtAShopStatusKey,
        ShopReadUnavailableStatusKey, ShopRefusedStatusKey, ShopHostUnavailableStatusKey,
    ];

    /// <summary>Every string key the Campfire / Shrine screen renders, buff names aside.</summary>
    internal static IReadOnlyList<string> CampfireKeys { get; } =
    [
        CampfireTitleNameKey, CampfireShrineTitleNameKey, CampfireShrineBuffsLabelKey,
        CampfireRestActionKey, CampfireUpgradePerkActionKey, CampfireRerollChargesActionKey,
        CampfireContinueActionKey,
        CampfireUpgradePerkBlockKey, CampfireRerollChargesBlockKey,
        CampfireShrineChoiceBlockKey, CampfireShrineCleanseBlockKey,
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
        var keys = DraftKeys.Concat(ShopKeys).Concat(CampfireKeys).Concat(ShrineBuffNameKeys).ToArray();

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
