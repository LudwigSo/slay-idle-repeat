using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Composition;

/// <summary>Everything one Dice Forge screen needs: the presenter that drives it.</summary>
/// <remarks>
/// A holder for one presenter rather than a bare presenter, matching every other composed screen in
/// this build — see <see cref="ComposedPerkDraftScreen"/> for why the shape is kept even at one.
/// </remarks>
public sealed class ComposedDiceForgeScreen
{
    /// <summary>Holds the presenter the forge screen renders.</summary>
    /// <param name="diceForge">Drives the Dice Forge screen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="diceForge"/> is null.</exception>
    public ComposedDiceForgeScreen(DiceForgePresenter diceForge)
    {
        ArgumentNullException.ThrowIfNull(diceForge);

        DiceForge = diceForge;
    }

    /// <summary>Drives the Dice Forge screen.</summary>
    public DiceForgePresenter DiceForge { get; }
}

/// <summary>
/// Builds the Dice Forge presenter out of the graph the application root already composed.
/// </summary>
/// <remarks>
/// It exists so the scene never has to: a scene renders and forwards input, and naming the device
/// locale source is the composition root's job.
/// <para>
/// The content set is read for the locale catalogue alone. The die the screen draws comes off the
/// RUN — <c>Rules.Dice.RunDieView</c> composes the persisted upgrades over the starting die — and
/// the menu comes off <c>Rules.Dice.DiceForgeMenu</c>, neither of which is content.
/// </para>
/// </remarks>
public static class DiceForgeComposition
{
    /// <summary>Wires the forge over an already-composed client, for one run of one player.</summary>
    /// <param name="composed">The graph the application root built and holds.</param>
    /// <param name="player">The profile the boot opened.</param>
    /// <param name="run">The run standing on the forge tile.</param>
    /// <exception cref="ArgumentNullException"><paramref name="composed"/> is null.</exception>
    public static ComposedDiceForgeScreen CreateDiceForgeScreen(
        ComposedGodotClient composed, PlayerId player, RunId run)
    {
        ArgumentNullException.ThrowIfNull(composed);

        var content = composed.Client.Content.Current;
        var strings = new LocaleStringCatalogue(content, composed.Capabilities.PlatformInfo.Locale);

        return new ComposedDiceForgeScreen(
            new DiceForgePresenter(composed.Client.GameHost, strings, player, run));
    }
}
