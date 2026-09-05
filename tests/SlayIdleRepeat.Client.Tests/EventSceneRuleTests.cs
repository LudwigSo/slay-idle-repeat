using System.Globalization;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The structural facts about the Event screen's scenes that nothing else can see go wrong.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The scenes are read as the text files they are</b>, through the same <see cref="SceneText"/>
/// the board's rules use. No engine, no Node, and no scene harness — this repository has none and is
/// not gaining one.
/// </para>
/// <para>
/// Each case below is a fact whose breakage produces a PICTURE rather than an error. A camera that
/// lost its unique name draws this screen through the previous one's camera and looks like a z-order
/// bug; a second WorldEnvironment silently wins or loses against AppRoot's depending on tree order;
/// a metallic material renders black in a build with no probe and no sky; a child renamed leaves the
/// presenter's binding pushing an error nobody reads.
/// </para>
/// <para>
/// 🔴 <b>The 144-pixel touch floor has never been tested anywhere in this repository, and this suite
/// is the first to enforce it.</b> Every shipped scene honours it — <c>ChapterRow.tscn</c> and
/// <c>GuaranteeRow.tscn</c> sit exactly on it and the action buttons are 340 — but it was an
/// authoring convention held by hand, so a new card authored with a button sized by its label alone
/// would have shipped a control too small to hit reliably and nothing would have said so. This
/// screen's option cards are the first pressable thing built after the rule was written down, which
/// is why the rule lands here rather than on a scene that already passes.
/// </para>
/// </remarks>
public sealed class EventSceneRuleTests
{
    private const string EventScene = "src/SlayIdleRepeat.Client/game/scenes/EventScreen.tscn";
    private const string OptionCardScene = "src/SlayIdleRepeat.Client/game/scenes/EventOptionCard.tscn";
    private const string ResultRowScene = "src/SlayIdleRepeat.Client/game/scenes/EventResultRow.tscn";

