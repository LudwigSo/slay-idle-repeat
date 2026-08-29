using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Composition;

/// <summary>
/// Builds the application root's own presenter out of the graph the root has just composed.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 It exists so the root scene never has to. Reading the host off the composed graph is naming a
/// port, and a scene holds no port references — so the one line that did it moved here, where naming
/// a port is the whole job. The root now composes the graph, asks for a presenter and renders what
/// the presenter says, which is the same shape every other screen already has.
/// </para>
/// <para>
/// Nothing is composed a second time. The host comes out of the graph the root owns and holds, so
/// the profile this presenter opens is the one every later screen reads.
/// </para>
/// </remarks>
public static class AppRootComposition
{
    /// <summary>Wires the root presenter over an already-composed client.</summary>
    /// <param name="composed">The graph the application root built and holds.</param>
    /// <exception cref="ArgumentNullException"><paramref name="composed"/> is null.</exception>
    public static AppRootPresenter CreateAppRootPresenter(ComposedGodotClient composed)
    {
        ArgumentNullException.ThrowIfNull(composed);

        return new AppRootPresenter(composed.Client.GameHost);
    }
}
