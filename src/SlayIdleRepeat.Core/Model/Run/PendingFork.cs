namespace SlayIdleRepeat.Core.Model;

/// <summary>
/// 🔒 `03` §1.1 / `30` §4 — a movement paused mid-move at a junction, waiting for
/// <c>CHOOSE_FORK</c>. M3-02's answer to the <c>PendingFork</c> entry of
/// <c>SlayIdleRepeat.Architecture.Tests.GapRegister</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b><c>Model</c>, and it has to be — not <c>Rules.Board</c>.</b> `30` §11.4 puts <c>Model</c>
/// <em>below</em> <c>Rules</c> in the internal layering (<c>Model</c> never references <c>Rules</c>),
/// so this type carries the junction's identity as a bare <see cref="int"/> — the same units
/// <see cref="Run.Position"/> already uses (a <see cref="Rules.Board.NodeId"/>'s <c>Value</c>) —
/// rather than a <see cref="Rules.Board.NodeId"/> itself. The handler that reaches for both
/// (<c>Handlers.RollDice</c>, <c>Handlers.ChooseFork</c>) sits in <c>Handlers</c>, above both
/// layers, and does the wrapping.
/// </para>
/// <para>
/// ⚠️ <b>No branch preview is carried here, deliberately.</b> `03` §3.1's honest preview (label
/// plus up to 3 tile icons) is <see cref="Rules.Board.ForkPreview"/> — a <c>Rules.Board</c> type —
/// so storing it on this <c>Model</c> record would hit the same layering wall
/// <see cref="JunctionPosition"/>'s remarks describe. The board regenerates identically from the
/// run's committed seed and stream positions on every command (`14` §8.1), so a handler that needs
/// the preview again — to answer the pause a second time, or to build the wire response — recomputes
/// it from <see cref="JunctionPosition"/> against the regenerated board rather than reading a stale
/// copy stored here.
/// </para>
/// </remarks>
/// <param name="JunctionPosition">
/// The paused junction's identity, in <see cref="Run.Position"/>'s own units.
/// </param>
/// <param name="RemainingSteps">
/// How many steps of the interrupted movement remain unspent once the chosen edge is taken — `03`
/// §1.1's junction pause always has at least one, since landing exactly on a junction with nothing
/// left to spend never pauses at all.
/// </param>
/// <remarks>
/// 🔒 <b><c>public</c>, unlike the <c>Rules.Board</c> types it counts against.</b> <c>Run.PendingFork</c>
/// (the getter) is one of `30` §11.2's public getters, so the type it returns must be at least as
/// visible — the same reason <c>DifficultyTier</c>/<c>RunId</c>/<c>PlayerId</c> are public while the
/// <c>Rules.Board</c> types this record's own remarks explain it cannot reference are not.
/// </remarks>
public readonly record struct PendingFork
{
    /// <summary>
    /// Builds a pending fork, validating both fields. <c>internal</c>, not <c>public</c> — `30`
    /// §11.2 makes the domain's own state constructible only from inside <c>Core</c>, and every
    /// caller (<c>Handlers.RollDice</c>, <c>Handlers.ChooseFork</c>) lives there.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="junctionPosition"/> is negative, or <paramref name="remainingSteps"/> is
    /// below 1.
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

    /// <inheritdoc cref="PendingFork"/>
    public int JunctionPosition { get; }

    /// <inheritdoc cref="PendingFork"/>
    public int RemainingSteps { get; }
}
