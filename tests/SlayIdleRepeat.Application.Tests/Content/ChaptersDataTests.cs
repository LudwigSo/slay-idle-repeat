using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// `03` §2.1 / §4, `03` §7a, `14` §6 — <c>content/chapters/</c> as the content pipeline sees it:
/// M3-14's two chapter files (<c>CH_01_GREENWOOD_VALE.json</c>, <c>CH_02_ASHEN_MIRE.json</c>) and
/// <b>R34</b>, the cross-file rule that keeps a chapter's <c>enemyPool</c> in step with
/// <c>enemies.json</c>'s producer-side transcription.
/// </summary>
/// <remarks>
/// Chapters 3-8 are M11-02's rows, not this suite's: the schema already spans <c>id</c> 1-8 and
/// nothing here asserts a chapter count of eight — only that the two chapters M3-14 actually
/// authored are shaped and cross-referenced correctly.
/// </remarks>
public sealed class ChaptersDataTests
{
    private const string ChapterOne = "content/chapters/CH_01_GREENWOOD_VALE.json";
    private const string ChapterTwo = "content/chapters/CH_02_ASHEN_MIRE.json";
    private const string EnemiesDocument = "content/enemies/enemies.json";

    private static readonly string[] TileKeys =
    [
        "TILE_ENEMY", "TILE_EMPTY", "TILE_SHRINE", "TILE_TREASURE", "TILE_EVENT", "TILE_MINIGAME",
        "TILE_CURSE", "TILE_SHOP", "TILE_ELITE", "TILE_CACHE", "TILE_PORTAL", "TILE_DICE_FORGE",
    ];

    private static ContentSnapshot Data() => ContentLoader.Load(RepoData.Source()).Require();

    [Fact]
    public void The_directory_is_governed_by_its_own_schema()
    {
        ContentLayout.SchemaFor(ChapterOne).ShouldBe("schema/chapter.schema.json");
        ContentLayout.SchemaFor(ChapterTwo).ShouldBe("schema/chapter.schema.json");

        ContentLoader.SchemasAwaitingContent.ShouldNotContain(
            "schema/chapter.schema.json",
            "the exemption expires the moment content/chapters/ holds its first file, and it now holds two");
    }

    [Fact]
    public void The_shipped_data_set_still_validates_with_both_chapter_files_present()
    {
        ContentLoader.Load(RepoData.Source()).Issues.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(ChapterOne, 1, "BOSS_THORNMAW")]
    [InlineData(ChapterTwo, 2, "BOSS_GULGROT")]
    public void Each_chapter_carries_its_own_id_and_boss(string document, int id, string bossId)
    {
        var data = Data();

        data.ReadInt32($"{document}#/id").ShouldBe(id);
        data.ReadText($"{document}#/bossId").ShouldBe(bossId, "17 §1.2's chapter column names this boss");
    }

    /// <summary>
    /// `03` §2.1's reference distribution, transcribed verbatim onto Chapter 1 Stage 1 — the one
    /// worked example in the design docs, so this pins the numbers rather than merely the sum.
    /// </summary>
    [Fact]
    public void Chapter_1_stage_1_matches_03_section_2_1s_reference_distribution_exactly()
    {
        var data = Data();
        const string stage = ChapterOne + "#/tileWeights/0/";

        data.ReadInt32(stage + "TILE_ENEMY").ShouldBe(34);
        data.ReadInt32(stage + "TILE_EMPTY").ShouldBe(12);
        data.ReadInt32(stage + "TILE_SHRINE").ShouldBe(9);
        data.ReadInt32(stage + "TILE_TREASURE").ShouldBe(8);
        data.ReadInt32(stage + "TILE_EVENT").ShouldBe(8);
        data.ReadInt32(stage + "TILE_MINIGAME").ShouldBe(7);
        data.ReadInt32(stage + "TILE_CURSE").ShouldBe(6);
        data.ReadInt32(stage + "TILE_SHOP").ShouldBe(5);
        data.ReadInt32(stage + "TILE_ELITE").ShouldBe(4);
        data.ReadInt32(stage + "TILE_CACHE").ShouldBe(3);
        data.ReadInt32(stage + "TILE_PORTAL").ShouldBe(2);
        data.ReadInt32(stage + "TILE_DICE_FORGE").ShouldBe(2);
    }

    /// <summary>
    /// `03` §2.1: "Later chapters shift weight from TILE_EMPTY toward TILE_ELITE and TILE_CURSE."
    /// Every one of the six authored stages (Ch1 S2-3, Ch2 S1-3) must honour the trend, strictly,
    /// against the immediately preceding stage — a guard that fails on a flat or reversed step.
    /// </summary>
    [Fact]
    public void Empty_falls_and_elite_and_curse_rise_monotonically_across_every_authored_stage()
    {
        var data = Data();
        var stages = new[]
        {
            $"{ChapterOne}#/tileWeights/0/", $"{ChapterOne}#/tileWeights/1/", $"{ChapterOne}#/tileWeights/2/",
            $"{ChapterTwo}#/tileWeights/0/", $"{ChapterTwo}#/tileWeights/1/", $"{ChapterTwo}#/tileWeights/2/",
        };

        for (var i = 1; i < stages.Length; i++)
        {
            data.ReadInt32(stages[i] + "TILE_EMPTY").ShouldBeLessThan(
                data.ReadInt32(stages[i - 1] + "TILE_EMPTY"), $"stage {i}: TILE_EMPTY must keep falling");
            data.ReadInt32(stages[i] + "TILE_ELITE").ShouldBeGreaterThan(
                data.ReadInt32(stages[i - 1] + "TILE_ELITE"), $"stage {i}: TILE_ELITE must keep rising");
            data.ReadInt32(stages[i] + "TILE_CURSE").ShouldBeGreaterThan(
                data.ReadInt32(stages[i - 1] + "TILE_CURSE"), $"stage {i}: TILE_CURSE must keep rising");
        }
    }

