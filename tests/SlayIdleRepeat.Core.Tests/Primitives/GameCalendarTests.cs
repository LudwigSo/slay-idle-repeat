using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Primitives;

/// <summary>The game calendar: the 05:00 UTC game day, and the Monday 05:00 UTC game week.</summary>
/// <remarks>
/// This arithmetic exists once, in <c>Primitives</c>, because <c>Player</c> holds the same boundary
/// as an invariant and <c>Model</c> cannot reference <c>Rules</c>; a second copy would be exactly
/// the kind of drift that lets two numbers agree until they do not.
/// <see cref="Every_computed_boundary_is_one_the_Player_aggregate_accepts"/> ties the computed
/// answer to the aggregate's own refusal so the two cannot drift apart silently.
/// <para>
/// Every instant below is UTC with a zero offset — the calendar's stated precondition — so it
/// carries no guard of its own; <c>GameContext.RequireUtc</c> refuses anything else upstream.
/// </para>
/// </remarks>
public sealed class GameCalendarTests
{
    /// <summary>2026-08-12T05:00:00Z — a Wednesday, so a legal game day and an illegal game week.</summary>
    private static readonly DateTimeOffset WednesdayBoundary = new(2026, 8, 12, 5, 0, 0, TimeSpan.Zero);

    /// <summary>2026-08-10T05:00:00Z — a real Monday, checked against the calendar rather than assumed.</summary>
    private static readonly DateTimeOffset MondayBoundary = new(2026, 8, 10, 5, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------------ the game day

    /// <summary>
    /// The game-day boundary is the latest 05:00 UTC at or before the instant. 05:00:00.000 exactly
    /// is its own boundary; one tick earlier belongs to the previous day.
    /// </summary>
    [Fact]
    public void The_game_day_boundary_is_the_latest_0500_UTC_at_or_before_the_instant()
    {
        // Asserted against the published constant, not against itself: every assertion below is
        // written in terms of WednesdayBoundary, which would stay green even if DayStart moved.
        GameCalendar.DayStart.ShouldBe(TimeSpan.FromHours(5), "30 §2.3 writes 05:00 UTC into the reset rule.");

        GameCalendar.GameDayStartAt(WednesdayBoundary).ShouldBe(
            WednesdayBoundary, "05:00:00.000 exactly is the boundary itself, not the previous day's.");

        GameCalendar.GameDayStartAt(WednesdayBoundary.AddTicks(-1)).ShouldBe(
            WednesdayBoundary.AddDays(-1), "one tick before 05:00 is still the previous game day.");

        GameCalendar.GameDayStartAt(WednesdayBoundary.AddTicks(1)).ShouldBe(WednesdayBoundary);
        GameCalendar.GameDayStartAt(new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero))
                    .ShouldBe(WednesdayBoundary.AddDays(-1), "midnight is five hours short of the new game day.");
        GameCalendar.GameDayStartAt(new DateTimeOffset(2026, 8, 12, 23, 59, 59, TimeSpan.Zero))
                    .ShouldBe(WednesdayBoundary);

        GameCalendar.IsGameDayBoundary(WednesdayBoundary).ShouldBeTrue();
        GameCalendar.IsGameDayBoundary(WednesdayBoundary.AddTicks(-1)).ShouldBeFalse();
        GameCalendar.IsGameDayBoundary(WednesdayBoundary.AddTicks(1)).ShouldBeFalse();
    }

    /// <summary>
    /// The game day steps back across a month and a year — cases a boundary computed by subtracting
    /// from the day-of-month, or by zeroing the time, would get wrong.
    /// </summary>
    [Fact]
    public void The_game_day_boundary_crosses_a_month_and_a_year()
    {
        GameCalendar.GameDayStartAt(new DateTimeOffset(2026, 9, 1, 4, 59, 59, TimeSpan.Zero))
                    .ShouldBe(new DateTimeOffset(2026, 8, 31, 5, 0, 0, TimeSpan.Zero));

        GameCalendar.GameDayStartAt(new DateTimeOffset(2027, 1, 1, 2, 0, 0, TimeSpan.Zero))
                    .ShouldBe(new DateTimeOffset(2026, 12, 31, 5, 0, 0, TimeSpan.Zero));

        GameCalendar.GameDayStartAt(new DateTimeOffset(2027, 1, 1, 5, 0, 0, TimeSpan.Zero))
                    .ShouldBe(new DateTimeOffset(2027, 1, 1, 5, 0, 0, TimeSpan.Zero));
    }

    // ------------------------------------------------------------------ the Monday game week

