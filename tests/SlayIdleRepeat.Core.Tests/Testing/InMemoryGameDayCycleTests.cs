using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Testing;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// 🔒 M1's exit criterion, driven: <em>"<c>InMemoryGame</c> drives a multi-day player through
/// commands with the day cycle, energy and currencies working."</em>
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Every assertion here is about something a RULE decided</b>, not about a number the harness
/// stored. That distinction is the failure mode `30` §6's harness is most exposed to:
/// <c>game.State(player).Player.Energy</c> equalling what a fixture just granted proves the
/// dictionary works. What is asserted below is accrual across a clock advance that nothing waited
/// for, the A1 anchor property, `28` C2's overflow cascade, `30` §2.3's per-game-day idempotence
/// under many commands per day, the `30` §7 attribution of both Energy movements, and `19` G's
/// deliberately paused calendar.
/// </para>
/// <para>
/// ⚠️ <b>The scope this file can honestly cover, stated once.</b> `14` §2.3's registry is
/// forty-nine commands and exactly <b>one</b> — <c>BEGIN_SESSION</c> — is <c>Handled</c>; the rest
/// answer <c>ILLEGAL_STATE</c>, and a refused command discards its catch-up. So the only accepted
/// command in M1 is also the one that grants the daily refill, which means every observation of
/// pure regeneration below has to be made in the <b>event list</b> — where the catch-up's row
/// precedes the handler's — rather than by watching a balance move on a command that grants nothing.
/// That is not a workaround; it is why `30` §6 calls the event list "the assertion surface".
/// </para>
/// </remarks>
public sealed class InMemoryGameDayCycleTests
{
    /// <summary>The Energy a run costs, and the two capacities, read from the tuning the rules use.</summary>
    private static int MaxEnergy => Harnesses.Tuning.MaxEnergyAt(ProgressionDocuments.ShippedLegendLevelMin);

    private static int ReserveCapacity =>
        Harnesses.Tuning.ReserveCapacityAt(ProgressionDocuments.ShippedLegendLevelMin);

