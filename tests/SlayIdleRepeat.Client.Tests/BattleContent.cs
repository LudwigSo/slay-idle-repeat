using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The Battle Replay screen's string keys, and the content sets its cases run against.
/// </summary>
/// <remarks>
/// <para>
/// Built in memory rather than loaded, so a case can state exactly which strings exist. Fixture
/// values are the key with a marker in front, so every string is unique, obviously not the shipped
/// copy, and impossible for a hard-coded literal in the presenter to match by accident.
/// </para>
/// <para>
/// 🔒 The German fixture value differs from the English one for the same reason it does on every
/// other screen: two documents carrying the same string would make the second add no coverage, and
/// a case asserting a locale difference would pass without the resolver ever choosing between them.
/// </para>
/// </remarks>
internal static class BattleContent
{
    internal const string TitleNameKey = "loc.battle.title.name";
    internal const string HeroLabelKey = "loc.battle.hero.label";
    internal const string EnemyLabelKey = "loc.battle.enemy.label";
    internal const string SpeedLabelKey = "loc.battle.speed.label";
    internal const string SpeedSingleActionKey = "loc.battle.speed_single.action";
    internal const string SpeedDoubleActionKey = "loc.battle.speed_double.action";
    internal const string SpeedTripleActionKey = "loc.battle.speed_triple.action";
    internal const string SkipActionKey = "loc.battle.skip.action";
    internal const string PhaseOneNameKey = "loc.battle.phase_one.name";
    internal const string PhaseTwoNameKey = "loc.battle.phase_two.name";
    internal const string PhaseThreeNameKey = "loc.battle.phase_three.name";
    internal const string LoadingStatusKey = "loc.battle.loading.status";
    internal const string NoRunStatusKey = "loc.battle.no_run.status";
    internal const string PhaseNotBattleStatusKey = "loc.battle.phase_not_battle.status";
    internal const string SeedUnavailableStatusKey = "loc.battle.seed_unavailable.status";
    internal const string SimulatorFailedStatusKey = "loc.battle.simulator_failed.status";
    internal const string LogEmptyStatusKey = "loc.battle.log_empty.status";
    internal const string ReadUnavailableStatusKey = "loc.battle.read_unavailable.status";
    internal const string VictoryStatusKey = "loc.battle.victory.status";
    internal const string DefeatStatusKey = "loc.battle.defeat.status";
    internal const string RefusedStatusKey = "loc.battle.refused.status";

    private static readonly ContentVersion FixtureStamp =
        ContentVersion.FromHex(new string('b', ContentVersion.HexLength));

    /// <summary>Every string key the Battle Replay screen renders.</summary>
    internal static IReadOnlyList<string> BattleKeys { get; } =
    [
        TitleNameKey, HeroLabelKey, EnemyLabelKey, SpeedLabelKey,
        SpeedSingleActionKey, SpeedDoubleActionKey, SpeedTripleActionKey, SkipActionKey,
        PhaseOneNameKey, PhaseTwoNameKey, PhaseThreeNameKey,
        LoadingStatusKey, NoRunStatusKey, PhaseNotBattleStatusKey, SeedUnavailableStatusKey,
        SimulatorFailedStatusKey, LogEmptyStatusKey,
        ReadUnavailableStatusKey, VictoryStatusKey, DefeatStatusKey, RefusedStatusKey,
    ];

    /// <summary>
    /// The four ways a replay can have nothing to animate, each with the sentence it is told by.
    /// </summary>
    /// <remarks>
    /// 🔒 The set the distinctness case is stated over. It deliberately excludes
    /// <see cref="BattleReadiness.NoRun"/>, which is a screen a player reached without a run at all
    /// rather than a fight that failed to materialise, and <see cref="BattleReadiness.Ready"/>,
    /// which has a fight.
    /// </remarks>
    internal static IReadOnlyList<(BattleReadiness Readiness, string SentenceKey)> StallCauses { get; } =
    [
        (BattleReadiness.PhaseNotBattle, PhaseNotBattleStatusKey),
        (BattleReadiness.SeedUnavailable, SeedUnavailableStatusKey),
        (BattleReadiness.SimulatorFailed, SimulatorFailedStatusKey),
        (BattleReadiness.LogEmpty, LogEmptyStatusKey),
    ];

    /// <summary>The English fixture value for a key.</summary>
    internal static string EnglishValueOf(string key) => "FIXTURE " + key;

    /// <summary>The German fixture value for a key — a marker, not a translation.</summary>
    internal static string GermanValueOf(string key) => "FIXTURE-DE " + key;

    /// <summary>
    /// The shipped English locale, for the cases whose claim is about the AUTHORED strings.
    /// </summary>
    /// <remarks>
    /// 🔒 "No two of the five stall sentences read the same" cannot be made against a fixture at
    /// all: every fixture value here is derived from its own key, so five distinct keys give five
    /// distinct values by construction and a fixture-based version of that case could never fail
    /// whatever anyone wrote in <c>en.json</c>. The claim is about what a player reads — five
    /// causes with five different things to do about them — and only the authored strings are that.
    /// </remarks>
    internal static IReadOnlyDictionary<string, string> ShippedEnglish { get; } = ReadShippedEnglish();

    /// <summary>A catalogue answering in English over a content set.</summary>
    internal static LocaleStringCatalogue Catalogue(ContentSnapshot content) =>
        new(content, ScreenContent.English);

    /// <summary>A catalogue answering in English over every string this screen needs.</summary>
    internal static LocaleStringCatalogue Catalogue() => Catalogue(Strings());

    /// <summary>A content set carrying this screen's strings in both locales.</summary>
    internal static ContentSnapshot Strings() => new(FixtureStamp, [.. Locales(BattleKeys)]);

    /// <summary>A content set carrying every string this screen needs except the named one.</summary>
    /// <param name="key">The key to leave out.</param>
    internal static ContentSnapshot Authoring(string key) =>
        new(FixtureStamp,
            [.. Locales([.. BattleKeys.Where(k => !string.Equals(k, key, StringComparison.Ordinal))])]);

    private static IReadOnlyList<ContentDocument> Locales(IReadOnlyList<string> keys) =>
    [
        Locale("loc/en.json", ScreenContent.English, keys, EnglishValueOf),
        Locale("loc/de.json", ScreenContent.German, keys, GermanValueOf),
    ];

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
