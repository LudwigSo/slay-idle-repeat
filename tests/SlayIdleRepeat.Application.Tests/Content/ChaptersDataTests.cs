using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// Tests <c>content/chapters/</c>'s two shipped chapter files and <b>R34</b>, the cross-file rule
/// that keeps a chapter's <c>enemyPool</c> in step with <c>enemies.json</c>'s transcription.
/// </summary>
/// <remarks>
/// The schema spans chapter ids 1-8, but only the two chapters actually authored so far are
/// asserted here; this suite does not assume a chapter count of eight.
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

    /// <summary>Chapter 1 Stage 1's tile weights, pinned exactly rather than just their sum.</summary>
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
    /// Empty tile weight must strictly fall and elite/curse weight must strictly rise across every
    /// authored stage, checked against the immediately preceding stage.
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

    /// <summary>Every authored stage's weight table sums to 100.</summary>
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

    /// <summary>R34 — a chapter file's enemyPool must exactly match enemies.json#/chapterPools for the same chapter id.</summary>
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

    /// <summary>R34 bites: edits a real weight away from the producer's and checks the rule reports it.</summary>
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

    /// <summary>Where the chapter documents live, as a path prefix rather than as two names.</summary>
    private const string ChaptersDirectory = "content/chapters/";

    /// <summary>
    /// How many chapter documents the shipped set holds today.
    /// </summary>
    /// <remarks>
    /// 🔴 The floor under the sweep below, and the reason it is a sweep at all. A pass over a
    /// directory says nothing when the directory came back empty: a renamed folder, a changed path
    /// prefix or a loader that stopped carrying these documents would all leave the assertion
    /// iterating nothing and reporting success. A chapter ADDED here should raise this number in the
    /// same edit that authors it — that is the moment the new chapter's own ids get checked.
    /// </remarks>
    private const int ShippedChapterCount = 2;

    /// <summary>
    /// EVERY chapter names the two mini-bosses its run fights, one per stage gate. Each is an elite
    /// of that chapter's own pool: a mini-boss is a predetermined elite, so an id from outside the
    /// pool would name an elite this chapter's biome never otherwise presents.
    /// </summary>
    /// <remarks>
    /// 🔒 Swept over the directory rather than stated per chapter. A rule spelled as one case per
    /// document is a rule the next document is authored without — and the ids this one checks are
    /// what a run puts in front of the player at both stage gates.
    /// </remarks>
    [Fact]
    public void Every_chapter_names_two_mini_bosses_drawn_from_its_own_elite_pool()
    {
        var data = Data();

        var documents = data.DocumentPaths
            .Where(path => path.StartsWith(ChaptersDirectory, StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        documents.Length.ShouldBe(
            ShippedChapterCount,
            "the sweep has to actually reach the chapter documents — an empty or mis-globbed " +
            "directory would satisfy every assertion below without reading a single id");

        foreach (var document in documents)
        {
            var miniBosses = data.Read($"{document}#/miniBossIds").Items.Select(i => i.AsText()).ToArray();
            var elites = data.Read($"{document}#/elitePool").Items.Select(i => i.AsText()).ToArray();

            miniBosses.Length.ShouldBe(
                2, $"{document}: the last node of stage 1 and of stage 2, and no others");
            miniBosses.ShouldBeUnique(
                $"{document}: two rows naming one elite is one mini-boss fought twice.");
            miniBosses.ShouldBeSubsetOf(
                elites,
                $"{document}: a mini-boss is a predetermined elite of this chapter's own pool.");
        }
    }

    [Fact]
    public void Chapter_1_has_no_unlock_condition_and_chapter_2_requires_clearing_chapter_1_normal()
    {
        var data = Data();

        data.Read($"{ChapterOne}#/unlockCondition").Kind.ShouldBe(ContentValueKind.Unauthorised);

        data.ReadInt32($"{ChapterTwo}#/unlockCondition/clearChapter").ShouldBe(1);
        data.ReadText($"{ChapterTwo}#/unlockCondition/tier").ShouldBe("NORMAL");
    }

    /// <summary>powerTarget must equal parPower[id].NORMAL, which par_power.json ships today.</summary>
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
