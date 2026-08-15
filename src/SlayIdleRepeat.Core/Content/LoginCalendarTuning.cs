using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>The login-calendar numbers, read out of <c>tuning/currencies.json</c>.</summary>
/// <remarks>
/// <para>
/// One leaf is read; the twenty-eight reward rows are deliberately not. This type only advances
/// the calendar's <em>pointer</em> — it never pays out, and the reward types those rows name
/// (Pet Eggs, premium chests, a full Energy refill) do not exist yet, so a reader here would be a
/// reader of numbers nothing can spend.
/// </para>
/// <para>
/// Lives in <c>Content/</c> for the same reason as <see cref="EnergyTuning"/>: the wrap point is
/// needed by both the aggregate and the advancing rule, and <c>Content</c> is the layer beneath
/// both.
/// </para>
/// <para>
/// <c>#/loginCalendar/everyCyclePaysIdentically</c> is a structural fact the wrap is written
/// against rather than a branch it takes — the pointer restarting at 1 with no cycle counter
/// beside it already <b>is</b> that fact, so it is not read either.
/// </para>
/// </remarks>
internal sealed class LoginCalendarTuning
{
    /// <summary>The document the calendar block lives in.</summary>
    internal const string DocumentPath = "tuning/currencies.json";

    private const string CalendarPointer = DocumentPath + "#/loginCalendar";

    /// <summary>How many days one calendar cycle runs before it restarts at day 1.</summary>
    internal const string CycleDaysReference = CalendarPointer + "/cycleDays";

    private LoginCalendarTuning(int cycleDays) => CycleDays = cycleDays;

    /// <summary>The cycle length. 28 as shipped, and the day the pointer wraps from.</summary>
    internal int CycleDays { get; }

    /// <summary>
    /// The calendar day that follows <paramref name="openDay"/>: the next one, or day 1 once the
    /// cycle is spent.
    /// </summary>
    /// <remarks>
    /// Answers 1 for a day already at or past the cycle length, not only for the last one: a
    /// balance patch that shortens the cycle can leave real players standing on a day the new table
    /// no longer has, and wrapping them to day 1 pays them a day they are owed rather than
    /// stranding them on one that does not exist.
    /// </remarks>
    /// <param name="openDay">The calendar day currently open. Never below 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="openDay"/> is below 1.</exception>
    internal int DayAfter(int openDay)
    {
        RequireCalendarDay(openDay, nameof(openDay));

        return openDay >= CycleDays ? FirstDay : openDay + 1;
    }

    /// <summary>
    /// The first day of every cycle — a named floor rather than a literal at four call sites.
    /// </summary>
    internal const int FirstDay = 1;

    /// <summary>
    /// <see cref="DayAfter"/>'s floor guard. Checks the floor only; the missing ceiling is the
    /// ruling rather than an omission — see <see cref="DayAfter"/>.
    /// </summary>
    /// <remarks>
    /// <c>private</c>, and <c>Player.Rehydrate</c> deliberately does not call it: that path
    /// accumulates a fault string rather than throwing, so it cannot use a guard that throws, and
    /// it already refuses a row below <see cref="FirstDay"/> before any handler runs. This guard
    /// remains as an assertion for direct callers of <see cref="DayAfter"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="calendarDay"/> is below 1.</exception>
    private static void RequireCalendarDay(int calendarDay, string parameterName)
    {
        if (calendarDay < FirstDay)
        {
            // The CALLER's parameter name, not this helper's — ParamName is what a host reports,
            // and callers thread their own name through so it matches their public signature.
            throw new ArgumentOutOfRangeException(
                parameterName,
                calendarDay,
                "19 G numbers the login calendar from day 1; " + Render(calendarDay) + " is not a " +
                "day it has. Day 0 is what an uninitialised column reads as, and advancing from it " +
                "would pay the player a day earlier than the one they are standing on for the rest " +
                "of the cycle.");
        }
    }

    /// <summary>
    /// Reads the calendar block. Throws rather than defaulting on anything missing, unauthorised,
    /// mistyped or nonsensical.
    /// </summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="MissingContentException">The document or the pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">The pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">The cycle length holds a fraction.</exception>
    /// <exception cref="InvalidTunableException">The value is authorised but unusable.</exception>
    internal static LoginCalendarTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var cycleDays = content.ReadInt32(CycleDaysReference);
        if (cycleDays < FirstDay)
        {
            throw new InvalidTunableException(
                CycleDaysReference,
                "A login calendar cycle must run at least one day. 19 G authors 28; this document " +
                "authors " + Render(cycleDays) + ", which is a calendar with no day to open.");
        }

        return new LoginCalendarTuning(cycleDays);
    }

    /// <summary>Renders a number with <see cref="CultureInfo.InvariantCulture"/>.</summary>
    private static string Render(int value) => value.ToString(CultureInfo.InvariantCulture);
}
