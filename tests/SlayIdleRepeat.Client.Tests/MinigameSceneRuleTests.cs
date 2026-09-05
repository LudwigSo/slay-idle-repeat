using System.Globalization;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The structural facts about the Minigame screen's scenes that nothing else can see go wrong.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The scenes are read as the text files they are</b>, through the same <see cref="SceneText"/>
/// the board's and the event screen's rules use. No engine, no Node, and no scene harness — this
/// repository has none and is not gaining one.
/// </para>
/// <para>
/// Each case below is a fact whose breakage produces a PICTURE rather than an error. A camera that
/// lost its unique name draws this screen through the previous one's camera and looks like a z-order
/// bug; a second WorldEnvironment silently wins or loses against AppRoot's depending on tree order; a
/// metallic material renders black in a build with no probe and no sky; a child renamed leaves the
/// presenter's binding pushing an error nobody reads.
/// </para>
/// <para>
/// 🔴 <b>The bar's own three nodes are the ones with no other witness at all.</b> The window a strike
/// is judged inside is a rule with a test of its own, but the band DRAWN for it is three anchors in a
/// scene: lose <c>HitWindow</c> or <c>Cursor</c> and the game still scores correctly while the player
/// aims at nothing. That is the one failure on this screen that costs a player a reward.
/// </para>
/// </remarks>
public sealed class MinigameSceneRuleTests
{
    private const string MinigameScene = "src/SlayIdleRepeat.Client/game/scenes/Minigame.tscn";
    private const string TierRowScene = "src/SlayIdleRepeat.Client/game/scenes/MinigameTierRow.tscn";

    /// <summary>
    /// The smallest a control may be in the direction a thumb has to hit it.
    /// </summary>
    /// <remarks>
    /// A floor and not an equality: every action control on this screen is 340 and both are correct.
    /// </remarks>
    private const double TouchFloorPixels = 144.0;

    /// <summary>
    /// How many pressable controls this screen carries, so a sweep of none cannot pass.
    /// </summary>
    /// <remarks>
    /// 🔴 Seven: three chests, a strike, a step, a roll and the way back. A loop over "every Button
    /// in the scene" is the shape that silently measures nothing, and a scene that lost its whole
    /// action column would otherwise report a clean touch floor.
    /// </remarks>
    private const int PressableControls = 7;

    /// <summary>
    /// 🔒 <b><c>ScreenStage</c>'s whole handover contract, in one line of a scene file.</b>
    /// </summary>
    /// <remarks>
    /// <c>ScreenStage.Show</c> resolves <c>%Camera</c> as a <c>Camera3D</c> and calls
    /// <c>MakeCurrent</c> on it. Drop the scene-unique flag, or move the camera under a rig without
    /// carrying the flag along, and every handover onto this screen draws through the outgoing
    /// screen's camera. Nothing throws; it looks like a z-order bug.
    /// </remarks>
    [Fact]
    public void The_minigame_screen_offers_a_scene_unique_Camera3D_for_the_handover_to_claim()
    {
        var camera = SceneText.Node(MinigameScene, "Camera").ShouldNotBeNull(
            "Minigame.tscn declares no node named 'Camera'. ScreenStage.Show looks it up by the " +
            "unique name '%Camera' and pushes an error when it resolves to nothing.");

        camera.Header.ShouldContain(
            "type=\"Camera3D\"",
            Case.Sensitive,
            "the node named 'Camera' is not a Camera3D. ScreenStage.Camera resolves it as one and " +
            "answers null for anything else, so no camera would be made current at all.");

        camera.Body.ShouldContain(
            "unique_name_in_owner = true",
            "the camera has no scene-unique flag, so '%Camera' resolves to nothing and every " +
            "handover onto this screen keeps drawing through the previous screen's camera.");
    }

    /// <summary>The overlay half of the same contract.</summary>
    [Fact]
    public void The_minigame_screen_offers_a_scene_unique_Ui_layer_for_the_handover_to_show()
    {
        var ui = SceneText.Node(MinigameScene, "Ui").ShouldNotBeNull(
            "Minigame.tscn declares no node named 'Ui', so ScreenStage has nothing to show or hide " +
            "this screen's interface through.");

        ui.Header.ShouldContain("type=\"CanvasLayer\"", Case.Sensitive);
        ui.Body.ShouldContain(
            "unique_name_in_owner = true",
            "ScreenStage shows and hides a screen's interface through '%Ui', because engine " +
            "visibility does not cross the Node3D-to-CanvasLayer seam — hiding the screen alone " +
            "leaves its whole interface drawn over whatever replaced it.");
    }

