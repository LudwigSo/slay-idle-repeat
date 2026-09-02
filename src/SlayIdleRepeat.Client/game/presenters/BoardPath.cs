using SlayIdleRepeat.Core.Rules.Board;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>
/// The nodes a single accepted movement walked through, reconstructed from where the run was and
/// where it ended up.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is reconstructed rather than reported.</b> <c>Core</c> emits no per-step movement:
/// its seven domain events carry no position, <c>ChooseFork</c> and the Portal arm of
/// <c>ResolveTile</c> accept with no events at all, and <c>MovementEngine</c> returns only the node
/// the run came to rest on. The client sees a start and an end.
/// </para>
/// <para>
/// 🔒 <b>And the reconstruction re-implements no rule.</b> <c>MovementEngine.Advance</c> tests
/// whether the current node is a junction BEFORE it reads that node's outgoing edges, and returns
/// paused. So no movement ever DEPARTS a junction — the single junction departure in the whole
/// codebase is <c>CHOOSE_FORK</c>'s indexed edge. Within one accepted command every node the run
/// leaves therefore has exactly one successor, and the walk is unique. Following successors is a
/// graph read. The junction pause, the stage-end clamp, the boss-exact rule and the Portal campfire
/// clamp all decide where a walk STOPS, and where it stopped is handed over as the new position.
/// </para>
/// <para>
/// A welcome consequence: the hop count comes from where the run actually ended, never from the
/// number rolled. A roll of 5 that the stage-end clamp stopped after two steps walks two nodes, so
/// the animation agrees with the sentence the screen prints about it instead of contradicting it.
/// </para>
/// </remarks>
internal static class BoardPath
{
    /// <summary>
    /// The most nodes one accepted command can walk. A die shows 1..6 and a Portal draws 3..6
    /// (`03` §1.1), so this bounds the COMMAND and not the board: it does not grow with board length,
    /// and a walk that exceeds it is a reconstruction fault rather than a long move.
    /// </summary>
    internal const int MaxHops = 8;

    /// <summary>
    /// The nodes the movement walked, in order, excluding the node it started on and including the
    /// one it ended on.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Empty means "put the hero there without walking", never "guess".</b> A walk that cannot
    /// reach the destination, or that would run past <see cref="MaxHops"/>, returns nothing rather
    /// than a plausible route: animating a path the run did not take is worse than not animating,
    /// because it shows the player tiles they never visited.
    /// </remarks>
    /// <param name="board">The projected board, which is where every successor comes from.</param>
    /// <param name="fromNodeId">
    /// Where the hero stood. <c>null</c> at the trailhead, which is not a node of the board
    /// (<c>Run</c> holds it as position −1): the walk then starts at the first spine node and
    /// includes it, without which the first roll of every run would animate nothing.
    /// </param>
    /// <param name="toNodeId">Where the run now stands.</param>
    /// <param name="viaNodeId">
    /// The first node of the walk when the movement left a junction — <c>CHOOSE_FORK</c> is the only
    /// command that has one, and it is needed because a junction has two successors. Without it a
    /// walk that starts on a junction has no single answer and this returns nothing.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="board"/> is null.</exception>
    internal static IReadOnlyList<int> Between(
        BoardView board, int? fromNodeId, int toNodeId, int? viaNodeId)
    {
        ArgumentNullException.ThrowIfNull(board);

        if (board.Spine.Count == 0 || board.Node(toNodeId) is null)
        {
            return [];
        }

        var walked = new List<int>();
        int current;

        if (fromNodeId is { } start)
        {
            if (board.Node(start) is null)
            {
                return [];
            }

            current = start;
        }
        else
        {
            // The trailhead is not a node, so the first hop of a run is onto the board rather than
            // across it. Node 0 is walked TO, which is why it is added here and the spine nodes
            // below are added as they are reached.
            current = board.Spine[0].NodeId;
            walked.Add(current);
        }

        if (viaNodeId is { } via)
        {
            if (!LeavesTowards(board, current, via))
            {
                return [];
            }

            walked.Add(via);
            current = via;
        }

        while (current != toNodeId)
        {
            if (walked.Count >= MaxHops || Successor(board, current) is not { } next)
            {
                return [];
            }

            walked.Add(next);
            current = next;
        }

        return walked;
    }

    /// <summary>Whether <paramref name="via"/> is one of the ways out of <paramref name="from"/>.</summary>
    /// <remarks>
    /// Checked rather than trusted, so a stale branch id — a fork panel pressed against a board that
    /// has since been re-projected — snaps the hero instead of teleporting it onto an unrelated node.
    /// </remarks>
    private static bool LeavesTowards(BoardView board, int from, int via)
    {
        foreach (var fork in board.Forks)
        {
            if (fork.JunctionNodeId != from)
            {
                continue;
            }

            return via == fork.ContinueNodeId ||
                   (fork.BranchNodeIds.Count > 0 && via == fork.BranchNodeIds[0]);
        }

        return Successor(board, from) == via;
    }

    /// <summary>
    /// The one node a movement leaving this one arrives at, or <c>null</c> when there is not exactly
    /// one: a junction has two, the boss has none, and a node off this board has none either.
    /// </summary>
    /// <remarks>
    /// Assembled from the two public readings of the graph rather than from an edge list, because
    /// <c>BoardView</c> publishes no edges: <c>Spine</c> gives every spine adjacency, and each
    /// <c>BoardFork</c> gives its branch chain and the spine node that chain rejoins onto. Between
    /// them they name the successor of every node a run can leave.
    /// </remarks>
    private static int? Successor(BoardView board, int nodeId)
    {
        foreach (var fork in board.Forks)
        {
            if (fork.JunctionNodeId == nodeId)
            {
                return null;
            }

            for (var k = 0; k < fork.BranchNodeIds.Count; k++)
            {
                if (fork.BranchNodeIds[k] != nodeId)
                {
                    continue;
                }

                return k + 1 < fork.BranchNodeIds.Count ? fork.BranchNodeIds[k + 1] : fork.RejoinNodeId;
            }
        }

        for (var linearIndex = 0; linearIndex < board.Spine.Count - 1; linearIndex++)
        {
            if (board.Spine[linearIndex].NodeId == nodeId)
            {
                return board.Spine[linearIndex + 1].NodeId;
            }
        }

        return null;
    }
}
