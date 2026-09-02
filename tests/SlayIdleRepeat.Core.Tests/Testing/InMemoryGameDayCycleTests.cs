using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Testing;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>Drives a multi-day player through commands, checking the day cycle, energy and
/// currencies.</summary>
/// <remarks>Only <c>BEGIN_SESSION</c> is <c>Handled</c>, and it is also the command that grants the
/// daily refill, so pure regeneration can only be observed in the event list — the catch-up's row
/// precedes the handler's.</remarks>
public sealed class InMemoryGameDayCycleTests
{
    /// <summary>The Energy a run costs, and the two capacities, read from the tuning the rules use.</summary>
    private static int MaxEnergy => Harnesses.Tuning.MaxEnergyAt(ProgressionDocuments.ShippedLegendLevelMin);

    private static int ReserveCapacity =>
        Harnesses.Tuning.ReserveCapacityAt(ProgressionDocuments.ShippedLegendLevelMin);

    /// <summary>Energy regenerates by rule, not by waiting: eight hours advanced on a clock nothing
    /// is watching fills an empty bar exactly, and the refill that follows finds no deficit.</summary>
    [Fact]
    public void Eight_hours_of_advanced_clock_fills_an_empty_bar_by_rule_before_the_refill_runs()
    {
        var (game, player) = Harnesses.WithPlayer();

        game.State(player).Player.Energy.ShouldBe(new EnergyBanks(0, 0));

        game.Clock.Advance(TimeSpan.FromHours(8));
        var result = game.Send(player, Harnesses.BeginSession);

        var accrued = TimeSpan.FromHours(8).Ticks / Harnesses.Tuning.RegenInterval.Ticks;
        accrued.ShouldBe(
            MaxEnergy,
            "eight hours at the shipped interval is exactly one full bar — 10 §3's own 'full refill " +
            "time 8 hours from empty'. If this ever stops being true the two assertions below are " +
            "measuring different things and should be split.");

        result.Accepted.ShouldBeTrue();
        result.Events.Count.ShouldBe(2);

        var regen = result.Events[0].ShouldBeOfType<CurrencyChanged>();
        regen.Id.ShouldBe(CurrencyId.ENERGY);
        regen.Reason.ShouldBe(Harnesses.EnergyRegenReason);
        regen.Delta.ShouldBe(accrued);
        regen.Sequence.ShouldBe(1);

        var refill = result.Events[1].ShouldBeOfType<CurrencyChanged>();
        refill.Id.ShouldBe(CurrencyId.ENERGY);
        refill.Reason.ShouldBe(Harnesses.DailyRefillReason);
        refill.Delta.ShouldBe(
            0L,
            "the catch-up already filled the bar, so 'to full' is zero — and the row still goes out, " +
            "because 21 §8.3 needs to see the refill was TAKEN (A6).");
        refill.Sequence.ShouldBe(2);

        game.State(player).Player.Energy.ShouldBe(new EnergyBanks(MaxEnergy, 0));
    }

    /// <summary>Regeneration past a full bar overflows into the Reserve as a single
    /// <c>CurrencyChanged</c> row, since the overflow is a movement within one currency.</summary>
    [Fact]
    public void Regeneration_past_a_full_bar_overflows_into_the_Reserve()
    {
        var (game, player) = Harnesses.WithPlayer();

        game.Clock.Advance(TimeSpan.FromHours(12));
        var result = game.Send(player, Harnesses.BeginSession);

        var accrued = (int)(TimeSpan.FromHours(12).Ticks / Harnesses.Tuning.RegenInterval.Ticks);
        accrued.ShouldBeGreaterThan(MaxEnergy);
        accrued.ShouldBeLessThanOrEqualTo(MaxEnergy + ReserveCapacity);

        var regen = result.Events[0].ShouldBeOfType<CurrencyChanged>();
        regen.Reason.ShouldBe(Harnesses.EnergyRegenReason);
        regen.Delta.ShouldBe(
            accrued,
            "one row for both banks — overflow is a movement within ENERGY, not a second currency.");

        game.State(player).Player.Energy.ShouldBe(new EnergyBanks(MaxEnergy, accrued - MaxEnergy));
    }

    /// <summary>The regeneration anchor advances by <c>wholeUnits × interval</c>, never snapping to
    /// the instant asked about, so the sub-unit remainder survives across commands.</summary>
    /// <remarks>Asserted on the anchor rather than the banks, which are refilled by the same command
    /// and would hide a remainder-discarding implementation.</remarks>
    [Fact]
    public void The_regeneration_anchor_moves_in_whole_units_so_the_remainder_survives()
    {
        var interval = Harnesses.Tuning.RegenInterval;
        interval.ShouldBe(
            TimeSpan.FromMinutes(ProgressionDocuments.ShippedRegenMinutesPerPoint),
            "the two steps below are chosen against this interval; if it moves they stop " +
            "discriminating.");

        var (game, player) = Harnesses.WithPlayer();
        var start = game.State(player).Player.EnergyAnchorUtc;

        game.Clock.Advance(interval + TimeSpan.FromMinutes(1));
        game.Send(player, Harnesses.BeginSession);

        game.State(player).Player.EnergyAnchorUtc.ShouldBe(
            start + interval,
            "one whole unit, not five minutes: A1 advances the anchor by wholeUnits × interval.");

        game.Clock.Advance(TimeSpan.FromMinutes(3));
        game.Send(player, Harnesses.BeginSession);

        game.State(player).Player.EnergyAnchorUtc.ShouldBe(
            start + interval + interval,
            "three minutes is less than one interval on its own. A second unit accrued only because " +
            "the minute left over from the first step was still there — which is exactly what an " +
            "implementation that snapped the anchor to NowUtc would have thrown away.");
    }

