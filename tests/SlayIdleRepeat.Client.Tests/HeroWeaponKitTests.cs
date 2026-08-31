using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The seam between the <c>.blend</c> and the build, measured from the shipped <c>.glb</c> files
/// rather than described.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>These cases exist because the whole weapon system rests on facts no code can state.</b>
/// A weapon is mounted by looking up a node called <c>Socket_HandL</c> inside
/// <c>chr_hero_rogue.glb</c>. That name is produced in Blender, by
/// <c>hero_kit.sockets()</c>, and consumed in C#, by <c>Hero.cs</c>, and nothing between the two
/// checks that they agree. A rebake that renamed or dropped a socket would leave a hero who simply
/// draws unarmed, on every screen, with one pushed error nobody is watching for.
/// </para>
/// <para>
/// 🔴 <b>The non-metallic rule was a comment in four files and a promise in none.</b> This build
/// lights 3D with one directional key, one fill and flat ambient, and has no reflection probe and
/// no sky, so a true metal has nothing to reflect and renders BLACK — which is how the rule was
/// found the first time, by exporting blades at <c>metallic 0.9</c> and watching them disappear.
/// Brass and blades are bright albedo at low roughness instead. Every asset added to the kit
/// inherits that, so it is asserted over the whole kit rather than over the one asset that
/// prompted it.
/// </para>
/// <para>
/// 🔒 <b>The files are read as the bytes they are</b>, so this needs no engine and stays inside a
/// suite whose premise is that nothing here is a Node. Same reason
/// <see cref="TrackNodeGateContrastTests"/> parses a scene as text: the alternative was reading
/// the values by hand and being wrong.
/// </para>
/// <para>
/// ⚠️ <b>The socket names are read out of <c>Hero.cs</c> as source text rather than referenced.</b>
/// They are <c>internal const</c> on a <c>Node3D</c>, and naming that type here would put an
/// engine type in a suite that deliberately has none. Reading the literal keeps the case
/// honest about which two things it is comparing — the model, and the source that indexes it.
/// </para>
/// </remarks>
public sealed class HeroWeaponKitTests
{
    private const string ArtDirectory = "src/SlayIdleRepeat.Client/game/art";

    private const string ScenesDirectory = "src/SlayIdleRepeat.Client/game/scenes";

    private const string HeroModel = "chr_hero_rogue.glb";

    private const string HeroScript = "Hero.cs";

    /// <summary>Every model the hero kit ships, hero and weapons alike.</summary>
    public static TheoryData<string> KitModels() =>
        new() { HeroModel, "wpn_sword.glb", "wpn_dagger.glb" };

    /// <summary>The weapons, which are the models a socket has to be able to hold.</summary>
    public static TheoryData<string, string> Weapons() =>
        new() { { "wpn_sword.glb", "WpnSword.tscn" }, { "wpn_dagger.glb", "WpnDagger.tscn" } };

    /// <inheritdoc cref="Weapons"/>
    public static TheoryData<string> WeaponModels() => new() { "wpn_sword.glb", "wpn_dagger.glb" };

    [Fact]
    public void TheHeroModelCarriesEverySocketTheScriptNames()
    {
        var named = SocketNamesIn(Source(ScenesDirectory, HeroScript));

        named.ShouldNotBeEmpty(
            $"{HeroScript} names no socket paths at all, so either the constants were renamed or " +
            "this case stopped reading them — and a green run would then mean nothing.");

        var inModel = NodeNames(Gltf(HeroModel));

        foreach (var socket in named)
        {
            inModel.ShouldContain(
                socket,
                $"{HeroScript} mounts weapons by looking up '{socket}', and no node in " +
                $"{HeroModel} is called that. Every hand would draw empty. The sockets are made " +
                "by hero_kit.sockets() in assets/source/hero_kit.py and survive the export as " +
                $"plain glTF nodes; a rebake that lost them is what this reads. Model has: " +
                string.Join(", ", inModel));
        }
    }

