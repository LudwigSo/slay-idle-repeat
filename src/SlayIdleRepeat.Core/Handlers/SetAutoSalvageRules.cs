using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>SET_AUTO_SALVAGE_RULES</c> handler: replaces the player's auto-salvage filter.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>This is the writer <c>Player.AutoSalvageRules</c> never had.</b> The column shipped with
/// M4-05, <c>Rules.Forge.AutoSalvageFilter</c> was written and tested against it, and no command
/// could put a row in it — so the filter was reachable from a fixture and from nothing a player
/// could send. ⚠️ <b>Nothing applies the filter even now.</b> Setting the rows is this ruling's
/// scope; sweeping the stock at run end is not, and the forge screen that edits them is M9-01's. The
/// honest statement is that <c>AutoSalvageFilter.Select</c> still has no production caller — this
/// change removes one of the two reasons it had none.
/// </para>
/// <para>
/// 🔒 <b>The payload shape is derived from <see cref="AutoSalvageRule"/>, which `08` §4.3's own
/// example authors.</b> `14` §2.3 sketches no payload for this row, and steering S6 forbids
/// inventing one, so the command carries a list of exactly the persisted row and the validation
/// below is stated over numbers the design set already authors: the <see cref="Rarity"/> ladder, and
/// <c>tuning/forge.json#/enhance</c>'s level range. Nothing here is a bound chosen for feel.
/// </para>
/// <para>
/// <b>Not refused mid-run</b>, on <c>SAVE_PRESET</c>'s reason: the filter is configuration, it moves
/// no currency and it changes nothing the run is fighting with. `07` §4's in-run freeze is about the
/// loadout.
/// </para>
/// <para>
/// ⚠️ <b>Every refusal here is <c>ILLEGAL_STATE</c>, because `14` §16.2 authors no finer domain-tier
/// value for a malformed configuration.</b> That makes the code alone useless for telling the four
/// rules apart, which is why the suite pins each one against a control world identical in every
/// respect but the fact its rule reads.
/// </para>
/// </remarks>
internal static class SetAutoSalvageRules
{
    /// <summary>Applies <c>SET_AUTO_SALVAGE_RULES</c>.</summary>
    /// <param name="command">The filter rows to store.</param>
    /// <param name="input">The cloned, already-caught-up slice.</param>
    /// <returns>
    /// Accepted with no events — a filter is player configuration and moves no currency — or
    /// <c>ILLEGAL_STATE</c> for a row list no filter may be.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal static HandlerResult Handle(SetAutoSalvageRulesCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        // 🔒 The bound is the LADDER's own length, not a number chosen here: one row per band is the
        // most a filter can carry once repeats are refused below, so a longer list is a payload that
        // could not describe a filter whatever it held. Derived rather than authored, because no
        // document authors a row count and S6 forbids inventing one — a literal 5 here would be a
        // limit nobody decided that happens to agree with the ladder today.
        //
        // ⚠️ MEASURED, and worth saying rather than implying: this arm is SUBSUMED by the repeat
        // rule below. By pigeonhole, a list longer than the ladder must name some band twice, so
        // deleting this check entirely left the whole suite green. What it buys is (a) the bound
        // stated where a reader looks for it and derived from the ladder rather than guessed, and
        // (b) a cap on the work a hostile payload can cause, since it runs before the tuning read
        // and before the loop. The half of it that IS independently provable is the VALUE: tighten
        // it by one and the case's control — one legal row per band — goes red.
        if (command.Rules.Count > Enum.GetValues<Rarity>().Length)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var forge = ForgeTuning.Read(input.Context.Content);
        var bands = new HashSet<Rarity>(command.Rules.Count);

        foreach (var rule in command.Rules)
        {
            // Rarity has no zero member on purpose, so this is what an uninitialised wire column
            // arrives as. A row over a band nothing can be would sweep nothing, forever, invisibly —
            // Player.Rehydrate refuses the same row on the way in, and refusing it here is what
            // stops it ever being written.
            if (!Enum.IsDefined(rule.Rarity))
            {
                return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
            }

            // 🔒 A repeat is refused HERE while AutoSalvageFilter deliberately tolerates one, and the
            // asymmetry is the point rather than a disagreement: the filter reads a PERSISTED row and
            // must do something defensible with whatever it finds, so it sweeps on either row and
            // silently ignores neither. The command is the writer, and a filter carrying two ceilings
            // for one band is a screen the player cannot read back — which of the two they set is
            // decided by array order and nothing tells them.
            if (!bands.Add(rule.Rarity))
            {
                return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
            }

            // The authored enhancement range, inclusive of one past the top: 08 §4.2 runs +0 to +15,
            // "below +16" is how a player says "sweep this band whatever it is enhanced to", and
            // "below +0" is how they keep a row in place with it switched off (AutoSalvageRule's own
            // remarks). Above the top plus one is a level no item can reach, which is a client
            // sending a number rather than a ceiling.
            if (rule.BelowEnhanceLevel < forge.MinEnhanceLevel ||
                rule.BelowEnhanceLevel > forge.MaxEnhanceLevel + 1)
            {
                return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
            }
        }

        input.Player.SetAutoSalvageRules(command.Rules);

        return HandlerResult.Accept();
    }
}
