using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Dice;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>USE_REROLL</c> handler: rerolls the die completely.
/// </summary>
/// <remarks>
/// There is no separate "preview" step to revert — <c>ROLL_DICE</c> answers face, movement and
/// landing in one command. So this handler instead burns one draw of the dice stream, advancing the
/// Fair-Dice bag exactly as a real roll would without moving the run, so the very next
/// <c>ROLL_DICE</c> draws a different index against updated weights.
/// </remarks>
internal static class UseReroll
{
    /// <summary>Applies <c>USE_REROLL</c>.</summary>
    internal static HandlerResult Handle(UseRerollCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        // Every bonus source besides the base allotment (talents, Campfire, perks, Reroll Tokens) is
        // still unbuilt, so this reads the base allotment alone until each lands.
        var totalCharges = RerollEconomy.TotalCharges(
            talentBonus: 0, campfireVisited: false, perkBonus: 0, rerollTokensUsed: 0);

        if (!RerollEconomy.CanAffordReroll(run.RerollChargesSpentThisStage, totalCharges))
        {
            return HandlerResult.Reject(RejectionReason.CAP_REACHED);
        }

        var committedDraws = run.StreamPosition(RngStreams.Dice);

        // resetAtDraw is the run's Stage Gate anchor, not a hard 0 — see Handlers.RollDice.
        var weights = FairDiceBag.Replay(
            run.RunSeed, resetAtDraw: run.StageGateDiceAnchor, uptoDraw: committedDraws);

        // The drawn face is discarded — a reroll's entire job is to move the bag forward without
        // moving the run.
        FairDiceBag.Step(input.Rng.Stream(RngStreams.Dice), weights);

        run.SpendReroll();

        return HandlerResult.Accept();
    }
}
