using System.Text;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The seam between <c>greenwood_enemies.blend</c> and the build, and between the shipped models
/// and the chapter that draws them — measured from the files rather than described.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>The non-metallic rule is the same rule <see cref="HeroWeaponKitTests"/> guards, and it
/// applies here for the same reason.</b> This build lights 3D with one directional key, one fill
/// and flat ambient, with no reflection probe and no sky, so a true metal has nothing to reflect
/// and renders BLACK. It is asserted per file rather than once over the kit because these are baked
/// independently, nine times, by a loop that could lose it on any one of them.
/// </para>
/// <para>
/// 🔴 <b>Feet on the floor is not cosmetic.</b> The engine puts an enemy on a board tile at the
/// origin. A model whose lowest point is a centimetre above zero hovers, and one a centimetre below
/// has its ankles in the tile. <c>greenwood_kit.settle</c> is what puts them there and
/// <c>greenwood_kit.flatten</c> is what stops the export from moving them again by leaving the
/// offset on the root node, and neither leaves any trace in the repository except these bytes.
/// </para>
/// <para>
/// 🔴 <b>The cast answers to the chapter data.</b> `CH_01_GREENWOOD_VALE.json` names the mini-boss
/// and boss ids, and the model filenames are those ids transliterated. Nothing in the build checks
/// that, so renaming a boss in data would leave a model nothing ever loads and a fight with no
/// model — the failure this reads for.
/// </para>
/// <para>
/// 🔒 <b>The files are read as the bytes they are</b>, so this needs no engine and stays inside a
/// suite whose premise is that nothing here is a Node. Same reader as
/// <see cref="HeroWeaponKitTests"/>, restated rather than shared: the alternative was making one of
/// the two suites depend on the other's private helpers for a twenty-line glTF header walk.
/// </para>
/// </remarks>
public sealed class GreenwoodCastTests
{
    private const string ArtDirectory = "src/SlayIdleRepeat.Client/game/art";

    private const string ChapterFile = "content/chapters/CH_01_GREENWOOD_VALE.json";

    /// <summary>The six standard enemies, keyed by the archetype each one is drawn as.</summary>
    public static TheoryData<string, string> StandardEnemies() =>
        new()
        {
            { "chr_enemy_greenwood_grunt.glb", "GRUNT" },
            { "chr_enemy_greenwood_swarm.glb", "SWARM" },
            { "chr_enemy_greenwood_brute.glb", "BRUTE" },
            { "chr_enemy_greenwood_skirmisher.glb", "SKIRMISHER" },
            { "chr_enemy_greenwood_warden.glb", "WARDEN" },
            { "chr_enemy_greenwood_caster.glb", "CASTER" },
        };

    /// <summary>Every model the chapter ships: the six, the two elites and the boss.</summary>
    public static TheoryData<string> Cast()
    {
        var data = new TheoryData<string>();

        foreach (var row in StandardEnemies())
        {
            data.Add((string)row[0]);
        }

        data.Add("chr_elite_thorn_sentinel.glb");
        data.Add("chr_elite_mossback_alpha.glb");
        data.Add("chr_boss_thornmaw.glb");

        return data;
    }

    [Theory]
    [MemberData(nameof(Cast))]
    public void NoMaterialInTheCastIsMetallic(string model)
    {
        var materials = Gltf(model).RootElement.GetProperty("materials");

        materials.GetArrayLength().ShouldBeGreaterThan(0, $"{model} ships no material at all.");

        foreach (var material in materials.EnumerateArray())
        {
            var name = material.TryGetProperty("name", out var n) ? n.GetString() : "(unnamed)";

            // Absent means 1.0 in glTF, not 0.0, so an unstated factor is a fully metallic
            // material — the exact failure this reads for, and the reason the check is not
            // "if it says anything, it says zero".
            material.TryGetProperty("pbrMetallicRoughness", out var pbr).ShouldBeTrue(
                $"'{name}' in {model} states no pbrMetallicRoughness block, so its metallic " +
                "factor defaults to 1.0 and it will render black under this build's lighting.");

            pbr.TryGetProperty("metallicFactor", out var metallic).ShouldBeTrue(
                $"'{name}' in {model} leaves metallicFactor unstated, which glTF reads as 1.0. " +
                "This build has no reflection probe and no sky, so it would render black.");

            metallic.GetDouble().ShouldBe(
                0.0,
                $"'{name}' in {model} is metallic, and a metal with nothing to reflect renders " +
                "black here. Tusks, thorn plate and pollen are bright albedo at low roughness.");
        }
    }

    [Theory]
    [MemberData(nameof(Cast))]
    public void EveryCreatureDrawsInOneCall(string model)
    {
        var meshes = Gltf(model).RootElement.GetProperty("meshes");

        meshes.GetArrayLength().ShouldBe(
            1,
            $"{model} holds more than one mesh. A chapter-one fight puts up to five enemies on " +
            "screen at once, so each is baked down to a single material over a single mesh and " +
            "costs one draw call.");

        meshes[0].GetProperty("primitives").GetArrayLength().ShouldBe(
            1,
            $"The mesh in {model} is split across primitives, which is one draw call each. That " +
            "happens when the bake leaves more than one material on the joined object.");
    }

