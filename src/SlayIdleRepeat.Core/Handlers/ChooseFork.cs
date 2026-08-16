using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Board.Resolution;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>CHOOSE_FORK</c> handler: resolves a <see cref="Run.PendingFork"/> by taking the chosen
/// edge and finishing the movement it interrupted.
/// </summary>
/// <remarks>
/// A junction always has exactly two outgoing edges, in <see cref="BoardGraph.OutgoingEdges"/>'s own
/// order: <see cref="EdgeKind.Continue"/> at index 0, then <see cref="EdgeKind.Branch"/> at index 1.
/// </remarks>
internal static class ChooseFork
{
    /// <summary>Applies <c>CHOOSE_FORK</c>.</summary>
    internal static HandlerResult Handle(ChooseForkCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;
        var pending = run.PendingFork;

        if (pending is null)
        {
            // Not a defect: a player can legitimately resend a fork the server already answered.
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var board = BoardResolution.Resolve(run, input.Context.Content, input.Rng);
        var junction = new NodeId(pending.Value.JunctionPosition);
        var edges = board.OutgoingEdges(junction);

        if (command.BranchIndex < 0 || command.BranchIndex >= edges.Count)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var chosen = edges[command.BranchIndex];

        // The chosen edge is the first of the resumed movement's remaining steps.
        var afterFirstStep = pending.Value.RemainingSteps - 1;
        var result = MovementEngine.Advance(board, chosen.To, afterFirstStep);

        run.ClearPendingFork();
        run.MoveTo(result.Node.Value);

        if (result.PausedAtJunction)
        {
            run.BeginPendingFork(new PendingFork(result.Node.Value, result.RemainingSteps));
            return HandlerResult.Accept();
        }

        // The resumed half of a movement is the same landing as an unbroken one, so it gates on the
        // node it rests on — mirrored in Handlers.RollDice and RESOLVE_TILE's Portal jump.
        if (!result.ReachedBoss && board.IsStageEndNode(result.Node))
        {
            StageGateResolver.Apply(input, run.CurrentHp);
        }

        var node = board.Node(result.Node);
        run.ArriveAtTile((int)node.Tile, node.LinearIndex, node.Stage);

        return HandlerResult.Accept();
    }
}
