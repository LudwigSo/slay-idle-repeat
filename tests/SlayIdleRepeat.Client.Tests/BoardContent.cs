using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>The Board screen's string keys, and the content sets its cases run against.</summary>
/// <remarks>
/// <para>
/// Built in memory rather than loaded, so a case can state exactly what a chapter's stage lengths
/// are. That is the only way to hold the presenter to reading them: the two shipped chapters both
/// author <c>[12, 14, 16]</c>, so a transcribed 3 agrees with them forever and a transcribed 16
/// agrees with the last stage of both.
/// </para>
/// <para>
/// 🔒 <b>The chapter is authored WHOLE, not only its stage lengths.</b> The presenter now projects
/// the run's board through <c>BoardView</c>, which reads the chapter's elite counts, its three tile
/// weight tables and its boss as well — so a fixture carrying only stage lengths would make every
/// case throw on a malformed chapter rather than exercise the screen. The weights are deliberately
/// NOT the shipped ones: one kind per stage at weight 1, so a generated board is made of tiles a
/// case can name, and the shipped balance can be re-tuned without moving a client assertion.
/// </para>
/// <para>
/// Fixture values are the key with a marker in front, so every string is unique, obviously not the
/// shipped copy, and impossible for a hard-coded literal to match by accident.
/// </para>
/// </remarks>
internal static class BoardContent
{
    internal const string HpLabelKey = "loc.board.hp.label";
    internal const string GoldLabelKey = "loc.board.gold.label";
    internal const string StageLabelKey = "loc.board.stage.label";
    internal const string RolledLabelKey = "loc.board.rolled.label";
    internal const string StandingOnLabelKey = "loc.board.standing_on.label";
    internal const string RollActionKey = "loc.board.roll.action";
    internal const string ResolveActionKey = "loc.board.resolve.action";
    internal const string AbandonActionKey = "loc.board.abandon.action";
    internal const string AbandonConfirmActionKey = "loc.board.abandon_confirm.action";
    internal const string ForkNameKey = "loc.board.fork.name";
    internal const string ForkContinueActionKey = "loc.board.fork_continue.action";
    internal const string ForkBranchActionKey = "loc.board.fork_branch.action";
    internal const string LoadingStatusKey = "loc.board.loading.status";
    internal const string RunMissingStatusKey = "loc.board.run_missing.status";
    internal const string RunEndedStatusKey = "loc.board.run_ended.status";
    internal const string UnavailableStatusKey = "loc.board.unavailable.status";
    internal const string RefusedStatusKey = "loc.board.refused.status";
    internal const string BlockedTileStatusKey = "loc.board.blocked_tile.status";
    internal const string BlockedForkStatusKey = "loc.board.blocked_fork.status";
    internal const string BlockedBattleStatusKey = "loc.board.blocked_battle.status";
    internal const string BlockedDraftStatusKey = "loc.board.blocked_draft.status";
    internal const string ForkPerilousLabelKey = "loc.board.fork_perilous.label";
    internal const string ForkShelteredLabelKey = "loc.board.fork_sheltered.label";
    internal const string ForkArcaneLabelKey = "loc.board.fork_arcane.label";
    internal const string ForkFeralLabelKey = "loc.board.fork_feral.label";
    internal const string FixedDiceHeldLabelKey = "loc.board.dice_held.label";
    internal const string FixedDiceChooseLabelKey = "loc.board.dice_choose.label";

    internal const string ChaptersDirectory = "content/chapters/";

    internal const string StageLengthsMember = "stageLengths";

    private static readonly ContentVersion FixtureStamp =
        ContentVersion.FromHex(new string('c', ContentVersion.HexLength));

    /// <summary>Every string key the Board screen renders, tile names aside.</summary>
    internal static IReadOnlyList<string> BoardKeys { get; } =
    [
        HpLabelKey, GoldLabelKey, StageLabelKey, RolledLabelKey, StandingOnLabelKey,
        RollActionKey, ResolveActionKey,
        AbandonActionKey, AbandonConfirmActionKey,
        ForkNameKey, ForkContinueActionKey, ForkBranchActionKey,
        LoadingStatusKey, RunMissingStatusKey, RunEndedStatusKey, UnavailableStatusKey,
        RefusedStatusKey,
        BlockedTileStatusKey, BlockedForkStatusKey, BlockedBattleStatusKey, BlockedDraftStatusKey,
        ForkPerilousLabelKey, ForkShelteredLabelKey, ForkArcaneLabelKey, ForkFeralLabelKey,
        FixedDiceHeldLabelKey, FixedDiceChooseLabelKey,
    ];

    /// <summary>The English fixture value for a key.</summary>
    internal static string EnglishValueOf(string key) => "FIXTURE " + key;

    /// <summary>The German fixture value for a key — a marker, not a translation.</summary>
    /// <remarks>
    /// Distinct from the English one, and that is the point: both documents carrying the same
    /// string would make the second add no coverage at all, and the first case to assert a locale
    /// difference would pass without the resolver ever choosing between them.
    /// </remarks>
    internal static string GermanValueOf(string key) => "FIXTURE-DE " + key;

