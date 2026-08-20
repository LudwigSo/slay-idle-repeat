using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Board.Resolution;

/// <summary>
/// Walks a movement over the run's board and commits wherever it comes to rest: a junction, the
/// boss, or an ordinary landing.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>One body, because there are now two ways to move and they must land identically.</b>
/// <c>Handlers.RollDice</c> draws a number and <c>Handlers.UseFixedDie</c> spends one, and the only
/// difference between them is where the number came from — the junction pause, the boss-exact rule,
/// the stage-gate heal and the landing are the same walk. Two copies would drift, and the drift
/// would be a run that gated on a roll and not on a fixed die.
/// </para>
/// <para>
/// ⚠️ It takes the step count already decided. Whether a curse penalty applied to it is the
/// caller's question, and the two callers answer it differently on purpose — see
/// <c>Handlers.UseFixedDie</c>.
/// </para>
/// </remarks>
internal static class RunMovement
{
    /// <summary>Walks <paramref name="steps"/> from where the run stands and commits the outcome.</summary>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <param name="board">The run's board, already resolved.</param>
    /// <param name="run">The run to move.</param>
    /// <param name="events">The events this command has produced so far; returned with the result.</param>
    /// <param name="steps">How far to walk. Always at least one — no movement source can produce zero.</param>
    internal static HandlerResult Advance(
        HandlerInput input,
        BoardGraph board,
        Model.Run run,
        List<DomainEvent> events,
        int steps)
    {
        // The virtual trailhead is one step before node 0 and is not itself a board node.
        var atTrailhead = run.Position == Model.Run.TrailheadPosition;

        var result = atTrailhead
            ? MovementEngine.Advance(board, board.FirstNodeId, steps - 1)
            : MovementEngine.Advance(board, new NodeId(run.Position), steps);

        var current = result.Node;

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

    /// <summary>
    /// The three states that refuse any movement command, checked identically by both of them.
    /// </summary>
    /// <returns>The rejection, or <c>null</c> when the run may move.</returns>
    /// <remarks>
    /// 🔒 Shared for <see cref="Advance"/>'s reason: a guard one movement command honoured and the
    /// other did not would be a way to move out of a state the game says you cannot move out of.
    /// </remarks>
    internal static HandlerResult? RefuseIfCannotMove(Model.Run run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.PendingFork is not null)
        {
            // Movement is paused at a junction; CHOOSE_FORK is the only legal next move.
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        if (run.HasPendingTile)
        {
            // A tile landed on and not yet resolved blocks a further move.
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        if (run.CurrentHp == 0)
        {
            // 🔒 A dead hero does not move. REVIVE, END_RUN and ABANDON_RUN are the legal moves, and
            // all three are reachable from here — a loss leaves the tile pending, so this arm only
            // fires for a death whose tile has already been cleared.
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        return null;
    }
}
