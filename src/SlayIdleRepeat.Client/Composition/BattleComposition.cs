using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Composition;

/// <summary>
/// Everything one Battle Replay screen needs: the presenter that drives it, and the one accessibility
/// setting the scene half has to honour by itself.
/// </summary>
/// <remarks>
/// 🔒 The reduced-motion flag is carried here rather than asked of the presenter because it is
/// answered in two halves. The presenter owns the half that is timing — how long the boss phase band
/// stays up — and the scene owns the half that is drawing: the particle bursts it must not fire and
/// the animations it must shorten. One value decided once in the composition root is what keeps the
/// two halves from disagreeing about whether motion is reduced.
/// </remarks>
public sealed class ComposedBattleScreen
{
    /// <summary>Pairs the replay presenter with the motion setting its scene half draws under.</summary>
    /// <param name="battle">Drives the Battle Replay screen.</param>
    /// <param name="reducedMotion">Whether bursts are suppressed and animations shortened.</param>
    /// <exception cref="ArgumentNullException"><paramref name="battle"/> is null.</exception>
    public ComposedBattleScreen(BattleReplayPresenter battle, bool reducedMotion)
    {
        ArgumentNullException.ThrowIfNull(battle);

        Battle = battle;
        ReducedMotion = reducedMotion;
    }

    /// <summary>Drives the Battle Replay screen.</summary>
    public BattleReplayPresenter Battle { get; }

    /// <summary>Whether every burst is suppressed and every animation shortened.</summary>
    public bool ReducedMotion { get; }
}

/// <summary>
/// Builds the Battle Replay presenter out of the graph the application root already composed.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 It exists so the scene never has to. This is the ONE place the concrete local prediction is
/// named: the presenter takes the prediction as an interface, every case drives it with a double, and
/// the choice of which implementation the shipped game runs is a composition decision rather than a
/// screen's. Composed by hand, with no container anywhere near it.
/// </para>
/// <para>
/// Nothing is composed a second time. The host, the content set and the locale all come out of the
/// graph the root owns — a second graph would mean a second cache over the same directory, and the
/// simulation reads its caps out of the same loaded content set the rest of the client does.
/// </para>
/// <para>
/// 🔴 Both the opening speed and the motion setting arrive as defaulted arguments, because the
/// settings screen that would remember either is not built. They are arguments rather than constants
/// so the store that will own them has a seam to arrive at — see <see cref="BattleReplayPresenter"/>.
/// </para>
/// </remarks>
public static class BattleComposition
{
    /// <summary>Wires the replay over an already-composed client, for one battle of one run.</summary>
    /// <param name="composed">The graph the application root built and holds.</param>
    /// <param name="player">The profile the boot opened.</param>
    /// <param name="run">The run whose open battle is being watched.</param>
    /// <param name="initialSpeed">The speed the replay opens at.</param>
    /// <param name="reducedMotion">Whether every animation is shortened and every burst suppressed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="composed"/> is null.</exception>
    public static ComposedBattleScreen CreateBattleScreen(
        ComposedGodotClient composed,
        PlayerId player,
        RunId run,
        BattleSpeed initialSpeed = BattleSpeed.Single,
        bool reducedMotion = false)
    {
        ArgumentNullException.ThrowIfNull(composed);

        var content = composed.Client.Content.Current;
        var strings = new LocaleStringCatalogue(content, composed.Capabilities.PlatformInfo.Locale);

        return new ComposedBattleScreen(
            new BattleReplayPresenter(
                composed.Client.GameHost,
                strings,
                content,
                new LocalBattleSimulation(content),
                player,
                run,
                initialSpeed,
                reducedMotion),
            reducedMotion);
    }
}
