using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Board.Resolution;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>ROLL_DICE</c> handler: draws one number 1..6 off the dice stream and walks that many nodes
/// over the run's actual board — stepwise, honouring the junction pause, the stage-end clamp, and
/// the boss-exact rule.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>A roll answers a number; the board answers what happens next.</b> The die carries no
/// per-face effect, so this handler has exactly two jobs — turn a draw into a movement, and commit
/// wherever that movement comes to rest. Healing, doubled payouts, re-resolving a tile and rolling
/// again were all face effects and are all gone; a tile that wants to do one of those things is the
/// board's business, reached through <see cref="TileArrival"/>.
/// </para>
/// <para>
/// A junction pause stops the roll: the run is committed at the junction and a
/// <see cref="Model.PendingFork"/> opened for <c>Handlers.ChooseFork</c> to resume. Reaching the
/// boss, or coming to rest on a stage's last node — clamped or exactly — is an ordinary end to the
/// movement, since nothing about a roll can continue past it.
/// </para>
/// <para>
/// The draw is uniform over the six sides, straight off the <c>dice</c> stream. There is no weighted
/// bag and no luck smoothing: an ordinary die is ordinary, and a run's sequence of rolls is a pure
/// function of its seed and the number of draws already taken.
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

        if (run.PendingFork is not null)
        {
            // Movement is paused at a junction; CHOOSE_FORK is the only legal next move.
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        if (run.HasPendingTile)
        {
            // A tile landed on and not yet resolved blocks a further roll.
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        if (run.CurrentHp == 0)
        {
            // 🔒 A dead hero does not roll. REVIVE, END_RUN and ABANDON_RUN are the legal moves, and
            // all three are reachable from here — a loss leaves the tile pending, so this arm only
            // fires for a death whose tile has already been cleared.
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var board = BoardResolution.Resolve(run, input.Context.Content, input.Rng);
        var pips = Draw(input);

        var events = new List<DomainEvent>(1)
        {
            new DiceRolled(DomainEvent.UnstampedSequence, pips),
        };

        // 19 Part E's CUR_SLIPPERY, honoured here rather than as a build effect: 18 §2.1 has no
        // "movement" stat, and the penalty's floor of 1 is what stops a cursed run standing still.
        var steps = PenalisedMovement(pips, run);

        return Move(input, board, run, events, steps);
    }

    /// <summary>Draws one number off the dice stream, uniform over the die's six sides.</summary>
    private static int Draw(HandlerInput input) =>
        input.Rng.Stream(RngStreams.Dice).Range(Die.MinPips, Die.MaxPips + 1);

    /// <summary>
    /// The movement a rolled number produces once the run's curses have had their say.
    /// </summary>
    /// <remarks>
    /// 🔒 The penalty stops at <see cref="CurseEffects.MinimumPipAfterPenalty"/> — `19` Part E's
    /// <c>CUR_SLIPPERY</c> reads <em>"-1 to all Pip rolls (minimum 1)"</em>, and the floor is the
    /// whole reason the curse cannot stop a run dead: a hero who rolled a 1 still moves one node.
    /// </remarks>
    private static int PenalisedMovement(int pips, Model.Run run)
    {
        var penalty = 0;

        foreach (var curseId in run.Curses)
        {
            penalty += CurseEffects.PipPenalty(curseId);
        }

        return penalty == 0
            ? pips
            : Math.Max(CurseEffects.MinimumPipAfterPenalty, pips - penalty);
    }

    /// <summary>
    /// Walks the movement this roll produced and commits wherever it comes to rest: a junction, the
    /// boss, or an ordinary landing.
    /// </summary>
    private static HandlerResult Move(
        HandlerInput input,
        BoardGraph board,
        Model.Run run,
        List<DomainEvent> events,
        int steps)
    {
        // The virtual trailhead is one step before node 0 and is not itself a board node.
        var atTrailhead = run.Position == Model.Run.TrailheadPosition;
        var current = atTrailhead ? default : new NodeId(run.Position);

        MovementEngine.AdvanceResult result;

        if (atTrailhead)
        {
            result = MovementEngine.Advance(board, board.FirstNodeId, steps - 1);
        }
        else
        {
            result = MovementEngine.Advance(board, current, steps);
        }

        atTrailhead = false;
        current = result.Node;

        if (result.PausedAtJunction)
        {
            run.MoveTo(current.Value);
            run.BeginPendingFork(new PendingFork(current.Value, result.RemainingSteps));

            return HandlerResult.Accept(events);
        }

        if (result.ReachedBoss)
        {
            run.MoveTo(current.Value);

            // The boss node is itself a resolvable tile, but it is not a Stage Gate — the boss
            // belongs to no stage.
            TileArrival.Land(run, board.Node(current));

            return HandlerResult.Accept(events);
        }

        // Positional, not a step count: an exact landing on a stage's last node gates just as a
        // clamped overshoot onto the same node does.
        var atStageEnd = board.IsStageEndNode(current);

        run.MoveTo(current.Value);

        // The gate fires before the landing is recorded, so its heal reads Run.MaxHp before anything
        // else this command touches. It fires whether or not an Escape Rope skips the tile: 03 §7.1
        // makes the gate POSITIONAL, not tile content.
        if (atStageEnd)
        {
            var gated = StageGateResolver.Apply(input, run.CurrentHp);

            if (gated != run.CurrentHp)
            {
                run.SetHitPoints(gated, run.MaxHp);
            }
        }

        TileArrival.Land(run, board.Node(current));

        return HandlerResult.Accept(events);
    }
}