    [Theory]
    [MemberData(nameof(KitModels))]
    public void NoMaterialInTheKitIsMetallic(string model)
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
                "black here. Brass and blades are bright albedo at low roughness instead.");
        }
    }

    [Theory]
    [MemberData(nameof(KitModels))]
    public void EveryModelInTheKitDrawsInOneCall(string model)
    {
        var meshes = Gltf(model).RootElement.GetProperty("meshes");

        meshes.GetArrayLength().ShouldBe(
            1,
            $"{model} holds more than one mesh. Everything in this kit is baked down to a single " +
            "material over a single mesh so it costs one draw call, and a weapon is swapped in " +
            "and out of a screen that already draws the hero.");

        meshes[0].GetProperty("primitives").GetArrayLength().ShouldBe(
            1,
            $"The mesh in {model} is split across primitives, which is one draw call each. That " +
            "happens when the bake leaves more than one material on the joined object.");
    }

    [Theory]
    [MemberData(nameof(WeaponModels))]
    public void EveryWeaponIsAuthoredAroundItsGrip(string model)
    {
        var bounds = PositionBounds(Gltf(model));

        // glTF is Y-up, and the kit authors a blade running up +Y out of the fist.
        bounds.MinY.ShouldBeLessThan(
            0.0,
            $"{model} has nothing below its origin, so its origin is its pommel rather than its " +
            "grip. A socket puts the origin in the middle of the fist, so a weapon authored " +
            "that way is held by the very end of its handle.");

        // Twice, not three times: this is asking whether the grip is near one end, not pinning
        // how long a pommel may be. The dagger clears three times by a margin thin enough that
        // lengthening its pommel would fail a weapon that is in fact authored correctly.
        bounds.MaxY.ShouldBeGreaterThan(
            2.0 * -bounds.MinY,
            $"{model} sits roughly centred on its origin rather than gripped near one end, so a " +
            "hand closing on the origin would close halfway up the blade.");

        // A blade is symmetric across its own flats, so its extents cancel about the grip axis.
        // Without this a weapon could be authored off to one side and still pass the two above.
        (bounds.MinX + bounds.MaxX).ShouldBe(
            0.0,
            0.02,
            $"{model} is not centred on its grip axis across the blade, so it would hang out of " +
            "the side of the fist.");

        (bounds.MinZ + bounds.MaxZ).ShouldBe(
            0.0,
            0.02,
            $"{model} is not centred on its grip axis through the flats of the blade.");
    }

    [Theory]
    [MemberData(nameof(Weapons))]
    public void EveryWeaponSceneMountsItsOwnModel(string model, string scene)
    {
        Source(ScenesDirectory, scene)
            .Contains($"res://game/art/{model}", StringComparison.Ordinal)
            .ShouldBeTrue(
                $"{scene} does not instance {model}. A weapon scene exists to carry the pose its " +
                "model is held in; one pointing at the wrong model is a swap that silently does " +
                "nothing.");
    }

    /// <summary>The socket node paths <c>Hero.cs</c> looks weapons up by, as it states them.</summary>
    /// <remarks>
    /// Only the last segment is the node name in the model — the rest is the path down from the
    /// hero to the instanced model, and belongs to the scene rather than to the export.
    /// </remarks>
    private static IReadOnlyList<string> SocketNamesIn(string source) =>
        Regex.Matches(source, @"SocketPath\s*=\s*""(?<path>[^""]+)""", RegexOptions.None,
                      TimeSpan.FromSeconds(5))
            .Select(m => m.Groups["path"].Value.Split('/')[^1])
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<string> NodeNames(JsonDocument gltf) =>
        gltf.RootElement.GetProperty("nodes").EnumerateArray()
            .Select(n => n.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "")
            .ToList();

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

    /// <summary>
    /// The JSON chunk of a binary glTF file.
    /// </summary>
    /// <remarks>
    /// A <c>.glb</c> is a 12-byte header and then length-prefixed chunks, the first of which is
    /// always the JSON. Twenty lines of reader, against pulling a glTF package into a suite that
    /// only ever wants to know what is in the file.
    /// </remarks>
    private static JsonDocument Gltf(string fileName)
    {
        var path = Path.Combine(RepoPaths.RepositoryRoot, ArtDirectory, fileName);

        File.Exists(path).ShouldBeTrue(
            $"No '{fileName}' at '{path}'. The kit is exported out of " +
            "assets/source/hero_rogue.blend by assets/source/hero_export.py.");

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

    private static string Source(string directory, string fileName)
    {
        var path = Path.Combine(RepoPaths.RepositoryRoot, directory, fileName);

        File.Exists(path).ShouldBeTrue($"No '{fileName}' at '{path}'.");

        return File.ReadAllText(path);
    }
}