    /// <summary>
    /// The game week starts Monday 05:00 UTC, and every day of that week answers the same Monday.
    /// All seven weekdays are checked because stepping back seven days answers a different date on
    /// six of them and the right one on Monday, so a single-day fixture would pass under the defect.
    /// </summary>
    [Theory]
    [InlineData(10, DayOfWeek.Monday)]
    [InlineData(11, DayOfWeek.Tuesday)]
    [InlineData(12, DayOfWeek.Wednesday)]
    [InlineData(13, DayOfWeek.Thursday)]
    [InlineData(14, DayOfWeek.Friday)]
    [InlineData(15, DayOfWeek.Saturday)]
    [InlineData(16, DayOfWeek.Sunday)]
    public void The_game_week_starts_on_Monday_at_0500_UTC(int dayOfMonth, DayOfWeek expected)
    {
        var instant = new DateTimeOffset(2026, 8, dayOfMonth, 12, 0, 0, TimeSpan.Zero);

        instant.DayOfWeek.ShouldBe(expected, "the fixture's own weekday is checked against the calendar.");

        // Asserted against the literal weekday, not against GameCalendar.WeekStart — the latter
        // would be checking the arithmetic against the very constant it is computed from.
        GameCalendar.WeekStart.ShouldBe(DayOfWeek.Monday, "A2, derived from 27 §4.");

        var weekStart = GameCalendar.GameWeekStartAt(instant);

        weekStart.ShouldBe(MondayBoundary);
        weekStart.DayOfWeek.ShouldBe(DayOfWeek.Monday);
        weekStart.TimeOfDay.ShouldBe(TimeSpan.FromHours(5));
        weekStart.Offset.ShouldBe(TimeSpan.Zero);
        GameCalendar.IsGameWeekBoundary(weekStart).ShouldBeTrue();
    }

    /// <summary>
    /// The game week is anchored to the game day, so Monday before 05:00 UTC still belongs to the
    /// previous week — "step back to the nearest Monday" would answer a Monday in the future
    /// relative to the instant.
    /// </summary>
    [Fact]
    public void A_Monday_before_0500_UTC_still_belongs_to_the_previous_game_week()
    {
        var earlyMonday = MondayBoundary.AddTicks(-1);

        earlyMonday.DayOfWeek.ShouldBe(DayOfWeek.Monday, "one tick before 05:00 is still the same Monday.");

        GameCalendar.GameWeekStartAt(earlyMonday).ShouldBe(MondayBoundary.AddDays(-7));
    }

    /// <summary>
    /// A game-week boundary is a Monday and 05:00:00.000 UTC, asserted separately: either half
    /// alone answers <c>true</c> for instants the aggregate refuses.
    /// </summary>
    [Fact]
    public void A_game_week_boundary_is_a_Monday_at_0500_UTC_exactly()
    {
        GameCalendar.IsGameWeekBoundary(MondayBoundary).ShouldBeTrue();

        GameCalendar.IsGameWeekBoundary(MondayBoundary.AddTicks(-1)).ShouldBeFalse(
            "a Monday at 04:59:59.9999999 is still inside the PREVIOUS game week.");
        GameCalendar.IsGameWeekBoundary(MondayBoundary.AddTicks(1)).ShouldBeFalse(
            "one tick past 05:00 is inside the week, not the instant it begins at.");
        GameCalendar.IsGameWeekBoundary(MondayBoundary.AddHours(12)).ShouldBeFalse(
            "Monday noon is a Monday and is not a boundary — the time of day is half the claim.");

        GameCalendar.IsGameWeekBoundary(WednesdayBoundary).ShouldBeFalse(
            "a Wednesday at 05:00 is a legal game DAY boundary and an illegal game WEEK boundary (A2).");
    }

    /// <summary>
    /// The week step-back crosses a year boundary: 2027-01-01 is a Friday whose game week began
    /// 2026-12-28, and clamping the step-back inside the calendar year would answer 2027-01-01
    /// instead — a Friday <c>ResetWeeklyCounters</c> refuses.
    /// </summary>
    [Fact]
    public void The_game_week_start_crosses_a_year_boundary()
    {
        var newYear = new DateTimeOffset(2027, 1, 1, 12, 0, 0, TimeSpan.Zero);

        newYear.DayOfWeek.ShouldBe(DayOfWeek.Friday, "the fixture's weekday is checked, not assumed.");

        var weekStart = GameCalendar.GameWeekStartAt(newYear);

        weekStart.ShouldBe(new DateTimeOffset(2026, 12, 28, 5, 0, 0, TimeSpan.Zero));
        weekStart.DayOfWeek.ShouldBe(DayOfWeek.Monday);
    }

