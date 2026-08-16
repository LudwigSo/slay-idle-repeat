using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Luck;
using SlayIdleRepeat.Core.Rules.Perks;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>PICK_PERK</c> handler: takes one of the three drafted options and closes the draft.
/// </summary>
/// <remarks>
/// The draft is regenerated, never persisted: the three options a client is shown are re-derived
/// here from the run's committed draft-stream position, so drafting stays byte-identical for a given
/// seed. <see cref="GenerateCurrentOptions"/> is duplicated identically on
/// <c>Handlers.RerollDraft</c> rather than shared through a non-handler type, since every type under
/// <c>Core/Handlers/</c> is expected to be a registered command handler.
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

        var options = GenerateCurrentOptions(input, run);

        if (command.OptionIndex < 0 || command.OptionIndex >= options.Count)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var chosen = options[command.OptionIndex];

        run.UpsertPerkTier(chosen.PerkId, chosen.NewTier);
        run.ClearDraftPending();

        return HandlerResult.Accept();
    }

    /// <summary>
    /// The three options currently on offer, drawn from <paramref name="input"/>'s <c>draft</c>
    /// stream at its current position — see this type's remarks for why this is duplicated rather
    /// than shared.
    /// </summary>
    internal static IReadOnlyList<DraftOption> GenerateCurrentOptions(HandlerInput input, Run run)
    {
        var catalogue = PerkCatalogue.Read(input.Context.Content);
        var tuning = LuckTuning.Read(input.Context.Content);
        var owned = run.DraftedPerks;
        var rng = input.Rng.Stream(RngStreams.Draft);

        var battleKind = (TileKind)run.DraftBattleKindValue;
        var isElite = battleKind == TileKind.Elite;
        var isBoss = battleKind == TileKind.Boss;

        return PerkDraftEngine.GenerateOptions(
            catalogue,
            owned,
            rng,
            tuning,
            // M4-01b Phase 3 replaces this with LuckService.ResolveDraft over the run's three draft
            // counters and its Sustain/non-maxed state, and moves the counters after the pick.
            // Until it does, this handler is reported by the routing rule, which is the point.
            Array.Empty<DraftForce>(),
            // ⚠️ The run's own drafted perks, which is a genuine SUBSET of "ever drafted": no
            // player-lifetime Codex exists yet and M4-11 owns building one. The rule is exact
            // against whatever set it is handed; the set is the incomplete half.
            owned.Tiers.Keys.ToHashSet(StringComparer.Ordinal),
            run.DraftBattleStage,
            isElite,
            isBoss);
    }
}