    /// <summary>
    /// The smallest a control may be in the direction a thumb has to hit it.
    /// </summary>
    /// <remarks>
    /// The floor every shipped scene already honours, stated here for the first time. It is a floor
    /// and not an equality: the action buttons are 340 and the rows that sit exactly on 144 are both
    /// correct.
    /// </remarks>
    private const double TouchFloorPixels = 144.0;

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
    public void The_event_screen_offers_a_scene_unique_Camera3D_for_the_handover_to_claim()
    {
        var camera = SceneText.Node(EventScene, "Camera").ShouldNotBeNull(
            "EventScreen.tscn declares no node named 'Camera'. ScreenStage.Show looks it up by the " +
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
    public void The_event_screen_offers_a_scene_unique_Ui_layer_for_the_handover_to_show()
    {
        var ui = SceneText.Node(EventScene, "Ui").ShouldNotBeNull(
            "EventScreen.tscn declares no node named 'Ui', so ScreenStage has nothing to show or " +
            "hide this screen's interface through.");

        ui.Header.ShouldContain("type=\"CanvasLayer\"", Case.Sensitive);
        ui.Body.ShouldContain(
            "unique_name_in_owner = true",
            "ScreenStage shows and hides a screen's interface through '%Ui', because engine " +
            "visibility does not cross the Node3D-to-CanvasLayer seam — hiding the screen alone " +
            "leaves its whole interface drawn over whatever replaced it.");
    }

    /// <summary>
    /// 🔒 The Event screen owns nothing there can only be one of.
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
    public void The_event_screen_declares_no_node_of_a_kind_only_AppRoot_may_own(string type) =>
        SceneText.Read(EventScene).ShouldNotContain(
            $"type=\"{type}\"",
            Case.Sensitive,
            $"EventScreen.tscn declares a {type}. AppRoot owns the one environment a viewport can " +
            "have and the lights every screen is lit by; a screen that brings its own decides by " +
            "tree order which of the two applies.");

    /// <summary>
    /// The screen still carries every child the presenter's binding names.
    /// </summary>
    /// <remarks>
    /// Each of these is looked up by <c>%UniqueName</c> and pushes an engine error when it is gone —
    /// and that error is the only thing in the build that would say so. What is lost is not
    /// decoration: without <c>CardBody</c> the player is offered a choice with no question attached,
    /// and without <c>ResultPanel</c> a resolved card reports nothing at all, which is the common
    /// case on this screen.
    /// </remarks>
    [Theory]
    [InlineData("World")]
    [InlineData("Ground")]
    [InlineData("Screen")]
    [InlineData("SafeArea")]
    [InlineData("Column")]
    [InlineData("TitleLabel")]
    [InlineData("CardScroll")]
    [InlineData("CardTitle")]
    [InlineData("CardBody")]
    [InlineData("OptionColumn")]
    [InlineData("ResultPanel")]
    [InlineData("ResultHeading")]
    [InlineData("NothingLabel")]
    [InlineData("RunRows")]
    [InlineData("WalletHeading")]
    [InlineData("WalletRows")]
    [InlineData("StatusLabel")]
    [InlineData("RejectionLabel")]
    [InlineData("ContinueButton")]
    public void The_event_screen_still_carries_the_child_the_presenter_binds_by_name(string name) =>
        SceneText.Node(EventScene, name).ShouldNotBeNull(
            $"EventScreen.tscn has no unique node named '{name}'. The binding resolves it by that " +
            "name and pushes an error into the log when it answers nothing — and null is also what " +
            "this lookup answers when TWO nodes share the name, which is the state a scene-unique " +
            "lookup resolves to whichever the engine reached first.");

    /// <summary>The option card still carries the three labels and the control that presses it.</summary>
    /// <remarks>
    /// <c>BlockLabel</c> in particular: it is the only place the screen can say why an option cannot
    /// be taken, and <c>EVENT_CHOOSE</c> answers an unaffordable choice with a wire value four other
    /// things share — so a card with no block line leaves a control that is refused for a reason the
    /// player can never see.
    /// </remarks>
    [Theory]
    [InlineData("OptionLabel")]
    [InlineData("CostLabel")]
    [InlineData("BlockLabel")]
    [InlineData("PressButton")]
    public void The_option_card_still_carries_the_child_the_row_binds_by_name(string name) =>
        SceneText.Node(OptionCardScene, name).ShouldNotBeNull(
            $"EventOptionCard.tscn has no unique node named '{name}', so one row of an option — its " +
            "caption, its price, the sentence blocking it, or the control that takes it — cannot be " +
            "drawn at all.");

    /// <summary>The result row still carries the caption and the number the panel writes into.</summary>
    /// <remarks>
    /// The row scene is instantiated once per movement a resolved card made, and each half is bound
    /// by a plain child path off the instance. Rename either and every row draws empty — the panel
    /// reports that the card moved nothing, which is also the honest report for a card that really
    /// moved nothing, so the two states are indistinguishable to anyone reading the screen.
    /// </remarks>
    [Theory]
    [InlineData("CaptionLabel")]
    [InlineData("ValueLabel")]
    public void The_result_row_still_carries_the_child_the_panel_binds_by_name(string name) =>
        SceneText.Node(ResultRowScene, name).ShouldNotBeNull(
            $"EventResultRow.tscn has no single node named '{name}', so one half of every result " +
            "line — its caption, or the signed amount itself — cannot be written at all.");

    /// <summary>
    /// 🔴 <b>The one thing on this screen a finger presses that is not a <c>Button</c>.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The touch floor above sweeps <c>Button</c>s, and a control that is pressed without being one
    /// walks straight past it. A result row's number is exactly that: it is held to swap the
    /// shortened figure for the exact one, so it is a touch target with none of a button's
    /// affordances and none of its pinning.
    /// </para>
    /// <para>
    /// 🔒 <c>mouse_filter</c> is pinned with it, and it is the half that fails SILENTLY. A
    /// <c>Label</c> ignores input by default, so a row that lost this line would raise no error,
    /// draw identically, and simply never answer a hold — the exact value would become unreachable
    /// with nothing anywhere saying why. <c>0</c> is <c>STOP</c> rather than <c>PASS</c> on purpose:
    /// these rows sit inside a <c>ScrollContainer</c>, which reads a touch it receives as the start
    /// of a drag, and a passed-through hold would be taken for a scroll.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_result_row_number_is_a_touch_target_and_is_built_like_one()
    {
        var value = SceneText.Node(ResultRowScene, "ValueLabel").ShouldNotBeNull(
            "EventResultRow.tscn has no single node named 'ValueLabel', so the number a long press " +
            "reveals is not there to press.");

        value.Body.ShouldContain(
            "mouse_filter = 0",
            "ValueLabel does not state mouse_filter = 0. A Label ignores input by default, so its " +
            "GuiInput never fires, the hold that reveals the exact figure does nothing at all, and " +
            "nothing in the build or in a diff says so.");

        var stated = value.Body.FirstOrDefault(
            line => line.StartsWith("custom_minimum_size = Vector2(", StringComparison.Ordinal))
            .ShouldNotBeNull(
                "ValueLabel states no custom_minimum_size, so the surface a finger has to hold is " +
                "only as big as the digits printed in it — and a shortened number is three " +
                "characters wide.");

        var width = WidthIn(stated);
        var height = HeightIn(stated);

        width.ShouldNotBeNull("ValueLabel's custom_minimum_size does not parse: " + stated);
        height.ShouldNotBeNull("ValueLabel's custom_minimum_size does not parse: " + stated);

        width.Value.ShouldBeGreaterThanOrEqualTo(
            TouchFloorPixels,
            "ValueLabel is narrower than the " + TouchFloorPixels + "-pixel floor. It is held, not " +
            "read, so it is a touch target in both directions rather than a column width.");
        height.Value.ShouldBeGreaterThanOrEqualTo(
            TouchFloorPixels,
            "ValueLabel is shorter than the " + TouchFloorPixels + "-pixel floor, so the row it " +
            "sits in is a strip of text a thumb has to land on exactly.");
    }

    /// <summary>
    /// 🔴 <b>Every button on either scene is at least 144 pixels tall.</b>
    /// </summary>
    /// <remarks>
    /// The first test in this repository to enforce the touch floor. A button whose height comes
    /// from its own label is a control a thumb misses on a phone, and there is no error and nothing
    /// in a diff to say so: it simply reads as an unresponsive screen. The option card's press
    /// control is the case that matters most — it covers the whole card face and its label lives on
    /// a sibling, so its natural minimum size is nothing.
    /// </remarks>
    [Theory]
    [InlineData(EventScene)]
    [InlineData(OptionCardScene)]
    public void Every_button_on_the_event_screen_is_tall_enough_to_hit(string scene)
    {
        var buttons = ButtonsOf(scene);

        buttons.ShouldNotBeEmpty(
            scene + " declares no Button at all, so this case swept nothing and would report a " +
            "clean floor whatever the scene says. Both of these scenes carry a pressable control.");

        foreach (var (name, minimumHeight) in buttons)
        {
            minimumHeight.ShouldNotBeNull(
                name + " in " + scene + " has no custom_minimum_size, so its height is whatever " +
                "its own content asks for. A control sized by its label is one a thumb misses, and " +
                "nothing in the build or in a diff says so.");
            minimumHeight.Value.ShouldBeGreaterThanOrEqualTo(
                TouchFloorPixels,
                name + " in " + scene + " asks for a minimum height of " + minimumHeight.Value +
                ", below the " + TouchFloorPixels + "-pixel floor every other shipped scene " +
                "honours. It is a floor rather than an equality — the action buttons are 340 — so " +
                "raise the number rather than lowering this one.");
        }
    }

    /// <summary>
    /// 🔒 Nothing either scene draws is metallic.
    /// </summary>
    /// <remarks>
    /// The same assertion the board's and the hero's materials are held to, for the same documented
    /// reason: this build has one key light, one fill, flat ambient, no reflection probe and no sky.
    /// A metallic surface has nothing to reflect and renders black — a card that disappears rather
    /// than an error.
    /// </remarks>
    [Theory]
    [InlineData(EventScene)]
    [InlineData(OptionCardScene)]
    [InlineData(ResultRowScene)]
    public void No_material_the_event_screen_draws_is_metallic(string scene) =>
        SceneText.Read(scene).ShouldNotContain(
            "metallic",
            Case.Insensitive,
            $"{scene} authors a metallic property. Nothing in this build reflects anything — no " +
            "probe, no sky, flat ambient — so a metallic surface renders black and the card simply " +
            "is not there.");

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

    /// <summary>The X of a <c>custom_minimum_size = Vector2(x, y)</c> line, or null for none.</summary>
    /// <remarks>
    /// Asked only of a control that is held rather than read, where the floor applies across as
    /// well as down. A button that fills its row is as wide as the row and needs no claim made
    /// about it.
    /// </remarks>
    private static double? WidthIn(string line) => ComponentIn(line, 0);

    /// <summary>
    /// The Y of a <c>custom_minimum_size = Vector2(x, y)</c> line, or null when it does not parse.
    /// </summary>
    /// <remarks>
    /// A line that will not parse answers null rather than zero, so it fails as "no floor stated"
    /// with the property's own name in the message rather than as a silently passing zero.
    /// </remarks>
    private static double? HeightIn(string line) => ComponentIn(line, 1);

    /// <summary>One component of the vector a <c>Vector2(x, y)</c> line writes.</summary>
    /// <param name="line">The property line.</param>
    /// <param name="component">0 for X, 1 for Y.</param>
    private static double? ComponentIn(string line, int component)
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
                   parts[component].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
