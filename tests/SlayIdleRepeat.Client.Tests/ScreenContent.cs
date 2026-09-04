using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The Home and Chapter Select screens' string keys, and the content sets their cases run against.
/// </summary>
/// <remarks>
/// <para>
/// Snapshots are built in memory rather than loaded, so a case can state exactly which chapters
/// exist and exactly what the gating ladder says. That is the only way to hold the presenter to
/// reading the ladder rather than carrying a copy of it: the shipped ladder cannot be varied, and a
/// hard-coded 60 agrees with it forever.
/// </para>
/// <para>
/// Fixture values are the key with a marker in front, so every string is unique, obviously not the
/// shipped copy, and impossible for a hard-coded literal to match by accident.
/// </para>
/// </remarks>
internal static class ScreenContent
{
    internal const string English = "en";
    internal const string German = "de";

    internal const string LegendLevelLabelKey = "loc.home.legend_level.label";
    internal const string EnergyLabelKey = "loc.home.energy.label";
    internal const string EnergyReserveLabelKey = "loc.home.energy_reserve.label";
    internal const string StartRunActionKey = "loc.home.start_run.action";
    internal const string ContinueRunActionKey = "loc.home.continue_run.action";
    internal const string LoadingStatusKey = "loc.home.loading.status";
    internal const string RunLapsedStatusKey = "loc.home.run_lapsed.status";
    internal const string UnavailableStatusKey = "loc.home.unavailable.status";
    internal const string PowerLabelKey = "loc.home.power.label";
    internal const string ProgressLabelKey = "loc.home.progress.label";
    internal const string StageLabelKey = "loc.home.stage.label";
    internal const string NothingClearedStatusKey = "loc.home.nothing_cleared.status";

    /// <summary>The currency names the HUD tiles borrow rather than author captions of their own.</summary>
    internal const string CrownsNameKey = "loc.currency.crowns.name";
    internal const string SoulShardsNameKey = "loc.currency.soul_shards.name";
    internal const string GoldNameKey = "loc.currency.gold.name";

    internal const string ChapterSelectTitleKey = "loc.chapter_select.title.name";
    internal const string TierNormalKey = "loc.chapter_select.tier_normal.name";
    internal const string TierHeroicKey = "loc.chapter_select.tier_heroic.name";
    internal const string TierMythicKey = "loc.chapter_select.tier_mythic.name";
    internal const string RequiresClearBlockKey = "loc.chapter_select.requires_clear.block";
    internal const string RequiresLegendLevelBlockKey = "loc.chapter_select.requires_legend_level.block";
    internal const string ConfirmActionKey = "loc.chapter_select.confirm.action";

    internal const string PickerLoadingStatusKey = "loc.chapter_select.loading.status";
    internal const string PickerProfileMissingStatusKey = "loc.chapter_select.profile_missing.status";
    internal const string PickerUnavailableStatusKey = "loc.chapter_select.unavailable.status";

    internal const string PickerStartingStatusKey = "loc.chapter_select.starting.status";

    internal const string PickerStartedStatusKey = "loc.chapter_select.started.status";

    internal const string PickerRefusedStatusKey = "loc.chapter_select.refused.status";

    /// <summary>Where the tier ladder is authored. Read, never transcribed.</summary>
    internal const string ChapterGatingMember = "chapterGating";

    internal const string ProgressionDocument = "tuning/progression.json";

    internal const string ChaptersDirectory = "content/chapters/";

    /// <summary>The three clear requirements the authored ladder can name.</summary>
    internal const string PreviousChapterNormal = "PREVIOUS_CHAPTER_NORMAL";

    internal const string SameChapterNormal = "SAME_CHAPTER_NORMAL";

    internal const string SameChapterHeroic = "SAME_CHAPTER_HEROIC";

    private const string RequiresClearMember = "requiresClear";

    private const string RequiresLegendLevelMember = "requiresLegendLevel";

    private static readonly ContentVersion FixtureStamp =
        ContentVersion.FromHex(new string('b', ContentVersion.HexLength));

    /// <summary>Every string key the Home screen renders.</summary>
    internal static IReadOnlyList<string> HomeKeys { get; } =
    [
        LegendLevelLabelKey, EnergyLabelKey, EnergyReserveLabelKey,
        StartRunActionKey, ContinueRunActionKey, LoadingStatusKey, UnavailableStatusKey,
        RunLapsedStatusKey, PowerLabelKey, ProgressLabelKey, StageLabelKey, NothingClearedStatusKey,
        CrownsNameKey, SoulShardsNameKey, GoldNameKey,
    ];

    /// <summary>Every string key the Chapter Select screen renders, chapter names aside.</summary>
    internal static IReadOnlyList<string> ChapterSelectKeys { get; } =
    [
        ChapterSelectTitleKey, TierNormalKey, TierHeroicKey, TierMythicKey,
        RequiresClearBlockKey, RequiresLegendLevelBlockKey, ConfirmActionKey,
        PickerLoadingStatusKey, PickerProfileMissingStatusKey, PickerUnavailableStatusKey,
        PickerStartingStatusKey, PickerStartedStatusKey, PickerRefusedStatusKey,
    ];

    /// <summary>The loc key a chapter document names its own display name with.</summary>
    internal static string ChapterNameKey(int chapterId) => $"loc.chapter.{chapterId}.name";

    /// <summary>The English fixture value for a key.</summary>
    internal static string EnglishValueOf(string key) => "FIXTURE " + key;

    /// <summary>The German fixture value for a key — a marker, not a translation.</summary>
    internal static string GermanValueOf(string key) => "FIXTURE-DE " + key;

    /// <summary>A catalogue answering in English over a content set.</summary>
    internal static LocaleStringCatalogue Catalogue(ContentSnapshot content) => new(content, English);

