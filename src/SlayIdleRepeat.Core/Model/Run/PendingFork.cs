namespace SlayIdleRepeat.Core.Model;

/// <summary>A movement paused mid-move at a junction, waiting for <c>CHOOSE_FORK</c>.</summary>
/// <remarks>
/// Carries the junction's identity as a bare <see cref="int"/> rather than a
/// <see cref="Rules.Board.NodeId"/>, since <c>Model</c> may not reference <c>Rules</c>. No branch
/// preview is carried here either — the board regenerates deterministically from the run's seed and
/// stream positions, so a handler recomputes the preview from <see cref="JunctionPosition"/> instead
/// of reading a stale copy.
/// </remarks>
/// <param name="JunctionPosition">The paused junction's identity, in <see cref="Run.Position"/>'s own units.</param>
/// <param name="RemainingSteps">
/// Steps of the interrupted movement remaining once the chosen edge is taken. Always at least one.
/// </param>
public readonly record struct PendingFork
{
    /// <summary>Validates both fields. <c>internal</c>: the domain's own state is constructible only from inside <c>Core</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="junctionPosition"/> is negative, or <paramref name="remainingSteps"/> is below 1.
    /// </exception>
    internal PendingFork(int junctionPosition, int remainingSteps)
    {
        if (junctionPosition < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(junctionPosition), junctionPosition,
                "A junction is a real node of the board, never the -1 trailhead sentinel and never " +
                "negative.");
        }

        if (remainingSteps < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(remainingSteps), remainingSteps,
                "03 §1.1: landing exactly on a junction with zero movement left does not prompt a " +
                "CHOOSE_FORK — so a PendingFork with nothing left to spend could never have been " +
                "created in the first place.");
        }

        JunctionPosition = junctionPosition;
        RemainingSteps = remainingSteps;
    }

    public int JunctionPosition { get; }

    public int RemainingSteps { get; }
}
