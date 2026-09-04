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
    /// <param name="home">Drives the Home screen.</param>
    /// <param name="chapterSelect">Drives the picker Home's primary action opens.</param>
    /// <param name="board">Builds the board for a run, once there is a run to build one for.</param>
    /// <param name="gear">Builds the Inventory screen, which needs no run at all.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public ComposedHomeScreen(
        HomePresenter home,
        ChapterSelectPresenter chapterSelect,
        Func<RunId, ComposedBoardScreen> board,
        Func<ComposedInventoryScreen> gear)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(chapterSelect);
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(gear);

        Home = home;
        ChapterSelect = chapterSelect;
        Board = board;
        Gear = gear;
    }

    /// <summary>Drives the Home screen.</summary>
    public HomePresenter Home { get; }

    /// <summary>Drives the Chapter Select screen Home's primary action opens.</summary>
    public ChapterSelectPresenter ChapterSelect { get; }

    /// <summary>
    /// Builds the Inventory screen (S16).
    /// </summary>
    /// <remarks>
    /// 🔒 A factory for the same reason <see cref="Board"/> is one — the scene calls it when the player
    /// presses, so the scene stays out of composition entirely. ⚠️ Unlike the board's, it takes NO
    /// argument: S16 is between runs, reads the player's own stock and submits <c>EQUIP</c>, which is a
    /// <c>CommandKind.Meta</c> command. Threading a run through would invite a screen that read one it
    /// had no use for and broke the moment a player opened their bag without one.
    /// </remarks>
    public Func<ComposedInventoryScreen> Gear { get; }

    /// <summary>
    /// Builds the board for one run.
    /// </summary>
    /// <remarks>
    /// 🔒 A factory rather than a built presenter, because a board is about a run and neither of
    /// the two screens that reach it knows which run until the moment it opens: Home learns it from
    /// the profile read, and the picker learns it from the state its own <c>START_RUN</c> answered
    /// with. Handing the scenes a factory is what keeps them out of composition entirely — they call
    /// it, they do not assemble it.
    /// </remarks>
    public Func<RunId, ComposedBoardScreen> Board { get; }
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
            new HomePresenter(
                composed.Client.GameHost,
                strings,
                content,
                composed.Client.Clock,
                new HeroPowerReadout(content),
                player),
            new ChapterSelectPresenter(composed.Client.GameHost, strings, content, player),
            run => BoardComposition.CreateBoardScreen(composed, player, run),
            () => InventoryComposition.CreateInventoryScreen(composed, player));
    }
}
