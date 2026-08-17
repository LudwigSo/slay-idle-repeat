using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>SAVE_PRESET</c> handler: writes the hero's current loadout into a named preset slot.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The entitlement decision lives here and can live nowhere else.</b> No type under
/// <c>Rules/</c> may name <c>Entitlements</c> — `30` §3 licenses exactly one exemption to that and it
/// is the ad-reward cap's — so "may this player write a fourth slot" is answered in a handler, where
/// the session is visible. It is two reads and a comparison: the free allowance is authored at
/// <c>ads.json#/plus/freePresets</c>, and Plus is on the session. Inside the allowance, anybody may
/// write. Outside it, only a subscriber may, and everyone else gets <c>NOT_ENTITLED</c> — `14` §662's
/// own example of that value is "a Plus-gated operation without Plus (e.g. preset slot 4+)".
/// </para>
/// <para>
/// 🔒 <b>There is no upper bound on the slot number for a subscriber, because `12` §2 authors none</b>
/// — "unlimited talent and loadout presets" is the grant, and a ceiling invented to sit beside it
/// would be a limit the design set does not have.
/// </para>
/// <para>
/// <b>Not refused mid-run.</b> Saving a preset records what the hero is wearing; it changes nothing.
/// `07` §4 forbids <em>changing</em> the loadout during a run, which is <c>APPLY_PRESET</c>'s
/// problem, not this one — and a player looking at their build mid-run is exactly when they think to
/// save it.
/// </para>
/// <para>
/// Overwriting an occupied slot is the normal case and is not refused: `07` §4 gives the player three
/// named slots to manage, and asking for a delete-then-save round trip would be a rule invented here.
/// </para>
/// </remarks>
internal static class SavePreset
{
    /// <summary>Applies <c>SAVE_PRESET</c>.</summary>
    /// <param name="command">The slot to write and the player's name for it.</param>
    /// <param name="input">The cloned, already-caught-up slice.</param>
    /// <returns>
    /// Accepted with no events — a preset is player state and moves no currency — or a rejection:
    /// <c>NOT_ENTITLED</c> for a slot past the free allowance without Plus, <c>ILLEGAL_STATE</c> for a
    /// slot or a name no preset may have.
    /// </returns>
    internal static HandlerResult Handle(SavePresetCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var tuning = PresetTuning.Read(input.Context.Content);

        // The entitlement check runs BEFORE the shape check, so a free player probing slot 400 with
        // a blank name is told the thing that is actually stopping them — but it fires only ABOVE
        // the allowance. A slot BELOW the first is a malformed payload, and reporting that as an
        // entitlement would tell the player to subscribe because their client sent a zero.
        if (tuning.IsBeyondFreeAllowance(command.PresetSlot) && !input.Context.Entitlements.HasPlus)
        {
            return HandlerResult.Reject(RejectionReason.NOT_ENTITLED);
        }

        var preset = LoadoutPreset.Create(command.PresetSlot, command.Name, input.Player.Loadout);

        if (preset.IsFailure)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        input.Player.SavePreset(preset.Value);

        return HandlerResult.Accept();
    }
}
