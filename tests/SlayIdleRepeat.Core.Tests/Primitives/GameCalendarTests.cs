using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Primitives;

/// <summary>
/// 🔒 `30` §2.3 — the game calendar: the <b>05:00 UTC</b> game day, and the <b>Monday</b> 05:00 UTC
/// game week (milestone assumption <b>A2</b>, derived from `27` §4).
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Why this arithmetic exists once, in <c>Primitives</c>.</b> <c>Player</c> holds `30` §2.3's
/// boundary as an invariant — it refuses a daily period that is not 05:00 UTC and a weekly one that
/// is not a Monday — and `30` §11.4 forbids <c>Model</c> from referencing <c>Rules</c>. A second
/// copy of "05:00 UTC" in <c>Rules/</c> would be exactly the drift <c>EnergyTuning.MaxEnergyAt</c>
/// and <c>EnergyBanks</c> were each moved to fix: two numbers that agree until they do not.
/// <see cref="Every_computed_boundary_is_one_the_Player_aggregate_accepts"/> is the assertion that
/// ties the computed answer to the aggregate's own refusal, so the two cannot drift apart silently.
/// </para>
/// <para>
/// ⚠️ Every instant below is UTC with a zero offset, which is the calendar's stated precondition:
/// <c>GameContext.RequireUtc</c> refuses anything else before it can reach here, so the calendar
/// carries no guard of its own (steering <b>S1</b> — a branch no input can reach is not defence in
/// depth).
/// </para>
/// </remarks>
public sealed class GameCalendarTests
{
    /// <summary>2026-08-12T05:00:00Z — a Wednesday, so a legal game day and an illegal game week.</summary>
    private static readonly DateTimeOffset WednesdayBoundary = new(2026, 8, 12, 5, 0, 0, TimeSpan.Zero);

