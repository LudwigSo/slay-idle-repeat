using SlayIdleRepeat.Client.Composition;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// The handover onto the shop, and the way back off it.
/// </summary>
/// <remarks>
/// 🔒 The board is reused and the shop screen frees itself — see <see cref="RunDecisionHandover"/>,
/// where that decision and its reasons are stated once for all three decision screens.
/// </remarks>
public static class ShopHandover
{
    /// <summary>Puts the shop the run is standing on beside the board and hides the board.</summary>
    /// <param name="from">The board handing over, which is hidden on success and returned to later.</param>
    /// <param name="screen">The composed shop, already built for the run standing on the tile.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <returns>True when the screen is in the tree and the board is hidden behind it.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static bool Show(Board from, ComposedShopScreen screen, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(screen);

        return RunDecisionHandover.Show<Shop>(
            from, Shop.ScenePath, shop => shop.Drive(screen.Shop, from, lifetime));
    }

    /// <summary>Hands control back to the board the shop was entered from, and frees the screen.</summary>
    /// <param name="screen">The shop standing down, which is queued for freeing.</param>
    /// <param name="to">The board the shop was entered from, which is shown and read again.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static void Return(Shop screen, Board to) => RunDecisionHandover.Return(screen, to);
}
