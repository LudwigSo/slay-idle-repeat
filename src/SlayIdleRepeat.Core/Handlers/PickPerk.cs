using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Luck;
using SlayIdleRepeat.Core.Rules.Perks;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>PICK_PERK</c> handler: takes one of the three drafted options and closes the draft.
/// </summary>
/// <remarks>
/// <para>
/// The draft is regenerated, never persisted: the three options a client is shown are re-derived
/// from the run's committed draft-stream position, so drafting stays byte-identical for a given seed.
/// That derivation is <see cref="CurrentDraft.Draw"/> and it lives in the rules layer rather than
/// here, because <c>REROLL_DRAFT</c> and the Perk Draft screen's read-only projection have to arrive
/// at the same three options this handler acts on — and a projection cannot reach a handler at all.
/// </para>
/// <para>
/// The three run-scoped draft counters move when a draft is <em>taken</em>, not when one is drawn:
/// a reroll redraws the options and must not push a guarantee closer, or the guarantee would be
/// purchasable with Gold.
/// </para>
/// </remarks>
internal static class PickPerk
{
    /// <summary>Applies <c>PICK_PERK</c>.</summary>
    /// <returns>
    /// <see cref="RejectionReason.ILLEGAL_STATE"/> when no draft is pending or the index names no
    /// option; otherwise accepted.
    /// </returns>
    internal static HandlerResult Handle(PickPerkCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (!run.DraftPending)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var options = CurrentDraft.Draw(
            DraftStanding.Of(run, input.Context.Content),
            input.Rng.Stream(RngStreams.Draft),
            out var demand);

        if (command.OptionIndex < 0 || command.OptionIndex >= options.Count)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var chosen = options[command.OptionIndex];

        MoveDraftCounters(run, options, demand);

        run.UpsertPerkTier(chosen.PerkId, chosen.NewTier);
        run.ClearDraftPending();

        return HandlerResult.Accept();
    }

    /// <summary>Stores the run's three draft counters as the closing offering leaves them.</summary>
    /// <remarks>
    /// <para>
    /// Here rather than where the options are drawn, because the counters count the drafts a run
    /// actually <em>picked from</em>. That excludes the two other ways a draft ends, for two
    /// different reasons: a reroll draws a set nobody was ever offered the chance to take, and a
    /// counter a reroll moved would let a player push a guarantee towards themselves with Gold; a
    /// skip takes no option at all, so there is no pick to count — it pays the player rather than
    /// costing them, so the purchasability argument never arises for it.
    /// </para>
    /// <para>
    /// The demand is the one the options were drawn under, read before the pick is applied — the
    /// counters are about the draft as it was offered, not about what taking it left the run holding.
    /// </para>
    /// </remarks>
    private static void MoveDraftCounters(
        Run run, IReadOnlyList<DraftOption> options, DraftDemand demand)
    {
        var legendary = false;
        var aboveCommon = false;
        var ownedUpgrade = false;

        foreach (var option in options)
        {
            legendary |= option.Rarity == PerkRarity.Legendary;
            aboveCommon |= option.Rarity > PerkRarity.Common;
            ownedUpgrade |= option.IsUpgrade;
        }

        var moved = LuckService.DraftCountersAfter(
            new DraftCounters(
                run.DraftsSinceLegendaryOffered,
                run.DraftsWithoutAboveCommon,
                run.DraftsWithoutOwnedUpgrade),
            new DraftOffering(legendary, aboveCommon, ownedUpgrade),
            demand);

        run.SetDraftCounters(
            moved.DraftsSinceLegendaryOffered,
            moved.DraftsWithoutAboveCommon,
            moved.DraftsWithoutOwnedUpgrade);
    }
}