    /// <summary>Every authored stage's weight table sums to 100, matching `03` §2.1's own row.</summary>
    [Theory]
    [InlineData(ChapterOne)]
    [InlineData(ChapterTwo)]
    public void Every_stage_of_a_chapter_sums_its_tile_weights_to_100(string document)
    {
        var data = Data();

        for (var stageIndex = 0; stageIndex < 3; stageIndex++)
        {
            var total = TileKeys.Sum(key => data.ReadInt32($"{document}#/tileWeights/{stageIndex}/{key}"));
            total.ShouldBe(100, $"{document} stage {stageIndex}");
        }
    }

    // ─────────────────────────────────────────────────────── R34

    /// <summary>
    /// 🔒 <b>R34</b> — a chapter file's <c>enemyPool</c> must be exactly the weight table
    /// <c>enemies.json#/chapterPools</c> already transcribes for the same chapter id.
    /// </summary>
    [Theory]
    [InlineData(ChapterOne, 0)]
    [InlineData(ChapterTwo, 1)]
    public void EnemyPool_mirrors_the_producer_side_transcription_in_enemies_json(string document, int poolIndex)
    {
        var data = Data();
        var chapterPool = data.Read($"{document}#/enemyPool");
        var producerPool = data.Read($"{EnemiesDocument}#/chapterPools/{poolIndex}/weights");

        chapterPool.MemberNames.OrderBy(n => n, StringComparer.Ordinal).ShouldBe(
            producerPool.MemberNames.OrderBy(n => n, StringComparer.Ordinal));

        foreach (var archetype in chapterPool.MemberNames)
        {
            data.ReadInt32($"{document}#/enemyPool/{archetype}").ShouldBe(
                data.ReadInt32($"{EnemiesDocument}#/chapterPools/{poolIndex}/weights/{archetype}"),
                $"'{archetype}' must agree between {document}#/enemyPool and {EnemiesDocument}");
        }
    }

    /// <summary>
    /// 🔒 <b>R34</b> bites — S1: a guard that cannot fail is a defect, so this edits a real weight
    /// away from the producer's and proves the rule actually reports it.
    /// </summary>
    [Fact]
    public void R34_refuses_a_chapter_enemy_pool_that_disagrees_with_enemies_json()
    {
        var source = RepoData.SourceWithEdit(
            ChapterOne, "\"GRUNT\": 40, \"SWARM\": 20", "\"GRUNT\": 41, \"SWARM\": 20");

        var issues = ContentLoader.Load(source).Issues;

        issues.ShouldContain(i =>
            i.Location.Contains(ChapterOne, StringComparison.Ordinal) &&
            i.Location.Contains("enemyPool", StringComparison.Ordinal),
            "a chapter enemyPool that drifted from enemies.json#/chapterPools must be reported, naming the chapter file");
    }

    /// <summary>The elitePool a chapter file authors is exactly enemies.json's own for that chapter.</summary>
    [Theory]
    [InlineData(ChapterOne, 0)]
    [InlineData(ChapterTwo, 1)]
    public void ElitePool_matches_the_chapters_two_biome_elites_in_enemies_json(string document, int poolIndex)
    {
        var data = Data();
        var chapterElites = data.Read($"{document}#/elitePool").Items.Select(i => i.AsText()).ToArray();
        var producerElites = data.Read($"{EnemiesDocument}#/chapterPools/{poolIndex}/elitePool")
            .Items.Select(i => i.AsText()).ToArray();

        chapterElites.ShouldBe(producerElites);
    }

    [Fact]
    public void Chapter_1_has_no_unlock_condition_and_chapter_2_requires_clearing_chapter_1_normal()
    {
        var data = Data();

        data.Read($"{ChapterOne}#/unlockCondition").Kind.ShouldBe(ContentValueKind.Unauthorised);

        data.ReadInt32($"{ChapterTwo}#/unlockCondition/clearChapter").ShouldBe(1);
        data.ReadText($"{ChapterTwo}#/unlockCondition/tier").ShouldBe("NORMAL");
    }

    /// <summary>`29` §4.1 — powerTarget must equal parPower[id].NORMAL, which par_power.json ships today.</summary>
    [Theory]
    [InlineData(ChapterOne, 1000)]
    [InlineData(ChapterTwo, 2000)]
    public void PowerTarget_equals_the_matching_par_power_normal_cell(string document, int expected)
    {
        Data().ReadInt32($"{document}#/powerTarget").ShouldBe(expected);
    }

    [Theory]
    [InlineData(ChapterOne, "loc.chapter.1.name")]
    [InlineData(ChapterTwo, "loc.chapter.2.name")]
    public void DisplayName_resolves_in_both_locales(string document, string key)
    {
        var data = Data();

        data.ReadText($"{document}#/displayName").ShouldBe(key);
        data.ReadText($"loc/en.json#/strings/{key}").ShouldNotBeNullOrWhiteSpace();
        data.ReadText($"loc/de.json#/strings/{key}").ShouldContain("##TODO_DE##");
    }
}
