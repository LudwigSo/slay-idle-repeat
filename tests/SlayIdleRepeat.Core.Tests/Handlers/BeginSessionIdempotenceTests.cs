using Shouldly;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Handlers;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// BEGIN_SESSION's daily effects are idempotent per game day: repeated calls must not re-grant them.
/// </summary>
public sealed class BeginSessionIdempotenceTests
{
    [Fact]
    public void A_second_BEGIN_SESSION_in_the_same_game_day_grants_nothing()
    {
        var first = BeginSessions.Send(BeginSessions.Slice());

        first.Accepted.ShouldBeTrue();
        first.NewState.Player.Energy.ShouldBe(
            new EnergyBanks(BeginSessions.Tuning.MaxEnergyAt(1), 0),
            "the first BEGIN_SESSION of the game day refills to full.");

        var second = BeginSessions.Send(first.NewState, BeginSessions.Morning.AddMinutes(1));

        second.Accepted.ShouldBeTrue("the repeat succeeds as a no-op, it is not refused.");
        second.NewState.Player.Energy.ShouldBe(
            first.NewState.Player.Energy,
            "the second call of the game day grants nothing.");

        // A zero-delta refill is invisible on the energy banks (the bar was already full) but must
        // still not appear as an event — otherwise a handler with no idempotence at all would pass.
        second.Events.ShouldBeEmpty("a no-op publishes no event.");
    }

    /// <remarks>Spread across the day at hour intervals, ruling out a key on the instant or a cooldown window.</remarks>
    [Fact]
    public void Ten_commands_in_one_game_day_pay_one_refill()
    {
        var state = BeginSessions.Slice();
        var refills = 0;

        // Ten commands at one-hour intervals from 09:41 stay before the next 05:00 UTC boundary.
        for (var hour = 0; hour < 10; hour++)
        {
            var result = BeginSessions.Send(state, BeginSessions.Morning.AddHours(hour));

            result.Accepted.ShouldBeTrue($"command {hour} must succeed.");
            refills += result.Events.Count(e => IsRefill(e));
            state = result.NewState;
        }

        refills.ShouldBe(1, "the free refill is once per day, not once per command.");

        state.Player.DailyCount(BeginSession.DailyRunCounter).ShouldBe(
            1, "the marker counts the day, not the calls.");
    }

    /// <remarks>
    /// The player claims the newly opened day between the two calls, so only the per-game-day
    /// idempotence — not the calendar's own claimed/unclaimed guard — can refuse the second advance.
    /// </remarks>
    [Fact]
    public void A_repeat_call_does_not_advance_the_calendar_a_second_time()
    {
        var state = BeginSessions.Slice(loginCalendarDay: 3, loginCalendarDayClaimed: true);

        var first = BeginSessions.Send(state);

        first.NewState.Player.LoginCalendarDay.ShouldBe(4, "day 3 was claimed, so the calendar advances.");
        first.NewState.Player.LoginCalendarDayClaimed.ShouldBeFalse("the newly opened day is unclaimed.");

        var second = BeginSessions.Send(first.NewState, BeginSessions.Morning.AddHours(2));

        second.NewState.Player.LoginCalendarDay.ShouldBe(
            4, "day 4 is unclaimed, so the pause guard alone would already stop this advance.");

        // The player claims day 4 later the same game day and re-sends; the day's marker is already
        // set, so only the per-game-day idempotence can refuse this one.
        var claimedAgain = BeginSessions.Slice(
            loginCalendarDay: 4,
            loginCalendarDayClaimed: true,
            dailyCounters: PlayerSnapshots.Counters((BeginSession.DailyRunCounter, 1)));

        var third = BeginSessions.Send(claimedAgain, BeginSessions.Morning.AddHours(9));

        third.NewState.Player.LoginCalendarDay.ShouldBe(
            4,
            "the calendar advances at most once per game day, even when the open day is claimed.");
        third.NewState.Player.LoginCalendarDayClaimed.ShouldBeTrue("…and the claim they made stands.");
    }

    /// <remarks>
    /// Opens below maximum on the second call, unlike the fixtures above — a handler that only
    /// re-grants on deficit would otherwise pass every test in this file.
    /// </remarks>
    [Fact]
    public void Energy_spent_during_the_day_is_not_topped_back_up()
    {
        var max = BeginSessions.Tuning.MaxEnergyAt(1);
        var first = BeginSessions.Send(BeginSessions.Slice());

        first.NewState.Player.Energy.ShouldBe(new EnergyBanks(max, 0));

        // Modelled by rehydrating a drained bar, since no M1 command spends Energy yet.
        var spent = BeginSessions.Slice(
            energy: new EnergyBanks(4, 0),
            dailyCounters: PlayerSnapshots.Counters((BeginSession.DailyRunCounter, 1)));

        // Two minutes later, inside the regeneration interval, so no accrual muddies the assertion.
        var second = BeginSessions.Send(spent, BeginSessions.Morning.AddMinutes(2));

        second.NewState.Player.Energy.ShouldBe(
            new EnergyBanks(4, 0),
            "the day's refill was already taken, so re-sending must NOT top the bar back up.");
        second.Events.ShouldBeEmpty("nothing moved, so nothing is attributed.");
    }

