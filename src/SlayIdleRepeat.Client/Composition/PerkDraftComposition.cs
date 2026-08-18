using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Composition;

/// <summary>Everything one Perk Draft screen needs: the presenter that drives it.</summary>
/// <remarks>
/// A holder for one presenter rather than a bare presenter, matching every other composed screen in
/// this build. It is what gives the screen somewhere to arrive when it grows a second collaborator
/// — the way the replay's motion setting arrived beside its presenter — without every caller and
/// every handover changing shape on that day.
/// </remarks>
public sealed class ComposedPerkDraftScreen
{
    /// <summary>Holds the presenter the draft screen renders.</summary>
    /// <param name="perkDraft">Drives the Perk Draft screen.</param>
    /// <exception cref="ArgumentNullException"><paramref name="perkDraft"/> is null.</exception>
    public ComposedPerkDraftScreen(PerkDraftPresenter perkDraft)
    {
        ArgumentNullException.ThrowIfNull(perkDraft);

        PerkDraft = perkDraft;
    }

    /// <summary>Drives the Perk Draft screen.</summary>
    public PerkDraftPresenter PerkDraft { get; }
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
    /// <exception cref="ArgumentNullException"><paramref name="composed"/> is null.</exception>
    public static ComposedPerkDraftScreen CreatePerkDraftScreen(
        ComposedGodotClient composed, PlayerId player, RunId run)
    {
        ArgumentNullException.ThrowIfNull(composed);

        var content = composed.Client.Content.Current;
        var strings = new LocaleStringCatalogue(content, composed.Capabilities.PlatformInfo.Locale);

        return new ComposedPerkDraftScreen(
            new PerkDraftPresenter(composed.Client.GameHost, strings, content, player, run));
    }
}
