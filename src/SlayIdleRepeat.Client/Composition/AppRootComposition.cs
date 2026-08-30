using SlayIdleRepeat.Client.Game.Net;
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

    /// <summary>
    /// Hands back the one connection presenter the build shares, or nothing when none was composed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 A factory rather than a property read, for the same reason
    /// <see cref="CreateAppRootPresenter"/> is one: reading the connection half off the graph means
    /// naming the wire port beside it, and a scene holds no port references. The root asks, and gets a
    /// presenter or a null.
    /// </para>
    /// <para>
    /// 🔴 <b>It answers null in every build that ships today.</b> The only production caller of
    /// <c>ClientComposition.Compose</c> is <c>GodotClientComposition.ComposeLocalHost</c>, which passes
    /// no wire seam — so the overlay is never instantiated, and the connection is drawn exactly as the
    /// specification says a working one is drawn: not at all. It stays honest rather than convenient:
    /// there is no live driver behind this until <c>M5-15</c> composes the HTTP adapter.
    /// </para>
    /// </remarks>
    /// <param name="composed">The graph the application root built and holds.</param>
    /// <exception cref="ArgumentNullException"><paramref name="composed"/> is null.</exception>
    public static ConnectionPresenter? CreateConnectionPresenter(ComposedGodotClient composed)
    {
        ArgumentNullException.ThrowIfNull(composed);

        return composed.Client.Connection;
    }

    /// <summary>
    /// Hands back the driver that advances the reconnect ladder each frame, or nothing on an arm
    /// with no connection to advance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 Stated over the PORTABLE half rather than over <see cref="ComposedGodotClient"/>, unlike
    /// its two siblings. The engine half cannot be constructed outside the engine — an audio
    /// capability needs a real node — so a factory keyed on it is one no case can drive, and this is
    /// the one factory whose two arms have to be told apart by a test rather than by a headless run.
    /// </para>
    /// <para>
    /// A scene reads no port off this: it asks, and gets a driver or a null.
    /// </para>
    /// </remarks>
    /// <param name="composed">The portable half of the graph the application root built and holds.</param>
    /// <exception cref="ArgumentNullException"><paramref name="composed"/> is null.</exception>
    public static ConnectionPump? CreateConnectionPump(ComposedClient composed)
    {
        ArgumentNullException.ThrowIfNull(composed);

        return composed.Pump;
    }
}
