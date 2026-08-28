using Shouldly;
using SlayIdleRepeat.Application.Services.Inbox;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Services.Inbox;

/// <summary>When the nightly sweep runs, and why it is that instant rather than one of its own.</summary>
public sealed class InboxExpiryScheduleTests
{
    [Fact]
    public void The_sweep_runs_at_the_games_own_daily_boundary()
    {
        InboxExpirySchedule.NextSweepAfter(new DateTimeOffset(2026, 8, 29, 4, 59, 0, TimeSpan.Zero))
            .ShouldBe(
                new DateTimeOffset(2026, 8, 29, 5, 0, 0, TimeSpan.Zero),
                "the same boundary every other daily reset in the game uses. A sweep on its own " +
                "schedule would be a second daily boundary, and the two would drift the first time " +
                "either moved.");
    }

    [Fact]
    public void The_boundary_is_read_from_the_calendar_rather_than_transcribed()
    {
        InboxExpirySchedule.NextSweepAfter(new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero))
            .TimeOfDay.ShouldBe(
                GameCalendar.DayStart,
                "stated against the calendar's own value, so a schedule that hard-coded 05:00 would " +
                "go red on the commit that moved the game day rather than a year later.");
    }

    [Fact]
    public void A_process_that_starts_exactly_on_the_boundary_waits_for_tomorrow()
    {
        var boundary = new DateTimeOffset(2026, 8, 29, 5, 0, 0, TimeSpan.Zero);

        InboxExpirySchedule.NextSweepAfter(boundary).ShouldBe(
            boundary.AddDays(1),
            "strictly after, so a service that wakes on the boundary and asks again schedules " +
            "tomorrow rather than spinning on today. Nothing is lost: an expired message stays due " +
            "until it is swept.");
    }

    [Fact]
    public void A_process_that_starts_after_the_boundary_waits_for_tomorrow()
    {
        InboxExpirySchedule.NextSweepAfter(new DateTimeOffset(2026, 8, 29, 5, 0, 1, TimeSpan.Zero))
            .ShouldBe(new DateTimeOffset(2026, 8, 30, 5, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void The_batch_limit_has_a_default_a_deployment_can_move()
    {
        InboxExpirySchedule.DefaultBatchLimit.ShouldBeGreaterThan(
            0,
            "a sweep that took no messages would report a clean run for an inbox filling up behind " +
            "it. This is an operations value, not a game tunable — it trades sweep duration against " +
            "how long a batch holds a connection, and nothing about the game changes when it moves.");
    }
}