    /// <summary>Many small advances accrue exactly as much Energy as one large one, landing on the
    /// same anchor.</summary>
    /// <remarks>The two players do not end with the same Energy — placement differs by where the
    /// refill lands each of them — but the total income and the anchor must agree.</remarks>
    [Fact]
    public void Many_small_advances_accrue_exactly_as_much_as_one_large_one()
    {
        var span = TimeSpan.FromHours(8);
        var stepCount = (int)span.TotalMinutes;

        var (busy, busyPlayer) = Harnesses.WithPlayer();
        for (var step = 0; step < stepCount; step++)
        {
            busy.Clock.Advance(TimeSpan.FromMinutes(1));
            busy.Send(busyPlayer, Harnesses.BeginSession);
        }

        var (idle, idlePlayer) = Harnesses.WithPlayer();
        idle.Clock.Advance(span);
        idle.Send(idlePlayer, Harnesses.BeginSession);

        busy.Clock.NowUtc.ShouldBe(idle.Clock.NowUtc);

        busy.State(busyPlayer).Player.EnergyAnchorUtc.ShouldBe(
            idle.State(idlePlayer).Player.EnergyAnchorUtc,
            "eight hours is a whole number of intervals, so both anchors land on the same instant " +
            "whatever the command cadence.");

        var busyIncome = Harnesses.CurrencyRows(busy, Harnesses.EnergyRegenReason).Sum(e => e.Delta);
        var idleIncome = Harnesses.CurrencyRows(idle, Harnesses.EnergyRegenReason).Sum(e => e.Delta);

        busyIncome.ShouldBe(
            idleIncome,
            "the frequency-independence A1 exists for. A remainder-discarding accrual gives the busy " +
            "player ZERO here, because no single one-minute step reaches the interval.");

        busyIncome.ShouldBeGreaterThan(0L, "…and the comparison is not two zeroes.");
    }

    /// <summary><c>BEGIN_SESSION</c>'s daily effects are idempotent per game day, driven with many
    /// commands per day across many days — the cadence needed to tell "grants once per day" apart
    /// from "grants on every command".</summary>
    [Fact]
    public void BEGIN_SESSION_grants_once_per_game_day_however_many_commands_arrive()
    {
        const int Days = 14;
        const int CommandsPerDay = 12;

        var (game, player) = Harnesses.WithPlayer();

        Harnesses.Drive(game, player, Days, CommandsPerDay);

        game.CommandsIssued.ShouldBe((long)Days * CommandsPerDay);

        Harnesses.CurrencyRows(game, Harnesses.DailyRefillReason).Count.ShouldBe(
            Days,
            "one daily free refill per game day (10 §3.1, 'to full, 1/day'), whatever the command " +
            "cadence — 30 §2.3's 'otherwise: succeeds as a no-op'.");

        game.State(player).Player.DailyCount("begin_session").ShouldBe(
            1L,
            "the per-game-day guard stands at exactly one within the day: cleared at each 05:00 UTC " +
            "boundary by the catch-up and set once by the day's first BEGIN_SESSION. A counter that " +
            "read 12 would mean the handler was re-running its daily block all day.");
    }

    /// <summary>The login calendar sits on day 1 forever in M1 — specified behaviour, since it only
    /// advances once the open day is claimed and no M1 command can claim it. Driven over more than
    /// one full cycle so the assertion is about the pause and not the wrap.</summary>
    [Fact]
    public void The_login_calendar_stays_on_day_one_because_nothing_in_M1_can_claim_it()
    {
        var (game, player) = Harnesses.WithPlayer();

        Harnesses.Drive(game, player, days: TuningDocuments.ShippedCycleDays + 2, commandsPerDay: 3);

        var state = game.State(player).Player;

        state.LoginCalendarDay.ShouldBe(
            LoginCalendarTuning.FirstDay,
            "19 G pauses the calendar on an unclaimed day. CLAIM_CALENDAR is Deferred to M4-09, so " +
            "no M1 command can claim, and thirty days of logins leave the open day where it was. " +
            "This is the rule, not a defect — the advance arm is proven from a persisted claimed row " +
            "by PlayerLoginCalendarTests.");

        state.LoginCalendarDayClaimed.ShouldBeFalse();
    }

