using System.Globalization;

namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>
/// The movement engine: stepwise traversal of a <see cref="BoardGraph"/>, the junction pause, the
/// stage-end clamp, the boss-exact rule and the Portal jump's extra pre-boss campfire clamp. Pure:
/// no <c>Run</c>, no RNG draw of its own beyond the one Portal distance draw
/// <see cref="DrawPortalDistance"/> exposes as a seam.
/// </summary>
/// <remarks>
/// <para>
/// The virtual trailhead (position −1) is not a <see cref="NodeId"/> and is deliberately outside
/// this type's vocabulary. <see cref="BoardGraph"/> has no node for it — <c>Run.Position</c>'s own
/// floor is the one place it is represented. A caller standing at the trailhead consumes the first
/// step of its roll onto <see cref="BoardGraph.FirstNodeId"/> itself, then calls
/// <see cref="Advance"/> for the remaining steps; <c>Handlers.RollDice</c> is that caller.
/// </para>
/// <para>
/// Every departure from a junction is a choice, including the spine's own continuation —
/// <see cref="Advance"/> therefore pauses the instant it would leave a junction with movement still
/// to spend, whether the edge it would take is <see cref="EdgeKind.Continue"/> or
/// <see cref="EdgeKind.Branch"/>. Landing exactly on a junction with nothing left to spend does not
/// pause — the loop below only checks <see cref="BoardGraph.IsJunction"/> while <c>remaining &gt; 0</c>.
/// </para>
/// <para>
/// The stage-end clamp is a <b>one-time stop, not a wall</b>. It stops the move that would carry
/// <em>past</em> a stage's last node, and a move that <em>begins</em> on that node has already paid
/// it: <see cref="Advance"/> therefore clamps only once a step has been spent. Nothing outside this
/// type consumes or clears the clamp, so clamping on the start node as well leaves a run unable to
/// leave its stage at all.
/// </para>
/// <para>
/// "A step has been spent" is measured per <em>call</em>, and a resumed move — <c>Handlers.ChooseFork</c>
/// finishing the roll a junction interrupted — is a fresh call. The two readings only diverge if a
/// junction's own outgoing edge lands on a stage's last node, which <c>BoardGenerator</c>'s fork
/// placement makes impossible: every junction sits at a local index no later than
/// <c>spineLength − 4</c>. That dependency is deliberate and is pinned by
/// <c>MovementEngineTests.A_move_resumed_exactly_on_a_stages_last_node_walks_off_the_boundary</c>.
/// </para>
/// </remarks>
internal static class MovementEngine
{
    /// <summary>A Portal jump distance is drawn uniformly from 3 to 6, inclusive.</summary>
    private const int PortalMinDistance = 3;

    /// <summary>Exclusive upper bound for <see cref="DeterministicRng.Range"/>'s 3–6 inclusive draw.</summary>
    private const int PortalMaxDistanceExclusive = 7;

    /// <summary>
    /// One call to <see cref="Advance"/> or <see cref="AdvancePortal"/>'s outcome.
    /// </summary>
    /// <param name="Node">
    /// Where the movement stands when it stops — the paused junction, the boss node, the
    /// stage-clamped node, or the exact landing node once every step is spent.
    /// </param>
    /// <param name="PausedAtJunction">
    /// True when movement stopped because it must leave a junction and a <c>CHOOSE_FORK</c> is
    /// needed. <see cref="RemainingSteps"/> is then the steps still unspent, including the one that
    /// leaves the junction — always at least 1 (a junction landed on with zero movement left does
    /// not pause).
    /// </param>
    /// <param name="ReachedBoss">
    /// True when the boss node was reached — either because the graph ran out of edges under it, or
    /// because the boss-exact rule fired a single mandatory step off stage 3's last node. Movement
    /// (and any chain in progress) ends here regardless of unspent steps.
    /// </param>
    /// <param name="RemainingSteps">
    /// Steps not yet spent. Zero unless <see cref="PausedAtJunction"/> is true or a stage-end clamp
    /// stopped the move short of a full landing.
    /// </param>
    internal readonly record struct AdvanceResult(
        NodeId Node, bool PausedAtJunction, bool ReachedBoss, int RemainingSteps);

    /// <summary>
    /// Steps <paramref name="steps"/> times from <paramref name="start"/>, one edge at a time,
    /// honouring the junction pause, the stage-end clamp and the boss-exact rule.
    /// </summary>
    /// <param name="board">The run's board.</param>
    /// <param name="start">
    /// The node movement begins from. Must be a real node of <paramref name="board"/> — the virtual
    /// trailhead is the caller's concern, not this type's (see this type's remarks).
    /// </param>
    /// <param name="steps">How many nodes to advance. Never negative.</param>
    /// <exception cref="ArgumentNullException"><paramref name="board"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="steps"/> is negative.</exception>
    internal static AdvanceResult Advance(BoardGraph board, NodeId start, int steps)
    {
        ArgumentNullException.ThrowIfNull(board);

        if (steps < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(steps), steps, "03 §1.1's movement is always forward; a negative step count " +
                "names no legal move.");
        }

