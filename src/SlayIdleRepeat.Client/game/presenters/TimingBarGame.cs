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
/// <para>
/// 🔒 <b>The bar is swept OUT AND BACK, and both legs are playable.</b> The cursor is a triangle wave
/// of the authored period: it stands at the far end after half a sweep and back at the near end after
/// a whole one. A saw-tooth that snapped back would agree over the outward leg and take away half the
/// moments a player aims in, and the reduced-motion cursor folds at the far end for the same reason.
/// </para>
/// </remarks>
public sealed class TimingBarGame
{
    /// <summary>The point of the bar a strike is judged against.</summary>
    private const double Centre = 0.5;

    /// <summary>One there-and-back journey of the cursor, in bar-lengths.</summary>
    private const double ThereAndBack = 2.0;

    /// <summary>The far end of the bar, which is where the cursor folds.</summary>
    private const double FarEnd = 1.0;

    private readonly TimingBarRules _rules;

    /// <summary>Seconds of play accumulated, which the timed cursor is DERIVED from.</summary>
    private double _elapsed;

    /// <summary>Steps taken, which the reduced-motion cursor is derived from.</summary>
    /// <remarks>
    /// Counted rather than accumulated as a distance, so a hundred steps of an authored fraction
    /// land where the fraction times a hundred does and no rounding creeps in per press.
    /// </remarks>
    private int _steps;

    private int _strikesTaken;

    /// <summary>Builds a game over the authored numbers.</summary>
    /// <param name="rules">The four authored numbers.</param>
    /// <param name="reducedMotion">
    /// When true the cursor never moves on its own and <see cref="Step"/> advances it instead.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null.</exception>
    public TimingBarGame(TimingBarRules rules, bool reducedMotion = false)
    {
        ArgumentNullException.ThrowIfNull(rules);

        _rules = rules;
        ReducedMotion = reducedMotion;
    }

    /// <summary>Where the cursor is on the bar, 0 at one end and 1 at the other.</summary>
    /// <remarks>
    /// 🔒 Computed, never stored. The stored quantity is the time — or the press count — the player
    /// actually spent, so nothing about where the cursor stands depends on how often it was asked.
    /// </remarks>
    public double Cursor => ReducedMotion
        ? Folded(_steps * _rules.ReducedMotionStepFraction)
        : Folded(_elapsed / _rules.SweepSeconds * ThereAndBack);

    /// <summary>Half the scoring window's width, so the scene can draw the window it is judged by.</summary>
    public double HalfWidth => _rules.HitWindowHalfWidth;

    /// <summary>How many strikes have landed inside the window.</summary>
    public int Hits { get; private set; }

    /// <summary>The outcome tier a submission would carry — the hit count, and nothing else.</summary>
    public int Tier => Hits;

    /// <summary>How many strikes are left to take.</summary>
    public int StrikesLeft => _rules.Strikes - _strikesTaken;

    /// <summary>Whether every strike has been taken, so the tier is settled.</summary>
    public bool Finished => _strikesTaken >= _rules.Strikes;

    /// <summary>Whether this game is being played without motion.</summary>
    public bool ReducedMotion { get; }

    /// <summary>Moves time forward. Does nothing at all under reduced motion.</summary>
    /// <remarks>
    /// The clock keeps running once the game is finished, because the cursor is still drawn: a bar
    /// that froze on the last strike would read as the screen having stopped answering.
    /// </remarks>
    /// <param name="delta">Seconds since the last frame. Never negative.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="delta"/> is negative.</exception>
    public void Advance(double delta)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(delta);

        if (ReducedMotion)
        {
            return;
        }

        _elapsed += delta;
    }

    /// <summary>Moves the cursor one authored step — the reduced-motion way to aim.</summary>
    /// <remarks>
    /// 🔒 Only under reduced motion. A timed game's cursor is derived from the clock, so a step
    /// there would be a second author of one position and the two would disagree the next frame.
    /// </remarks>
    public void Step()
    {
        if (!ReducedMotion)
        {
            return;
        }

        _steps++;
    }

    /// <summary>
    /// Takes one strike where the cursor stands, and answers whether it scored.
    /// </summary>
    /// <remarks>
    /// 🔴 The comparison is INCLUSIVE. The scene draws the window from <see cref="HalfWidth"/>, so a
    /// strike dead on the edge of the band a player was shown has to score — refusing it means the
    /// game refuses exactly the shot someone aiming at the edge takes.
    /// </remarks>
    /// <returns>
    /// <see langword="true"/> when the cursor was inside the window. A strike taken once the game is
    /// <see cref="Finished"/> scores nothing and consumes nothing.
    /// </returns>
    public bool Strike()
    {
        if (Finished)
        {
            // A fourth hit would be a tier the reward table has no row for, and MINIGAME_SUBMIT
            // refuses it — so a game that kept counting turns a perfect play into a refusal.
            return false;
        }

        _strikesTaken++;

        var hit = Math.Abs(Cursor - Centre) <= _rules.HitWindowHalfWidth;

        if (hit)
        {
            Hits++;
        }

        return hit;
    }

    /// <summary>
    /// One position on a bar swept out and back: the triangle wave a journey of that length ends on.
    /// </summary>
    /// <param name="travelled">How far the cursor has gone in bar-lengths, unbounded.</param>
    private static double Folded(double travelled)
    {
        var phase = travelled % ThereAndBack;

        return phase <= FarEnd ? phase : ThereAndBack - phase;
    }
}