    /// <summary>
    /// 🔒 The Minigame screen owns nothing there can only be one of.
    /// </summary>
    /// <remarks>
    /// A viewport has exactly one <c>WorldEnvironment</c>, and AppRoot owns it along with the
    /// app-wide key and fill light. A second one here would win or lose against AppRoot's depending
    /// on tree order, which is not a thing a screen may decide — and would light every other screen
    /// differently on the way past.
    /// </remarks>
    [Theory]
    [InlineData("WorldEnvironment")]
    [InlineData("DirectionalLight3D")]
    [InlineData("OmniLight3D")]
    public void The_minigame_screen_declares_no_node_of_a_kind_only_AppRoot_may_own(string type) =>
        SceneText.Read(MinigameScene).ShouldNotContain(
            $"type=\"{type}\"",
            Case.Sensitive,
            $"Minigame.tscn declares a {type}. AppRoot owns the one environment a viewport can have " +
            "and the lights every screen is lit by; a screen that brings its own decides by tree " +
            "order which of the two applies.");

    /// <summary>
    /// The screen still carries every child the scene's binding names.
    /// </summary>
    /// <remarks>
    /// Each of these is looked up by <c>%UniqueName</c> in <c>_Ready</c> and pushes an engine error
    /// when it is gone — and that error is the only thing in the build that would say so. What is
    /// lost is not decoration: without <c>RewardRows</c> the player chooses a game with no idea what
    /// any outcome pays, without <c>HitsLabel</c> a bar reports neither hits nor strikes left, and
    /// without <c>ResultLabel</c> a resolved tile never says what was won.
    /// </remarks>
    [Theory]
    [InlineData("World")]
    [InlineData("Ground")]
    [InlineData("Screen")]
    [InlineData("SafeArea")]
    [InlineData("Column")]
    [InlineData("TitleLabel")]
    [InlineData("ArmLabel")]
    [InlineData("RuleLabel")]
    [InlineData("GameScroll")]
    [InlineData("RewardsPanel")]
    [InlineData("RewardsHeading")]
    [InlineData("RewardRows")]
    [InlineData("GuaranteeLabel")]
    [InlineData("TimingBarPanel")]
    [InlineData("BarTrack")]
    [InlineData("HitWindow")]
    [InlineData("Cursor")]
    [InlineData("HitsLabel")]
    [InlineData("ChestPanel")]
    [InlineData("ChestOne")]
    [InlineData("ChestTwo")]
    [InlineData("ChestThree")]
    [InlineData("DiceDuelPanel")]
    [InlineData("DiceLabel")]
    [InlineData("ResultPanel")]
    [InlineData("ResultHeading")]
    [InlineData("ResultLabel")]
    [InlineData("StatusLabel")]
    [InlineData("RejectionLabel")]
    [InlineData("ActionColumn")]
    [InlineData("StrikeRow")]
    [InlineData("StrikeButton")]
    [InlineData("StepButton")]
    [InlineData("RollButton")]
    [InlineData("ContinueButton")]
    public void The_minigame_screen_still_carries_the_child_the_scene_binds_by_name(string name) =>
        SceneText.Node(MinigameScene, name).ShouldNotBeNull(
            $"Minigame.tscn has no unique node named '{name}'. The binding resolves it by that name " +
            "and pushes an error into the log when it answers nothing — and null is also what this " +
            "lookup answers when TWO nodes share the name, which is the state a scene-unique lookup " +
            "resolves to whichever the engine reached first.");

    /// <summary>The tier row still carries the caption and the amounts the ladder writes into.</summary>
    /// <remarks>
    /// The row scene is instantiated once per outcome tier, and each half is bound by a plain child
    /// path off the instance. Rename either and every row draws empty — a ladder of blank lines,
    /// which is what a game with no authored rewards would also look like.
    /// </remarks>
    [Theory]
    [InlineData("CaptionLabel")]
    [InlineData("RewardLabel")]
    public void The_tier_row_still_carries_the_child_the_ladder_binds_by_name(string name) =>
        SceneText.Node(TierRowScene, name).ShouldNotBeNull(
            $"MinigameTierRow.tscn has no single node named '{name}', so one half of every reward " +
            "row — which outcome it is, or what it pays — cannot be written at all.");

    /// <summary>
    /// 🔴 <b>The bar's band and its marker are anchored controls, and the scene has to say so.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both are positioned every draw by writing their anchors from the presenter's own numbers, and
    /// an anchor written to a child laid out by its parent is simply ignored. <c>layout_mode = 1</c>
    /// is what says this child positions itself — lose it and the band and the marker both sit
    /// wherever the container put them, motionless, with nothing in the build saying why.
    /// </para>
    /// <para>
    /// 🔒 The failure is silent AND expensive: the game keeps scoring against the authored window
    /// while the picture stops describing it, so a player aims at a band that is not the one they
    /// are judged by and loses a reward for it.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("HitWindow")]
    [InlineData("Cursor")]
    public void The_timing_bars_moving_parts_position_themselves(string name)
    {
        var part = SceneText.Node(MinigameScene, name).ShouldNotBeNull(
            $"Minigame.tscn has no single node named '{name}', so the timing bar draws no " +
            "band or no marker and the player has nothing to aim at.");

        part.Header.ShouldContain(
            "type=\"ColorRect\"",
            Case.Sensitive,
            $"'{name}' is not a ColorRect. It is written to as a Control with anchors, and a node " +
            "resolved as one it is not answers null rather than throwing.");

        part.Body.ShouldContain(
            "layout_mode = 1",
            $"'{name}' is not in anchor layout mode, so the anchors this screen writes every draw " +
            "are ignored and the part of the bar it draws never moves.");
    }

