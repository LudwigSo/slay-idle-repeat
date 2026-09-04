namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>
/// The timing-bar minigame itself: a cursor sweeping the bar, a fixed number of strikes, and the hit
/// count that becomes the outcome tier.
/// </summary>
/// <remarks>
/// <para>
/// Engine-free on purpose, on <see cref="BoardWalk"/>'s precedent, so the parts with a wrong answer
/// possible — where the cursor is, whether a long frame lands where several short ones do, whether a
/// strike was inside the window — are testable without booting anything. The bar, the window and the
/// cursor are drawn by the scene from these numbers.
/// </para>
/// <para>
/// 🔒 <b><see cref="Advance"/> is frame-rate independent by construction.</b> Elapsed time is
/// accumulated and the cursor derived from it, rather than the cursor being nudged per frame: a
/// per-frame nudge makes the sweep faster on a fast handset and turns the window into a different
/// size of target depending on hardware, which on a skill game is the difference between fair and not.
/// </para>
/// <para>
/// 🔒 <b>Reduced motion is a real alternative game, not a disabled one.</b> A player who cannot watch
/// a moving cursor still has to be able to score, so <see cref="Advance"/> does nothing at all and
/// <see cref="Step"/> moves the cursor a fixed fraction per press — the same tier is reachable, by
/// counting rather than by timing.
/// </para>
/// </remarks>
public sealed class TimingBarGame
{
    /// <summary>Builds a game over the authored numbers.</summary>
    /// <param name="rules">The four authored numbers.</param>
    /// <param name="reducedMotion">
    /// When true the cursor never moves on its own and <see cref="Step"/> advances it instead.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null.</exception>
    public TimingBarGame(TimingBarRules rules, bool reducedMotion = false)
    {
        ArgumentNullException.ThrowIfNull(rules);

        throw new NotImplementedException(NotBuiltYet);
    }

    /// <summary>Where the cursor is on the bar, 0 at one end and 1 at the other.</summary>
    public double Cursor => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Half the scoring window's width, so the scene can draw the window it is judged by.</summary>
    public double HalfWidth => throw new NotImplementedException(NotBuiltYet);

    /// <summary>How many strikes have landed inside the window.</summary>
    public int Hits => throw new NotImplementedException(NotBuiltYet);

    /// <summary>The outcome tier a submission would carry — the hit count, and nothing else.</summary>
    public int Tier => throw new NotImplementedException(NotBuiltYet);

    /// <summary>How many strikes are left to take.</summary>
    public int StrikesLeft => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Whether every strike has been taken, so the tier is settled.</summary>
    public bool Finished => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Whether this game is being played without motion.</summary>
    public bool ReducedMotion => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Moves time forward. Does nothing at all under reduced motion.</summary>
    /// <param name="delta">Seconds since the last frame. Never negative.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="delta"/> is negative.</exception>
    public void Advance(double delta) => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Moves the cursor one authored step — the reduced-motion way to aim.</summary>
    public void Step() => throw new NotImplementedException(NotBuiltYet);

    /// <summary>
    /// Takes one strike where the cursor stands, and answers whether it scored.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the cursor was inside the window. A strike taken once the game is
    /// <see cref="Finished"/> scores nothing and consumes nothing.
    /// </returns>
    public bool Strike() => throw new NotImplementedException(NotBuiltYet);

    private const string NotBuiltYet =
        "TimingBarGame is a signature-only stub: the triangle-wave cursor, the window test and the " +
        "reduced-motion arm land with the tests written against them.";
}
