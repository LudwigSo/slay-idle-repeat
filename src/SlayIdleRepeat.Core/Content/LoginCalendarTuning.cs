using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// 🔒 The `19` Part G login-calendar numbers, read out of <c>tuning/currencies.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// `21` §3.1: <em>"A 📐 TUNABLE number that is not in this directory is a bug."</em> `19` G marks
/// <b>all</b> of its values tunable and names the file, so there is no <c>const int CycleDays = 28</c>
/// anywhere in <c>Core</c> — a number in code is a number the economy simulator (`21`) cannot sweep.
/// </para>
/// <para>
/// 🔒 <b>One leaf is read, and the twenty-eight reward rows are deliberately not.</b> M1-09 advances
/// the calendar's <em>pointer</em>; it never pays out. `19` G puts the payout on <c>CLAIM_CALENDAR</c>,
/// whose dispatch row is <c>Deferred</c> to <b>M4-09</b>, and the reward rows name Pet Eggs,
/// <c>CHEST_PREMIUM</c> containers and a full Energy refill — three shapes whose types
/// (<c>ContainerClass</c> above all) do not exist, and which <c>GapRegister</c> already defers. A
/// reader here would be a reader of numbers nothing can spend.
/// </para>
/// <para>
/// 🔒 <b>Why it lives in <c>Content/</c>.</b> The same forced placement <see cref="EnergyTuning"/>
/// records: <c>Player.AdvanceLoginCalendar</c> needs the wrap point to hold `19` G's <em>"after day
/// 28 it restarts at day 1"</em>, and `30` §11.4 forbids <c>Model</c> from referencing <c>Rules</c>.
/// <c>Content</c> sits beneath both <c>Model</c> and <c>Handlers</c>, so one definition serves both.
/// <c>internal</c> — nothing outside <c>Core</c> needs it.
/// </para>
/// <para>
/// ⚠️ <b>What is authored here and deliberately not read.</b>
/// <c>#/loginCalendar/everyCyclePaysIdentically</c> is `19` G's <em>"every cycle pays identically —
/// nothing is first-cycle-exclusive"</em>, which is a structural fact the wrap is <em>written
/// against</em> rather than a branch it takes: the pointer restarting at 1 with no cycle counter
/// beside it <b>is</b> that sentence, and reading the flag would imply a code path for the false
/// case that `19` G authors none of. <c>#/loginCalendar/days</c> is the reward table, deferred above.
/// </para>
/// </remarks>
internal sealed class LoginCalendarTuning
{
    /// <summary>The document `19` G's calendar block lives in.</summary>
    internal const string DocumentPath = "tuning/currencies.json";

    private const string CalendarPointer = DocumentPath + "#/loginCalendar";

    /// <summary>`19` G — how many days one calendar cycle runs before it restarts at day 1.</summary>
    internal const string CycleDaysReference = CalendarPointer + "/cycleDays";

    private LoginCalendarTuning(int cycleDays) => CycleDays = cycleDays;

    /// <summary>
    /// 🔒 `19` G — the cycle length. <b>28 as shipped</b>, and the day the pointer wraps from.
    /// </summary>
    internal int CycleDays { get; }

    /// <summary>
    /// 🔒 `19` G — the calendar day that follows <paramref name="openDay"/>: the next one, or day 1
    /// once the cycle is spent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is a <b>definition type deriving a value from its own authored field</b> — the same thing
    /// <see cref="EnergyTuning.MaxEnergyAt"/> does — rather than the computation `30` §11.5 keeps off
    /// the aggregate: no state, no player, no rule.
    /// </para>
    /// <para>
    /// ⚠️ <b>It answers 1 for a day already at or past the cycle length, not only for the last
    /// one.</b> A balance patch that <em>shortens</em> the cycle leaves real players standing on a
    /// day the new table no longer has, and the same reasoning <c>EnergyMath.Deposit</c> records for
    /// a lowered <c>baseMax</c> applies: refusing such a player would turn a tuning change into an
    /// outage. Wrapping them to day 1 pays them a day they are owed rather than stranding them on
    /// one that does not exist.
    /// </para>
    /// </remarks>
    /// <param name="openDay">The calendar day currently open. Never below 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="openDay"/> is below 1.</exception>
    internal int DayAfter(int openDay)
    {
        RequireCalendarDay(openDay);

        return openDay >= CycleDays ? FirstDay : openDay + 1;
    }

    /// <summary>
    /// 🔒 The first day of every cycle. `19` G numbers its table from <b>1</b>, and day 0 is what an
    /// uninitialised column reads as — so it is a named floor rather than a literal at four call
    /// sites.
    /// </summary>
    /// <remarks>
    /// Not a 📐 tunable: `19` G's table is authored as days 1..28 and <c>cycleDays</c> is the dial on
    /// its <em>length</em>. A calendar that started at 0 would make the shipped 28-row table a
    /// 29-day cycle.
    /// </remarks>
    internal const int FirstDay = 1;

    /// <summary>
    /// The calendar-day guard both this type and <c>Player.Rehydrate</c> are written against.
    /// </summary>
    /// <remarks>
    /// 🔒 It checks the <b>floor only</b>, and the missing ceiling is the ruling rather than an
    /// omission — see <see cref="DayAfter"/>. <c>internal static</c> so the aggregate can state the
    /// same bound without transcribing the reason.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="calendarDay"/> is below 1.</exception>
    internal static void RequireCalendarDay(int calendarDay)
    {
        if (calendarDay < FirstDay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(calendarDay),
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
    /// <param name="content">The version-stamped snapshot the command is reading (`30` §3).</param>
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

    /// <summary>
    /// 🔒 Renders a number with <see cref="CultureInfo.InvariantCulture"/>, for the reason
    /// <see cref="EnergyTuning"/> has the same helper: `14` §8.2 wants <c>Core</c> reading
    /// identically everywhere, and
    /// <c>AmbientApiTests.Core_and_Application_contain_no_culture_sensitive_formatting</c> fails the
    /// build without it.
    /// </summary>
    private static string Render(int value) => value.ToString(CultureInfo.InvariantCulture);
}