    /// <summary>2026-08-10T05:00:00Z — a real Monday, checked against the calendar rather than assumed.</summary>
    private static readonly DateTimeOffset MondayBoundary = new(2026, 8, 10, 5, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------------ 30 §2.3 · the game day

    /// <summary>
    /// 🔒 `30` §2.3 — the game-day boundary is the <b>latest 05:00 UTC at or before</b> the instant.
    /// 05:00:00.000 exactly is its own boundary; one tick earlier belongs to the previous day.
    /// </summary>
    /// <remarks>
    /// The single-tick pair is the whole test. `30` §2.3 resets quest expiry, the wheel's free spin,
    /// ad caps and dungeon entries at 05:00 UTC "whether or not anyone logs in", and an
    /// off-by-one-tick boundary gives one player in every few thousand a second free spin or a
    /// missing one — the kind of defect that is only ever reported as "it happened once".
    /// </remarks>
    [Fact]
    public void The_game_day_boundary_is_the_latest_0500_UTC_at_or_before_the_instant()
    {
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
    /// 🔒 `30` §2.3 — the game day steps back across a month <b>and</b> a year, because
    /// <c>AddDays(-1)</c> is the only step and the calendar owns the rest.
    /// </summary>
    /// <remarks>
    /// A day boundary computed by subtracting from the day-of-month, or by zeroing the time and
    /// hoping, breaks on exactly these two instants and on no fixture that stays inside one month —
    /// which is every other fixture in this file.
    /// </remarks>
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

    // ------------------------------------------------------------------ A2 · the Monday game week

    /// <summary>
    /// 🔒 Milestone assumption <b>A2</b> (`27` §4) — the game week starts on <b>Monday at 05:00
    /// UTC</b>, and every day of that week answers the same Monday.
    /// </summary>
    /// <remarks>
    /// The theory runs all seven weekdays because the failure mode is directional: an implementation
    /// that stepped back seven days from the current game day answers a different date on six of
    /// them and the right one on Monday, so a single-day fixture would pass under it. The weekday of
    /// each fixture is asserted, not assumed.
    /// </remarks>
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

        var weekStart = GameCalendar.GameWeekStartAt(instant);

        weekStart.ShouldBe(MondayBoundary);
        weekStart.DayOfWeek.ShouldBe(GameCalendar.WeekStart);
        weekStart.TimeOfDay.ShouldBe(GameCalendar.DayStart);
        weekStart.Offset.ShouldBe(TimeSpan.Zero);
        GameCalendar.IsGameWeekBoundary(weekStart).ShouldBeTrue();
    }

    /// <summary>
    /// 🔒 <b>A2</b> — the game week is anchored to the game <b>day</b>, so Monday before 05:00 UTC
    /// still belongs to the previous week.
    /// </summary>
    /// <remarks>
    /// This is the case a "step back to the nearest Monday" implementation gets wrong: it answers
    /// this Monday, which is in the future relative to the instant, and
    /// <c>Player.ResetWeeklyCounters</c> would then record a week the player has not reached.
    /// </remarks>
    [Fact]
    public void A_Monday_before_0500_UTC_still_belongs_to_the_previous_game_week()
    {
        var earlyMonday = MondayBoundary.AddTicks(-1);

        earlyMonday.DayOfWeek.ShouldBe(DayOfWeek.Monday, "one tick before 05:00 is still the same Monday.");

        GameCalendar.GameWeekStartAt(earlyMonday).ShouldBe(MondayBoundary.AddDays(-7));
        GameCalendar.IsGameWeekBoundary(WednesdayBoundary).ShouldBeFalse(
            "a Wednesday at 05:00 is a legal game DAY boundary and an illegal game WEEK boundary (A2).");
    }

    /// <summary>
    /// 🔒 <b>A2</b> — the week step-back crosses a year boundary, where "the Monday of this week"
    /// and "the Monday of this year" are different answers.
    /// </summary>
    /// <remarks>
    /// 2027-01-01 is a Friday and its game week began on 2026-12-28 — a Monday in the previous
    /// year. An implementation that clamped the step-back inside the calendar year would answer
    /// 2027-01-01 and hand <c>Player.ResetWeeklyCounters</c> a Friday, which it refuses outright:
    /// a `30` §2.1 <b>P3</b> violation on every command in the first days of January.
    /// </remarks>
    [Fact]
    public void The_game_week_start_crosses_a_year_boundary()
    {
        var newYear = new DateTimeOffset(2027, 1, 1, 12, 0, 0, TimeSpan.Zero);

        newYear.DayOfWeek.ShouldBe(DayOfWeek.Friday, "the fixture's weekday is checked, not assumed.");

        var weekStart = GameCalendar.GameWeekStartAt(newYear);

        weekStart.ShouldBe(new DateTimeOffset(2026, 12, 28, 5, 0, 0, TimeSpan.Zero));
        weekStart.DayOfWeek.ShouldBe(DayOfWeek.Monday);
    }

    // ------------------------------------------------------------------ A7 · the floor

    /// <summary>
    /// 🔒 Recorded assumption <b>A7</b> / `30` §2.1 <b>P3</b> — an instant before the first game day
    /// answers the <b>floor</b> rather than throwing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Reachable, not theoretical.</b> <c>default(DateTimeOffset)</c> is
    /// <c>0001-01-01T00:00:00+00:00</c>, which passes <c>GameContext</c>'s zero-offset guard — so a
    /// <c>VirtualClock</c> (M1-11) or a fixture that forgot to set <c>NowUtc</c> lands exactly there.
    /// The naive <c>startOfDay.AddDays(-1)</c> throws <see cref="ArgumentOutOfRangeException"/> from
    /// <see cref="DateTimeOffset"/> itself, which would come out of <c>GameRules.Apply</c> and
    /// violate P3.
    /// </para>
    /// <para>
    /// The weekly answer is the same instant because <c>0001-01-01</c> is a Monday in .NET's
    /// proleptic Gregorian calendar — asserted below rather than taken on trust, since the whole
    /// floor rests on it not underflowing a second time.
    /// </para>
    /// </remarks>
    [Fact]
    public void An_instant_before_the_first_game_day_answers_the_floor()
    {
        default(DateTimeOffset).Offset.ShouldBe(TimeSpan.Zero, "which is why GameContext accepts it.");
        DateTimeOffset.MinValue.DayOfWeek.ShouldBe(
            DayOfWeek.Monday, "0001-01-01 is a Monday, so the weekly step-back cannot underflow the floor.");

        GameCalendar.GameDayStartAt(default).ShouldBe(GameCalendar.FirstGameDay);
        GameCalendar.GameWeekStartAt(default).ShouldBe(GameCalendar.FirstGameDay);

        GameCalendar.GameDayStartAt(GameCalendar.FirstGameDay).ShouldBe(GameCalendar.FirstGameDay);
        GameCalendar.GameWeekStartAt(GameCalendar.FirstGameDay).ShouldBe(GameCalendar.FirstGameDay);

        GameCalendar.FirstGameDay.TimeOfDay.ShouldBe(GameCalendar.DayStart);
        GameCalendar.FirstGameDay.DayOfWeek.ShouldBe(GameCalendar.WeekStart);
    }

    // ------------------------------------------------------------------ the aggregate agrees

    /// <summary>
    /// 🔒 `30` §2.3 — every boundary this calendar computes is one the <c>Player</c> aggregate
    /// <b>accepts</b>: as a persisted period start, and as an argument to its own resets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two halves of the design are written in two layers that cannot see each other — the
    /// arithmetic here, the invariant on the aggregate — and this is the only place they meet. A
    /// calendar that answered 04:00, a non-Monday week start or a non-zero offset would compile, and
    /// M1-08's <c>AdvanceTime</c> would then throw <see cref="ArgumentOutOfRangeException"/> out of
    /// <c>GameRules.Apply</c> on the first command that crossed a boundary — a `30` §2.1 <b>P3</b>
    /// violation reported as a crash rather than as a wrong number.
    /// </para>
    /// <para>
    /// Driven through <c>Player.Rehydrate</c> (which validates the two persisted boundaries) and
    /// through the mutators themselves. Resetting to the boundary already in force is legal and a
    /// no-op, so the aggregate is built <em>at</em> the computed boundaries — nothing here can pass
    /// by moving a period backwards.
    /// </para>
    /// </remarks>
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

        var rehydrated = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(
                energyAnchorUtc: day,
                lastAppliedAtUtc: day,
                dailyPeriodStartUtc: day,
                weeklyPeriodStartUtc: week),
            ProgressionDocuments.Shipped);

        rehydrated.IsSuccess.ShouldBeTrue(
            $"the Player aggregate refuses the boundaries computed for {instant}: {rehydrated.Error}");

        Should.NotThrow(
            () => rehydrated.Value.ResetDailyCounters(day),
            "the aggregate must accept the very day boundary the calendar computed.");
        Should.NotThrow(
            () => rehydrated.Value.ResetWeeklyCounters(week),
            "…and the very week boundary, which it refuses on any weekday but Monday (A2).");

        week.ShouldBeLessThanOrEqualTo(day, "a game week begins on or before the game day inside it.");
        GameCalendar.IsGameDayBoundary(day).ShouldBeTrue();
        GameCalendar.IsGameWeekBoundary(week).ShouldBeTrue();
    }
}