    [Theory]
    [MemberData(nameof(Cast))]
    public void EveryCreatureStandsOnTheOrigin(string model)
    {
        var gltf = Gltf(model);
        var root = gltf.RootElement.GetProperty("nodes")[0];

        // A translated root node is not wrong glTF, but it means the mesh inside it is authored
        // off the floor and the offset lives somewhere the modelling source cannot see. The hero
        // has an identity root; so do these.
        root.TryGetProperty("translation", out _).ShouldBeFalse(
            $"The root node of {model} carries a translation, so its geometry is not authored " +
            "where it stands. greenwood_kit.flatten() bakes object transforms into the mesh " +
            "precisely so this stays empty.");

        var bounds = PositionBounds(gltf);

        // glTF is Y-up. One millimetre either way: the floor is decided by the subdivided
        // surface and then moved again by the decimator, so it lands near zero rather than on it.
        bounds.MinY.ShouldBe(
            0.0,
            0.001,
            $"{model} does not stand on its own origin — its lowest point is at {bounds.MinY:0.###}. " +
            "The engine drops an enemy onto a board tile at zero, so it would hover or sink by " +
            "exactly that much. greenwood_kit.settle() is what puts it there.");

        bounds.MaxY.ShouldBeGreaterThan(
            0.5,
            $"{model} is less than half a unit tall, which is smaller than the smallest thing in " +
            "the cast. Something collapsed in the bake.");
    }

    [Theory]
    [MemberData(nameof(StandardEnemies))]
    public void EveryStandardEnemyIsAnArchetypeTheChapterActuallyDraws(string model, string archetype)
    {
        _ = Gltf(model);

        var pool = Chapter().RootElement.GetProperty("enemyPool");

        pool.TryGetProperty(archetype, out var weight).ShouldBeTrue(
            $"{model} is modelled as the {archetype} archetype, and CH_01_GREENWOOD_VALE names no " +
            "such archetype in its enemyPool. Either the pool was renamed or the model is for a " +
            "chapter that does not exist.");

        weight.GetInt32().ShouldBeGreaterThan(
            0,
            $"{model} is modelled as the {archetype} archetype, which chapter one draws at weight " +
            "zero — the model would never appear. Six of the pool's eight archetypes are " +
            "modelled, and they are meant to be the six the chapter actually rolls.");
    }

    [Fact]
    public void TheMiniBossesAndBossTheChapterNamesAllHaveAModel()
    {
        var chapter = Chapter().RootElement;

        var named = chapter.GetProperty("miniBossIds").EnumerateArray()
            .Select(id => id.GetString() ?? "")
            .Append(chapter.GetProperty("bossId").GetString() ?? "")
            .ToList();

        named.ShouldNotBeEmpty(
            "CH_01_GREENWOOD_VALE names no mini-bosses and no boss, so either the keys were " +
            "renamed or this case stopped reading them — and a green run would mean nothing.");

        foreach (var id in named)
        {
            // EL_THORN_SENTINEL -> chr_elite_thorn_sentinel.glb, BOSS_THORNMAW -> chr_boss_thornmaw.glb
            var body = id.ToLowerInvariant();
            var file = body.StartsWith("el_", StringComparison.Ordinal)
                ? "chr_elite_" + body[3..] + ".glb"
                : "chr_boss_" + body["boss_".Length..] + ".glb";

            File.Exists(Path.Combine(RepoPaths.RepositoryRoot, ArtDirectory, file)).ShouldBeTrue(
                $"Chapter one fights '{id}' and there is no '{file}' to draw it with. The cast is " +
                "built by assets/source/greenwood_kit.py and exported by greenwood_export.py; a " +
                "rename on either side of this leaves a fight with no model.");
        }
    }

    private static (double MinX, double MaxX, double MinY, double MaxY, double MinZ, double MaxZ)
        PositionBounds(JsonDocument gltf)
    {
        var root = gltf.RootElement;
        var accessor = root.GetProperty("meshes")[0].GetProperty("primitives")[0]
            .GetProperty("attributes").GetProperty("POSITION").GetInt32();
        var element = root.GetProperty("accessors")[accessor];
        var min = element.GetProperty("min").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        var max = element.GetProperty("max").EnumerateArray().Select(v => v.GetDouble()).ToArray();

        return (min[0], max[0], min[1], max[1], min[2], max[2]);
    }

    private static JsonDocument Chapter()
    {
        var path = Path.Combine(RepoPaths.ContentDataRoot, ChapterFile);

        File.Exists(path).ShouldBeTrue($"No chapter file at '{path}'.");

        return JsonDocument.Parse(File.ReadAllText(path));
    }

    /// <summary>The JSON chunk of a binary glTF file.</summary>
    /// <remarks>
    /// A <c>.glb</c> is a 12-byte header and then length-prefixed chunks, the first of which is
    /// always the JSON. Twenty lines of reader, against pulling a glTF package into a suite that
    /// only ever wants to know what is in the file.
    /// </remarks>
    private static JsonDocument Gltf(string fileName)
    {
        var path = Path.Combine(RepoPaths.RepositoryRoot, ArtDirectory, fileName);

        File.Exists(path).ShouldBeTrue(
            $"No '{fileName}' at '{path}'. The chapter-one cast is exported out of " +
            "assets/source/greenwood_enemies.blend by assets/source/greenwood_export.py.");

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var magic = reader.ReadUInt32();
        magic.ShouldBe(0x46546C67u, $"'{fileName}' is not a binary glTF file.");
        _ = reader.ReadUInt32();
        _ = reader.ReadUInt32();

        var chunkLength = reader.ReadUInt32();
        var chunkType = reader.ReadUInt32();
        chunkType.ShouldBe(0x4E4F534Au, $"The first chunk of '{fileName}' is not JSON.");

        return JsonDocument.Parse(Encoding.UTF8.GetString(reader.ReadBytes((int)chunkLength)));
    }
}
