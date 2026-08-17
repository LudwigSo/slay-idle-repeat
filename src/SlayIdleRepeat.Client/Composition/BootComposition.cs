using SlayIdleRepeat.Adapters.Ambient.System;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Composition;

/// <summary>
/// Builds the boot screen's presenter out of the graph the application root already composed.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 It exists so the boot scene never has to. A scene renders and forwards input; naming the
/// device locale source, a clock adapter or a filesystem-backed catalogue is the composition root's
/// job, and this is the part of it the boot screen needs.
/// </para>
/// <para>
/// Nothing is composed a second time. The host, the content set and the resolved paths all come out
/// of the graph the root owns — a second graph would mean a second cache over the same directory.
/// </para>
/// </remarks>
public static class BootComposition
{
    /// <summary>Wires the boot presenter over an already-composed client.</summary>
    /// <param name="composed">The graph the application root built and holds.</param>
    /// <exception cref="ArgumentNullException"><paramref name="composed"/> is null.</exception>
    /// <exception cref="DirectoryNotFoundException">The content data root the engine resolves does not exist.</exception>
    public static BootPresenter CreateBootPresenter(ComposedGodotClient composed)
    {
        ArgumentNullException.ThrowIfNull(composed);

        return new BootPresenter(
            composed.Client.GameHost,
            new BootStringCatalogue(
                composed.Client.Content.Current, composed.Capabilities.PlatformInfo.Locale),
            new PlaceholderAtlasCatalogue(composed.Capabilities.Paths.ResolveContentDataRoot()),
            new SystemClock());
    }
}
