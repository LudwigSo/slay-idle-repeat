using SlayIdleRepeat.Client.Composition;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// The handover onto the run-end screen (S13 / S14), and the way back off it.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 The board is reused and the screen frees itself — see <see cref="RunDecisionHandover"/>, where that
/// decision and its reasons are stated once for the screens the board hands over to. They hold here for
/// the same reasons: the board is one long-lived screen a run stands on for its whole length, and this
/// one is about a single run's ending, holding one projection nothing else could reuse.
/// </para>
/// <para>
/// 🔒 <b>One handover for both S13 and S14, because <c>02</c> §6 makes them one moment</b> — its step 3
/// sends a declined or spent revive straight on to the results. Which of the two a player is looking at
/// is the presenter's answer to the run it reads, never a second destination for the board to choose
/// between.
/// </para>
/// <para>
/// 🔒 <b>The board is re-READ on return, and here that is what makes this screen terminate.</b> Both ways
/// off it move the run — a revive puts it back into the fight it lost, <c>END_RUN</c> closes it — so the
/// board comes back to a run that no longer awaits results, its latch clears, and the player is sent on
/// rather than round again.
/// </para>
/// <para>
/// 🔴 <b>An accepted <c>END_RUN</c> returns to a board that has no way off itself, and closing that gap is
/// not this task's.</b> The run is finished, the board draws its *"run ended"* state, and nothing in this
/// build navigates from there back to Home: <c>BoardRollBlock.RunEnded</c> has said so since M7-05
/// (<em>"escaped by leaving, which no screen here can do yet"</em>), and <see cref="BoardHandover"/> is
/// explicit that the handover onto the board is one-way and that *"the navigation stack that introduces a
/// back path owes this a free-or-reuse decision; it does not inherit one"*. Inventing that stack here
/// would be inventing the thing that remark refuses. What this screen owns is that the run is properly
/// closed and its rewards banked before a player arrives there.
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
    /// <param name="screen">The screen standing down, which is queued for freeing.</param>
    /// <param name="to">The board it was entered from, which is shown and read again.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static void Return(RunEnd screen, Board to) => RunDecisionHandover.Return(screen, to);
}
