using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// Hermetic <c>content/chapters/*.json</c> fixtures — <c>Rules.Board.ChapterBoardTuning</c> reads
/// this shape to build the <see cref="Rules.Board.ChapterBoardConfig"/> a run's board generates from.
/// </summary>
/// <remarks>
/// <c>Core.Tests</c> is hermetic, so this mirrors the shipped
/// <c>content/chapters/CH_01_GREENWOOD_VALE.json</c> rather than reading it — the same numbers
/// <c>Rules.Board.Tests.BoardFixtures.ChapterOneConfig</c> already transcribes, so a handler-level
/// test and a Rules-level test agree on what chapter 1 actually is.
/// </remarks>
internal static class ChapterDocuments
{
    /// <summary>Where chapter 1's document lives.</summary>
    internal const string ChapterOnePath = "content/chapters/CH_01_TEST.json";

    /// <summary>Where the handler-test-only tiny chapter (2) lives — see <see cref="TinyChapterDocument"/>.</summary>
    internal const string TinyChapterPath = "content/chapters/CH_02_TEST.json";

    /// <summary>Room for at least one fork per stage: 12+ nodes (see <c>BoardGenerator</c>'s candidate window).</summary>
    internal const int TinyChapterStageLength = 12;

    /// <summary>A content set holding chapter 1's board-relevant document.</summary>
    internal static ContentSnapshot ChapterOne { get; } = new(
        ContentVersion.FromHex(new string('b', ContentVersion.HexLength)),
        [Document(chapterId: 1, ChapterOnePath), ForkBiasDocument()]);

    /// <summary>
    /// Fork-bias multipliers, global rather than chapter-scoped —
    /// <see cref="Rules.Board.ChapterBoardTuning.Read"/> reads them out of
    /// <c>tuning/currencies.json#/boardGeneration</c> alongside the chapter document. Mirrors
    /// <see cref="TuningDocuments.ShippedForkBiasPlusMultiplier"/>/<c>MinusMultiplier</c>.
    /// </summary>
    private static ContentDocument ForkBiasDocument() =>
        new("tuning/currencies.json", ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["boardGeneration"] = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
            {
                ["forkBiasPlusMultiplier"] = ContentValue.Number((decimal)TuningDocuments.ShippedForkBiasPlusMultiplier),
                ["forkBiasMinusMultiplier"] = ContentValue.Number((decimal)TuningDocuments.ShippedForkBiasMinusMultiplier),
            }),
        }));

    /// <summary>
    /// A small, evenly-weighted chapter document for <c>Handlers</c> tests that need to reason
    /// about an actual generated board (junction pauses, boss-exact) without hand-decoding chapter
    /// 1's real numbers — the same shape <c>Rules.Board.Tests.BoardFixtures.TinyConfig</c> gives the
    /// Rules-level generator tests, transcribed as content here so <c>ChapterBoardTuning.Read</c> is
    /// exercised too.
    /// </summary>
    internal static ContentDocument TinyChapterDocument(int chapterId) =>
        new(TinyChapterPath, ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["id"] = ContentValue.Number(chapterId),
            ["stageLengths"] = ContentValue.Array(Enumerable.Repeat(TinyChapterStageLength, 3).Select(n => ContentValue.Number(n))),
            ["eliteCount"] = ContentValue.Array(Enumerable.Repeat(1, 3).Select(n => ContentValue.Number(n))),
            ["tileWeights"] = ContentValue.Array(Enumerable.Repeat(
                Weights(enemy: 34, empty: 12, shrine: 9, treasure: 8, evt: 8, minigame: 7, curse: 6, shop: 5, elite: 4, cache: 3, portal: 2, diceForge: 2),
                3)),
            ["forksPerStage"] = ForksPerStage(),
            ["bossId"] = ContentValue.Text("BOSS_TEST"),
        }));

    /// <summary>Builds one chapter's document with the shipped chapter 1 numbers, under a chosen id/path.</summary>
    internal static ContentDocument Document(int chapterId, string path) =>
        new(path, ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["id"] = ContentValue.Number(chapterId),
            ["stageLengths"] = ContentValue.Array(new[] { 12, 14, 16 }.Select(n => ContentValue.Number(n))),
            ["eliteCount"] = ContentValue.Array(new[] { 1, 2, 2 }.Select(n => ContentValue.Number(n))),
            ["tileWeights"] = ContentValue.Array(new[]
            {
                Weights(enemy: 34, empty: 12, shrine: 9, treasure: 8, evt: 8, minigame: 7, curse: 6, shop: 5, elite: 4, cache: 3, portal: 2, diceForge: 2),
                Weights(enemy: 34, empty: 10, shrine: 9, treasure: 8, evt: 8, minigame: 7, curse: 7, shop: 5, elite: 5, cache: 3, portal: 2, diceForge: 2),
                Weights(enemy: 34, empty: 8, shrine: 9, treasure: 8, evt: 8, minigame: 7, curse: 8, shop: 5, elite: 6, cache: 3, portal: 2, diceForge: 2),
            }),
            ["forksPerStage"] = ForksPerStage(),
            ["bossId"] = ContentValue.Text("BOSS_THORNMAW"),
        }));

    /// <summary>
    /// The shipped chapters' authored fork density: `03` §1's one-or-two per stage, stated as
    /// content since `16` D69 moved it out of <c>BoardGenerator</c>.
    /// </summary>
    internal static ContentValue ForksPerStage(int minimum = 1, int maximum = 2) =>
        ContentValue.Array(Enumerable.Repeat(
            ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
            {
                ["min"] = ContentValue.Number(minimum),
                ["max"] = ContentValue.Number(maximum),
            }),
            3));

    private static ContentValue Weights(
        double enemy, double empty, double shrine, double treasure, double evt, double minigame,
        double curse, double shop, double elite, double cache, double portal, double diceForge) =>
        ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["TILE_ENEMY"] = ContentValue.Number((decimal)enemy),
            ["TILE_EMPTY"] = ContentValue.Number((decimal)empty),
            ["TILE_SHRINE"] = ContentValue.Number((decimal)shrine),
            ["TILE_TREASURE"] = ContentValue.Number((decimal)treasure),
            ["TILE_EVENT"] = ContentValue.Number((decimal)evt),
            ["TILE_MINIGAME"] = ContentValue.Number((decimal)minigame),
            ["TILE_CURSE"] = ContentValue.Number((decimal)curse),
            ["TILE_SHOP"] = ContentValue.Number((decimal)shop),
            ["TILE_ELITE"] = ContentValue.Number((decimal)elite),
            ["TILE_CACHE"] = ContentValue.Number((decimal)cache),
            ["TILE_PORTAL"] = ContentValue.Number((decimal)portal),
            ["TILE_DICE_FORGE"] = ContentValue.Number((decimal)diceForge),
        });
}
