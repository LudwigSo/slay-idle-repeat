using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Dice;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// 🔒 `04` §3 — the <c>USE_REROLL</c> handler (M3-04): "a reroll re-rolls the die completely."
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>What "re-rolls completely" means with no pending-roll state to discard.</b> `02` §1.1's
/// run-phase state machine — the thing that would hold "a roll has been shown, awaiting accept or
/// reroll" — is still a <c>GapRegister</c> entry (<c>RunPhase</c>, M3-05), and `14` §2.3's own note on
/// <c>ROLL_DICE</c> is that it "answers face, movement and landing in ONE command": there is no
/// separate "preview" step to revert. This handler instead <b>burns one draw</b> of the <c>dice</c>
/// stream through <see cref="FairDiceBag.Step"/> — advancing the Fair-Dice bag exactly as a real roll
/// would, without moving the run — so the very next <c>ROLL_DICE</c> draws a different index against
/// updated weights: a genuinely different outcome, which is what "reroll" means from the player's
/// side, achieved with the one seam that exists today.
/// </para>
/// <para>
/// 🔴 <b>Charge-cap enforcement is NOT wired, and this is an open gap stated rather than a silent
/// bug.</b> `04` §3's charges (base 1/stage + bonuses, capped at 5, refreshed at Stage Gate) need a
/// persisted "charges spent this stage" count, and <c>Run</c> has no such field — inventing one here
/// would mean guessing at a schema change `30` §4's Run-contents row does not authorise and this
/// milestone was not asked to make. <see cref="RerollEconomy"/> implements the arithmetic `04` §3
/// specifies and is fully unit-tested; this handler accepts every <c>USE_REROLL</c> while the run is
/// active (the same existence guard every other <c>CommandKind.Run</c> row already gets from
/// <c>GameRules.Execute</c>) until a Stage Gate concept exists to hang a spent-count on. Recorded in
/// the M3-04 completion report as an explicit "out of scope" line, not discovered later as a bug.
/// </para>
/// </remarks>
internal static class UseReroll
{
    /// <summary>🔒 `04` §3 — applies <c>USE_REROLL</c>.</summary>
    internal static HandlerResult Handle(UseRerollCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        // 🔒 M3-05 closed the gap this handler's own remarks used to name: Run now carries a real
        // "charges spent this stage" count, reset at every Stage Gate. Every bonus source
        // RerollEconomy.TotalCharges takes is still a GapRegister entry (talents M4-06, Campfire's
        // visited flag, perks M3-06, the Reroll Token consumable M3-08), so this reads the base
        // allotment alone until each of those lands — a caller passing its real numbers in is exactly
        // what RerollEconomy's own remarks describe as the open seam.
        var totalCharges = RerollEconomy.TotalCharges(
            talentBonus: 0, campfireVisited: false, perkBonus: 0, rerollTokensUsed: 0);

        if (!RerollEconomy.CanAffordReroll(run.RerollChargesSpentThisStage, totalCharges))
        {
            return HandlerResult.Reject(RejectionReason.CAP_REACHED);
        }

        var committedDraws = run.StreamPosition(RngStreams.Dice);

        // 🔒 M3-05 — resetAtDraw is the run's Stage Gate anchor, not a hard 0. See Handlers.RollDice's
        // matching comment.
        var weights = FairDiceBag.Replay(
            run.RunSeed, resetAtDraw: run.StageGateDiceAnchor, uptoDraw: committedDraws);

        // The drawn face is discarded — a reroll's entire job is to move the bag forward without
        // moving the run, so the NEXT roll draws a different index against updated weights.
        FairDiceBag.Step(input.Rng.Stream(RngStreams.Dice), weights);

        run.SpendReroll();

        return HandlerResult.Accept();
    }
}
