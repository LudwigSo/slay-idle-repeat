using SlayIdleRepeat.Client.Composition;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// The handover onto the Dice Forge, and the way back off it.
/// </summary>
/// <remarks>
/// 🔒 The board is reused and the forge screen frees itself — see <see cref="RunDecisionHandover"/>,
/// where that decision and its reasons are stated once for every decision screen.
/// </remarks>
public static class DiceForgeHandover
{
    /// <summary>Puts the forge the run is standing on beside the board and hides the board.</summary>
    /// <param name="from">The board handing over, which is hidden on success and returned to later.</param>
    /// <param name="screen">The composed forge, already built for the run standing on the tile.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <returns>True when the screen is in the tree and the board is hidden behind it.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static bool Show(Board from, ComposedDiceForgeScreen screen, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(screen);

        return RunDecisionHandover.Show<DiceForge>(
            from, DiceForge.ScenePath, forge => forge.Drive(screen.DiceForge, from, lifetime));
    }

    /// <summary>Hands control back to the board the forge was entered from, and frees the screen.</summary>
    /// <param name="screen">The forge standing down, which is queued for freeing.</param>
    /// <param name="to">The board the forge was entered from, which is shown and read again.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static void Return(DiceForge screen, Board to) => RunDecisionHandover.Return(screen, to);
}