    /// <summary>The weekly boundary moves on Monday 05:00 UTC, not seven days after whatever day the
    /// simulation started on. The fixture starts on a Wednesday deliberately — from a Monday the two
    /// readings would coincide.</summary>
    [Fact]
    public void The_weekly_boundary_lands_on_Monday_and_not_seven_days_after_the_start()
    {
        Harnesses.Start.DayOfWeek.ShouldBe(DayOfWeek.Wednesday);

        var (game, player) = Harnesses.WithPlayer();
        var openingWeek = game.State(player).Player.WeeklyPeriodStartUtc;

        openingWeek.DayOfWeek.ShouldBe(DayOfWeek.Monday);
        openingWeek.ShouldBe(Harnesses.Start.AddDays(-2));

        Harnesses.Drive(game, player, days: 10, commandsPerDay: 2);

        var settled = game.State(player).Player.WeeklyPeriodStartUtc;

        settled.DayOfWeek.ShouldBe(DayOfWeek.Monday);
        settled.TimeOfDay.ShouldBe(TimeSpan.FromHours(5));
        settled.ShouldBe(
            GameCalendar.GameWeekStartAt(game.Clock.NowUtc),
            "the boundary in force is the one the calendar computes for the current instant — not " +
            "an accumulation of seven-day steps from wherever the player started.");
        settled.ShouldBeGreaterThan(openingWeek, "ten days is more than one week; it moved.");
    }

    /// <summary>An idle player at a full tank emits one zero-delta <c>energy_regen</c> row per
    /// command sent more than one interval apart, and none for a command sent inside one. The zero
    /// row must still be published, not filtered — its presence is how a consumer tells "did not log
    /// in" from "logged in full".</summary>
    [Fact]
    public void An_idle_player_at_a_full_tank_emits_one_zero_delta_regen_row_per_command_apart()
    {
        var (game, player) = Harnesses.WithPlayer();

        game.Clock.Advance(TimeSpan.FromDays(1));
        game.Send(player, Harnesses.BeginSession);

        game.State(player).Player.Energy.ShouldBe(
            new EnergyBanks(MaxEnergy, ReserveCapacity),
            "the tank is full in both banks, which is the state A6 is about.");

        var interval = Harnesses.Tuning.RegenInterval;

        game.Clock.Advance(interval + interval);
        var apart = game.Send(player, Harnesses.BeginSession);

        var zeroDelta = apart.Events.ShouldHaveSingleItem().ShouldBeOfType<CurrencyChanged>();
        zeroDelta.Reason.ShouldBe(Harnesses.EnergyRegenReason);
        zeroDelta.Delta.ShouldBe(
            0L,
            "time passed and the anchor moved, so the accrual ran; both banks were full, so nothing " +
            "landed. The row is PUBLISHED, not filtered (A6).");

        apart.Events.OfType<CurrencyChanged>()
            .Any(e => e.Reason.Equals(Harnesses.DailyRefillReason, StringComparison.Ordinal))
            .ShouldBeFalse("this is the day's second BEGIN_SESSION — 30 §2.3's no-op.");

        game.Clock.Advance(TimeSpan.FromTicks(interval.Ticks - 1));
        game.Send(player, Harnesses.BeginSession).Events.ShouldBeEmpty(
            "under one whole interval the anchor does not move, the aggregate is never touched and " +
            "no event is constructed at all — which is what makes the row above affordable.");
    }

    /// <summary>The daily reset happens whether or not anyone logs in: a hundred days away comes back
    /// to one day's boundary, not a hundred. Driven for a day first so the counter is genuinely set
    /// before the gap, rather than starting from an already-clear state.</summary>
    [Fact]
    public void A_hundred_day_absence_lands_on_one_boundary_and_clears_the_days_counters()
    {
        var (game, player) = Harnesses.WithPlayer();

        Harnesses.Drive(game, player, days: 1, commandsPerDay: 3);
        game.State(player).Player.DailyCount("begin_session").ShouldBe(1L);

        var before = game.State(player).Player.DailyPeriodStartUtc;

        game.Clock.Advance(TimeSpan.FromDays(100));
        var homecoming = game.Send(player, Harnesses.BeginSession);

        var state = game.State(player).Player;

        state.DailyPeriodStartUtc.ShouldBe(
            GameCalendar.GameDayStartAt(game.Clock.NowUtc),
            "the boundary recorded is the one the calendar computes for NOW — reached in one step, " +
            "whatever the gap. That is the whole of 30 §2.3's 'whether or not anyone logs in'.");

        (state.DailyPeriodStartUtc - before).ShouldBeGreaterThanOrEqualTo(
            TimeSpan.FromDays(100),
            "…and it really did move a hundred days, in ONE command. Without this the assertion " +
            "above is satisfied by a boundary that never moved at all.");

        state.DailyCount("begin_session").ShouldBe(
            1L,
            "the hundred days' counters were cleared and today's guard was set once — not " +
            "accumulated across a hundred boundaries.");

        homecoming.Events.OfType<CurrencyChanged>()
            .Count(e => e.Reason.Equals(Harnesses.DailyRefillReason, StringComparison.Ordinal))
            .ShouldBe(1, "one homecoming is one free refill, not a hundred.");
    }
}
