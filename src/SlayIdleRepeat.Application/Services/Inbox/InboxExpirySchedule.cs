using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Services.Inbox;

/// <summary>When the nightly expiry sweep runs.</summary>
/// <remarks>
/// <para>
/// 🔒 At the authored daily boundary — the same 05:00 UTC every other daily reset in the game uses,
/// read from <see cref="GameCalendar"/> rather than transcribed. A sweep on its own schedule would
/// be a second daily boundary, and the two would drift the first time either moved.
/// </para>
/// <para>
/// A pure function of an instant rather than a timer: a hosted service asks it when to wake, and a
/// unit test asks it the same question at any instant without waiting for one.
/// </para>
/// </remarks>
public static class InboxExpirySchedule
{
    /// <summary>
    /// How many due messages one sweep takes, when the deployment names no other number.
    /// </summary>
    /// <remarks>
    /// ⚠️ An operations value, not a game tunable: it trades sweep duration against how long a batch
    /// holds a connection, and nothing about the game changes when it moves. It belongs to the
    /// deployment, which is why it is configurable and why it is not in the content set.
    /// </remarks>
    public const int DefaultBatchLimit = 500;

    /// <summary>The next sweep at or after <paramref name="afterUtc"/>.</summary>
    /// <param name="afterUtc">The instant to schedule from.</param>
    /// <returns>The next 05:00 UTC boundary strictly after the given instant.</returns>
    /// <remarks>
    /// Strictly after, so a service that wakes exactly on the boundary and asks again schedules
    /// tomorrow rather than spinning on today. A process that starts at 05:00:01 waits until
    /// tomorrow, which is correct: the sweep it just missed grants nothing that the next one will
    /// not, since an expired message stays due until it is swept.
    /// </remarks>
    public static DateTimeOffset NextSweepAfter(DateTimeOffset afterUtc)
    {
        var boundary = GameCalendar.GameDayStartAt(afterUtc);

        return boundary > afterUtc ? boundary : boundary.AddDays(1);
    }
}
