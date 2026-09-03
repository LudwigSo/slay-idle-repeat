using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The structural facts about the board's 3D scenes that nothing else can see go wrong.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The scenes are read as the text files they are</b>, the way <c>BoardTileGateContrastTests</c>
/// reads their colours and <c>HeroWeaponKitTests</c> reads the hero's bytes. No engine, no Node, and
/// no scene harness — this repository has none and is not gaining one.
/// </para>
/// <para>
/// Each case below is a fact whose breakage produces a PICTURE rather than an error. A camera that
/// lost its unique name draws the board through the previous screen's camera and looks like a
/// z-order bug; a second WorldEnvironment silently wins or loses against AppRoot's; a metallic
/// material renders black in a build with no probe and no sky. None of them throws, and none of them
/// is visible in a diff.
/// </para>
/// </remarks>
public sealed class BoardSceneRuleTests
{
    private const string BoardScene = "src/SlayIdleRepeat.Client/game/scenes/Board.tscn";
    private const string TileScene = "src/SlayIdleRepeat.Client/game/scenes/BoardTile.tscn";
    private const string SegmentScene = "src/SlayIdleRepeat.Client/game/scenes/BoardPathSegment.tscn";

    /// <summary>
    /// 🔒 <b><c>ScreenStage</c>'s whole handover contract, in one line of a scene file.</b>
    /// </summary>
    /// <remarks>
    /// <c>ScreenStage.Show</c> resolves <c>%Camera</c> as a <see cref="Godot.Camera3D"/> and calls
    /// <c>MakeCurrent</c> on it. The 3D board moved that camera two levels down, under a rig that
    /// writes its transform every frame — and the obvious next edit is to put the rig's script on
    /// the camera itself and delete a node. Do that, or drop the scene-unique flag in the move, and
    /// every handover in the build draws through the outgoing screen's camera instead. Nothing
    /// throws; it looks like a z-order bug.
    /// </remarks>
    [Fact]
    public void The_board_still_offers_a_scene_unique_Camera3D_for_the_handover_to_claim()
    {
        var camera = SceneText.Node(BoardScene, "Camera").ShouldNotBeNull(
            "Board.tscn declares no node named 'Camera'. ScreenStage.Show looks it up by the unique " +
            "name '%Camera' and pushes an error when it resolves to nothing.");

        camera.Header.ShouldContain(
            "type=\"Camera3D\"",
            Case.Sensitive,
            "the node named 'Camera' is no longer a Camera3D. ScreenStage.Camera resolves it as one " +
            "and answers null for anything else, so no camera would be made current at all.");

        camera.Body.ShouldContain(
            "unique_name_in_owner = true",
            "the board's camera lost its scene-unique flag, so '%Camera' resolves to nothing and " +
            "every handover onto this screen keeps drawing through the previous screen's camera.");
    }

    /// <summary>The overlay half of the same contract.</summary>
    [Fact]
    public void The_board_still_offers_a_scene_unique_Ui_layer_for_the_handover_to_show()
    {
        var ui = SceneText.Node(BoardScene, "Ui").ShouldNotBeNull();

        ui.Header.ShouldContain("type=\"CanvasLayer\"", Case.Sensitive);
        ui.Body.ShouldContain(
            "unique_name_in_owner = true",
            "ScreenStage shows and hides a screen's interface through '%Ui', because engine " +
            "visibility does not cross the Node3D-to-CanvasLayer seam — hiding the screen alone " +
            "leaves its whole interface drawn over whatever replaced it.");
    }

