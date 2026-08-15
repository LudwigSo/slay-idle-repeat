using System.Text.Json;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>Checks that the login calendar in <c>tuning/currencies.json</c> and Core's <c>LoginCalendarTuning</c> reader agree.</summary>
/// <remarks>
/// <c>Core.Tests</c> is hermetic and mirrors the shipped file in a fixture; the reader is internal
/// to Core, so this suite can't call it directly and instead reads the real file via
/// <see cref="RepoData"/>. This exists because a prior version of the Core-side test compared its
/// own fixture constant against itself, so nothing anywhere actually read the shipped file — and
/// <c>currencies.schema.json</c> types <c>cycleDays</c> as just a positive integer, not tied to the
/// days array length, so a mismatch would ship, pass validation, and still break the calendar at
/// runtime.
/// </remarks>
public sealed class LoginCalendarTuningMatchesTuningDataTests
{
    private const string CurrenciesDocument = "tuning/currencies.json";

    private const string CalendarPointer = CurrenciesDocument + "#/loginCalendar";

    /// <summary>The cycle length the reader reads, at the pointer it reads it from.</summary>
    /// <remarks>
    /// The pointer string is written out rather than shared with the reader (which this assembly
    /// can't see), so a rename in one place without the other fails this test.
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

    /// <summary>The cycle length equals the number of authored reward rows — an invariant the schema cannot express.</summary>
    /// <remarks>
    /// If the two disagree, the calendar breaks silently: a <c>cycleDays</c> above the table opens
    /// days that pay nothing, and one below it makes the last rows permanently unreachable. Neither
    /// throws nor fails schema validation. This also pins that the days are exactly 1..cycleDays
    /// with no gaps or repeats, not merely the right count of rows.
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

    /// <summary>Every cycle pays identically, which is why <c>Player</c> stores an open-day pointer and no cycle counter.</summary>
    /// <remarks>
    /// The flag is deliberately not read by <c>LoginCalendarTuning</c> — it's a structural fact the
    /// wrap is written against, not a branch it takes. Pinned here so authoring <c>false</c> is
    /// caught by a test rather than silently doing nothing.
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
    /// <remarks><c>JsonElement.GetProperty</c> throws before any Shouldly message can print, so this helper names the missing pointer instead.</remarks>
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