    /// <summary>A catalogue answering in English over the strings both screens need.</summary>
    internal static LocaleStringCatalogue Catalogue() => Catalogue(Strings());

    /// <summary>
    /// How long a run may be left alone in this fixture's content before it lapses.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT the shipped 48 hours. Home reads the window from content, and a fixture that
    /// transcribed the shipped number would agree with a presenter carrying its own copy of it.
    /// </remarks>
    internal const int FixtureRunExpiryHours = 5;

    /// <summary>A content set carrying both screens' strings and the run window.</summary>
    /// <remarks>
    /// The window is in the base fixture rather than an opt-in, because Home now reads it on every
    /// decision an open run reaches — a content set without it makes every case throw
    /// <c>MissingContentException</c> rather than exercise the screen.
    /// </remarks>
    internal static ContentSnapshot Strings() =>
        new(FixtureStamp, [.. Locales([]), Progression()]);

    /// <summary>`03`'s authored run window, as the progression document states it.</summary>
    /// <remarks>
    /// 🔒 A MEMBER of the progression document rather than a document of its own, because a
    /// <c>ContentSnapshot</c> refuses the same path twice — deliberately, so that neither of two
    /// copies wins by ordering accident. The chapter ladder lives in the same file, so the two are
    /// assembled together below.
    /// </remarks>
    private static KeyValuePair<string, ContentValue> RunLifetimeMember() =>
        new("runLifetime", ContentValue.Object(
        [
            new KeyValuePair<string, ContentValue>("expiryHours", ContentValue.Number(FixtureRunExpiryHours)),
        ]));

    /// <summary>The progression document carrying only the run window — no ladder authored.</summary>
    private static ContentDocument Progression() =>
        new(ProgressionDocument, ContentValue.Object([RunLifetimeMember()]));

    /// <summary>
    /// A content set authoring the given chapters and a gating ladder.
    /// </summary>
    /// <param name="chapterIds">The chapters that exist. Anything else is unauthored.</param>
    /// <param name="mythicLegendLevel">
    /// What the ladder demands of Mythic, or <c>null</c> for a rung that demands no level. Varied by
    /// the case that proves the presenter reads it rather than carrying its own copy.
    /// </param>
    internal static ContentSnapshot Authoring(IReadOnlyList<int> chapterIds, int? mythicLegendLevel = 60)
    {
        var documents = new List<ContentDocument>(Locales(chapterIds))
        {
            Gating(mythicLegendLevel),
        };

        documents.AddRange(chapterIds.Select(Chapter));

        return new ContentSnapshot(FixtureStamp, documents);
    }

    private static IReadOnlyList<ContentDocument> Locales(IReadOnlyList<int> chapterIds)
    {
        var keys = HomeKeys
            .Concat(ChapterSelectKeys)
            .Concat(chapterIds.Select(ChapterNameKey))
            .ToArray();

        return
        [
            Locale("loc/en.json", English, keys, EnglishValueOf),
            Locale("loc/de.json", German, keys, GermanValueOf),
        ];
    }

    /// <summary>
    /// One chapter document.
    /// </summary>
    /// <remarks>
    /// 🔒 The file name counts <em>down</em> as the id counts up, so the snapshot's ordinal path
    /// order is the reverse of the id order. A presenter listing chapters in the order the content
    /// set hands them over would produce a descending list here and agree with an ascending one
    /// against the shipped files, where the two orders happen to coincide.
    /// </remarks>
    private static ContentDocument Chapter(int chapterId) =>
        new($"{ChaptersDirectory}CH_{99 - chapterId:00}_FIXTURE.json", ContentValue.Object(
        [
            new KeyValuePair<string, ContentValue>("id", ContentValue.Number(chapterId)),
            new KeyValuePair<string, ContentValue>(
                "displayName", ContentValue.Text(ChapterNameKey(chapterId))),
        ]));

    private static ContentDocument Gating(int? mythicLegendLevel) =>
        new(ProgressionDocument, ContentValue.Object(
        [
            new KeyValuePair<string, ContentValue>(ChapterGatingMember, ContentValue.Object(
            [
                // The shipped ladder carries this prose member beside the three rungs. Kept here so
                // a reader that walks the rungs meets it under a fixture too, not only on a handset.
                new KeyValuePair<string, ContentValue>("_doc", ContentValue.Text("fixture ladder")),
                new KeyValuePair<string, ContentValue>("NORMAL", Rung(PreviousChapterNormal, null)),
                new KeyValuePair<string, ContentValue>("HEROIC", Rung(SameChapterNormal, null)),
                new KeyValuePair<string, ContentValue>("MYTHIC", Rung(SameChapterHeroic, mythicLegendLevel)),
            ])),

            // The shipped file carries both, and so must this one: Home reads the window off the
            // same document Chapter Select reads the ladder off.
            RunLifetimeMember(),
        ]));

    private static ContentValue Rung(string requiresClear, int? requiresLegendLevel) =>
        ContentValue.Object(
        [
            new KeyValuePair<string, ContentValue>(RequiresClearMember, ContentValue.Text(requiresClear)),
            new KeyValuePair<string, ContentValue>(
                RequiresLegendLevelMember,
                requiresLegendLevel is { } level ? ContentValue.Number(level) : ContentValue.Unauthorised),
        ]);

    private static ContentDocument Locale(
        string path, string localeTag, IReadOnlyList<string> keys, Func<string, string> valueOf) =>
        new(path, ContentValue.Object(
        [
            new KeyValuePair<string, ContentValue>("_locale", ContentValue.Text(localeTag)),
            new KeyValuePair<string, ContentValue>("strings", ContentValue.Object(
                keys.Select(k => new KeyValuePair<string, ContentValue>(k, ContentValue.Text(valueOf(k)))))),
        ]));
}
