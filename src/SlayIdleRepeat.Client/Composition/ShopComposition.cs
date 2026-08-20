using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Composition;

/// <summary>Everything one Shop screen needs: the presenter that drives it.</summary>
/// <remarks>
/// A holder for one presenter rather than a bare presenter, matching every other composed screen in
/// this build — see <see cref="ComposedPerkDraftScreen"/> for why the shape is kept even at one.
/// </remarks>
public sealed class ComposedShopScreen
{
    /// <summary>Holds the presenter the shop screen renders.</summary>
    /// <param name="shop">Drives the Shop screen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="shop"/> is null.</exception>
    public ComposedShopScreen(ShopPresenter shop)
    {
        ArgumentNullException.ThrowIfNull(shop);

        Shop = shop;
    }

    /// <summary>Drives the Shop screen.</summary>
    public ShopPresenter Shop { get; }
}

/// <summary>
/// Builds the Shop presenter out of the graph the application root already composed.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 It exists so the scene never has to. A scene renders and forwards input; naming the device
/// locale source is the composition root's job, and this is the part of it this screen needs.
/// </para>
/// <para>
/// The content set reaches the presenter as well as the locale catalogue: the offer's pools and its
/// price tables are content, and <c>Rules.Economy.ShopView</c> projects the four rows out of them
/// against the position the run recorded when it stocked.
/// </para>
/// </remarks>
public static class ShopComposition
{
    /// <summary>Wires the shop over an already-composed client, for one run of one player.</summary>
    /// <param name="composed">The graph the application root built and holds.</param>
    /// <param name="player">The profile the boot opened.</param>
    /// <param name="run">The run standing on the shop tile.</param>
    /// <exception cref="ArgumentNullException"><paramref name="composed"/> is null.</exception>
    public static ComposedShopScreen CreateShopScreen(
        ComposedGodotClient composed, PlayerId player, RunId run)
    {
        ArgumentNullException.ThrowIfNull(composed);

        var content = composed.Client.Content.Current;
        var strings = new LocaleStringCatalogue(content, composed.Capabilities.PlatformInfo.Locale);

        return new ComposedShopScreen(
            new ShopPresenter(composed.Client.GameHost, strings, player, run, content));
    }
}
