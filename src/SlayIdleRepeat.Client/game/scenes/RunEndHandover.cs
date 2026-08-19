using Godot;
using SlayIdleRepeat.Client.Composition;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// The handover onto the run-end screen (S13 / S14), and the two ways back off it.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 The screen frees itself — see <see cref="RunDecisionHandover"/>, where that decision and its
/// reasons are stated once for the screens the board hands over to. They hold here for the same
/// reason: this one is about a single run's ending, holding one projection nothing else could reuse.
/// </para>
/// <para>
/// 🔒 <b>One handover for both S13 and S14, because <c>02</c> §6 makes them one moment</b> — its step 3
/// sends a declined or spent revive straight on to the results. Which of the two a player is looking at
/// is the presenter's answer to the run it reads, never a second destination for the board to choose
/// between.
/// </para>
/// <para>
/// 🔒 <b>Two ways off, because the two commands do opposite things to the run</b> — and which one was
/// accepted is what tells them apart, not a second read. <see cref="Return"/> is the revive's: the run
/// is back in the fight it lost, so the board is re-read and carries on. <see cref="Leave"/> is
/// <c>END_RUN</c>'s: the run is closed and will never be played again, so the board is freed and the
/// starting menu comes back. Both terminate — a revive's board comes back to a run that no longer
/// awaits results and its latch clears, and a closed run's board is gone.
/// </para>
/// <para>
/// 🔒 <b>The dead end this file used to name is CLOSED.</b> An accepted <c>END_RUN</c> once returned to
/// a board drawing its <c>BoardRollBlock.RunEnded</c> state with nothing that could navigate away from
/// it — the gap <see cref="BoardHandover"/> described as a free-or-reuse decision the introducing task
/// would owe. This is that task: Home is kept and re-read, the board and the picker are freed, and the
/// next run is played on a board of its own. See <see cref="BoardHandover"/> for the decision itself.
/// </para>
/// </remarks>
public static class RunEndHandover
{
    /// <summary>Puts the ending of the run beside the board, and hides it.</summary>
    /// <param name="from">The board handing over, which is hidden on success and returned to later.</param>
    /// <param name="screen">The composed screen, already built for the run being closed.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <returns>True when the screen is in the tree and the board is hidden behind it.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static bool Show(Board from, ComposedRunEndScreen screen, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(screen);

        return RunDecisionHandover.Show<RunEnd>(
            from, RunEnd.ScenePath, runEnd => runEnd.Drive(screen.RunEnd, from, lifetime));
    }

    /// <summary>Hands control back to the board the run was closed from, and frees the screen.</summary>
    /// <remarks>
    /// The revive's way off, and the only one that leaves a run to keep playing: <c>02</c> §6's revive
    /// puts the hero back into the fight that killed them, so the board is shown and read again exactly
    /// as it is after any other decision.
    /// </remarks>
    /// <param name="screen">The screen standing down, which is queued for freeing.</param>
    /// <param name="to">The board it was entered from, which is shown and read again.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static void Return(RunEnd screen, Board to) => RunDecisionHandover.Return(screen, to);

    /// <summary>
    /// Takes the player off the finished run altogether: the starting menu comes back, and both this
    /// screen and the board the run was played on are freed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Reached only from an accepted <c>END_RUN</c>, whichever way the run ended.</b> A death and a
    /// cleared chapter arrive on this screen by the same route and leave by the same one — <c>02</c> §6
    /// makes them one moment, and a player who has just won is as finished with that board as one who
    /// has just died.
    /// </para>
    /// <para>
    /// 🔒 <b>A board that cannot reach Home keeps the player rather than stranding them.</b> If the
    /// board has no starting menu to return to, the run is still closed and the tally still stands, so
    /// the fallback is the ordinary revive path: the board comes back, drawing the state it has for a
    /// finished run. That is a dead end, and it is a better one than a screen freed over nothing.
    /// </para>
    /// <para>
    /// 🔒 <b>Detached before it is queued</b>, for the reason <see cref="InventoryHandover.Return"/>
    /// gives: <c>QueueFree</c> alone defers removal to the end of the frame, which would leave this
    /// screen drawn over the Home it just revealed. The free stays queued because this is reached from
    /// this screen's own handler, where freeing outright would destroy the object the call is running on.
    /// </para>
    /// </remarks>
    /// <param name="screen">The screen standing down for good, which is queued for freeing.</param>
    /// <param name="from">The board the finished run was played on, which stands down with it.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static void Leave(RunEnd screen, Board from)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(from);

        if (!GodotObject.IsInstanceValid(from))
        {
            // A board freed underneath a running screen is the ordinary way a shutdown mid-command
            // happens on a handset. The run is closed either way, and a screen with nowhere to hand
            // back to is still a screen that has finished.
            GD.PushError("A run was closed and the board it was played on is gone.");

            screen.QueueFree();

            return;
        }

        if (!from.LeaveToHome())
        {
            Return(screen, from);

            return;
        }

        screen.GetParent()?.RemoveChild(screen);
        screen.QueueFree();
    }
}
