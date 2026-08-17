using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Composition;

/// <summary>
/// The two presenters one Home handover needs: the screen itself, and the picker its primary
/// action opens onto.
/// </summary>
/// <remarks>
/// 🔒 Built together because they are built from the same three things — the host, the loaded
/// content set and the profile — and because building the picker at the moment of the tap would
/// put a read on the far side of a button. Home renders the first and forwards the second; it
/// composes neither.
/// </remarks>
public sealed class ComposedHomeScreen
{
    /// <summary>Pairs the Home presenter with the Chapter Select presenter it hands on.</summary>
    /// <exception cref="ArgumentNullException">Either presenter is null.</exception>
    public ComposedHomeScreen(HomePresenter home, ChapterSelectPresenter chapterSelect)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(chapterSelect);

        Home = home;
        ChapterSelect = chapterSelect;
    }

    /// <summary>Drives the Home screen.</summary>
    public HomePresenter Home { get; }

    /// <summary>Drives the Chapter Select screen Home's primary action opens.</summary>
    public ChapterSelectPresenter ChapterSelect { get; }
}

/// <summary>
/// Builds the Home and Chapter Select presenters out of the graph the application root already
/// composed.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 It exists so neither scene ever has to. A scene renders and forwards input; naming the device
/// locale source and reaching into the loaded content set is the composition root's job, and this
/// is the part of it these two screens need.
/// </para>
/// <para>
/// Nothing is composed a second time. The host, the content set and the locale all come out of the
/// graph the root owns — a second graph would mean a second cache over the same directory. One
/// catalogue serves both screens for the same reason.
/// </para>
/// <para>
/// 🔒 Both screens read their strings and their chapters out of the already-loaded content
/// snapshot, so neither resolves a content path of its own. That matters beyond tidiness: the
/// resolution the graph did once is the one that cannot be done at all from inside a packed build,
/// and a second caller here would be a second thing to fix when it is.
/// </para>
/// </remarks>
public static class HomeComposition
{
    /// <summary>Wires both screens over an already-composed client.</summary>
    /// <param name="composed">The graph the application root built and holds.</param>
    /// <param name="player">The profile the boot opened.</param>
    /// <exception cref="ArgumentNullException"><paramref name="composed"/> is null.</exception>
    public static ComposedHomeScreen CreateHomeScreen(ComposedGodotClient composed, PlayerId player)
    {
        ArgumentNullException.ThrowIfNull(composed);

        var content = composed.Client.Content.Current;
        var strings = new LocaleStringCatalogue(content, composed.Capabilities.PlatformInfo.Locale);

        return new ComposedHomeScreen(
            new HomePresenter(composed.Client.GameHost, strings, player),
            new ChapterSelectPresenter(composed.Client.GameHost, strings, content, player));
    }
}
