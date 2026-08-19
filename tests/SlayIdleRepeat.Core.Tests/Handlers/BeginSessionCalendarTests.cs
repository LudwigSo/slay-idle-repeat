using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// The 28-day login calendar's advancement rule: it advances at BEGIN_SESSION at most once per game
/// day, and only when the currently open day has been claimed.
/// </summary>
/// <remarks>
/// The payout is CLAIM_CALENDAR's, which is deferred, so "claimed" fixtures are built through
/// Player.Rehydrate — the same door the persistence adapter uses.
/// </remarks>
public sealed class BeginSessionCalendarTests
{
    /// <remarks>Also asserts the daily block still ran, so a handler ignoring BEGIN_SESSION entirely fails.</remarks>
    [Fact]
    public void An_unclaimed_open_day_pauses_the_calendar()
    {
        var result = BeginSessions.Send(
            BeginSessions.Slice(loginCalendarDay: 5, loginCalendarDayClaimed: false));

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.LoginCalendarDay.ShouldBe(
            5, "a missed or unclaimed day pauses the calendar; nothing is skipped or lost.");
        result.NewState.Player.LoginCalendarDayClaimed.ShouldBeFalse("…and it is still unclaimed.");

        result.Events.ShouldNotBeEmpty("positive evidence the daily block did run.");
    }

    [Fact]
    public void A_claimed_open_day_advances_and_the_new_day_opens_unclaimed()
    {
        var result = BeginSessions.Send(
            BeginSessions.Slice(loginCalendarDay: 5, loginCalendarDayClaimed: true));

        result.NewState.Player.LoginCalendarDay.ShouldBe(6);
        result.NewState.Player.LoginCalendarDayClaimed.ShouldBeFalse(
            "the day that just opened has not been claimed, so it pauses the calendar again.");
    }

    [Theory]
    [InlineData(TuningDocuments.ShippedCycleDays, LoginCalendarTuning.FirstDay)]
    [InlineData(TuningDocuments.ShippedCycleDays - 1, TuningDocuments.ShippedCycleDays)]
    [InlineData(LoginCalendarTuning.FirstDay, LoginCalendarTuning.FirstDay + 1)]
    public void The_cycle_restarts_at_day_one_after_the_last_day(int openDay, int expected)
    {
        var result = BeginSessions.Send(
            BeginSessions.Slice(loginCalendarDay: openDay, loginCalendarDayClaimed: true));

        result.NewState.Player.LoginCalendarDay.ShouldBe(
            expected, "every cycle pays identically; a calendar stopping at the last day would end " +
            "a returning player's daily reward permanently.");
    }

    /// <summary>A player who never claims stays on day one across many day boundaries.</summary>
    [Fact]
    public void A_player_who_never_claims_stays_on_the_same_day_for_a_week()
    {
        var state = BeginSessions.Slice();
        var at = BeginSessions.Morning;

        for (var day = 0; day < 7; day++)
        {
            var result = BeginSessions.Send(state, at);

            result.Accepted.ShouldBeTrue();
            state = result.NewState;
            at = Worlds.NextDay(at);
        }

        state.Player.LoginCalendarDay.ShouldBe(
            LoginCalendarTuning.FirstDay, "the calendar is paused, not slowed.");
    }

    /// <remarks>
    /// Compared by event-list count against a paused run, rather than asserting "no calendar event
    /// exists" — which would also be true of an implementation that granted currency through the
    /// refill's own row instead.
    /// </remarks>
    [Fact]
    public void Advancing_the_calendar_moves_no_currency()
    {
        var advanced = BeginSessions.Send(
            BeginSessions.Slice(loginCalendarDay: 1, loginCalendarDayClaimed: true));
        var paused = BeginSessions.Send(
            BeginSessions.Slice(loginCalendarDay: 1, loginCalendarDayClaimed: false));

        advanced.NewState.Player.LoginCalendarDay.ShouldBe(2, "the first advanced…");
        paused.NewState.Player.LoginCalendarDay.ShouldBe(1, "…and the second did not.");

        advanced.Events.Count.ShouldBe(
            paused.Events.Count,
            "the calendar's payout belongs to CLAIM_CALENDAR, not BEGIN_SESSION.");

        advanced.NewState.Player.Wallet.ShouldBe(
            paused.NewState.Player.Wallet, "…and no wallet row moved either.");
    }
}
