using SlayIdleRepeat.Client.Composition;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// The handover onto the Event screen, and the way back off it.
/// </summary>
/// <remarks>
/// 🔒 The board is reused and the screen frees itself — see <see cref="RunDecisionHandover"/>, where
/// that decision and its reasons are stated once for every decision screen.
/// <para>
/// 🔒 <b>The board hands over on the tile alone, drawn card or not.</b> The draw is the screen's own
/// first command, so an Event tile the run has just landed on and one it is resuming onto are the
/// same destination — and the screen decides which of the two it is from the row it reads for
/// itself. A board that handed over only once a card existed would be waiting for a command nobody
/// was going to send.
/// </para>
/// </remarks>
public static class EventHandover
{
    /// <summary>Puts the event tile the run is standing on beside the board, and hides it.</summary>
    /// <param name="from">The board handing over, which is hidden on success and returned to later.</param>
    /// <param name="screen">The composed screen, already built for the run standing on the tile.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <returns>True when the screen is in the tree and the board is hidden behind it.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static bool Show(Board from, ComposedEventScreen screen, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(screen);

        return RunDecisionHandover.Show<EventScreen>(
            from, EventScreen.ScenePath, opened => opened.Drive(screen.Event, from, lifetime));
    }

    /// <summary>Hands control back to the board the tile was entered from, and frees the screen.</summary>
    /// <param name="screen">The screen standing down, which is queued for freeing.</param>
    /// <param name="to">The board the tile was entered from, which is shown and read again.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static void Return(EventScreen screen, Board to) => RunDecisionHandover.Return(screen, to);
}
