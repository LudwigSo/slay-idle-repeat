using Shouldly;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// Login-calendar tunables, read out of <c>tuning/currencies.json</c> — and the four ways a data
/// set can fail to answer, told apart.
/// </summary>
/// <remarks>
/// No <c>const int CycleDays = 28</c> anywhere in <c>Core</c>: a missing cycle length read as
/// <c>0</c> would make the calendar wrap on every advance, and nobody would learn that a number
/// nobody authored had been treated as zero. Only <c>cycleDays</c> is read here — the reward shapes
/// are a different suite's concern.
/// </remarks>
public sealed class LoginCalendarTuningTests
{
    /// <summary>
    /// The reader reads the pointer where the cycle length is authored — proved by moving the
    /// value, not by agreeing with the fixture's own constant.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(365)]
    public void The_reader_answers_the_cycle_length_the_document_authors(int authored)
    {
        LoginCalendarTuning.Read(TuningDocuments.CurrenciesOnly(ContentValue.Number(authored)))
            .CycleDays.ShouldBe(
                authored,
                "the reader must resolve currencies.json#/loginCalendar/cycleDays and answer what it " +
                "finds there — not a number compiled into Core. 21 §3.1: a 📐 TUNABLE that is not in " +
                "game-data/tuning/ is a bug.");
    }

    /// <summary>The next day is the next one, and day 1 once the cycle is spent.</summary>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(27, 28)]
    [InlineData(28, 1)]
    public void DayAfter_walks_the_cycle_and_wraps(int openDay, int expected)
    {
        LoginCalendarTuning.Read(TuningDocuments.Shipped).DayAfter(openDay).ShouldBe(expected);
    }

    /// <summary>A day <b>past</b> the cycle length wraps to day 1 rather than being refused.</summary>
    /// <remarks>
    /// Reachable, not theoretical: a balance patch that shortens the cycle leaves real players
    /// standing on a day the new table no longer has — the same ruling <c>EnergyMath.Deposit</c>
    /// applies to a lowered <c>baseMax</c>. Wrapping pays them a day they are owed rather than
    /// stranding them on one that does not exist.
    /// </remarks>
    [Theory]
    [InlineData(29)]
    [InlineData(400)]
    [InlineData(int.MaxValue)]
    public void A_day_past_the_cycle_length_wraps_rather_than_being_refused(int openDay)
    {
        LoginCalendarTuning.Read(TuningDocuments.Shipped).DayAfter(openDay).ShouldBe(
            LoginCalendarTuning.FirstDay,
            "a shortened cycle must not strand the players it left behind.");
    }

    /// <summary>Day 0 and below is refused: the table is numbered from 1.</summary>
    /// <remarks>
    /// Day 0 is what an uninitialised column reads as, and advancing from it would pay the player one
    /// day behind the table for the rest of the cycle — a wrong reward every day, which is worse than
    /// a loud failure once.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void A_day_below_one_is_refused(int openDay)
    {
        Should.Throw<ArgumentOutOfRangeException>(
                () => LoginCalendarTuning.Read(TuningDocuments.Shipped).DayAfter(openDay))
            .Message.ShouldContain("19 G", Case.Sensitive);
    }

    // ---------------------------------------------------------------- the four ways data can fail

    /// <summary>A missing document is a <c>MissingContentException</c>, not a zero.</summary>
    [Fact]
    public void A_missing_document_throws_rather_than_defaulting()
    {
        Should.Throw<MissingContentException>(
            () => LoginCalendarTuning.Read(TuningDocuments.WithoutCurrencies()));
    }

    /// <summary>A deliberate <c>null</c> is an <c>UnauthorisedTunableException</c> — the hole must stay a hole.</summary>
    [Fact]
    public void An_unauthorised_null_throws_rather_than_defaulting()
    {
        Should.Throw<UnauthorisedTunableException>(
            () => LoginCalendarTuning.Read(TuningDocuments.CurrenciesOnly(ContentValue.Unauthorised)));
    }

    /// <summary>A fractional cycle length is a type mismatch: cycle days are whole.</summary>
    /// <remarks>
    /// No document authors a rounding rule for it, so the read fails rather than picking one — the
    /// same line <c>EnergyTuning</c> draws for its whole-number tunables.
    /// </remarks>
    [Fact]
    public void A_fractional_cycle_length_is_refused()
    {
        Should.Throw<ContentTypeMismatchException>(
            () => LoginCalendarTuning.Read(TuningDocuments.CurrenciesOnly(ContentValue.Number(27.5m))));
    }

    /// <summary>A cycle shorter than one day is authorised but unusable.</summary>
    /// <remarks>
    /// A zero-day cycle has no day to open at all, and <c>DayAfter</c> would answer day 1 forever —
    /// a calendar permanently stuck on its first reward. Refused at the read, where the data set is
    /// still nameable, rather than at the advance, where it would be one player's problem.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(-28)]
    public void A_cycle_shorter_than_one_day_is_refused(int cycleDays)
    {
        Should.Throw<InvalidTunableException>(
                () => LoginCalendarTuning.Read(TuningDocuments.CurrenciesOnly(ContentValue.Number(cycleDays))))
            .Message.ShouldContain("19 G", Case.Sensitive);
    }

    /// <summary>The reader refuses a null content set rather than dereferencing it.</summary>
    [Fact]
    public void A_null_content_set_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => LoginCalendarTuning.Read(null!));
    }
}