    [Fact]
    public void A_BEGIN_SESSION_on_the_next_game_day_grants_again()
    {
        var max = BeginSessions.Tuning.MaxEnergyAt(1);
        var first = BeginSessions.Send(BeginSessions.Slice());

        first.NewState.Player.Energy.ShouldBe(new EnergyBanks(max, 0));

        // 24 hours later: past the 05:00 UTC boundary, so a new game day, and catch-up clears the marker.
        var nextDay = BeginSessions.Send(first.NewState, Worlds.NextDay(BeginSessions.Morning));

        // Counted by reason, not "the list is non-empty": 24 hours also crosses 360 regeneration
        // intervals, so an energy_regen row goes out regardless of whether this handler grants anything.
        nextDay.Events.Count(IsRefill).ShouldBe(1, "a new game day pays the free refill again.");
        nextDay.NewState.Player.DailyCount(BeginSession.DailyRunCounter).ShouldBe(
            1, "the marker was cleared at the boundary and set again.");
    }

    [Fact]
    public void The_marker_is_set_by_the_handler_and_cleared_by_the_day_boundary()
    {
        var first = BeginSessions.Send(BeginSessions.Slice());

        first.NewState.Player.DailyCount(BeginSession.DailyRunCounter).ShouldBe(
            1, "the handler sets the marker after catch-up runs, in the same command.");

        // Another command later the same day: its catch-up must NOT clear the marker.
        var interleaved = GameRules.Execute(
            Worlds.MetaTable((_, _) => HandlerResult.Accept()),
            first.NewState,
            new Worlds.MetaFixtureCommand(),
            Worlds.Context with { NowUtc = BeginSessions.Morning.AddHours(3) });

        interleaved.NewState.Player.DailyCount(BeginSession.DailyRunCounter).ShouldBe(
            1,
            "Player.ResetDailyCounters is a no-op on a boundary already crossed, so an unrelated " +
            "command must leave the day's marker standing.");

        // And a command on the next game day must clear it.
        var nextDay = GameRules.Execute(
            Worlds.MetaTable((_, _) => HandlerResult.Accept()),
            interleaved.NewState,
            new Worlds.MetaFixtureCommand(),
            Worlds.Context with { NowUtc = Worlds.NextDay(BeginSessions.Morning) });

        nextDay.NewState.Player.DailyCount(BeginSession.DailyRunCounter).ShouldBe(
            0,
            "crossing 05:00 UTC clears the daily counters via the catch-up of ANY command, not just " +
            "BEGIN_SESSION, so the next BEGIN_SESSION always reads zero however the player got there.");
    }

    /// <remarks>
    /// The skew pins DailyPeriodStartUtc forward, so a corrected clock computes an earlier boundary
    /// and clears nothing. The cost is one day's grants received early, recovered at the next boundary.
    /// </remarks>
    [Fact]
    public void A_forward_clock_jump_pays_early_and_never_twice()
    {
        // The clock is a day fast: the command lands in what it believes is the next game day.
        var skewed = BeginSessions.Send(
            BeginSessions.Slice(), Worlds.NextDay(BeginSessions.Morning));

        skewed.Events.Count(IsRefill).ShouldBe(1, "the skewed command pays the day it thinks it is in.");
        skewed.NewState.Player.DailyPeriodStartUtc.ShouldBe(
            Worlds.NextDay(BeginSessions.Today),
            "…and the boundary it stored is the later one, which pins the day forward.");

        // The operator corrects the clock. GameRules.MarkApplied floors the instant it hands the
        // aggregates, so the command still succeeds rather than throwing.
        var corrected = BeginSessions.Send(skewed.NewState, BeginSessions.Morning.AddMinutes(1));

        corrected.Accepted.ShouldBeTrue("every command on every state returns a result, never a throw.");

        corrected.Events.Count(IsRefill).ShouldBe(
            0,
            "the correction pays nothing: the skewed command already set this game day's marker, and " +
            "a boundary pinned forward by the skew is later than the one a corrected clock computes.");

        corrected.NewState.Player.DailyPeriodStartUtc.ShouldBe(
            Worlds.NextDay(BeginSessions.Today),
            "…and the boundary stays where the skew put it, rather than walking backwards, which " +
            "would let the next command re-open the day and pay the refill a second time.");

        corrected.NewState.Player.LastAppliedAtUtc.ShouldBe(
            skewed.NewState.Player.LastAppliedAtUtc,
            "the anchor is floored at the stored instant, not moved backwards.");

        // Real time reaches the day the skewed command already claimed.
        var caughtUp = BeginSessions.Send(
            corrected.NewState, Worlds.NextDay(BeginSessions.Morning).AddHours(1));

        caughtUp.Events.Count(IsRefill).ShouldBe(
            0, "this day's grants were already paid by the skewed command.");

        // …and the day after that recovers, so the cost is bounded rather than permanent.
        var recovered = BeginSessions.Send(
            caughtUp.NewState, Worlds.NextDay(Worlds.NextDay(BeginSessions.Morning)));

        recovered.Events.Count(IsRefill).ShouldBe(1, "recovery is automatic on the next boundary.");
    }

    /// <summary>Whether an event is the daily free refill (rather than a regeneration accrual).</summary>
    /// <remarks>
    /// Matched on Reason, not event type: both are CurrencyChanged for ENERGY, so matching on type
    /// alone would count an energy_regen row as a refill.
    /// </remarks>
    private static bool IsRefill(DomainEvent produced) =>
        produced is CurrencyChanged { Id: CurrencyId.ENERGY } change &&
        string.Equals(change.Reason, BeginSession.DailyRefillReason, StringComparison.Ordinal);
}
