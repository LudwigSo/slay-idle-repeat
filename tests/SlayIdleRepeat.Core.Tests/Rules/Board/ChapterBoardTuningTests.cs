using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Tests.Content;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

public sealed class ChapterBoardTuningTests
{
    [Fact]
    public void Reads_stage_lengths_elite_counts_and_boss_id()
    {
        var config = ChapterBoardTuning.Read(ChapterDocuments.ChapterOne, chapterId: 1);

        config.ChapterId.ShouldBe(1);
        config.StageLengths.ShouldBe(new[] { 12, 14, 16 });
        config.EliteCount.ShouldBe(new[] { 1, 2, 2 });
        config.BossId.ShouldBe("BOSS_THORNMAW");
    }

    [Fact]
    public void Reads_every_stages_tile_weights()
    {
        var config = ChapterBoardTuning.Read(ChapterDocuments.ChapterOne, chapterId: 1);

        config.TileWeights.Count.ShouldBe(3);
        config.TileWeights[0][TileKind.Enemy].ShouldBe(34.0);
        config.TileWeights[0][TileKind.Portal].ShouldBe(2.0);
        config.TileWeights[2][TileKind.Curse].ShouldBe(8.0, "the third stage's own row, not the first's.");
    }

    [Fact]
    public void An_unknown_chapter_id_is_a_missing_content_exception()
    {
        Should.Throw<MissingContentException>(() =>
            ChapterBoardTuning.Read(ChapterDocuments.ChapterOne, chapterId: 999));
    }
}
