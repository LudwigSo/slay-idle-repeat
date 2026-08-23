using Godot;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// Shows and hides one screen — its 3D world and its UI overlay together — for every handover in
/// the build.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>This type exists because <c>Visible</c> stopped being one write.</b> While every screen was
/// a <c>Control</c>, hiding one was <c>Visible = false</c> on the root and the whole subtree went
/// with it. A screen is now a <see cref="Node3D"/> whose UI hangs off a <see cref="CanvasLayer"/>,
/// and a canvas layer is not a canvas item: engine visibility does not propagate across that seam
/// in either direction. So <c>screen.Visible = false</c> now hides the 3D world and leaves the
/// entire interface drawn on top of the screen that replaced it. That failure is silent, it looks
/// like a z-order bug rather than a visibility one, and every handover in this directory would have
/// had to remember the second write independently.
/// </para>
/// <para>
/// 🔒 <b>The camera is made current on show, rather than left to the engine.</b> Every screen
/// carries its own <see cref="Camera3D"/>, and every handover in this build has two screens alive at
/// once — the outgoing one is hidden rather than freed, or freed only on the frame after. Two
/// cameras in one viewport is a race the engine resolves by whichever entered the tree last, which
/// is the right answer on the way in and the wrong one on the way back: a decision screen that frees
/// itself hands back to a board whose camera stopped being current when the decision opened. Saying
/// so is one line and removes the question.
/// </para>
/// <para>
/// ⚠️ <b>The environment is deliberately not here.</b> A viewport has ONE
/// <see cref="WorldEnvironment"/> — a second in the tree overwrites the first and the editor calls
/// it a configuration error — so it lives on <see cref="AppRoot"/>, once, for the life of the
/// application. What a screen owns of the 3D world is its content and its framing. What it must not
/// own is anything there can only be one of.
/// </para>
/// <para>
/// 🔴 <b>Both nodes are required, and a screen missing either is reported rather than skipped.</b>
/// A screen whose overlay could not be found would hand over to a blank frame with no error, which
/// is the shape of bug that survives a release. <c>GetNodeOrNull</c> and a pushed error, not
/// <c>GetNode</c> and a crash: a handover is reached from a continuation on a screen that may
/// already be leaving, and taking the application down for a node lookup is worse than saying so.
/// </para>
/// </remarks>
internal static class ScreenStage
{
    /// <summary>The overlay every screen draws its interface into.</summary>
    private const string UiLayerPath = "%Ui";

    /// <summary>The screen's own camera onto its own 3D world.</summary>
    private const string CameraPath = "%Camera";

    /// <summary>Puts a screen on screen: its world visible, its overlay drawn, its camera current.</summary>
    /// <param name="screen">The screen taking over.</param>
    /// <exception cref="ArgumentNullException"><paramref name="screen"/> is null.</exception>
    internal static void Show(Node3D screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        if (!Addressable(screen))
        {
            return;
        }

        screen.Visible = true;

        if (Overlay(screen) is { } overlay)
        {
            overlay.Visible = true;
        }

        // MakeCurrent rather than Current = true: the setter is the same call, but only this one
        // says the point out loud — the screen being shown is claiming the viewport from whichever
        // screen held it.
        Camera(screen)?.MakeCurrent();
    }

    /// <summary>Stands a screen down: its world hidden and its overlay with it.</summary>
    /// <remarks>
    /// The camera is left alone. Hiding is always half of a handover, and the incoming screen's
    /// <see cref="Show"/> claims the viewport on the same frame — clearing the camera here would put
    /// one frame of no camera at all between the two, which the engine draws as the environment's
    /// background and a player reads as a flash.
    /// </remarks>
    /// <param name="screen">The screen standing down.</param>
    /// <exception cref="ArgumentNullException"><paramref name="screen"/> is null.</exception>
    internal static void Hide(Node3D screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        if (!Addressable(screen))
        {
            return;
        }

        screen.Visible = false;

        if (Overlay(screen) is { } overlay)
        {
            overlay.Visible = false;
        }
    }

    /// <summary>
    /// Whether the screen is still a node in a tree, and so still has nodes to write to.
    /// </summary>
    /// <remarks>
    /// Validity before tree membership, for the same reason every screen in this directory checks
    /// them in that order: asking a freed node whether it is inside the tree is itself the crash,
    /// and a shutdown mid-handover is the ordinary case on a handset. Silent, unlike the lookups
    /// below — a screen that has already gone is not a defect, it is the application closing.
    /// </remarks>
    private static bool Addressable(Node3D screen) =>
        GodotObject.IsInstanceValid(screen) && screen.IsInsideTree();

    private static CanvasLayer? Overlay(Node3D screen)
    {
        var overlay = screen.GetNodeOrNull<CanvasLayer>(UiLayerPath);

        if (overlay is null)
        {
            GD.PushError(
                $"The screen '{screen.Name}' has no '{UiLayerPath}' overlay, so its interface was " +
                "neither shown nor hidden and is now drawn over whatever replaced it. Every screen " +
                "scene carries a CanvasLayer marked scene-unique as Ui.");
        }

        return overlay;
    }

    private static Camera3D? Camera(Node3D screen)
    {
        var camera = screen.GetNodeOrNull<Camera3D>(CameraPath);

        if (camera is null)
        {
            GD.PushError(
                $"The screen '{screen.Name}' has no '{CameraPath}', so its 3D world is drawn " +
                "through whichever camera the previous screen left current. Every screen scene " +
                "carries a Camera3D marked scene-unique as Camera.");
        }

        return camera;
    }
}
