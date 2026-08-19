using SlayIdleRepeat.Core.Rules.Board;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

public sealed class ChapterBoardConfigTests
{
    private static readonly Dictionary<TileKind, double> ValidWeights = new()
    {
        [TileKind.Enemy] = 34,
        [TileKind.Empty] = 12,
    };

    [Fact]
    public void Wrong_stage_length_count_is_rejected()
    {
        var ex = Should.Throw<ArgumentException>(() => ChapterBoardConfig.From(
            1, new[] { 12, 14 }, new[] { 1, 1, 1 },
            new[] { ValidWeights, ValidWeights, ValidWeights }, "BOSS_X"));

        ex.ParamName.ShouldBe("stageLengths");
    }

    [Fact]
    public void Wrong_elite_count_array_length_is_rejected()
    {
        var ex = Should.Throw<ArgumentException>(() => ChapterBoardConfig.From(
            1, new[] { 12, 14, 16 }, new[] { 1, 1 },
            new[] { ValidWeights, ValidWeights, ValidWeights }, "BOSS_X"));

        ex.ParamName.ShouldBe("eliteCount");
    }

    [Fact]
    public void An_elite_count_exceeding_its_stage_length_is_rejected()
    {
        var ex = Should.Throw<ArgumentException>(() => ChapterBoardConfig.From(
            1, new[] { 12, 14, 16 }, new[] { 100, 1, 1 },
            new[] { ValidWeights, ValidWeights, ValidWeights }, "BOSS_X"));

        ex.ParamName.ShouldBe("eliteCount");
    }

    [Fact]
    public void A_weight_table_stating_TILE_BOSS_is_rejected()
    {
        var withBoss = new Dictionary<TileKind, double>(ValidWeights) { [TileKind.Boss] = 1 };

        var ex = Should.Throw<ArgumentException>(() => ChapterBoardConfig.From(
            1, new[] { 12, 14, 16 }, new[] { 1, 1, 1 },
            new[] { withBoss, ValidWeights, ValidWeights }, "BOSS_X"));

        ex.ParamName.ShouldBe("tileWeights");
        ex.Message.ShouldContain("TILE_BOSS");
    }

    [Fact]
    public void An_all_zero_weight_table_is_rejected()
    {
        var allZero = new Dictionary<TileKind, double> { [TileKind.Enemy] = 0 };

        var ex = Should.Throw<ArgumentException>(() => ChapterBoardConfig.From(
            1, new[] { 12, 14, 16 }, new[] { 1, 1, 1 },
            new[] { allZero, ValidWeights, ValidWeights }, "BOSS_X"));

        ex.ParamName.ShouldBe("tileWeights");
    }

    [Fact]
    public void An_empty_boss_id_is_rejected()
    {
        Should.Throw<ArgumentException>(() => ChapterBoardConfig.From(
            1, new[] { 12, 14, 16 }, new[] { 1, 1, 1 },
            new[] { ValidWeights, ValidWeights, ValidWeights }, ""));
    }
}
