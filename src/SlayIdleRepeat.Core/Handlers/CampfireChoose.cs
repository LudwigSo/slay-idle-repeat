using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Board.Resolution;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// 🔒 M3-03, `03` §2 — the <c>CAMPFIRE_CHOOSE</c> handler: one of the campfire's three fixed options.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Only option 0 — rest — is implemented, and the other two are refused rather than accepted
/// as no-ops.</b> That is the whole judgement in this file, so it is stated plainly:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Option 1, upgrade a perk's tier</b>, acts on drafted perks. <c>Run</c> holds none —
/// <c>GapRegister</c>'s <c>DraftedPerks</c> entry, M3-06's, keyed on a <c>PerkDefinition</c> that
/// must not yet exist — so there is nothing to upgrade.
/// </item>
/// <item>
/// <b>Option 2, gain 2 Reroll Charges</b>, acts on a reroll-charge count that is tracked
/// <em>nowhere in this codebase</em>. <see cref="Content.MinigameReward.RerollCharges"/> is read from
/// `03` §6.1 and deliberately not granted for the same reason.
/// </item>
/// </list>
/// <para>
/// 🔒 <b>Why <c>ILLEGAL_STATE</c> and not a silent accept.</b> An accepted command tells the client
/// the rest happened: the campfire is consumed, the animation plays, and the player believes they
/// spent their one rest on a perk upgrade that never occurred. A refusal is visible, is the honest
/// description of the state (`14` §16.2's <c>ILLEGAL_STATE</c> — the command is legal in general and
/// not in this state), and goes green on its own the day either system lands.
/// </para>
/// <para>
/// ⚠️ The pending tile is <b>not</b> cleared on a refusal, so the player may still take the rest.
/// A refused command leaves the caller's own slice untouched anyway (`30` §2.1's P4), which makes
/// that automatic rather than remembered.
/// </para>
/// </remarks>
internal static class CampfireChoose
{
    /// <summary>`03` §2 — the campfire's rest option: heal a share of Max HP.</summary>
    internal const int RestChoiceIndex = 0;

    /// <summary>`03` §2 — upgrade one drafted perk's tier. M3-06's <c>DraftedPerks</c> gap.</summary>
    internal const int UpgradePerkChoiceIndex = 1;

    /// <summary>`03` §2 — gain 2 Reroll Charges. Tracked nowhere in this codebase yet.</summary>
    internal const int RerollChargesChoiceIndex = 2;

    /// <summary>`03` §2 — applies <c>CAMPFIRE_CHOOSE</c>.</summary>
    /// <param name="command">Which of the three options.</param>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <returns>
    /// <see cref="RejectionReason.ILLEGAL_STATE"/> when no campfire is pending, or for either of the
    /// two options whose systems do not exist, or for an index outside the three; otherwise the rest
    /// is applied and the tile cleared.
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

        if (command.ChoiceIndex is UpgradePerkChoiceIndex or RerollChargesChoiceIndex or not RestChoiceIndex)
        {
            // UpgradePerkChoiceIndex, RerollChargesChoiceIndex and anything outside the three share
            // one answer, and deliberately: 14 §16.2 has no finer value, and the two unbuilt options
            // are genuinely as unavailable as an option that does not exist.
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var result = CampfireResolver.Heal(input);

        run.ClearPendingTile();

        return result;
    }
}
