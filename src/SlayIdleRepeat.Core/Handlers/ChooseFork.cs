using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// 🔒 `03` §1.1 — the <c>CHOOSE_FORK</c> handler (M3-02): resolves a <see cref="Run.PendingFork"/>
/// by taking the chosen edge and finishing the movement it interrupted.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The branch list a <c>BranchIndex</c> indexes is <see cref="BoardGraph.OutgoingEdges"/>'s own
/// order.</b> A junction always has exactly two outgoing edges (`03` §1.1):
/// <see cref="EdgeKind.Continue"/> at index 0 — the spine's own next node — then
/// <see cref="EdgeKind.Branch"/> at index 1, `03` §3.1's honest preview attached. Anything else is
/// not a choice this junction offers.
/// </para>
/// <para>
/// ⚠️ <b>What this handler does not do.</b> `03` §1.1's chain (⛓) and Portal continuations that
/// might resume mid-jump through a pause are both unreachable today — the starting die is all-Pip
/// (`04` §1) and Portal tiles resolve under <c>RESOLVE_TILE</c> (M3-03, not yet built) — so the only
/// pause this handler ever actually resumes is a single Pip roll's own movement. It resumes
/// generically (any remaining steps, any further junction) rather than assuming that, so it needs no
/// revisiting the day either becomes reachable.
/// </para>
/// </remarks>
internal static class ChooseFork
{
    /// <summary>🔒 `03` §1.1 — applies <c>CHOOSE_FORK</c>.</summary>
    internal static HandlerResult Handle(ChooseForkCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;
        var pending = run.PendingFork;

        if (pending is null)
        {
            // 🔒 Not a defect: a player can legitimately resend a CHOOSE_FORK the server already
            // answered (30 §2.1's P3 — an illegal move is data, never an exception out of Apply).
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

        // The chosen edge is the first of the resumed movement's remaining steps; Advance's own
        // loop already lands exactly (no pause) when there is nothing left to spend after it.
        var afterFirstStep = pending.Value.RemainingSteps - 1;
        var result = MovementEngine.Advance(board, chosen.To, afterFirstStep);

        run.ClearPendingFork();
        run.MoveTo(result.Node.Value);

        if (result.PausedAtJunction)
        {
            run.BeginPendingFork(new PendingFork(result.Node.Value, result.RemainingSteps));
        }

        return HandlerResult.Accept();
    }
}
