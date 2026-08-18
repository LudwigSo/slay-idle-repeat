using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Composition;

/// <summary>Everything one Inventory screen needs: the presenter that drives it.</summary>
/// <remarks>
/// A holder for one presenter rather than a bare presenter, matching every other composed screen in
/// this build. It is what gives the screen somewhere to arrive when it grows a second collaborator —
/// the way the replay's and the draft's motion settings arrived beside theirs — without every caller
/// and every handover changing shape on that day.
/// </remarks>
public sealed class ComposedInventoryScreen
{
    /// <summary>Holds the presenter the inventory screen renders.</summary>
    /// <param name="inventory">Drives the Inventory screen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inventory"/> is null.</exception>
    public ComposedInventoryScreen(InventoryPresenter inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        Inventory = inventory;
    }

    /// <summary>Drives the Inventory screen.</summary>
    public InventoryPresenter Inventory { get; }
}

/// <summary>
/// Builds the Inventory presenter out of the graph the application root already composed.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 It exists so the scene never has to. A scene renders and forwards input; naming the device
/// locale source and reaching into the loaded content set the stock is projected against are both the
/// composition root's job, and this is the part of it this screen needs.
/// </para>
/// <para>
/// Nothing is composed a second time. The host, the content set and the locale all come out of the
/// graph the root owns — a second graph would mean a second cache over the same directory, and the
/// stock is projected against the same loaded content set the rest of the client draws from.
/// </para>
/// <para>
/// 🔒 <b>No run is passed, and that is the shape of this screen rather than an omission.</b> S16 is
/// between runs: it reads the PLAYER's stock, submits <c>EQUIP</c>, which is a <c>CommandKind.Meta</c>
/// command, and needs no run at all. Threading one in would invite a screen that read a run it had no
/// reason to and broke the moment a player opened their bag without one.
/// </para>
/// </remarks>
public static class InventoryComposition
{
    /// <summary>Wires the inventory over an already-composed client, for one player.</summary>
    /// <param name="composed">The graph the application root built and holds.</param>
    /// <param name="player">The profile the boot opened.</param>
    /// <exception cref="ArgumentNullException"><paramref name="composed"/> is null.</exception>
    public static ComposedInventoryScreen CreateInventoryScreen(
        ComposedGodotClient composed, PlayerId player)
    {
        ArgumentNullException.ThrowIfNull(composed);

        var content = composed.Client.Content.Current;
        var strings = new LocaleStringCatalogue(content, composed.Capabilities.PlatformInfo.Locale);

        return new ComposedInventoryScreen(
            new InventoryPresenter(composed.Client.GameHost, strings, content, player));
    }
}