    /// <summary>
    /// 🔒 `30` §6's own claim — <em>"energy regenerates by RULE, not by waiting"</em>: eight hours
    /// advanced on a clock nothing is watching fills an empty bar exactly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two events are the whole assertion, in order and with their `30` §7 attributions. The
    /// catch-up's <c>energy_regen</c> comes <b>first</b> because it happened first (`14` §7.1's
    /// economy log and `14` §2.4's animation script both need that order), and the handler's
    /// <c>daily_free_refill</c> follows carrying <b>zero</b> — the bar the regeneration just filled
    /// leaves the refill nothing to do, which is <c>EnergyMath.RefillToFull</c>'s deficit-only
    /// reading and A6's "published, not filtered" in the same row.
    /// </para>
    /// <para>
    /// 🔒 The expected amount is derived from the tuning rather than written as 120: eight hours at
    /// one point per <c>regenMinutesPerPoint</c>. A literal would keep passing after the data moved,
    /// and `21` §3.1 makes every one of these numbers a 📐 tunable.
    /// </para>
    /// </remarks>
    [Fact]
    public void Eight_hours_of_advanced_clock_fills_an_empty_bar_by_rule_before_the_refill_runs()
    {
        var (game, player) = Harnesses.WithPlayer();

        game.State(player).Player.Energy.ShouldBe(new EnergyBanks(0, 0));

        game.Clock.Advance(TimeSpan.FromHours(8));
        var result = game.Send(player, Harnesses.BeginSession);

        var accrued = (long)(TimeSpan.FromHours(8).Ticks / Harnesses.Tuning.RegenInterval.Ticks);
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

    /// <summary>
    /// 🔒 `28` C2 — regeneration past a full bar overflows into the Reserve, through the harness.
    /// </summary>
    /// <remarks>
    /// Twelve hours is one and a half bars at the shipped rate, so the split is exact and states
    /// itself: the bar takes its maximum and the Reserve takes the remainder. A single
    /// <c>CurrencyChanged</c> covers both, because overflow into the Reserve is a movement
    /// <em>within</em> one currency and `21` §8.3 must see one row rather than two.
    /// </remarks>
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

    /// <summary>
    /// 🔒 Recorded assumption <b>A1</b>, through the harness: the anchor advances by
    /// <c>wholeUnits × interval</c> and <b>never</b> to the instant asked about — so the sub-unit
    /// remainder survives across commands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the property that makes regeneration frequency-independent, and the naive
    /// implementation — floor the elapsed time, then set the anchor to now — fails it while passing
    /// every test that advances the clock once. The two steps below are chosen so that a
    /// remainder-discarding implementation gives a <b>different</b> answer: five minutes accrues one
    /// whole unit and leaves one minute over; three more minutes is under the interval on its own,
    /// and only accrues a second unit if that minute survived.
    /// </para>
    /// <para>
    /// ⚠️ Asserted on the <b>anchor</b> rather than on the banks, because the banks are refilled by
    /// the same command and would hide it.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// 🔒 A1's consequence, driven the hard way: <b>many small advances accrue exactly as much as one
    /// large one</b>, and land on the same anchor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// `10` §3.2 wants a committed player never stopped by Energy, and the remainder-discarding
    /// implementation punishes precisely them — a player sending 480 commands in eight hours would
    /// regenerate a fraction of what one sending a single command does. Four hundred and eighty
    /// one-minute steps against one eight-hour step is that comparison at full strength: three out
    /// of every four commands accrue <em>nothing</em>, and if the remainder were dropped each time
    /// the busy player would end with zero.
    /// </para>
    /// <para>
    /// ⚠️ <b>The two players do NOT end with the same Energy, and that is correct rather than a
    /// weakened assertion.</b> The busy player's first command arrives one minute in with an empty
    /// bar, so `10` §3.1's daily refill fills it and the eight hours of regeneration then land in
    /// the Reserve; the idle player's single command arrives with the bar already regenerated, so
    /// the refill finds a deficit of zero. Same rules, same total income, different placement — the
    /// invariant A1 actually claims is the total and the anchor, and asserting a total the rules do
    /// not produce would be asserting the fixture.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// 🔒 `30` §2.3 — <c>BEGIN_SESSION</c>'s daily effects are idempotent per game day, driven with
    /// <b>many commands per day</b> across many days.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The cadence is the test.</b> M1-09 measured that only 3 of its 7 idempotence tests
    /// failed when the per-game-day guard was removed, because a suite that sends one command per
    /// day cannot tell "grants once per day" from "grants on every command". Twelve commands a day
    /// for fourteen days is 168 commands and 14 grants; a handler that re-granted would produce 168
    /// refill rows here, and a handler that granted once ever would produce 1.
    /// </para>
    /// <para>
    /// The counter is asserted alongside the rows because they answer different questions: the rows
    /// say how often the <em>grant</em> ran, the counter says the <em>guard</em> is being reset and
    /// re-set rather than merely never cleared.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// 🔒 `19` Part G — the login calendar correctly sits on <b>day 1 forever</b> in M1. This is
    /// specified behaviour and must not be "fixed".
    /// </summary>
    /// <remarks>
    /// `19` G advances the calendar <em>"only when the currently open day has been claimed"</em>,
    /// and claiming is <c>CLAIM_CALENDAR</c>'s — a <c>Deferred</c> row owned by <b>M4-09</b>. So an
    /// M1 player's calendar is paused on day 1 with the day unclaimed, which is exactly what §G
    /// specifies for a player who has not claimed: <em>"nothing is skipped or lost"</em>. Driven over
    /// more than one full cycle so a reader can see the claim is about the pause and not about the
    /// wrap.
    /// </remarks>
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

    /// <summary>
    /// 🔒 A2 — the weekly boundary moves on <b>Monday 05:00 UTC</b> and not on the seventh day after
    /// whatever day the simulation started on.
    /// </summary>
    /// <remarks>
    /// The fixture starts on a Wednesday deliberately: a simulation that started on a Monday could
    /// not tell "step back to the week's start" from "step back seven days", and
    /// <c>Player.ResetWeeklyCounters</c> refuses any boundary that is not a Monday — so the wrong
    /// arithmetic surfaces as an <see cref="ArgumentOutOfRangeException"/> out of <c>Apply</c> on six
    /// days in seven, a `30` §2.1 <b>P3</b> violation rather than a wrong number.
    /// </remarks>
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

    /// <summary>
    /// 🔒 Recorded assumption <b>A6</b>, both arms: an idle player at a full tank emits <b>one
    /// zero-delta</b> <c>energy_regen</c> row per command sent more than one interval apart — and
    /// <b>none at all</b> for a command sent inside one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a cost of the design rather than a feature, and it is pinned so that nobody
    /// "optimises" it away without re-reading the ruling: filtering the row here would reintroduce
    /// the constructed-then-discarded shape M1-08 removed, and `21` §8.3 distinguishes "the player
    /// did not log in" from "the player logged in full" by exactly this row's presence.
    /// </para>
    /// <para>
    /// 🔒 The second arm is the one that keeps the volume sane and is the reason the first is
    /// affordable: when the anchor does not move at all the aggregate is never touched and no event
    /// is constructed. Both are asserted, because a filter that suppressed the first would also
    /// pass a test that only checked the second.
    /// </para>
    /// </remarks>
    [Fact]
    public void An_idle_player_at_a_full_tank_emits_one_zero_delta_regen_row_per_command_apart()
    {
        var (game, player) = Harnesses.WithPlayer();

        // A day of regeneration fills both banks: 24h at the shipped rate is more than max + reserve.
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

    /// <summary>
    /// 🔒 `30` §2.3's daily reset happens <b>whether or not anyone logs in</b>: a player who is away
    /// for a hundred days comes back to one day's boundary, not a hundred.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The counter half is the visible consequence and is what a returning player actually notices:
    /// the day's caps are theirs again. The boundary half is the structural claim — the period the
    /// aggregate records is the one the calendar computes for <em>now</em>, reached in one step. See
    /// <c>InMemoryGamePerformanceTests</c> for the same fact asserted as a cost.
    /// </para>
    /// <para>
    /// ⚠️ The player is driven for a day <em>first</em> so the daily counter is genuinely set before
    /// the gap. Asserting "the counter is 1 after the return" on a player who had never played would
    /// be true of a harness that never reset anything.
    /// </para>
    /// </remarks>
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
