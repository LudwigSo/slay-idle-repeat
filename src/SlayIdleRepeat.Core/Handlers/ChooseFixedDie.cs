using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>CHOOSE_FIXED_DIE</c> handler: answers one owed fixed-die grant by naming the number the
/// die shows.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Every grant site owes a CHOICE rather than handing over a die, and this is the one command
/// that answers.</b> Most sites carry no command a number could ride on: an event outcome is drawn
/// by weight, a minigame reward is decided by how the player played, an ad reward and a set bonus
/// are passive. Recording the debt on the run and answering it here is what lets all of them offer
/// the same real choice, instead of a chosen number at the two interactive sites and an authored one
/// everywhere else.
/// </para>
/// <para>
/// 🔒 <b>An owed choice blocks nothing</b> — unlike a pending draft, which refuses every other run
/// command. A grant can land in the middle of a shop visit the player is not finished with, and a
/// mechanic that interrupted them to pick a number would be a worse trade than letting the debt sit.
/// It survives to the end of the run, and an unanswered one is simply never taken.
/// </para>
/// <para>
/// ⚠️ Nothing here checks a cap, because there is none: the holding is uncapped by ruling, so a
/// grant can always be taken and the number is the only decision.
/// </para>
/// </remarks>
internal static class ChooseFixedDie
{
    /// <summary>Applies <c>CHOOSE_FIXED_DIE</c>.</summary>
    internal static HandlerResult Handle(ChooseFixedDieCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        if (!Die.IsPips(command.Pips))
        {
            // A number the die cannot show is not a die the run could ever spend.
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        return input.Run.TakeFixedDieChoice(command.Pips)
            ? HandlerResult.Accept()
            : HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
    }
}
