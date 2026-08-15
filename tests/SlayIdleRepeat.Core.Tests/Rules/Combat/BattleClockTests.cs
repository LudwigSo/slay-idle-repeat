using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>Battle time is a function of the tick, never an accumulator.</summary>
public sealed class BattleClockTests
{
    /// <summary>
    /// Accumulating TICK 1400 times drifts below 70.0 by IEEE-754 rounding, which flips a
    /// <c>&gt;=</c> threshold check at exactly that boundary.
    /// </summary>
    [Fact]
    public void Battle_time_at_tick_1400_is_exactly_70_seconds_and_an_accumulator_is_not()
    {
        BattleClock.SecondsAt(1400).ShouldBe(70.0);

        var accumulated = 0.0;
        for (var i = 0; i < 1400; i++)
        {
            accumulated += BattleClock.TickSeconds;
        }

        accumulated.ShouldNotBe(70.0);
        accumulated.ShouldBeLessThan(70.0);

        // Pinned to the exact literal so a regression's failure message carries the defect.
        accumulated.ToString("R", CultureInfo.InvariantCulture).ShouldBe("69.99999999999817");

        // Consequence: a >= 70.0 threshold check would miss by ~1.8e-12.
        (accumulated >= 70.0).ShouldBeFalse();
        (BattleClock.SecondsAt(1400) >= 70.0).ShouldBeTrue();
    }

    /// <summary>Every whole second of a 90 s fight is exact, and every tick is on the 0.05 grid.</summary>
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

    /// <summary>20 ticks a second, so <c>TICK</c> is 0.05 s.</summary>
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
