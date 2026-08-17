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
/// 🔴 <b>The handover is ONE-WAY, and hiding instead of freeing is only safe because of it.</b>
/// Every screen this build opens stays in the tree for the life of the application, inert: a hidden
/// <c>Control</c> takes no input, so the button behind it cannot be reached again, and neither
/// screen draws or processes. What it is NOT is reusable. This instantiates unconditionally, so the
/// first back path that returns a player to either caller and lets them press again adds a second
/// board beside the first, with its own presenter and its own read. The navigation stack that
/// introduces a back path owes this a free-or-reuse decision; it does not inherit one.
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
    /// <param name="screen">The composed board, already built for the run being entered.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static void Show(Control from, ComposedBoardScreen screen, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(screen);

        var parent = from.GetParent();

        if (parent is null)
        {
            GD.PushError("The outgoing screen has no parent to hand the board to.");

            return;
        }

        var packed = GD.Load<PackedScene>(Board.ScenePath);

        if (packed is null)
        {
            // Load answers null rather than throwing when the resource is missing or its import
            // cannot be read, so an unnamed null reference is all a caller gets unless it says so.
            GD.PushError($"The board could not be loaded from '{Board.ScenePath}'.");

            return;
        }

        var board = packed.Instantiate<Board>();

        board.Drive(screen.Board, screen.DiePanel, lifetime);

        from.Visible = false;

        parent.AddChild(board);
    }
}
