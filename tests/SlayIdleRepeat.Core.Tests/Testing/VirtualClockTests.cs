using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Testing;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary><c>VirtualClock</c>: advanced explicitly, nothing ever waits.</summary>
public sealed class VirtualClockTests
{
    /// <summary>A clock reads exactly the instant it was started at, and nothing else.</summary>
    [Fact]
    public void A_clock_reads_the_instant_it_was_started_at()
    {
        new VirtualClock(Harnesses.Start).NowUtc.ShouldBe(Harnesses.Start);
    }

    /// <summary>An advance moves the clock by exactly the span asked for, and advances accumulate —
    /// a clock that set rather than added would pass a single-advance check but fail this one.</summary>
    [Fact]
    public void Advancing_moves_the_clock_by_exactly_the_span_and_advances_accumulate()
    {
        var clock = new VirtualClock(Harnesses.Start);

        clock.Advance(TimeSpan.FromHours(8));
        clock.NowUtc.ShouldBe(Harnesses.Start.AddHours(8));

        clock.Advance(TimeSpan.FromMinutes(90));
        clock.NowUtc.ShouldBe(Harnesses.Start.AddHours(9).AddMinutes(30));

        clock.Advance(TimeSpan.FromTicks(1));
        clock.NowUtc.ShouldBe(Harnesses.Start.AddHours(9).AddMinutes(30).AddTicks(1));
    }

    /// <summary>
    /// A zero advance is a legal no-op — a loop that computes its own step must not have to branch.
    /// </summary>
    [Fact]
    public void A_zero_advance_is_a_no_op()
    {
        var clock = new VirtualClock(Harnesses.Start);

        clock.Advance(TimeSpan.Zero);

        clock.NowUtc.ShouldBe(Harnesses.Start);
    }

    /// <summary>The clock only goes forwards, and the refusal names the reason: the message fragment
    /// is pinned, not only the exception type, since four guards on this type throw the same type
    /// with different fixes.</summary>
    [Fact]
    public void A_negative_advance_is_refused_and_names_the_ruling_it_declines_to_make()
    {
        var clock = new VirtualClock(Harnesses.Start);

        var refusal = Should.Throw<ArgumentOutOfRangeException>(
            () => clock.Advance(TimeSpan.FromTicks(-1)));

        refusal.ParamName.ShouldBe("by");
        refusal.Message.ShouldContain("only moves FORWARDS", Case.Sensitive);
        refusal.Message.ShouldContain(
            "Rehydrate",
            Case.Sensitive,
            "the refusal must tell the caller what to do INSTEAD — build skewed state through " +
            "30 §11.3's Rehydrate — not merely that it will not rewind. This fragment replaced " +
            "'carried-forward item' when M1-12 settled item 20; see the remarks.");

        clock.NowUtc.ShouldBe(
            Harnesses.Start,
            "a refused advance leaves the clock where it was — a guard that threw after writing " +
            "would leave the simulation in a state no caller asked for.");
    }

    /// <summary><c>default(DateTimeOffset)</c> cannot be used as a start — closed rather than
    /// documented, since it passes <c>GameContext</c>'s zero-offset guard and would otherwise let a
    /// fixture that forgot to set <c>NowUtc</c> land there silently.</summary>
    [Fact]
    public void The_default_DateTimeOffset_cannot_be_used_as_a_start()
    {
        default(DateTimeOffset).Offset.ShouldBe(
            TimeSpan.Zero,
            "M1-08's premise: default(DateTimeOffset) PASSES GameContext's zero-offset guard, which " +
            "is why it needs a guard of its own here rather than being caught downstream.");

        var refusal = Should.Throw<ArgumentOutOfRangeException>(
            () => new VirtualClock(default));

        refusal.ParamName.ShouldBe("startUtc");
        refusal.Message.ShouldContain("default(DateTimeOffset)", Case.Sensitive);
        refusal.Message.ShouldContain("A7", Case.Sensitive);
    }

    /// <summary>The negative half: the first game day itself is accepted, so the guard above is a
    /// floor rather than a refusal of everything.</summary>
    [Fact]
    public void The_first_game_day_itself_is_an_acceptable_start()
    {
        new VirtualClock(GameCalendar.FirstGameDay).NowUtc.ShouldBe(GameCalendar.FirstGameDay);

        Should.Throw<ArgumentOutOfRangeException>(
            () => new VirtualClock(GameCalendar.FirstGameDay.AddTicks(-1)),
            "one tick earlier is one tick before the first game day the calendar can answer.");
    }

    /// <summary>A start stated in anything but UTC is refused where it was written, not left to
    /// surface later from <c>GameContext</c> against the first command a caller sends.</summary>
    [Fact]
    public void A_start_with_a_non_zero_offset_is_refused()
    {
        var refusal = Should.Throw<ArgumentOutOfRangeException>(
            () => new VirtualClock(new DateTimeOffset(2026, 8, 12, 5, 0, 0, TimeSpan.FromHours(2))));

        refusal.ParamName.ShouldBe("startUtc");
        refusal.Message.ShouldContain("offset", Case.Insensitive);
        refusal.Message.ShouldNotContain(
            "default(DateTimeOffset)",
            Case.Sensitive,
            "an offset failure and a before-the-first-game-day failure have different fixes; a " +
            "single message would send whoever hit it to the wrong line (S2).");
    }

    /// <summary>An advance past the end of representable time is refused rather than overflowing.</summary>
    [Fact]
    public void An_advance_past_the_end_of_time_is_refused()
    {
        var clock = new VirtualClock(Harnesses.Start);

        var refusal = Should.Throw<ArgumentOutOfRangeException>(
            () => clock.Advance(DateTimeOffset.MaxValue - Harnesses.Start + TimeSpan.FromTicks(1)));

        refusal.ParamName.ShouldBe("by");
        refusal.Message.ShouldContain("end of representable time", Case.Sensitive);

        clock.NowUtc.ShouldBe(Harnesses.Start);

        // The boundary itself is reachable, so the guard is not off by one in the direction that
        // would quietly cost a caller the last representable tick.
        clock.Advance(DateTimeOffset.MaxValue - Harnesses.Start);
        clock.NowUtc.ShouldBe(DateTimeOffset.MaxValue);
    }

    /// <summary>Two clocks are independent: advancing one does not move the other. A clock that was
    /// shared static state would make simulations depend on the order others ran in.</summary>
    [Fact]
    public void Two_clocks_are_independent()
    {
        var first = new VirtualClock(Harnesses.Start);
        var second = new VirtualClock(Harnesses.Start);

        first.Advance(TimeSpan.FromDays(180));

        second.NowUtc.ShouldBe(Harnesses.Start);
        first.NowUtc.ShouldBe(Harnesses.Start.AddDays(180));
    }
}
