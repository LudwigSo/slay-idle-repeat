using System.Text;
using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

public sealed class EnemyModelCatalogueTests
{
    private const string Biome = "greenwood";
    private const string ClientDirectory = "src/SlayIdleRepeat.Client";
    private const string ChapterFile = "content/chapters/CH_01_GREENWOOD_VALE.json";

    private static readonly string[] CastIds =
    [
        "GRUNT", "SWARM", "BRUTE", "SKIRMISHER", "WARDEN", "CASTER",
        "EL_THORN_SENTINEL", "EL_MOSSBACK_ALPHA", "BOSS_THORNMAW",
    ];

    public static TheoryData<string> Cast()
    {
        var data = new TheoryData<string>();

        foreach (var id in CastIds)
        {
            data.Add(id);
        }

        return data;
    }

    [Theory]
    [InlineData("GRUNT", "res://game/art/chr_enemy_greenwood_grunt.glb")]
    [InlineData("SWARM", "res://game/art/chr_enemy_greenwood_swarm.glb")]
    [InlineData("BRUTE", "res://game/art/chr_enemy_greenwood_brute.glb")]
    [InlineData("SKIRMISHER", "res://game/art/chr_enemy_greenwood_skirmisher.glb")]
    [InlineData("WARDEN", "res://game/art/chr_enemy_greenwood_warden.glb")]
    [InlineData("CASTER", "res://game/art/chr_enemy_greenwood_caster.glb")]
    public void For_resolves_each_archetype_to_the_biomes_own_model(string archetype, string path) =>
        EnemyModelCatalogue.For(Biome, archetype).ShouldNotBeNull().ScenePath.ShouldBe(path);

    [Theory]
    [InlineData("EL_THORN_SENTINEL", "res://game/art/chr_elite_thorn_sentinel.glb")]
    [InlineData("EL_MOSSBACK_ALPHA", "res://game/art/chr_elite_mossback_alpha.glb")]
    [InlineData("BOSS_THORNMAW", "res://game/art/chr_boss_thornmaw.glb")]
    public void For_resolves_an_elite_or_boss_to_its_own_model(string identity, string path) =>
        EnemyModelCatalogue.For(Biome, identity).ShouldNotBeNull().ScenePath.ShouldBe(path);

    [Fact]
    public void For_has_a_model_for_every_identity_chapter_one_can_field()
    {
        var chapter = Chapter().RootElement;

        var fielded = chapter.GetProperty("enemyPool").EnumerateObject()
            .Where(p => p.Value.GetInt32() > 0)
            .Select(p => p.Name)
            .Concat(chapter.GetProperty("elitePool").EnumerateArray().Select(e => e.GetString() ?? ""))
            .Append(chapter.GetProperty("bossId").GetString() ?? "")
            .ToList();

        fielded.Count.ShouldBeGreaterThanOrEqualTo(9, "six archetypes, two elites and the boss.");

        fielded.Where(id => EnemyModelCatalogue.For(Biome, id) is null).ShouldBeEmpty(
            "chapter one can put each of these on the stage, and one with no model draws nothing.");
    }

    [Theory]
    [MemberData(nameof(Cast))]
    public void For_names_a_model_file_that_ships(string identity)
    {
        var model = EnemyModelCatalogue.For(Biome, identity).ShouldNotBeNull();

        File.Exists(LocalPathOf(model.ScenePath)).ShouldBeTrue(
            $"{identity} resolves to '{model.ScenePath}' and no such file ships.");
    }

    [Theory]
    [MemberData(nameof(Cast))]
    public void For_states_the_height_the_model_actually_stands(string identity)
    {
        var model = EnemyModelCatalogue.For(Biome, identity).ShouldNotBeNull();

        var maxY = PositionMaxY(Gltf(LocalPathOf(model.ScenePath)));

        ((double)model.HeightUnits).ShouldBe(
            maxY,
            0.05,
            $"a plate sits above the model's head; the glb's highest vertex is at {maxY:0.###}.");
    }

    [Theory]
    [InlineData("greenwood", "LEECH")]
    [InlineData("greenwood", "REAVER")]
    [InlineData("greenwood", "PETRIFIED_OAK")]
    [InlineData("ashlands", "GRUNT")]
    [InlineData("greenwood", "grunt")]
    public void For_answers_nothing_rather_than_a_stand_in_for_an_identity_it_has_no_model_for(
        string biome, string identity) =>
        EnemyModelCatalogue.For(biome, identity).ShouldBeNull();

    private static string LocalPathOf(string scenePath)
    {
        scenePath.ShouldStartWith("res://");

        return Path.Combine(RepoPaths.RepositoryRoot, ClientDirectory, scenePath["res://".Length..]);
    }

    private static JsonDocument Chapter()
    {
        var path = Path.Combine(RepoPaths.ContentDataRoot, ChapterFile);

        File.Exists(path).ShouldBeTrue($"No chapter file at '{path}'.");

        return JsonDocument.Parse(File.ReadAllText(path));
    }

    /// <summary>The highest vertex over every primitive of every mesh, so a second mesh cannot hide above the first.</summary>
    private static double PositionMaxY(JsonDocument gltf)
    {
        var root = gltf.RootElement;
        var accessors = root.GetProperty("accessors");
        var maxYs = root.GetProperty("meshes").EnumerateArray()
            .SelectMany(mesh => mesh.GetProperty("primitives").EnumerateArray())
            .Select(primitive => primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32())
            .Select(accessor => accessors[accessor].GetProperty("max")[1].GetDouble())
            .ToList();

        maxYs.ShouldNotBeEmpty("a model with no POSITION accessor has no height to measure.");

        return maxYs.Max();
    }

    /// <summary>The JSON chunk of a binary glTF file: a 12-byte header, then the length-prefixed JSON chunk.</summary>
    private static JsonDocument Gltf(string path)
    {
        File.Exists(path).ShouldBeTrue($"No model at '{path}'.");

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        reader.ReadUInt32().ShouldBe(0x46546C67u, $"'{path}' is not a binary glTF file.");
        _ = reader.ReadUInt32();
        _ = reader.ReadUInt32();

        var chunkLength = reader.ReadUInt32();
        reader.ReadUInt32().ShouldBe(0x4E4F534Au, $"The first chunk of '{path}' is not JSON.");

        return JsonDocument.Parse(Encoding.UTF8.GetString(reader.ReadBytes((int)chunkLength)));
    }
}
