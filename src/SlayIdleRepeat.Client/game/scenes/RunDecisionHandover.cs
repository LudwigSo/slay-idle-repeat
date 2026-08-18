using Godot;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// The one handover the run's three decision screens share, and the way back off each of them.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The free-or-reuse decision <see cref="BoardHandover"/> says the introducing task owes is
/// made here, and it is <see cref="BattleHandover"/>'s: the board is REUSED and the decision screen
/// FREES ITSELF.</b> Both halves follow from what each screen is. The board is one long-lived
/// screen a run stands on for its whole length, so re-instantiating it per decision would add a
/// board, a presenter and a fresh read for every draft, shop, campfire and shrine of a chapter —
/// and the tenth decision of a run would be made on the tenth board. A decision screen is the
/// opposite: it is about one draft or one tile, its presenter holds that projection, and there is
/// nothing in it a second draft could reuse. Keeping it hidden would leave one dead projection in
/// memory per tile for the life of the application, and a run passes many.
/// </para>
/// <para>
/// 🔒 <b>The board is re-READ on return, not merely un-hidden.</b> Every way off these screens is a
/// command that moves the run — a perk taken, a draft skipped, a tile resolved, a rest slept — so
/// the run behind the hidden board is a different row from the one it drew. Making the screen
/// visible without reading again would put a pre-decision board in front of a post-decision run.
/// </para>
/// <para>
/// 🔒 <b>The free is deferred, and that is not a detail.</b> <see cref="Return"/> is reached from
/// the decision screen's own handler, so freeing outright would destroy the object the call is
/// running on; <c>QueueFree</c> lets the frame finish first. It is also why the board is shown
/// BEFORE the screen is queued — the order the other way round leaves one frame with nothing on it.
/// </para>
/// <para>
/// 🔒 Written once rather than three times, for the reason <see cref="BoardHandover"/> gives about
/// its own single copy: all three screens make the SAME handover for different reasons, and a
/// handover written three times is a handover that gets fixed once. What is per-screen — which
/// packed scene, and what driving it needs — is the caller's, and stays with the caller.
/// </para>
/// </remarks>
internal static class RunDecisionHandover
{
    /// <summary>Puts one decision screen beside the board and hides the board.</summary>
    /// <remarks>
    /// The screen is added to the board's own parent rather than to the board, matching every other
    /// handover in this build: a child would be drawn inside a ground the board still owns, and the
    /// board has to survive the decision to be handed back to.
    /// </remarks>
    /// <typeparam name="TScreen">The scene's own script type, as its packed scene instantiates it.</typeparam>
    /// <param name="from">The board handing over, which is hidden on success and returned to later.</param>
    /// <param name="scenePath">Where the screen's packed scene lives.</param>
    /// <param name="drive">Hands the instantiated screen its presenter, its board and its lifetime.</param>
    /// <returns>
    /// True when the screen is in the tree and the board has been hidden behind it. False says the
    /// handover did not happen at all, which the board latches on: a screen reported as opened when
    /// nothing was ever shown would then refuse to open it a second time and blame the run.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal static bool Show<TScreen>(Board from, string scenePath, Action<TScreen> drive)
        where TScreen : Control
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(scenePath);
        ArgumentNullException.ThrowIfNull(drive);

        var parent = from.GetParent();

        if (parent is null)
        {
            GD.PushError($"The board has no parent to hand '{scenePath}' to.");

            return false;
        }

        var packed = GD.Load<PackedScene>(scenePath);

        if (packed is null)
        {
            // Load answers null rather than throwing when the resource is missing or its import
            // cannot be read, so an unnamed null reference is all a caller gets unless it says so.
            GD.PushError($"The decision screen could not be loaded from '{scenePath}'.");

            return false;
        }

        var screen = packed.Instantiate<TScreen>();

        drive(screen);

        from.Visible = false;

        parent.AddChild(screen);

        return true;
    }

    /// <summary>Hands control back to the board the decision was entered from, and frees the screen.</summary>
    /// <remarks>
    /// A board that has been freed underneath a running screen — a shutdown mid-decision is the
    /// ordinary way that happens on a handset — is named rather than written to, and the screen is
    /// freed regardless: a screen with nowhere to return to is still a screen that has finished.
    /// </remarks>
    /// <param name="screen">The decision screen standing down, which is queued for freeing.</param>
    /// <param name="to">The board the decision was entered from, which is shown and read again.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    internal static void Return(Control screen, Board to)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(to);

        if (GodotObject.IsInstanceValid(to))
        {
            to.Resume();
        }
        else
        {
            GD.PushError("A decision screen finished and the board it was entered from is gone.");
        }

        screen.QueueFree();
    }
}
