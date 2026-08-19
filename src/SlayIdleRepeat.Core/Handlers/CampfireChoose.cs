using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Board.Resolution;
using SlayIdleRepeat.Core.Rules.Dice;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>CAMPFIRE_CHOOSE</c> handler: one of the campfire's three fixed options (`03` §2) — rest,
/// upgrade an owned perk, or gain two Reroll Charges.
/// </summary>
/// <remarks>
/// <para>
/// All three are implemented. Two of them used to be refused as <c>ILLEGAL_STATE</c> because neither
/// drafted perks nor a reroll-charge grant was tracked anywhere; both are now, so a campfire before
/// the boss is the three-way decision the document describes rather than a heal with two greyed-out
/// buttons beside it.
/// </para>
/// <para>
/// 🔒 <b>The perk upgrade is refused when there is nothing to upgrade</b> — a run with no perks, or
/// one whose every perk is already at its top authored tier. Refusing is what stops the option
/// silently consuming the campfire, which is the run's one guaranteed pre-boss decision; the player
/// keeps the tile and can rest instead. The pending tile is left in place on every refusal for
/// exactly that reason.
/// </para>
/// <para>
/// ⚠️ <b>WHICH perk is upgraded is not the player's choice</b>, and that is a gap rather than a
/// design: `03` §2 reads "upgrade one owned perk to its next tier", and
/// <c>CampfireChooseCommand</c> carries a choice index over the three OPTIONS with no room for a
/// perk id. <see cref="PerkToUpgrade"/> picks deterministically and says how; widening the payload
/// is what replaces it.
/// </para>
/// </remarks>
internal static class CampfireChoose
{
    /// <summary>The campfire's rest option: heal a share of Max HP.</summary>
    internal const int RestChoiceIndex = 0;

    /// <summary>Upgrade one owned perk to its next tier.</summary>
    internal const int UpgradePerkChoiceIndex = 1;

    /// <summary>Gain Reroll Charges for the current stage.</summary>
    internal const int RerollChargesChoiceIndex = 2;

    /// <summary>Applies <c>CAMPFIRE_CHOOSE</c>.</summary>
    /// <param name="command">Which of the three options.</param>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <returns>
    /// <see cref="RejectionReason.ILLEGAL_STATE"/> when no campfire is pending, the index is outside
    /// the three, or the upgrade option was chosen with no upgradeable perk; otherwise accepted, with
    /// the option applied and the tile cleared.
    /// </returns>
    internal static HandlerResult Handle(CampfireChooseCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (!run.HasPendingTile || (TileKind)run.PendingTileKindValue != TileKind.Campfire)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        switch (command.ChoiceIndex)
        {
            case RestChoiceIndex:
            {
                var result = CampfireResolver.Heal(input);
                run.ClearPendingTile();

                return result;
            }

            case UpgradePerkChoiceIndex:
            {
                if (PerkToUpgrade(input) is not { } perk)
                {
                    // Refused with the tile INTACT, so the player can still rest. A campfire spent on
                    // an option that did nothing is the run's one guaranteed pre-boss heal, gone.
                    return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
                }

                run.UpsertPerkTier(perk.PerkId, perk.NextTier);
                run.ClearPendingTile();

                return HandlerResult.Accept();
            }

            case RerollChargesChoiceIndex:
                run.GrantRerollCharges(RerollEconomy.CampfireBonus);
                run.ClearPendingTile();

                return HandlerResult.Accept();

            default:
                return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }
    }

    /// <summary>Which perk the campfire upgrades, and to which tier — or <c>null</c> when none can be.</summary>
    /// <remarks>
    /// 🔒 <b>The LOWEST owned tier, ties broken by catalogue order.</b> Deterministic is the
    /// requirement — client and server must agree without a payload to agree on — and lowest-first is
    /// the choice that is defensible rather than arbitrary: a Tier I perk gains the most from one
    /// step, and the catalogue's own order is the only stable tiebreak available, since a run stores
    /// its perks in a dictionary and enumerating that would put hash order into the decision.
    /// <para>
    /// A perk already at the top tier its catalogue row authors is skipped — <c>Run.UpsertPerkTier</c>
    /// refuses a fourth tier, and offering one would be an option that throws rather than one that
    /// declines.
    /// </para>
    /// <para>
    /// A perk the run owns that this content version no longer authors is skipped too, on the perk
    /// effect source's precedent: a content rollback across a live run must not make the campfire
    /// unusable.
    /// </para>
    /// </remarks>
    internal static (string PerkId, int NextTier)? PerkToUpgrade(HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var owned = input.Run.DraftedPerks;

        if (owned.Tiers.Count == 0)
        {
            return null;
        }

        (string PerkId, int NextTier)? best = null;
        var bestTier = int.MaxValue;

        foreach (var row in PerkCatalogue.Read(input.Context.Content).All)
        {
            var tier = owned.TierOf(row.Id);

            if (tier < 1 || tier >= row.TierCount || tier >= bestTier)
            {
                continue;
            }

            bestTier = tier;
            best = (row.Id, tier + 1);
        }

        return best;
    }
}
