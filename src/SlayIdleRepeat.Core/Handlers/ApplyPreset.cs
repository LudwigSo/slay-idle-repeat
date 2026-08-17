using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Hero;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>APPLY_PRESET</c> handler: wears a saved preset, restoring the items the player still owns.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Refused during a run.</b> `07` §4 makes the loadout free to change outside a run and
/// unchangeable inside one, and this is the command that would break that — the run carries its own
/// frozen <c>StartingLoadout</c>, so a mid-run apply could not affect the run it was aimed at anyway
/// and would only desynchronise what the player is looking at from what they are playing. A run is
/// present in a meta command's slice (a player can visit a meta screen mid-run), which is exactly why
/// the check is a rule over state rather than something the dispatch table could express.
/// </para>
/// <para>
/// 🔒 <b>Applying is never entitlement-gated.</b> `12` §2.2 is explicit that presets beyond the free
/// allowance become <b>read-only</b> rather than deleted when Plus lapses — so a lapsed subscriber
/// may still load every preset they saved, and only <c>SAVE_PRESET</c> asks about Plus. This handler
/// deliberately does not name <c>Entitlements</c> at all.
/// </para>
/// <para>
/// <b>Best-effort, and the rule says why.</b> Items get salvaged, merged and lost; a preset that
/// refused to load because one ring is gone would stop working exactly as the player played, and
/// deleting it would destroy the only record of the build. The slots whose items survive are restored
/// and the rest are left empty. The preset itself is not rewritten — reloading it after the ring
/// comes back restores the ring.
/// </para>
/// </remarks>
internal static class ApplyPreset
{
    /// <summary>Applies <c>APPLY_PRESET</c>.</summary>
    /// <param name="command">The slot to load.</param>
    /// <param name="input">The cloned, already-caught-up slice.</param>
    /// <returns>
    /// Accepted with no events — wearing gear moves no currency — or <c>ILLEGAL_STATE</c> when a run
    /// is in progress or the slot holds no preset.
    /// </returns>
    internal static HandlerResult Handle(ApplyPresetCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        if (!LoadoutRules.MayChangeLoadout(input.State.Run))
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        if (!input.Player.TryGetPreset(command.PresetSlot, out var preset))
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        input.Player.WearLoadout(LoadoutRules.Applied(preset!.Loadout, input.Player.Inventory));

        return HandlerResult.Accept();
    }
}
