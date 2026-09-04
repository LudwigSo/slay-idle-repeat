using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Composition;

/// <summary>Everything one Event screen needs: the presenter that draws the card and spends it.</summary>
public sealed class ComposedEventScreen
{
    /// <summary>Holds the presenter the drawn card and its options are rendered from.</summary>
    /// <param name="eventScreen">Drives the Event screen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="eventScreen"/> is null.</exception>
    public ComposedEventScreen(EventPresenter eventScreen)
    {
        ArgumentNullException.ThrowIfNull(eventScreen);

        Event = eventScreen;
    }

    /// <summary>Drives the Event screen.</summary>
    public EventPresenter Event { get; }
}

/// <summary>
/// Builds the Event presenter out of the graph the application root already composed.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 It exists so the scene never has to. A scene renders and forwards input; naming the device
/// locale source and reaching into the loaded content set the drawn card is projected against are
/// both the composition root's job, and this is the part of it this screen needs.
/// </para>
/// <para>
/// Nothing is composed a second time. The host, the content set and the locale all come out of the
/// graph the root owns, so the card this screen draws its prose and prices from is the one the
/// choice will be charged against.
/// </para>
/// </remarks>
public static class EventComposition
{
    /// <summary>Wires the screen over an already-composed client, for one run of one player.</summary>
    /// <param name="composed">The graph the application root built and holds.</param>
    /// <param name="player">The profile the boot opened.</param>
    /// <param name="run">The run standing on the event tile.</param>
    /// <exception cref="ArgumentNullException"><paramref name="composed"/> is null.</exception>
    public static ComposedEventScreen CreateEventScreen(
        ComposedGodotClient composed, PlayerId player, RunId run)
    {
        ArgumentNullException.ThrowIfNull(composed);

        var content = composed.Client.Content.Current;
        var strings = new LocaleStringCatalogue(content, composed.Capabilities.PlatformInfo.Locale);

        return new ComposedEventScreen(
            new EventPresenter(composed.Client.GameHost, strings, content, player, run));
    }
}
