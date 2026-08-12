using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// 🔴 `05` §3 — battle time is a function of the tick, never an accumulator.
/// </summary>
public sealed class BattleClockTests
{
    /// <summary>
    /// 🔴 The defect, stated as the two numbers. `05` §3.1's <c>SYS_ENRAGE</c> is
    /// <c>startDelay: 70.0</c>, tick 1400 is 70 seconds, and `18` §4's <c>BATTLE_TIME</c>
    /// comparators are <c>&gt;=</c>.
    /// </summary>
    [Fact]
    public void Battle_time_at_tick_1400_is_exactly_70_seconds_and_an_accumulator_is_not()
    {
        BattleClock.SecondsAt(1400).ShouldBe(70.0);

        // The implementation `05` §3.1 invites and M2-06 found: t += TICK, once per tick.
        var accumulated = 0.0;
        for (var i = 0; i < 1400; i++)
        {
            accumulated += BattleClock.TickSeconds;
        }

        accumulated.ShouldNotBe(70.0);
        accumulated.ShouldBeLessThan(70.0);

        // Stated as the literal so the failure message carries the defect rather than "not equal".
        //
        // ⚠️ The number is 69.99999999999817 and not 69.99999999999967, which is what the M2-08
        // dispatch prompt quoted from M2-06's report. IEEE 754 addition is exactly specified and .NET
        // does not contract an explicit `+`, so 1400 accumulations of the double nearest 0.05 give
        // one answer everywhere; the two literals are therefore not a platform difference but two
        // different accumulations (a different loop bound, or a different starting value). Recorded
        // rather than transcribed — steering S9. The defect is identical either way: the accumulator
        // lands ~1.8e-12 BELOW 70.0 and `18` §4's >= comparator reads false.
        accumulated.ToString("R", CultureInfo.InvariantCulture).ShouldBe("69.99999999999817");

        // 🔒 And the consequence, as `18` §4 would evaluate it: the enrage would not have started.
        (accumulated >= 70.0).ShouldBeFalse();
        (BattleClock.SecondsAt(1400) >= 70.0).ShouldBeTrue();
    }

    /// <summary>
    /// Every whole second of a 90 s fight is exact, and every tick between them is on the 0.05 grid.
    /// </summary>
    /// <remarks>
    /// The tick-1400 case alone would pass for an implementation that special-cased it; this is the
    /// whole domain `05` §3 defines, 0..1799.
    /// </remarks>
    [Fact]
    public void Every_tick_of_a_90_second_fight_is_on_the_0_05_grid()
    {
        for (var tick = 0; tick < CombatLog.MaxTicks; tick++)
        {
            var seconds = BattleClock.SecondsAt(tick);

            seconds.ShouldBe(tick / 20.0);
            Math.Round(seconds, 4).ShouldBe(seconds);

            if (tick % CombatLog.TicksPerSecond == 0)
            {
                seconds.ShouldBe(tick / CombatLog.TicksPerSecond);
            }
        }
    }

    /// <summary>`05` §3 — 20 ticks a second, so <c>TICK</c> is 0.05 s.</summary>
    [Fact]
    public void The_tick_is_a_twentieth_of_a_second()
    {
        CombatLog.TicksPerSecond.ShouldBe(20);
        BattleClock.TickSeconds.ShouldBe(0.05);
        BattleClock.SecondsAt(CombatLog.MaxTicks).ShouldBe(90.0);
    }

    /// <summary>The inverse — a <c>startDelay</c> or a cooldown authored in seconds.</summary>
    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(1.0, 20)]
    [InlineData(1.5, 30)]
    [InlineData(70.0, 1400)]
    [InlineData(90.0, 1800)]
    public void Seconds_convert_back_to_whole_ticks(double seconds, int expected) =>
        BattleClock.TicksFor(seconds).ShouldBe(expected);

    /// <summary>A clock that ran backwards is a loop bug, not a time to convert.</summary>
    [Fact]
    public void The_clock_refuses_a_negative_tick_and_a_negative_duration()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => BattleClock.SecondsAt(-1));
        Should.Throw<ArgumentOutOfRangeException>(() => BattleClock.TicksFor(-0.05));
        Should.Throw<ArgumentOutOfRangeException>(() => BattleClock.TicksFor(double.NaN));
    }
}
