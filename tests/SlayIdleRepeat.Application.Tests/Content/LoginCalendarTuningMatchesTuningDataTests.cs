using System.Text.Json;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// 🔒 `21` §3.1 — `19` Part G's login calendar and <c>game-data/tuning/currencies.json</c> cannot
/// drift apart.
/// </summary>
/// <remarks>
/// <para>
/// The same seam, the same reason and the same mechanism as
/// <c>Rules.Economy.EnergyTuningMatchesTuningDataTests</c>.
/// <c>SlayIdleRepeat.Core.Content.LoginCalendarTuning</c> reads
/// <c>currencies.json#/loginCalendar/cycleDays</c> and M1-09's <c>BEGIN_SESSION</c> wraps the
/// calendar on it; neither half can see the other, because <c>Core.Tests</c> is hermetic and mirrors
/// the shipped file in a fixture, and the reader is <c>internal</c> to <c>Core</c> so this suite
/// cannot call it (<c>InternalsVisibleTo</c> names <c>SlayIdleRepeat.Core.Tests</c> alone, `30`
/// §11.3).
/// </para>
/// <para>
/// 🔴 <b>Why this file was written, stated plainly.</b> M1-09's review found that
/// <c>Core.Tests</c>' <c>LoginCalendarTuningTests</c> compared its own fixture constant against
/// itself: the fixture authors <c>cycleDays = 28</c> and the test asserted the reader answers 28, so
/// both sides were one <c>const</c> and the case could only fail if <c>ContentSnapshot.ReadInt32</c>
/// broke. Nothing anywhere read the shipped file. ⚠️ And <c>currencies.schema.json</c> types
/// <c>cycleDays</c> as no more than a positive integer while pinning <c>days</c> at exactly 28
/// entries — <b>the two are not tied together</b> — so <c>"cycleDays": 40</c> ships, passes content
/// validation, leaves every Core test green, and wraps the calendar twelve days past the last
/// authored reward row. This is the half that can see that.
/// </para>
/// <para>
/// It reads files, which is why it is here rather than in <c>Core.Tests</c>. No adapter, no port, no
/// container, no network: <see cref="RepoData"/> over the checkout the test runs from.
/// </para>
/// </remarks>
public sealed class LoginCalendarTuningMatchesTuningDataTests
{
    private const string CurrenciesDocument = "tuning/currencies.json";

    private const string CalendarPointer = CurrenciesDocument + "#/loginCalendar";

    /// <summary>
    /// 🔒 `19` G — the cycle length the reader reads, at the pointer it reads it from.
    /// </summary>
    /// <remarks>
    /// The pointer string is written out rather than taken from
    /// <c>LoginCalendarTuning.CycleDaysReference</c>, which this assembly cannot see. That is the
    /// point of the pin: a rename in the reader and a rename in the data have to be made in two
    /// places, and this is the case that fails when only one of them is.
    /// </remarks>
    [Fact]
    public void The_shipped_cycle_length_is_the_twenty_eight_days_19_G_authors()
    {
        Calendar().GetProperty("cycleDays").GetInt32().ShouldBe(
            28,
            $"19 G authors a 28-day login calendar and {CalendarPointer}/cycleDays is the one place " +
            "it is a number. Core's LoginCalendarTuning reads exactly this pointer; a rename or a " +
            "retype here leaves every hermetic Core test green and throws MissingContentException on " +
            "the first BEGIN_SESSION of the shipped game.");
    }

    /// <summary>
    /// 🔒 The cycle length equals the number of authored reward rows — the invariant the schema
    /// cannot express and the one that actually matters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is the assertion the file exists for.</b> `19` G's table is days 1..28 and
    /// <c>cycleDays</c> is where the wrap happens. If the two disagree the calendar is broken in one
    /// of two silent ways: a <c>cycleDays</c> <em>above</em> the table opens days that pay nothing,
    /// and one <em>below</em> it makes the last rows — including the day-28 S-tier chest plus Pet Egg
    /// — permanently unreachable. Neither throws, neither fails validation, and
    /// <c>CLAIM_CALENDAR</c> (M4-09) is where a player would eventually notice.
    /// </para>
    /// <para>
    /// 🔒 It also pins that the days are <b>1..cycleDays with no gaps and no repeats</b>, because
    /// "28 rows" and "the rows 1 through 28" are different claims and only the second is the one
    /// `19` G's <em>"nothing is skipped or lost"</em> rests on.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_day_of_the_cycle_has_exactly_one_authored_reward_row()
    {
        var calendar = Calendar();
        var cycleDays = calendar.GetProperty("cycleDays").GetInt32();

        var days = calendar.GetProperty("days")
            .EnumerateArray()
            .Select(row => row.GetProperty("day").GetInt32())
            .OrderBy(day => day)
            .ToArray();

        days.ShouldBe(
            Enumerable.Range(1, cycleDays).ToArray(),
            $"{CalendarPointer}/days must be exactly days 1..cycleDays, once each. A cycleDays above " +
            "the table opens calendar days that pay nothing; one below it makes the last rows — the " +
            "day-28 S-tier chest and Pet Egg among them — unreachable forever. currencies.schema.json " +
            "types cycleDays as a positive integer and pins the array length separately, so it cannot " +
            "see this and neither can any hermetic Core test.");
    }

    /// <summary>
    /// 🔒 `19` G — <em>"every cycle pays identically — nothing is first-cycle-exclusive"</em>, which
    /// is why <c>Player</c> carries an open-day pointer and <b>no cycle counter</b>.
    /// </summary>
    /// <remarks>
    /// The flag is deliberately not read by <c>LoginCalendarTuning</c> — it is a structural fact the
    /// wrap is <em>written against</em> rather than a branch it takes, and reading it would imply a
    /// code path for the false case that `19` G authors none of. Pinned here instead, so the day
    /// somebody authors <c>false</c> is the day a test says the domain has no way to honour it.
    /// </remarks>
    [Fact]
    public void Every_cycle_pays_identically_which_is_why_no_cycle_counter_is_stored()
    {
        Calendar().GetProperty("everyCyclePaysIdentically").GetBoolean().ShouldBeTrue(
            "19 G: 'after day 28 it restarts at day 1. Every cycle pays identically — nothing is " +
            "first-cycle-exclusive.' Player stores the open DAY and no cycle number, so a data set " +
            "authoring false would be asking for a rule the aggregate cannot express — and M4-09's " +
            "CLAIM_CALENDAR would have to bump SnapshotSchema.SchemaVersion to honour it.");
    }

    /// <summary>The <c>loginCalendar</c> block, or a failure naming the pointer that is missing.</summary>
    /// <remarks>
    /// 🔒 <c>JsonElement.GetProperty</c> throws before any <c>Shouldly</c> message can be printed, so
    /// the block is resolved through one helper that says which pointer went missing — the same
    /// construction <c>EnergyTuningMatchesTuningDataTests</c> uses for the same reason.
    /// </remarks>
    private static JsonElement Calendar()
    {
        using var document = JsonDocument.Parse(RepoData.Documents[CurrenciesDocument]);

        return document.RootElement.TryGetProperty("loginCalendar", out var calendar)
            ? calendar.Clone()
            : throw new InvalidOperationException(
                $"{CurrenciesDocument} has no 'loginCalendar' block. 19 G authors the 28-day login " +
                "calendar there and 21 §3.1 makes a 📐 TUNABLE outside game-data/tuning/ a bug; " +
                "Core.Content.LoginCalendarTuning reads " + CalendarPointer + "/cycleDays and every " +
                "BEGIN_SESSION would throw MissingContentException.");
    }
}
