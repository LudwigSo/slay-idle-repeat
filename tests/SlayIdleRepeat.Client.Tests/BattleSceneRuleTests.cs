using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The structural facts about the battle's 3D scenes that nothing else can see go wrong, read the way
/// <see cref="BoardSceneRuleTests"/> reads the board's.
/// </summary>
public sealed class BattleSceneRuleTests
{
    private const string ScenesDirectory = "src/SlayIdleRepeat.Client/game/scenes";
    private const string BattleScene = ScenesDirectory + "/BattleReplay.tscn";
    private const string EnemyScene = ScenesDirectory + "/Enemy.tscn";
    private const string PlateScene = ScenesDirectory + "/ActorPlate.tscn";

    /// <summary>The scene scripts of the battle, and the scene each one's exports are assigned in.</summary>
    private static readonly (string Script, string Scene)[] Scripts =
    [
        ("BattleReplay.cs", BattleScene),
        ("BattleWorld.cs", BattleScene),
        ("BattleCameraRig.cs", BattleScene),
        ("Enemy.cs", EnemyScene),
    ];

    public static TheoryData<string> ScriptNames()
    {
        var data = new TheoryData<string>();

        foreach (var (script, _) in Scripts)
        {
            data.Add(script);
        }

        return data;
    }

    public static TheoryData<string> Scenes() => new() { BattleScene, EnemyScene, PlateScene };

    [Fact]
    public void The_battle_offers_a_scene_unique_Camera3D_under_its_rig_for_the_handover_to_claim()
    {
        var camera = SceneText.Node(BattleScene, "Camera").ShouldNotBeNull(
            "ScreenStage.Show looks the camera up by the unique name '%Camera'.");

        camera.Header.ShouldContain("type=\"Camera3D\"", Case.Sensitive);
        camera.Header.ShouldContain(
            "parent=\"World/CameraRig/Pitch\"",
            Case.Sensitive,
            "the rig yaws, its Pitch child tilts, and the camera under both only sits back — a camera " +
            "anywhere else is one the rig's transforms do not reach.");
        camera.Body.ShouldContain("unique_name_in_owner = true");
    }

    [Fact]
    public void The_battle_offers_a_scene_unique_Ui_layer_for_the_handover_to_show()
    {
        var ui = SceneText.Node(BattleScene, "Ui").ShouldNotBeNull();

        ui.Header.ShouldContain("type=\"CanvasLayer\"", Case.Sensitive);
        ui.Body.ShouldContain("unique_name_in_owner = true");
    }

    [Theory]
    [InlineData("World")]
    [InlineData("Plates")]
    [InlineData("Effects")]
    [InlineData("HitSparks")]
    [InlineData("CritPop")]
    [InlineData("DeathPuff")]
    [InlineData("WardMotes")]
    [InlineData("FloatingText")]
    [InlineData("SafeArea")]
    [InlineData("TitleLabel")]
    [InlineData("OpponentLabel")]
    [InlineData("StatusLabel")]
    [InlineData("SpeedLabel")]
    [InlineData("SpeedButton")]
    [InlineData("SkipButton")]
    [InlineData("PhaseBand")]
    [InlineData("PhaseBandLabel")]
    public void The_battle_declares_each_node_its_script_resolves_by_unique_name(string name)
    {
        var node = SceneText.Node(BattleScene, name).ShouldNotBeNull(
            $"BattleReplay.tscn declares no single node named '{name}', and the script resolves '%{name}'.");

        node.Body.ShouldContain("unique_name_in_owner = true");
    }

    [Fact]
    public void The_battle_carries_no_backdrop_plane_of_its_own() =>
        SceneText.Read(BattleScene).ShouldNotContain(
            "QuadMesh",
            Case.Sensitive,
            "a quad pinned in front of the camera is left behind by a rig that frames the stage; the " +
            "floor is a PlaneMesh and the backdrop is AppRoot's environment.");

    [Theory]
    [InlineData(BattleScene, "WorldEnvironment")]
    [InlineData(BattleScene, "DirectionalLight3D")]
    [InlineData(BattleScene, "OmniLight3D")]
    [InlineData(EnemyScene, "WorldEnvironment")]
    [InlineData(EnemyScene, "DirectionalLight3D")]
    [InlineData(EnemyScene, "OmniLight3D")]
    [InlineData(PlateScene, "WorldEnvironment")]
    [InlineData(PlateScene, "DirectionalLight3D")]
    [InlineData(PlateScene, "OmniLight3D")]
    public void No_battle_scene_declares_a_node_of_a_kind_only_AppRoot_may_own(string scene, string type) =>
        SceneText.Read(scene).ShouldNotContain(
            $"type=\"{type}\"",
            Case.Sensitive,
            $"{scene} declares a {type}; AppRoot owns the one environment and the lights every screen is lit by.");

