using Godot;
using SlayIdleRepeat.Client.Composition;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// The handover onto the board, written once for the two screens that make it.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 One copy because both callers make the SAME handover for different reasons — Home resumes an
/// open run, the picker enters a run it has just started — and a handover written twice is a
/// handover that gets fixed once. Everything about it that is a decision is the caller's: which run,
/// and whether there is one at all.
/// </para>
/// <para>
/// 🔒 <b>The free-or-reuse decision this file used to say the introducing task owed is now MADE, and
/// it is split, because the two screens behind a run are not the same kind of thing.</b> HOME IS
/// REUSED: it is the one screen a player always comes back to, it holds a profile read nothing else
/// re-issues, and it is where a finished run leads — so it is hidden, kept, and re-read on return.
/// THE PICKER FREES ITSELF the moment it has handed a board over, and so does the board once its run
/// is over: a picker is about one pick and a board is about one run, and neither has anything a
/// second run could reuse. That is the same rule <see cref="RunDecisionHandover"/> states for the
/// screens a run passes through, applied to the two screens a run is entered from.
/// </para>
/// <para>
/// 🔒 <b>Where the run's ending leads travels WITH the handover rather than being looked up.</b> The
/// board is what the run-end screen hands back to, so the board is what has to know where a closed
/// run goes — and only its caller knows. The picker is not that destination even though it is the
/// screen the board was entered from in one of the two cases: a player whose run has ended is owed
/// the starting menu, not the list they last picked a chapter from. So <c>home</c> is passed
/// separately from <paramref name="from"/>, and the two coincide only when Home is the caller.
/// </para>
/// </remarks>
public static class BoardHandover
{
    /// <summary>Puts a board beside the outgoing screen and hides it.</summary>
    /// <remarks>
    /// The board is added to the outgoing screen's own parent rather than to it, and the outgoing
    /// screen is hidden — the same shape every other handover in this build uses. A child would be
    /// drawn inside a ground the outgoing screen still owns, and freeing it from inside its own
    /// handler is a node destroying the object the call is running on.
    /// </remarks>
    /// <param name="from">The screen handing over, which is hidden on success.</param>
    /// <param name="home">
    /// The starting menu this run's ending leads back to, which the board keeps and returns to when
    /// <c>END_RUN</c> closes the run. Hidden rather than freed for the life of the application, so it
    /// is still there to be shown again — see the remarks on this type for why it, alone, is kept.
    /// </param>
    /// <param name="screen">The composed board, already built for the run being entered.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <returns>
    /// True when the board is in the tree and the outgoing screen is hidden behind it. False says the
    /// handover did not happen at all, which a caller that frees itself on the way out has to know:
    /// a picker that stood down for a board that was never shown would leave a player looking at
    /// nothing.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static bool Show(Control from, Home home, ComposedBoardScreen screen, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(screen);

        var parent = from.GetParent();

        if (parent is null)
        {
            GD.PushError("The outgoing screen has no parent to hand the board to.");

            return false;
        }

        var packed = GD.Load<PackedScene>(Board.ScenePath);

        if (packed is null)
        {
            // Load answers null rather than throwing when the resource is missing or its import
            // cannot be read, so an unnamed null reference is all a caller gets unless it says so.
            GD.PushError($"The board could not be loaded from '{Board.ScenePath}'.");

            return false;
        }

        var board = packed.Instantiate<Board>();

        board.Drive(screen, home, lifetime);

        from.Visible = false;

        parent.AddChild(board);

        return true;
    }
}
