using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Testing;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

public sealed class VirtualClockTests
{
    /// <summary>A clock that set rather than added would pass a single-advance check but fail this one.</summary>
    [Fact]
    public void Advance_moves_the_clock_by_exactly_the_span_and_advances_accumulate()
    {
        var clock = new VirtualClock(Harnesses.Start);

        clock.Advance(TimeSpan.FromHours(8));
        clock.NowUtc.ShouldBe(Harnesses.Start.AddHours(8));

        clock.Advance(TimeSpan.FromMinutes(90));
        clock.NowUtc.ShouldBe(Harnesses.Start.AddHours(9).AddMinutes(30));

        clock.Advance(TimeSpan.FromTicks(1));
        clock.NowUtc.ShouldBe(Harnesses.Start.AddHours(9).AddMinutes(30).AddTicks(1));
    }

    [Fact]
    public void Advance_refuses_a_negative_span_and_leaves_the_clock_where_it_was()
    {
        var clock = new VirtualClock(Harnesses.Start);

        Should.Throw<ArgumentOutOfRangeException>(() => clock.Advance(TimeSpan.FromTicks(-1)))
            .ParamName.ShouldBe("by");

        clock.NowUtc.ShouldBe(
            Harnesses.Start,
            "a guard that threw after writing would leave the simulation in a state no caller asked for.");
    }

    /// <summary>default(DateTimeOffset) passes GameContext's zero-offset guard, so it needs a guard
    /// of its own here rather than being caught downstream.</summary>
    [Fact]
    public void The_constructor_refuses_a_start_before_the_first_game_day_or_off_UTC()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new VirtualClock(default))
            .ParamName.ShouldBe("startUtc");

        Should.Throw<ArgumentOutOfRangeException>(
            () => new VirtualClock(GameCalendar.FirstGameDay.AddTicks(-1)));

        Should.Throw<ArgumentOutOfRangeException>(
            () => new VirtualClock(new DateTimeOffset(2026, 8, 12, 5, 0, 0, TimeSpan.FromHours(2))));

        new VirtualClock(GameCalendar.FirstGameDay).NowUtc.ShouldBe(
            GameCalendar.FirstGameDay,
            "the floor itself is accepted — the guard refuses only what the calendar cannot answer.");
    }
}
