using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Perks;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// 🔒 M3-06, `14` §2.3 — the <c>PICK_PERK</c> handler: takes one of the three drafted options
/// (`06` §1) and closes the draft.
/// </summary>
/// <remarks>
/// 🔒 <b>The draft is regenerated, never persisted.</b> `14` §8.1 keeps drafting byte-identical
/// for a given seed, so the three options a client is shown are re-derived here from the run's
/// committed <c>draft</c>-stream position and the battle <c>Run.MarkDraftPending</c> captured —
/// see <see cref="GenerateCurrentOptions"/>, duplicated identically on <c>Handlers.RerollDraft</c>
/// rather than shared through a non-handler type: `30` §11.4 makes every type under
/// <c>Core/Handlers/</c> a registered command handler, so a shared helper type there would widen
/// <c>DomainPurityTests.Every_command_type_is_handled_by_Apply</c>'s dispatch surface for free.
/// Taking an option then advances the stream (via the two draws per slot the regeneration itself
/// consumes) exactly once <c>GameRules.Apply</c> folds this command's <c>RunRngScope</c> back, the
/// same "one command, one atomic draw" shape <c>ROLL_DICE</c> uses.
/// </remarks>
internal static class PickPerk
{
    /// <summary>`06` §1 — applies <c>PICK_PERK</c>.</summary>
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
    /// stream at its current (uncommitted-until-<c>Apply</c>-folds-it) position — see this type's
    /// remarks for why this is duplicated rather than shared.
    /// </summary>
    internal static IReadOnlyList<DraftOption> GenerateCurrentOptions(HandlerInput input, Run run)
    {
        var catalogue = PerkCatalogue.Read(input.Context.Content);
        var owned = run.DraftedPerks;
        var rng = input.Rng.Stream(RngStreams.Draft);

        var battleKind = (TileKind)run.DraftBattleKindValue;
        var isElite = battleKind == TileKind.Elite;
        var isBoss = battleKind == TileKind.Boss;

        return PerkDraftEngine.GenerateOptions(catalogue, owned, rng, run.DraftBattleStage, isElite, isBoss);
    }
}