    /// <summary>
    /// 🔴 <b>Every button on this screen is at least 144 pixels tall.</b>
    /// </summary>
    /// <remarks>
    /// A button whose height comes from its own label is a control a thumb misses on a phone, and
    /// there is no error and nothing in a diff to say so: it simply reads as an unresponsive screen.
    /// The chest controls matter most — they are the whole of one game, and a player who cannot hit
    /// one cannot play it at all.
    /// </remarks>
    [Fact]
    public void Every_button_on_the_minigame_screen_is_tall_enough_to_hit()
    {
        var buttons = ButtonsOf(MinigameScene);

        buttons.Count.ShouldBe(
            PressableControls,
            "Minigame.tscn declares " + buttons.Count + " Buttons and this screen carries " +
            PressableControls + ": three chests, a strike, a step, a roll and the way back. A " +
            "sweep over a scene that lost its action column would otherwise report a clean floor " +
            "over the controls it still had — so the count is stated as well as the heights.");

        foreach (var (name, minimumHeight) in buttons)
        {
            minimumHeight.ShouldNotBeNull(
                name + " in " + MinigameScene + " has no custom_minimum_size, so its height is " +
                "whatever its own content asks for. A control sized by its label is one a thumb " +
                "misses, and nothing in the build or in a diff says so.");
            minimumHeight.Value.ShouldBeGreaterThanOrEqualTo(
                TouchFloorPixels,
                name + " asks for a minimum height of " + minimumHeight.Value + ", below the " +
                TouchFloorPixels + "-pixel floor every other shipped scene honours. It is a floor " +
                "rather than an equality — this screen's action controls are 340 — so raise the " +
                "number rather than lowering this one.");
        }
    }

    /// <summary>
    /// 🔒 Nothing either scene draws is metallic.
    /// </summary>
    /// <remarks>
    /// The same assertion the board's, the hero's and the event screen's materials are held to, for
    /// the same documented reason: this build has one key light, one fill, flat ambient, no
    /// reflection probe and no sky. A metallic surface has nothing to reflect and renders black — a
    /// panel that disappears rather than an error.
    /// </remarks>
    [Theory]
    [InlineData(MinigameScene)]
    [InlineData(TierRowScene)]
    public void No_material_the_minigame_screen_draws_is_metallic(string scene) =>
        SceneText.Read(scene).ShouldNotContain(
            "metallic",
            Case.Insensitive,
            $"{scene} authors a metallic property. Nothing in this build reflects anything — no " +
            "probe, no sky, flat ambient — so a metallic surface renders black and what it draws " +
            "simply is not there.");

    /// <summary>
    /// Every <c>Button</c> the scene declares, with the minimum height it asks for or null for none.
    /// </summary>
    /// <remarks>
    /// A scan of its own rather than <see cref="SceneText.Node"/>: that answers about ONE node named
    /// in advance, and the claim here is about every button whatever it is called — including one
    /// added later, which is the whole point of a floor.
    /// </remarks>
    private static IReadOnlyList<(string Name, double? MinimumHeight)> ButtonsOf(string scene)
    {
        var buttons = new List<(string, double?)>();
        var name = "";
        var isButton = false;
        double? minimumHeight = null;

        foreach (var line in SceneText.Read(scene).Split('\n').Select(text => text.Trim()))
        {
            if (line.StartsWith('['))
            {
                if (isButton)
                {
                    buttons.Add((name, minimumHeight));
                }

                isButton = line.StartsWith("[node ", StringComparison.Ordinal) &&
                           line.Contains("type=\"Button\"", StringComparison.Ordinal);
                name = isButton ? NameIn(line) : "";
                minimumHeight = null;

                continue;
            }

            if (isButton && line.StartsWith("custom_minimum_size = Vector2(", StringComparison.Ordinal))
            {
                minimumHeight = HeightIn(line);
            }
        }

        if (isButton)
        {
            buttons.Add((name, minimumHeight));
        }

        return buttons;
    }

    /// <summary>The <c>name="…"</c> of a node header.</summary>
    private static string NameIn(string header)
    {
        const string Marker = "name=\"";

        var opens = header.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length;
        var closes = header.IndexOf('"', opens);

        return closes > opens ? header[opens..closes] : header;
    }

    /// <summary>
    /// The Y of a <c>custom_minimum_size = Vector2(x, y)</c> line, or null when it does not parse.
    /// </summary>
    /// <remarks>
    /// A line that will not parse answers null rather than zero, so it fails as "no floor stated"
    /// with the property's own name in the message rather than as a silently passing zero.
    /// </remarks>
    private static double? HeightIn(string line)
    {
        var opens = line.IndexOf('(');
        var closes = line.IndexOf(')', opens + 1);

        if (opens < 0 || closes < 0)
        {
            return null;
        }

        var parts = line[(opens + 1)..closes].Split(',');

        return parts.Length == 2 &&
               double.TryParse(
                   parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