    [Theory]
    [MemberData(nameof(Scenes))]
    public void No_material_a_battle_scene_draws_is_metallic(string scene) =>
        SceneText.Read(scene).ShouldNotContain(
            "metallic",
            Case.Insensitive,
            "nothing in this build reflects anything, so a metallic surface renders black.");

    [Fact]
    public void The_enemy_template_runs_the_enemy_script() =>
        SceneText.Read(EnemyScene).ShouldContain(
            "path=\"res://game/scenes/Enemy.cs\"",
            Case.Sensitive,
            "Enemy.tscn is the template every catalogue model is instanced under, and Enemy.cs is what " +
            "turns the model to face the hero.");

    [Fact]
    public void The_enemy_template_carries_a_Model_child_for_the_catalogue_to_fill() =>
        SceneText.Node(EnemyScene, "Model").ShouldNotBeNull(
            "Enemy.tscn has no 'Model' child, so there is nowhere to put the model the catalogue names.");

    [Theory]
    [InlineData("Name")]
    [InlineData("HpBar")]
    [InlineData("HpText")]
    [InlineData("Statuses")]
    public void The_actor_plate_carries_each_part_its_script_fills_by_unique_name(string name)
    {
        var node = SceneText.Node(PlateScene, name).ShouldNotBeNull(
            $"ActorPlate.tscn declares no single node named '{name}'.");

        node.Body.ShouldContain("unique_name_in_owner = true");
    }

    [Fact]
    public void The_actor_plates_bar_is_a_ProgressBar() =>
        SceneText.Node(PlateScene, "HpBar").ShouldNotBeNull().Header
            .ShouldContain("type=\"ProgressBar\"", Case.Sensitive);

    [Fact]
    public void The_actor_plates_status_row_is_an_HBoxContainer() =>
        SceneText.Node(PlateScene, "Statuses").ShouldNotBeNull().Header
            .ShouldContain("type=\"HBoxContainer\"", Case.Sensitive);

    [Theory]
    [MemberData(nameof(ScriptNames))]
    public void No_battle_scene_script_declares_a_float_or_double_constant(string script)
    {
        var constants = Regex.Matches(
                Source(script), @"\bconst\s+(?:float|double)\s+(?<name>\w+)", RegexOptions.None, TimeSpan.FromSeconds(5))
            .Select(m => m.Groups["name"].Value);

        constants.ShouldBeEmpty(
            $"{script} hard-codes a number the scene should export, so nobody can tune it without a rebuild.");
    }

    [Fact]
    public void The_battle_scene_scripts_export_at_least_ten_numbers() =>
        Exports().Count.ShouldBeGreaterThanOrEqualTo(
            10, "every distance, timing and camera number the stage types take arrives through an export.");

    [Fact]
    public void Every_export_a_battle_scene_script_declares_is_assigned_in_its_scene()
    {
        var unassigned = Exports()
            .Where(e => !Regex.IsMatch(
                SceneText.Read(e.Scene), $@"^\s*{Regex.Escape(e.Name)} = ", RegexOptions.Multiline, TimeSpan.FromSeconds(5)))
            .Select(e => $"{e.Script}.{e.Name} in {e.Scene}");

        unassigned.ShouldBeEmpty(
            "an export the scene leaves unassigned falls back to whatever the script says, which is a " +
            "number nobody chose in the one place it was supposed to be chosen.");
    }

    private static IReadOnlyList<(string Script, string Name, string Scene)> Exports() =>
        Scripts
            .SelectMany(entry => Regex.Matches(
                    Source(entry.Script),
                    @"\[Export[^\]]*\]\s*(?:\w+\s+)*?(?<type>[\w<>\[\]?.]+)\s+(?<name>\w+)\s*[{;=]",
                    RegexOptions.None,
                    TimeSpan.FromSeconds(5))
                .Select(m => (entry.Script, m.Groups["name"].Value, entry.Scene)))
            .ToList();

    private static string Source(string script)
    {
        var path = Path.Combine(RepoPaths.RepositoryRoot, ScenesDirectory, script);

        File.Exists(path).ShouldBeTrue($"No '{script}' under '{ScenesDirectory}'.");

        return File.ReadAllText(path);
    }
}