    /// <summary>
    /// The shipped English locale, for the cases whose claim is about the AUTHORED strings.
    /// </summary>
    /// <remarks>
    /// 🔒 Two distinctness claims on these screens cannot be made against a fixture at all. "The
    /// four block sentences differ" and "no two face kinds read the same" are claims about what a
    /// player is shown — and a fixture whose every value is derived from its own key makes them
    /// true by construction, so a case stated over one asserts nothing it could ever fail. The real
    /// locale is the only subject those claims have.
    /// </remarks>
    internal static IReadOnlyDictionary<string, string> ShippedEnglish { get; } = ReadShippedEnglish();

    /// <summary>A catalogue answering in English over a content set.</summary>
    internal static LocaleStringCatalogue Catalogue(ContentSnapshot content) =>
        new(content, ScreenContent.English);

    /// <summary>A catalogue answering in English over every string these two screens need.</summary>
    internal static LocaleStringCatalogue Catalogue() => Catalogue(Strings());

    /// <summary>A content set carrying both screens' strings and no chapter at all.</summary>
    internal static ContentSnapshot Strings() => new(FixtureStamp, [.. Locales()]);

    /// <summary>
    /// A content set carrying the strings and one chapter with the given stage lengths.
    /// </summary>
    /// <param name="chapterId">The chapter the run names.</param>
    /// <param name="stageLengths">
    /// How long each stage is, in nodes. Varied by the cases that prove the presenter reads the
    /// chapter rather than carrying the shipped shape as a constant.
    /// </param>
    internal static ContentSnapshot Authoring(int chapterId, params int[] stageLengths)
    {
        var documents = new List<ContentDocument>(Locales())
        {
            Chapter(chapterId, stageLengths),
            BoardGeneration(),
        };

        return new ContentSnapshot(FixtureStamp, documents);
    }

    /// <summary>The one tile kind each stage of the fixture chapter is paved with.</summary>
    /// <remarks>
    /// 🔒 One kind per stage at weight 1, so the generated board is entirely predictable without a
    /// case having to know the weighted draw: stage 1 is Empty, stage 2 Enemy, stage 3 Shrine. Three
    /// DIFFERENT kinds rather than one, so a case can tell the stages apart on the track — and none
    /// of them is the shipped distribution, so re-tuning `03` §2's weights moves no client assertion.
    /// ⚠️ The generator's own constraints still place the elites and the boss, so a board is never
    /// only these three.
    /// </remarks>
    internal static IReadOnlyList<string> FixtureStageTiles { get; } =
        ["TILE_EMPTY", "TILE_ENEMY", "TILE_SHRINE"];

    /// <summary>The boss the fixture chapter authors, for the last node of the track.</summary>
    internal const string FixtureBossId = "BOSS_FIXTURE";

    private static ContentDocument Chapter(int chapterId, IReadOnlyList<int> stageLengths) =>
        new($"{ChaptersDirectory}CH_{chapterId:00}_FIXTURE.json", ContentValue.Object(
        [
            new KeyValuePair<string, ContentValue>("id", ContentValue.Number(chapterId)),
            new KeyValuePair<string, ContentValue>(
                StageLengthsMember,
                ContentValue.Array(stageLengths.Select(length => ContentValue.Number(length)))),
            new KeyValuePair<string, ContentValue>(
                "eliteCount",
                ContentValue.Array(stageLengths.Select(_ => ContentValue.Number(1)))),
            new KeyValuePair<string, ContentValue>(
                "tileWeights",
                ContentValue.Array(FixtureStageTiles.Select(tile => ContentValue.Object(
                [
                    new KeyValuePair<string, ContentValue>(tile, ContentValue.Number(1)),
                ])))),
            new KeyValuePair<string, ContentValue>("bossId", ContentValue.Text(FixtureBossId)),
        ]));

    /// <summary>
    /// The fork-bias multipliers, which are global rather than per-chapter and so live in their own
    /// document.
    /// </summary>
    /// <remarks>
    /// The shipped pair (2.5 / 0.2). Transcribed rather than read, because what these cases are about
    /// is the SCREEN: a fixture that read the shipped tuning would fail here when that tuning was
    /// re-balanced, which is a board-generation change and not a client one.
    /// </remarks>
    private static ContentDocument BoardGeneration() =>
        new("tuning/currencies.json", ContentValue.Object(
        [
            new KeyValuePair<string, ContentValue>("boardGeneration", ContentValue.Object(
            [
                new KeyValuePair<string, ContentValue>(
                    "forkBiasPlusMultiplier", ContentValue.Number(2.5m)),
                new KeyValuePair<string, ContentValue>(
                    "forkBiasMinusMultiplier", ContentValue.Number(0.2m)),
            ])),
        ]));

    private static IReadOnlyList<ContentDocument> Locales()
    {
        var keys = BoardKeys.Concat(BoardTileKinds.NameKeys).ToArray();

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
