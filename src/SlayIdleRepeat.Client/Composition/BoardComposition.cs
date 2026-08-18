using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Composition;

/// <summary>
/// Everything one Board screen needs: its own presenter, the die panel its HUD opens, and a factory
/// for each screen the run's next stop can be.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 The die panel is built alongside because the panel is part of the board rather than a place the
/// board navigates to — it is opened over the HUD and closed again without the run moving — and
/// because building it on the press would put a content read behind a long press. The board renders
/// the first and shows the second; it composes neither.
/// </para>
/// <para>
/// 🔒 The four destinations are FACTORIES rather than built presenters, for the reason
/// <see cref="Battle"/> states at length about its own: each is about one fight, one draft or one
/// tile, and building one at composition time would hand every fight, every draft and every shop of
/// a run the first one's presenter, its projection and its read.
/// </para>
/// </remarks>
public sealed class ComposedBoardScreen
{
    /// <summary>Pairs the Board presenter with the panel it shows and the screens it hands over to.</summary>
    /// <param name="board">Drives the Board screen.</param>
    /// <param name="diePanel">Drives the Die Panel the board's HUD opens.</param>
    /// <param name="battle">Builds the replay for the fight the run is standing in.</param>
    /// <param name="perkDraft">Builds the draft screen for the draft the run has open.</param>
    /// <param name="shop">Builds the shop screen for the shop tile the run is standing on.</param>
    /// <param name="campfire">Builds the campfire / shrine screen for the tile the run is standing on.</param>
    /// <param name="runEnd">Builds the run-end screen for the run this board is playing.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public ComposedBoardScreen(
        BoardPresenter board,
        DiePanelPresenter diePanel,
        Func<ComposedBattleScreen> battle,
        Func<ComposedPerkDraftScreen> perkDraft,
        Func<ComposedShopScreen> shop,
        Func<ComposedCampfireScreen> campfire,
        Func<ComposedRunEndScreen> runEnd)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(diePanel);
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(perkDraft);
        ArgumentNullException.ThrowIfNull(shop);
        ArgumentNullException.ThrowIfNull(campfire);
        ArgumentNullException.ThrowIfNull(runEnd);

        Board = board;
        DiePanel = diePanel;
        Battle = battle;
        PerkDraft = perkDraft;
        Shop = shop;
        Campfire = campfire;
        RunEnd = runEnd;
    }

    /// <summary>Drives the Board screen.</summary>
    public BoardPresenter Board { get; }

    /// <summary>Drives the Die Panel the board's HUD opens.</summary>
    public DiePanelPresenter DiePanel { get; }

    /// <summary>
    /// Builds the replay of the fight the run is standing in.
    /// </summary>
    /// <remarks>
    /// 🔒 A factory rather than a built presenter, and it takes nothing: unlike the board — which is
    /// about a run neither screen that reaches it knows in advance — a battle is always the one the
    /// board's own run is standing in, so the run is already fixed by the time this exists. What is
    /// not fixed is WHEN: a run fights many battles, each needs its own log, its own playhead and its
    /// own confirmation, and building one at composition time would hand every fight of the run the
    /// first fight's presenter. Calling it per fight is what keeps the board out of composition
    /// entirely — it calls, it does not assemble.
    /// </remarks>
    public Func<ComposedBattleScreen> Battle { get; }

    /// <summary>Builds the draft screen for the draft a won fight has left open.</summary>
    /// <remarks>
    /// A run drafts once per won battle and the offer is regenerated from the run's own committed
    /// stream position, so each draft is a different three cards read at a different moment.
    /// </remarks>
    public Func<ComposedPerkDraftScreen> PerkDraft { get; }

    /// <summary>Builds the shop screen for the shop tile the run has landed on.</summary>
    public Func<ComposedShopScreen> Shop { get; }

    /// <summary>Builds the campfire / shrine screen for the tile the run has landed on.</summary>
    /// <remarks>
    /// One factory for two tile kinds, because it is one screen with two arms: which arm opens is
    /// answered inside the presenter, from the run's own pending tile, and neither the board nor
    /// this decides it.
    /// </remarks>
    public Func<ComposedCampfireScreen> Campfire { get; }

    /// <summary>Builds the run-end screen (S13 / S14) for the run this board is playing.</summary>
    /// <remarks>
    /// 🔒 One factory for both screens, because <c>02</c> §6 makes them one moment — and a factory
    /// rather than a built presenter for the reason <see cref="Battle"/> gives about its own: the run is
    /// already fixed, but WHEN it ends is not, and a presenter built at composition time would hold a
    /// projection of a run that had not finished yet. It is called at most once per run in practice, and
    /// once per attempt if a revive sends the player back into the fight.
    /// </remarks>
    public Func<ComposedRunEndScreen> RunEnd { get; }
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
            new DiePanelPresenter(strings),

            // 🔴 The replay's opening speed and its motion setting take their defaults, because the
            // settings screen that would remember either is not built — see BattleComposition. The
            // ring's own accessibility flag above is deliberately not reused for them: a player who
            // turned off one soft timer has not said anything about how fast a fight should play.
            () => BattleComposition.CreateBattleScreen(composed, player, run),
            () => PerkDraftComposition.CreatePerkDraftScreen(composed, player, run),
            () => ShopComposition.CreateShopScreen(composed, player, run),
            () => CampfireComposition.CreateCampfireScreen(composed, player, run),
            () => RunEndComposition.CreateRunEndScreen(composed, player, run));
    }
}
