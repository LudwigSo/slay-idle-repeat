namespace SlayIdleRepeat.Core.Primitives;

/// <summary>The game calendar — the 05:00 UTC game day and the Monday 05:00 UTC game week — computed from an instant.</summary>
/// <remarks>
/// Exists so <c>Player</c> (which holds the 05:00/Monday boundary as an invariant) and
/// <c>GameRules.AdvanceTime</c> (which computes that boundary) share one definition instead of two
/// transcriptions of the same number that could drift and turn a soft rule violation into a crash.
/// <para>
/// Lives in <c>Primitives</c> rather than duplicated in <c>Rules/</c> because <c>Model</c> may not
/// reference <c>Rules</c>, and <c>Primitives</c> sits beneath both.
/// </para>
/// <para>
/// Every instant handed in is assumed UTC; <c>GameContext.RequireUtc</c> already refuses anything
/// else on every path, so this type does not re-guard it.
/// </para>
/// </remarks>
internal static class GameCalendar
{
    /// <summary>The UTC time of day every game day begins at: <b>05:00</b>.</summary>
    internal static readonly TimeSpan DayStart = TimeSpan.FromHours(5);

    /// <summary>The weekday every game week begins on: <b>Monday</b>.</summary>
    internal const DayOfWeek WeekStart = DayOfWeek.Monday;

    /// <summary>The first game day the calendar can answer: <c>0001-01-01T05:00:00Z</c>.</summary>
    /// <remarks>
    /// Reachable, not theoretical: <c>default(DateTimeOffset)</c> passes the zero-offset guard, so an
    /// unset clock lands exactly here, and the naive <c>startOfDay.AddDays(-1)</c> would otherwise
    /// underflow. The calendar floors at this value instead. <c>0001-01-01</c> is itself a Monday in
    /// .NET's proleptic Gregorian calendar, so the weekly step-back cannot underflow either.
    /// </remarks>
    internal static readonly DateTimeOffset FirstGameDay = new(1, 1, 1, 5, 0, 0, TimeSpan.Zero);

    /// <summary>The latest 05:00 UTC game-day boundary at or before <paramref name="nowUtc"/>, floored at <see cref="FirstGameDay"/>.</summary>
    /// <param name="nowUtc">A UTC instant.</param>
    internal static DateTimeOffset GameDayStartAt(DateTimeOffset nowUtc)
    {
        if (nowUtc < FirstGameDay)
        {
            return FirstGameDay;
        }

        var todaysStart = nowUtc - nowUtc.TimeOfDay + DayStart;

        return nowUtc.TimeOfDay >= DayStart ? todaysStart : todaysStart.AddDays(-1);
    }

    /// <summary>The Monday 05:00 UTC game-week boundary the game day of <paramref name="nowUtc"/> belongs to, floored at <see cref="FirstGameDay"/>.</summary>
    /// <remarks>
    /// Anchored to the game <em>day</em>, not the raw instant: a Monday at 04:59 UTC is still inside
    /// the previous game week, since the game day it belongs to is Sunday's. Stepping back from the
    /// raw instant would answer a future Monday for those five hours every week.
    /// <para>
    /// Steps back to <see cref="WeekStart"/> rather than a fixed seven days from the last reset, so it
    /// always lands on the correct weekday regardless of when the last reset happened to fire.
    /// </para>
    /// </remarks>
    /// <param name="nowUtc">A UTC instant.</param>
    internal static DateTimeOffset GameWeekStartAt(DateTimeOffset nowUtc)
    {
        var dayStart = GameDayStartAt(nowUtc);
        var daysIntoWeek = ((int)dayStart.DayOfWeek - (int)WeekStart + 7) % 7;

        return dayStart.AddDays(-daysIntoWeek);
    }

    /// <summary>Whether <paramref name="instant"/> is itself a 05:00 UTC game-day boundary.</summary>
    /// <param name="instant">A UTC instant.</param>
    internal static bool IsGameDayBoundary(DateTimeOffset instant) => instant.TimeOfDay == DayStart;

    /// <summary>Whether <paramref name="instant"/> is itself a Monday 05:00 UTC game-week boundary.</summary>
    /// <remarks>
    /// Checks both the weekday and the time of day: checking <see cref="WeekStart"/> alone would call
    /// every instant of Monday a boundary, including 00:00, which is still inside the previous week.
    /// </remarks>
    /// <param name="instant">A UTC instant.</param>
    internal static bool IsGameWeekBoundary(DateTimeOffset instant) =>
        instant.DayOfWeek == WeekStart && IsGameDayBoundary(instant);
}
