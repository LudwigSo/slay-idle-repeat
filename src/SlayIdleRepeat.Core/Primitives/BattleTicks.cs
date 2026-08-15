namespace SlayIdleRepeat.Core.Primitives;

/// <summary>The fixed-tick rule — the tick rate, the fight cap and the whole-tick predicate, stated once for the whole assembly.</summary>
/// <remarks>
/// The simulation is fixed-tick at 0.05 s (20 ticks/second), and a fight is capped at 90 s, so ticks
/// run <c>0..1799</c>. A trigger, telegraph or duration all fire <b>on</b> a tick, which is what makes
/// a fractional span meaningless rather than merely imprecise — it points between two ticks.
/// <para>
/// This type consolidates a rate/cap/tolerance/predicate that used to be stated independently in
/// three places and had already begun to drift (one copy hard-coded its cap as <c>1800</c> instead of
/// deriving it from the rate). Callers keep their own exception types and wording — only the
/// arithmetic constants live here.
/// </para>
/// </remarks>
internal static class BattleTicks
{
    /// <summary>The tick rate: 0.05 s per tick, 20 ticks per second.</summary>
    internal const int PerSecond = 20;

    /// <summary>The PvE fight cap: 90 s at 20 ticks/second. Ticks run <c>0..1799</c>.</summary>
    /// <remarks>PvE only — a duel's 60 s cap is a rules choice per fight, carried by <c>CombatRules</c> instead.</remarks>
    internal const int MaxPerFight = 90 * PerSecond;

    /// <summary>How far a span may sit from a whole tick before it is a fractional span rather than floating-point residue.</summary>
    /// <remarks>
    /// Sized between the two things it stands between: <c>1.2 × 20</c> is not bit-exactly <c>24.0</c>
    /// for every value in the telegraph band, while the defect being caught — a lead of
    /// <c>1.0001 s</c> — misses by <c>2e-3</c>, six orders of magnitude outside anything rounding
    /// can explain.
    /// </remarks>
    internal const double WholeTickTolerance = 1e-9;

    /// <summary>One tick, in seconds — what a refusal tells the author to use a multiple of.</summary>
    internal const double SecondsPerTick = 1.0 / PerSecond;

    /// <summary>Whether a span of seconds lands on a whole tick, and what that tick count is.</summary>
    /// <remarks>
    /// Answers <c>false</c> for NaN or infinity as well as for a fractional span. Callers that need
    /// to tell the two apart check finiteness first and use this for the whole-tick question alone.
    /// </remarks>
    /// <param name="seconds">The span, in seconds.</param>
    /// <param name="ticks">The span in ticks, rounded — meaningful only when this returns <c>true</c>.</param>
    /// <returns><c>true</c> when the span is a whole number of ticks.</returns>
    internal static bool IsWhole(double seconds, out double ticks)
    {
        ticks = seconds * PerSecond;

        if (double.IsNaN(ticks) || double.IsInfinity(ticks))
        {
            return false;
        }

        var whole = Math.Round(ticks);
        var isWhole = Math.Abs(ticks - whole) <= WholeTickTolerance;

        if (isWhole)
        {
            ticks = whole;
        }

        return isWhole;
    }
}