        var current = start;
        var remaining = steps;

        while (remaining > 0)
        {
            // Checked BEFORE the outgoing edges are read: every departure from a junction is a
            // choice, so even the single-edge-looking "continue straight" option is one of the two
            // the pause offers, never taken silently.
            if (board.IsJunction(current))
            {
                return new AdvanceResult(current, true, false, remaining);
            }

            var edges = board.OutgoingEdges(current);
            if (edges.Count == 0)
            {
                // The boss node has no outgoing edge. Movement ends here, whatever is left unspent.
                return new AdvanceResult(current, false, true, 0);
            }

            var edge = edges[0]; // the node's one Continue edge — IsJunction already ruled out a fork here.
            var currentStage = board.Node(current).Stage;
            var nextNode = board.Node(edge.To);

            if (nextNode.Stage != currentStage)
            {
                // Stage-end clamp, with the boss-exact rule as its one named exception: any roll
                // taken from stage 3's last node moves exactly one step onto the boss node.
                if (nextNode.Tile == TileKind.Boss)
                {
                    return new AdvanceResult(edge.To, false, true, 0);
                }

                if (remaining < steps)
                {
                    // A move that would carry past the last node of a stage stops on that node
                    // instead — 'current' is already that node, since every edge but this one has
                    // already been taken.
                    return new AdvanceResult(current, false, false, remaining);
                }

                // Nothing spent yet, so this move BEGAN on the stage's last node: the clamp that put
                // it there has already been paid and stopping again would be a wall, not a stop. Fall
                // through and take the edge.
            }

            current = edge.To;
            remaining--;
        }

        return new AdvanceResult(current, false, current.Equals(board.BossNodeId), 0);
    }

    /// <summary>
    /// A Portal jump: <see cref="Advance"/>'s stepwise traversal (junctions inside the jump still
    /// pause, the stage-end clamp and boss-exact rule still apply), plus stage 3's extra rule: the
    /// jump may never carry past the guaranteed pre-boss campfire — a distance that would lands ON
    /// the campfire instead. Consequence: Portal can never reach the boss node.
    /// </summary>
    /// <param name="board">The run's board.</param>
    /// <param name="start">The Portal tile's own node. Never inside the last 4 nodes of a stage (C4).</param>
    /// <param name="distance">The drawn jump distance, 3–6 inclusive.</param>
    /// <remarks>
    /// The clamp is applied only to a completed or boss-reaching result. A jump that pauses at a
    /// junction mid-flight is left as-is: every junction candidate sits at a local index no later
    /// than <c>spineLength − 4</c>, strictly before the pre-boss campfire at <c>spineLength − 2</c>,
    /// so a paused Portal jump can never itself have already passed the campfire. Re-applying this
    /// clamp to whatever remains after a <c>CHOOSE_FORK</c> resumes a paused Portal jump is the
    /// resuming caller's concern.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="board"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="distance"/> is outside 3..6.</exception>
    internal static AdvanceResult AdvancePortal(BoardGraph board, NodeId start, int distance)
    {
        ArgumentNullException.ThrowIfNull(board);

        if (distance is < PortalMinDistance or >= PortalMaxDistanceExclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(distance), distance, "03 §1.1 draws a Portal jump uniformly from 3 to 6, " +
                "inclusive; " + distance.ToString(CultureInfo.InvariantCulture) + " is outside that " +
                "range.");
        }

        var natural = Advance(board, start, distance);

        if (board.Node(start).Stage != 3 || natural.PausedAtJunction)
        {
            return natural;
        }

        var bossLinearIndex = board.Node(board.BossNodeId).LinearIndex;
        var campfireLinearIndex = bossLinearIndex - 2; // BoardGenerator always places the campfire there.
        var campfireNode = board.SpineNode(campfireLinearIndex);

        if (natural.ReachedBoss || board.Node(natural.Node).LinearIndex > campfireLinearIndex)
        {
            return new AdvanceResult(campfireNode, false, false, 0);
        }

        return natural;
    }

    /// <summary>Draws a Portal jump's distance: uniform 3–6 inclusive, from the run's own <c>board</c> stream. Never player/client chosen.</summary>
    /// <param name="rng">The run's <c>board</c> stream, already positioned past its board-layout draws.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rng"/> is null.</exception>
    internal static int DrawPortalDistance(Rng.DeterministicRng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        return rng.Range(PortalMinDistance, PortalMaxDistanceExclusive);
    }
}
