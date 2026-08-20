using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Board.Resolution;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>USE_FIXED_DIE</c> handler: spends one held fixed die and walks exactly its number.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The whole point is that the number is not a surprise.</b> The player owns a die showing a
/// chosen number, can see the board it will walk them over, and spends it to land where they mean
/// to. So this handler takes no draw from the <c>dice</c> stream at all — a deterministic move that
/// consumed randomness would desynchronise every later roll from the same seed for no reason.
/// </para>
/// <para>
/// 🔒 <b>No curse penalty.</b> <c>CUR_SLIPPERY</c> takes 1 off a ROLL (`19` Part E, honoured in
/// <c>Handlers.RollDice</c>) and deliberately does not touch a fixed die: the die's only promise to
/// the player is <em>this many steps</em>, and a curse that silently broke that promise would make
/// the one deterministic tool in the game undependable — which is worse than the curse being weaker.
/// A cursed run is still slower on every roll it takes.
/// </para>
/// <para>
/// Everything after the movement is <see cref="RunMovement.Advance"/>'s, shared with the roll: the
/// junction pause, the boss-exact rule, the stage-gate heal and the landing are the same walk
/// whichever way the number was decided.
/// </para>
/// </remarks>
internal static class UseFixedDie
{
    /// <summary>Applies <c>USE_FIXED_DIE</c>.</summary>
    internal static HandlerResult Handle(UseFixedDieCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (!Die.IsPips(command.Pips))
        {
            // A number the die cannot show names no holding, so it can never be one the run has.
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        if (RunMovement.RefuseIfCannotMove(run) is { } refusal)
        {
            return refusal;
        }

        if (!run.SpendFixedDie(command.Pips))
        {
            // 🔒 Spent BEFORE the walk, and refused here rather than after it: a run that moved and
            // then failed to pay would have walked for free.
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var board = BoardResolution.Resolve(run, input.Context.Content, input.Rng);

        var events = new List<DomainEvent>(1)
        {
            new FixedDieUsed(DomainEvent.UnstampedSequence, command.Pips),
        };

        return RunMovement.Advance(input, board, run, events, command.Pips);
    }
}
