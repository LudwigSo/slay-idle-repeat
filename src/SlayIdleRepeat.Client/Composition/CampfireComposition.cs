using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Composition;

/// <summary>Everything one Campfire / Shrine screen needs: the presenter that drives both arms.</summary>
/// <remarks>
/// One holder for two arms, because it is one presenter for two arms: which of them opens is
/// answered by the run's pending tile, inside the presenter, and nothing composed here chooses it.
/// </remarks>
public sealed class ComposedCampfireScreen
{
    /// <summary>Holds the presenter the campfire and shrine arms are both rendered from.</summary>
    /// <param name="campfire">Drives the Campfire / Shrine screen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="campfire"/> is null.</exception>
    public ComposedCampfireScreen(CampfirePresenter campfire)
    {
        ArgumentNullException.ThrowIfNull(campfire);

        Campfire = campfire;
    }

    /// <summary>Drives the Campfire / Shrine screen.</summary>
    public CampfirePresenter Campfire { get; }
}

/// <summary>
/// Builds the Campfire / Shrine presenter out of the graph the application root already composed.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 It exists so the scene never has to. A scene renders and forwards input; naming the device
/// locale source and reaching into the loaded content set the shrine's draw is projected against
/// are both the composition root's job, and this is the part of it this screen needs.
/// </para>
/// <para>
/// Nothing is composed a second time. The host, the content set and the locale all come out of the
/// graph the root owns, so the buff pool this screen names is the one the tile will resolve from.
/// </para>
/// </remarks>
public static class CampfireComposition
{
    /// <summary>Wires the screen over an already-composed client, for one run of one player.</summary>
    /// <param name="composed">The graph the application root built and holds.</param>
    /// <param name="player">The profile the boot opened.</param>
    /// <param name="run">The run standing on the campfire or shrine tile.</param>
    /// <exception cref="ArgumentNullException"><paramref name="composed"/> is null.</exception>
    public static ComposedCampfireScreen CreateCampfireScreen(
        ComposedGodotClient composed, PlayerId player, RunId run)
    {
        ArgumentNullException.ThrowIfNull(composed);

        var content = composed.Client.Content.Current;
        var strings = new LocaleStringCatalogue(content, composed.Capabilities.PlatformInfo.Locale);

        return new ComposedCampfireScreen(
            new CampfirePresenter(composed.Client.GameHost, strings, content, player, run));
    }
}
