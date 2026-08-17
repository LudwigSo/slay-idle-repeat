using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Composition;

/// <summary>
/// The two presenters one Board screen needs: the board itself, and the die panel its HUD opens.
/// </summary>
/// <remarks>
/// 🔒 Built together because the panel is part of the board rather than a place the board navigates
/// to — it is opened over the HUD and closed again without the run moving — and because building it
/// on the press would put a content read behind a long press. The board renders the first and shows
/// the second; it composes neither.
/// </remarks>
public sealed class ComposedBoardScreen
{
    /// <summary>Pairs the Board presenter with the Die Panel presenter it shows.</summary>
    /// <exception cref="ArgumentNullException">Either presenter is null.</exception>
    public ComposedBoardScreen(BoardPresenter board, DiePanelPresenter diePanel)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(diePanel);

        Board = board;
        DiePanel = diePanel;
    }

    /// <summary>Drives the Board screen.</summary>
    public BoardPresenter Board { get; }

    /// <summary>Drives the Die Panel the board's HUD opens.</summary>
    public DiePanelPresenter DiePanel { get; }
}

/// <summary>
/// Builds the Board and Die Panel presenters out of the graph the application root already composed.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 It exists so neither scene ever has to. A scene renders and forwards input; naming the device
/// locale source, reaching into the loaded content set and handing over the clock a soft timer is
/// measured against are all the composition root's job, and this is the part of it these two need.
/// </para>
/// <para>
/// Nothing is composed a second time. The host, the content set, the locale and the clock all come
/// out of the graph the root owns — a second graph would mean a second cache over the same
/// directory, and a second clock would mean a second answer to what time it is.
/// </para>
/// </remarks>
public static class BoardComposition
{
    /// <summary>Wires both over an already-composed client, for one run of one player.</summary>
    /// <param name="composed">The graph the application root built and holds.</param>
    /// <param name="player">The profile the boot opened.</param>
    /// <param name="run">The run being played.</param>
    /// <param name="rerollRingLapses">
    /// Whether the reroll ring expires by itself. Passed through rather than read, because the
    /// accessibility screen that would turn it off is not built — see <see cref="BoardPresenter"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="composed"/> is null.</exception>
    public static ComposedBoardScreen CreateBoardScreen(
        ComposedGodotClient composed,
        PlayerId player,
        RunId run,
        bool rerollRingLapses = true)
    {
        ArgumentNullException.ThrowIfNull(composed);

        var content = composed.Client.Content.Current;
        var strings = new LocaleStringCatalogue(content, composed.Capabilities.PlatformInfo.Locale);

        return new ComposedBoardScreen(
            new BoardPresenter(
                composed.Client.GameHost,
                strings,
                content,
                composed.Client.Clock,
                player,
                run,
                rerollRingLapses),
            new DiePanelPresenter(strings));
    }
}
