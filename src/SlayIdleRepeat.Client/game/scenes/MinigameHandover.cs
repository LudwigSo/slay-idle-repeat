using SlayIdleRepeat.Client.Composition;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// The handover onto the Minigame screen, and the way back off it.
/// </summary>
/// <remarks>
/// 🔒 The board is reused and the screen frees itself — see <see cref="RunDecisionHandover"/>, where
/// that decision and its reasons are stated once for every decision screen.
/// <para>
/// 🔒 <b>The tile's own identity travels with the handover.</b> Which minigame a tile offers is
/// picked from the run seed and the tile's linear index, so both arrive from the board rather than
/// being read again here — a second read would be a second chance for the two to disagree, and the
/// arm a player is looking at is exactly the thing that must not change under them.
/// </para>
/// </remarks>
public static class MinigameHandover
{
    /// <summary>Puts the minigame tile the run is standing on beside the board, and hides it.</summary>
    /// <param name="from">The board handing over, which is hidden on success and returned to later.</param>
    /// <param name="screen">The composed screen, already built for the tile the run is standing on.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <returns>True when the screen is in the tree and the board is hidden behind it.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static bool Show(Board from, ComposedMinigameScreen screen, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(screen);

        return RunDecisionHandover.Show<Minigame>(
            from, Minigame.ScenePath, opened => opened.Drive(screen.Minigame, from, lifetime));
    }

    /// <summary>Hands control back to the board the tile was entered from, and frees the screen.</summary>
    /// <param name="screen">The screen standing down, which is queued for freeing.</param>
    /// <param name="to">The board the tile was entered from, which is shown and read again.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static void Return(Minigame screen, Board to) => RunDecisionHandover.Return(screen, to);
}
