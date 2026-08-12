using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// 🔒 `05` §3 — the fixed-tick clock: <c>TICK = 0.05 s</c> at 20 ticks/second, and the one
/// sanctioned way to turn a tick number into battle time.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Battle time is a function of the tick, never an accumulator.</b> The obvious
/// implementation — <c>seconds += TICK</c> once per iteration — is a determinism defect, not a
/// rounding nicety:
/// </para>
/// <code>
/// var t = 0.0; for (var i = 0; i &lt; 1400; i++) { t += 0.05; }   // 69.99999999999967
/// BattleClock.SecondsAt(1400)                                    // 70
/// </code>
/// <para>
/// `05` §3.1's <c>SYS_ENRAGE</c> is <c>startDelay: 70.0</c>, and `18` §4's <c>BATTLE_TIME</c>
/// comparators are <c>&gt;=</c>: an accumulated clock puts tick 1400 <em>below</em> the threshold and
/// the enrage starts one tick late — on x64. On ARM64 the same accumulation can land on the other
/// side of the comparison, and `11` §6's tamper check then rejects an honest duel. M2-06 found this
/// while authoring the R8 anchor.
/// </para>
/// <para>
/// 🔒 <b>Division, not multiplication, and then `05` §1.1's rounding on top.</b>
/// <c>tick / 20.0</c> is the correctly-rounded value of the exact rational <c>tick/20</c>, so every
/// whole second of battle time is exact; <c>tick * 0.05</c> multiplies by the double nearest 0.05,
/// which is slightly above it, and drifts upward with the tick. Both are then rounded to four
/// decimals, which is the rule `05` §1.1 states for every accumulation point — so the two agree at
/// every tick in <c>0..1799</c>, and the division is chosen because it is the one that is right
/// before the rounding rescues it. Rounding a wrong number is how a determinism rule becomes a
/// coincidence.
/// </para>
/// </remarks>
internal static class BattleClock
{
    /// <summary>🔒 `05` §3 — <c>TICK = 0.05 s</c>. 20 ticks per second.</summary>
    internal const double TickSeconds = 1.0 / CombatLog.TicksPerSecond;

    /// <summary>
    /// 🔒 Battle time at a tick, in seconds, rounded to `05` §1.1's four decimals.
    /// </summary>
    /// <param name="tick">The tick, <c>0</c> or above. Tick 0 is the first tick of the fight.</param>
    /// <exception cref="ArgumentOutOfRangeException">The tick is negative.</exception>
    internal static double SecondsAt(int tick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);

        return Math.Round((double)tick / CombatLog.TicksPerSecond, StatRounding.Decimals);
    }

    /// <summary>
    /// How many whole ticks a duration in seconds covers — the inverse of <see cref="SecondsAt"/>,
    /// for a <c>startDelay</c> or an ability cooldown authored in seconds (`18` §3).
    /// </summary>
    /// <param name="seconds">A non-negative duration in seconds.</param>
    /// <exception cref="ArgumentOutOfRangeException">The duration is negative or not finite.</exception>
    internal static int TicksFor(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seconds), seconds,
                "`05` §3's clock runs forward from 0 in whole ticks. A negative, NaN or infinite " +
                "duration is an unauthored value reaching the clock, not a time to convert.");
        }

        return (int)Math.Round(seconds * CombatLog.TicksPerSecond, MidpointRounding.AwayFromZero);
    }
}
