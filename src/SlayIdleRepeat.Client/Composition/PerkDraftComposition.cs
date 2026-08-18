using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Composition;

/// <summary>
/// Everything one Perk Draft screen needs: the presenter that drives it, and the one accessibility
/// setting the scene half has to honour by itself.
/// </summary>
/// <remarks>
/// 🔒 The motion setting is carried here for the reason <c>ComposedBattleScreen</c> carries its own:
/// the scene owns an animation the presenter knows nothing about — the upgrade card's entrance — and a
/// screen that read the setting for itself would be a second place the answer could differ. This is
/// the second collaborator this holder was written to have somewhere to put.
/// </remarks>
public sealed class ComposedPerkDraftScreen
{
    /// <summary>Pairs the draft presenter with the motion setting its scene half draws under.</summary>
    /// <param name="perkDraft">Drives the Perk Draft screen.</param>
    /// <param name="reducedMotion">Whether the card entrance is shortened to the accessibility floor.</param>
    /// <exception cref="ArgumentNullException"><paramref name="perkDraft"/> is null.</exception>
    public ComposedPerkDraftScreen(PerkDraftPresenter perkDraft, bool reducedMotion)
    {
        ArgumentNullException.ThrowIfNull(perkDraft);

        PerkDraft = perkDraft;
        ReducedMotion = reducedMotion;
    }

    /// <summary>Drives the Perk Draft screen.</summary>
    public PerkDraftPresenter PerkDraft { get; }

    /// <summary>Whether this screen's own animation is shortened to the accessibility floor.</summary>
    public bool ReducedMotion { get; }
}

/// <summary>
/// Builds the Perk Draft presenter out of the graph the application root already composed.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 It exists so the scene never has to. A scene renders and forwards input; naming the device
/// locale source and reaching into the loaded content set the draft is projected against are both
/// the composition root's job, and this is the part of it this screen needs.
/// </para>
/// <para>
/// Nothing is composed a second time. The host, the content set and the locale all come out of the
/// graph the root owns — a second graph would mean a second cache over the same directory, and the
/// draft is projected against the same loaded content set the rest of the client draws from.
/// </para>
/// </remarks>
public static class PerkDraftComposition
{
    /// <summary>Wires the draft over an already-composed client, for one run of one player.</summary>
    /// <param name="composed">The graph the application root built and holds.</param>
    /// <param name="player">The profile the boot opened.</param>
    /// <param name="run">The run whose draft is open.</param>
    /// <param name="reducedMotion">
    /// 🔴 Whether the card entrance is shortened. An argument rather than a constant, and defaulted,
    /// because the settings screen that would remember it is not built — see
    /// <c>BattleComposition.CreateBattleScreen</c>, which carries the identical seam for the identical
    /// reason. The default is the one that plays the animation, matching the replay's.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="composed"/> is null.</exception>
    public static ComposedPerkDraftScreen CreatePerkDraftScreen(
        ComposedGodotClient composed, PlayerId player, RunId run, bool reducedMotion = false)
    {
        ArgumentNullException.ThrowIfNull(composed);

        var content = composed.Client.Content.Current;
        var strings = new LocaleStringCatalogue(content, composed.Capabilities.PlatformInfo.Locale);

        return new ComposedPerkDraftScreen(
            new PerkDraftPresenter(composed.Client.GameHost, strings, content, player, run),
            reducedMotion);
    }
}
