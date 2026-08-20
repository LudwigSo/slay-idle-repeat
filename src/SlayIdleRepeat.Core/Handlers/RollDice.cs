using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Board.Resolution;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>ROLL_DICE</c> handler: draws one number 1..6 off the dice stream and walks that many nodes
/// over the run's actual board.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>A roll answers a number; the board answers what happens next.</b> The die carries no
/// per-face effect, so this handler has exactly two jobs — turn a draw into a movement, and hand it
/// to <see cref="RunMovement.Advance"/>, which is the one body both movement commands share.
/// </para>
/// <para>
/// The draw is uniform over the six sides, straight off the <c>dice</c> stream. There is no weighted
/// bag and no luck smoothing: an ordinary die is ordinary, and a run's sequence of rolls is a pure
/// function of its seed and the number of draws already taken.
/// </para>
/// <para>
/// 🔒 <b>The alternative to rolling is <c>USE_FIXED_DIE</c>, and it is a different command on
/// purpose.</b> This one is the random one and consumes exactly one dice-stream draw; that one
/// spends a held die showing a chosen number and consumes none. Keeping them apart is what stops a
/// replay having to read a payload to know whether the stream moved.
/// </para>
/// </remarks>
internal static class RollDice
{
    /// <summary>Applies <c>ROLL_DICE</c>.</summary>
    internal static HandlerResult Handle(RollDiceCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (RunMovement.RefuseIfCannotMove(run) is { } refusal)
        {
            return refusal;
        }

        var board = BoardResolution.Resolve(run, input.Context.Content, input.Rng);
        var pips = input.Rng.Stream(RngStreams.Dice).Range(Die.MinPips, Die.MaxPips + 1);

        var events = new List<DomainEvent>(1)
        {
            new DiceRolled(DomainEvent.UnstampedSequence, pips),
        };

        // 19 Part E's CUR_SLIPPERY, honoured here rather than as a build effect: 18 §2.1 has no
        // "movement" stat, and the penalty's floor of 1 is what stops a cursed run standing still.
        // 🔒 It applies to a ROLL and not to a fixed die — see Handlers.UseFixedDie for why.
        var steps = CurseEffects.PenalisedMovement(pips, run.Curses);

        return RunMovement.Advance(input, board, run, events, steps);
    }
}
