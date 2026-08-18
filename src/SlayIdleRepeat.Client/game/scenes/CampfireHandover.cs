using SlayIdleRepeat.Client.Composition;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// The handover onto the campfire / shrine screen, and the way back off it.
/// </summary>
/// <remarks>
/// 🔒 The board is reused and the screen frees itself — see <see cref="RunDecisionHandover"/>, where
/// that decision and its reasons are stated once for all three decision screens.
/// <para>
/// One handover for two tile kinds, because it is one screen with two arms: which arm opens is the
/// presenter's answer to the run's pending tile, not a second destination for the board to choose
/// between. A board that picked the arm would be deciding, off a transcribed tile number, something
/// the screen decides again anyway from the row it reads for itself.
/// </para>
/// </remarks>
public static class CampfireHandover
{
    /// <summary>Puts the campfire or shrine the run is standing on beside the board, and hides it.</summary>
    /// <param name="from">The board handing over, which is hidden on success and returned to later.</param>
    /// <param name="screen">The composed screen, already built for the run standing on the tile.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <returns>True when the screen is in the tree and the board is hidden behind it.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static bool Show(Board from, ComposedCampfireScreen screen, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(screen);

        return RunDecisionHandover.Show<Campfire>(
            from, Campfire.ScenePath, campfire => campfire.Drive(screen.Campfire, from, lifetime));
    }

    /// <summary>Hands control back to the board the tile was entered from, and frees the screen.</summary>
    /// <param name="screen">The screen standing down, which is queued for freeing.</param>
    /// <param name="to">The board the tile was entered from, which is shown and read again.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static void Return(Campfire screen, Board to) => RunDecisionHandover.Return(screen, to);
}