    /// <summary>
    /// 🔒 The board owns nothing there can only be one of.
    /// </summary>
    /// <remarks>
    /// A viewport has exactly one <c>WorldEnvironment</c>, and AppRoot owns it along with the
    /// app-wide key and fill light — <c>ScreenStage</c>'s remarks and <c>ARCHITECTURE.md</c> both
    /// state it. A second one here would win or lose against AppRoot's depending on tree order,
    /// which is not a thing a screen may decide. The board is also the screen most likely to acquire
    /// one by accident, because it is the first with real geometry to light.
    /// </remarks>
    [Theory]
    [InlineData("WorldEnvironment")]
    [InlineData("DirectionalLight3D")]
    [InlineData("OmniLight3D")]
    public void The_board_declares_no_node_of_a_kind_only_AppRoot_may_own(string type) =>
        SceneText.Read(BoardScene).ShouldNotContain(
            $"type=\"{type}\"",
            Case.Sensitive,
            $"Board.tscn declares a {type}. AppRoot owns the one environment a viewport can have and " +
            "the lights every screen is lit by; a screen that brings its own decides by tree order " +
            "which of the two applies, and lights every other screen differently on the way past.");

    /// <summary>
    /// The tile template still carries the child the gate mark is shown through.
    /// </summary>
    /// <remarks>
    /// <c>BoardWorld</c> shows the mark by that name and pushes an error when it is gone — and that
    /// error is the only thing in the build that says so. What is lost is not a decoration: it is
    /// the one thing on the screen telling a player which node a roll cannot carry past.
    /// </remarks>
    [Fact]
    public void The_tile_template_still_carries_the_gate_mark_by_name()
    {
        SceneText.Node(TileScene, "Gate").ShouldNotBeNull(
            "BoardTile.tscn has no 'Gate' child, so no node can be marked as one the run may not " +
            "walk past.");

        SceneText.Node(TileScene, "Puck").ShouldNotBeNull(
            "BoardTile.tscn has no 'Puck' child, so there is no mesh to draw a node of the board " +
            "with and the whole board comes up empty.");

        SceneText.Node(SegmentScene, "Ribbon").ShouldNotBeNull(
            "BoardPathSegment.tscn has no 'Ribbon' child, so the tiles would be drawn with nothing " +
            "joining them and the board would read as a scatter rather than as a track.");
    }

    /// <summary>
    /// 🔒 Nothing the board draws is metallic.
    /// </summary>
    /// <remarks>
    /// The same assertion <c>HeroWeaponKitTests</c> makes over the hero's own materials, for the
    /// same documented reason: this build has one key light, one fill, flat ambient, no reflection
    /// probe and no sky. A metallic surface has nothing to reflect and renders black — a tile that
    /// disappears rather than an error.
    /// </remarks>
    [Theory]
    [InlineData(TileScene)]
    [InlineData(SegmentScene)]
    public void No_material_the_board_draws_is_metallic(string scene) =>
        SceneText.Read(scene).ShouldNotContain(
            "metallic",
            Case.Insensitive,
            $"{scene} authors a metallic property. Nothing in this build reflects anything — no " +
            "probe, no sky, flat ambient — so a metallic surface renders black and the tile simply " +
            "is not there.");

    /// <summary>
    /// 🔴 The 24-by-42 backdrop plane is gone and must stay gone.
    /// </summary>
    /// <remarks>
    /// It was pinned six units in front of a camera that now travels the length of the board, so it
    /// is left behind on the first hop — and a board a chapter may author a hundred units of is not
    /// something a quad that size could cover in any case. Its replacement is AppRoot's environment
    /// background, which is the same colour byte for byte and cannot be outrun. Re-adding one is the
    /// obvious fix for "the far end of the board looks empty" and is the wrong one.
    /// </remarks>
    [Fact]
    public void The_board_carries_no_backdrop_plane_of_its_own() =>
        SceneText.Read(BoardScene).ShouldNotContain(
            "QuadMesh",
            Case.Sensitive,
            "Board.tscn has a quad in it again. A backdrop plane at a fixed distance is left behind " +
            "the moment the camera moves; BoardTileGateContrastTests measures the board's backdrop " +
            "on AppRoot for that reason, and a second one here would put a colour on the screen that " +
            "nothing measures.");
}
