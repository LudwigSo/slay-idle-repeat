using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Composition;

/// <summary>Everything one Minigame screen needs, which is the presenter that drives the game.</summary>
/// <remarks>
/// 🔒 <b>The motion setting is NOT carried beside the presenter here, and that is the one way this
/// holder differs from <c>ComposedBattleScreen</c> and <c>ComposedPerkDraftScreen</c>.</b> Those two
/// keep their own copy because their presenters take the flag and keep it private, so the scene half
/// — which has its own drawing to answer under it — has nowhere else to read it. This screen's
/// presenter publishes <see cref="MinigamePresenter.ReducedMotion"/>, so a copy here would be a
/// second holder of one setting on one screen, and the failure it invites is precisely the one the
/// setting exists to prevent: a Strike control drawn over a game that is stepped.
/// </remarks>
public sealed class ComposedMinigameScreen
{
    /// <summary>Holds the presenter the Minigame screen is driven by.</summary>
    /// <param name="minigame">Drives the Minigame screen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="minigame"/> is null.</exception>
    public ComposedMinigameScreen(MinigamePresenter minigame)
    {
        ArgumentNullException.ThrowIfNull(minigame);

        Minigame = minigame;
    }

    /// <summary>Drives the Minigame screen.</summary>
    public MinigamePresenter Minigame { get; }
}

/// <summary>
/// Builds the Minigame presenter out of the graph the application root already composed.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 It exists so the scene never has to. A scene renders and forwards input; naming the device
/// locale source and reaching into the loaded content set the reward ladder is projected against are
/// both the composition root's job, and this is the part of it this screen needs.
/// </para>
/// <para>
/// 🔴 <b>This is the one place the minigame is CHOSEN, and the choice is unenforced.</b> Nothing in
/// the run says which minigame a tile offers — the run carries the bare tile kind and
/// <c>MINIGAME_SUBMIT</c> accepts any of the four ids at any Minigame tile — so a modified client
/// could open the most generous arm every time. Moving the decision to the server needs a new
/// <c>RunSnapshot</c> field and a <c>SchemaVersion</c> bump, which is a migration rather than a
/// screen. <see cref="MinigameChoice"/> carries the full statement of what the pick is worth; it is
/// made here, once, so the presenter takes an arm rather than deciding one and every case can arm a
/// screen with the game it is about.
/// </para>
/// <para>
/// Nothing is composed a second time. The host, the content set and the locale all come out of the
/// graph the root owns, so the ladder this screen previews is read from the same content the payout
/// is scaled by.
/// </para>
/// </remarks>
public static class MinigameComposition
{
    /// <summary>Wires the screen over an already-composed client, for one tile of one run.</summary>
    /// <param name="composed">The graph the application root built and holds.</param>
    /// <param name="player">The profile the boot opened.</param>
    /// <param name="run">The run standing on the minigame tile.</param>
    /// <param name="runSeed">
    /// The run's committed seed. Half the pick's identity: without it every player in the game meets
    /// the same minigames in the same order.
    /// </param>
    /// <param name="tileLinearIndex">
    /// The tile's own linear node index — the other half. It is what makes the pick stable across a
    /// resume, so reloading cannot shop for a more generous arm.
    /// </param>
    /// <param name="reducedMotion">
    /// 🔴 Whether the timing bar is aimed by stepping rather than by timing. An argument rather than
    /// a constant, and defaulted, because the settings screen that would remember it is not built —
    /// the same seam <c>BattleComposition</c> and <c>PerkDraftComposition</c> carry for the same
    /// reason. It is handed to the presenter and to nothing else, which is where every half of the
    /// screen then reads it from.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="composed"/> is null.</exception>
    public static ComposedMinigameScreen CreateMinigameScreen(
        ComposedGodotClient composed,
        PlayerId player,
        RunId run,
        ulong runSeed,
        int tileLinearIndex,
        bool reducedMotion = false)
    {
        ArgumentNullException.ThrowIfNull(composed);

        var content = composed.Client.Content.Current;
        var strings = new LocaleStringCatalogue(content, composed.Capabilities.PlatformInfo.Locale);
        var arm = MinigameChoice.For(runSeed, tileLinearIndex, MinigameArms.Built);

        return new ComposedMinigameScreen(
            new MinigamePresenter(
                composed.Client.GameHost, strings, content, player, run, arm, reducedMotion));
    }
}
