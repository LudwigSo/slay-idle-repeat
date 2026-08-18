using SlayIdleRepeat.Client.Composition;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// The handover onto the perk draft, and the way back off it.
/// </summary>
/// <remarks>
/// 🔒 The board is reused and the draft screen frees itself — see <see cref="RunDecisionHandover"/>,
/// where that decision and its reasons are stated once for all three decision screens.
/// </remarks>
public static class PerkDraftHandover
{
    /// <summary>Puts the draft the run has open beside the board and hides the board.</summary>
    /// <param name="from">The board handing over, which is hidden on success and returned to later.</param>
    /// <param name="screen">The composed draft, already built for the run whose draft is open.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <returns>True when the screen is in the tree and the board is hidden behind it.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static bool Show(Board from, ComposedPerkDraftScreen screen, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(screen);

        return RunDecisionHandover.Show<PerkDraft>(
            from,
            PerkDraft.ScenePath,
            draft => draft.Drive(screen.PerkDraft, screen.ReducedMotion, from, lifetime));
    }

    /// <summary>Hands control back to the board the draft was entered from, and frees the screen.</summary>
    /// <param name="screen">The draft standing down, which is queued for freeing.</param>
    /// <param name="to">The board the draft was entered from, which is shown and read again.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static void Return(PerkDraft screen, Board to) => RunDecisionHandover.Return(screen, to);
}
