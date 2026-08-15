using SlayIdleRepeat.Core.Rules.Board;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

/// <summary>Board test fixtures. <see cref="ChapterOneConfig"/> mirrors the shipped chapter 1 config exactly, not invented values.</summary>
internal static class BoardFixtures
{
    /// <summary>Chapter 1's real board config: stageLengths [12,14,16], eliteCount [1,2,2].</summary>
    public static ChapterBoardConfig ChapterOneConfig() => ChapterBoardConfig.From(
        chapterId: 1,
        stageLengths: new[] { 12, 14, 16 },
        eliteCount: new[] { 1, 2, 2 },
        tileWeights: new[]
        {
            Weights(enemy: 34, empty: 12, shrine: 9, treasure: 8, evt: 8, minigame: 7, curse: 6, shop: 5, elite: 4, cache: 3, portal: 2, diceForge: 2),
            Weights(enemy: 34, empty: 10, shrine: 9, treasure: 8, evt: 8, minigame: 7, curse: 7, shop: 5, elite: 5, cache: 3, portal: 2, diceForge: 2),
            Weights(enemy: 34, empty: 8, shrine: 9, treasure: 8, evt: 8, minigame: 7, curse: 8, shop: 5, elite: 6, cache: 3, portal: 2, diceForge: 2),
        },
        bossId: "BOSS_THORNMAW");

    /// <summary>A minimal, tiny-stage config for tests that want short, easy-to-reason-about stages.</summary>
    public static ChapterBoardConfig TinyConfig(int chapterId = 9) => ChapterBoardConfig.From(
        chapterId: chapterId,
        stageLengths: new[] { 12, 12, 12 },
        eliteCount: new[] { 1, 1, 1 },
        tileWeights: new[]
        {
            Weights(enemy: 34, empty: 12, shrine: 9, treasure: 8, evt: 8, minigame: 7, curse: 6, shop: 5, elite: 4, cache: 3, portal: 2, diceForge: 2),
            Weights(enemy: 34, empty: 12, shrine: 9, treasure: 8, evt: 8, minigame: 7, curse: 6, shop: 5, elite: 4, cache: 3, portal: 2, diceForge: 2),
            Weights(enemy: 34, empty: 12, shrine: 9, treasure: 8, evt: 8, minigame: 7, curse: 6, shop: 5, elite: 4, cache: 3, portal: 2, diceForge: 2),
        },
        bossId: "BOSS_TEST");

    private static Dictionary<TileKind, double> Weights(
        double enemy, double empty, double shrine, double treasure, double evt, double minigame,
        double curse, double shop, double elite, double cache, double portal, double diceForge) => new()
    {
        [TileKind.Enemy] = enemy,
        [TileKind.Empty] = empty,
        [TileKind.Shrine] = shrine,
        [TileKind.Treasure] = treasure,
        [TileKind.Event] = evt,
        [TileKind.Minigame] = minigame,
        [TileKind.Curse] = curse,
        [TileKind.Shop] = shop,
        [TileKind.Elite] = elite,
        [TileKind.Cache] = cache,
        [TileKind.Portal] = portal,
        [TileKind.DiceForge] = diceForge,
    };
}