    // ------------------------------------------------------------------ the floor

    /// <summary>
    /// An instant before the first game day answers the floor rather than throwing. Reachable, not
    /// theoretical: <c>default(DateTimeOffset)</c> passes <c>GameContext</c>'s zero-offset guard, so
    /// a fixture that forgot to set <c>NowUtc</c> lands exactly there, and the naive
    /// <c>startOfDay.AddDays(-1)</c> would throw out of <c>Apply</c>.
    /// </summary>
    [Fact]
    public void An_instant_before_the_first_game_day_answers_the_floor()
    {
        var firstGameDay = new DateTimeOffset(1, 1, 1, 5, 0, 0, TimeSpan.Zero);

        default(DateTimeOffset).Offset.ShouldBe(TimeSpan.Zero, "which is why GameContext accepts it.");
        DateTimeOffset.MinValue.DayOfWeek.ShouldBe(
            DayOfWeek.Monday, "0001-01-01 is a Monday, so the weekly step-back cannot underflow the floor.");

        GameCalendar.FirstGameDay.ShouldBe(
            firstGameDay, "A7 records 0001-01-01T05:00:00Z as the floor, not merely 'some Monday at 05:00'.");

        GameCalendar.GameDayStartAt(default).ShouldBe(firstGameDay);
        GameCalendar.GameWeekStartAt(default).ShouldBe(firstGameDay);

        GameCalendar.GameDayStartAt(firstGameDay).ShouldBe(firstGameDay);
        GameCalendar.GameWeekStartAt(firstGameDay).ShouldBe(firstGameDay);

        firstGameDay.TimeOfDay.ShouldBe(TimeSpan.FromHours(5), "30 §2.3's game day starts at 05:00 UTC.");
        firstGameDay.DayOfWeek.ShouldBe(DayOfWeek.Monday, "A2's game week starts on a Monday.");
        GameCalendar.IsGameDayBoundary(firstGameDay).ShouldBeTrue();
        GameCalendar.IsGameWeekBoundary(firstGameDay).ShouldBeTrue();
    }

    // ------------------------------------------------------------------ the aggregate agrees

    /// <summary>
    /// Every boundary this calendar computes is one the <c>Player</c> aggregate accepts: as a
    /// persisted period start, and as an argument to its own resets. The arithmetic here and the
    /// invariant on the aggregate are written in two layers that cannot see each other, and this is
    /// the only place they meet.
    /// </summary>
    [Theory]
    [InlineData(2026, 8, 12, 9, 41, 8)]    // an ordinary Wednesday morning
    [InlineData(2026, 8, 10, 5, 0, 0)]     // a Monday, exactly on both boundaries
    [InlineData(2026, 8, 16, 23, 59, 59)]  // the Sunday at the far end of that week
    [InlineData(2026, 9, 1, 4, 59, 59)]    // one second before a month rolls over
    [InlineData(2027, 1, 1, 12, 0, 0)]     // a Friday whose game week began in the previous year
    [InlineData(1, 1, 1, 0, 0, 0)]         // default(DateTimeOffset) — the A7 floor
    public void Every_computed_boundary_is_one_the_Player_aggregate_accepts(
        int year, int month, int day0, int hour, int minute, int second)
    {
        var nowUtc = new DateTimeOffset(year, month, day0, hour, minute, second, TimeSpan.Zero);
        var instant = nowUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

        var day = GameCalendar.GameDayStartAt(nowUtc);
        var week = GameCalendar.GameWeekStartAt(nowUtc);

        var player = Should.NotThrow(
            () => Core.Model.Player.Rehydrate(
                      PlayerSnapshots.With(
                          energyAnchorUtc: day,
                          lastAppliedAtUtc: day,
                          dailyPeriodStartUtc: day,
                          weeklyPeriodStartUtc: week),
                      ProgressionDocuments.Shipped)
                  .Value,
            $"the Player aggregate refuses the boundaries the calendar computed for {instant}.");

        Should.NotThrow(
            () => player.ResetDailyCounters(day),
            "the aggregate must accept the very day boundary the calendar computed.");
        Should.NotThrow(
            () => player.ResetWeeklyCounters(week),
            "…and the very week boundary, which it refuses on any weekday but Monday (A2).");

        week.ShouldBeLessThanOrEqualTo(day, "a game week begins on or before the game day inside it.");
        GameCalendar.IsGameDayBoundary(day).ShouldBeTrue();
        GameCalendar.IsGameWeekBoundary(week).ShouldBeTrue();
    }
}
