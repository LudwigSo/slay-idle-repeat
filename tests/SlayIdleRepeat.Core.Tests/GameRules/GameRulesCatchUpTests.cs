using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Economy;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

// Namespace SlayIdleRepeat.Core.Tests, not ...Tests.GameRules, even though the file sits under
// GameRules/: a child namespace named `GameRules` shadows the type GameRules for everything inside
// SlayIdleRepeat.Core.Tests, so a test written here could not name the very class it is testing
// (CS0118).

/// <summary>
/// Lazy catch-up: the first step of every command handler rolls the aggregate forward across
/// every reset boundary crossed since the last command. No job, no timer, no clock call.
/// </summary>
/// <remarks>
/// Every test here asserting a negative also carries positive evidence that the catch-up ran — a
/// seam that does nothing satisfies "no event emitted" and "counters not wiped" equally, so each
/// fixture crosses some other boundary in the same command and asserts that too.
/// <para>
/// Catch-up deliberately does not handle Plus expiry, run TTL, quest, daily-shop or event-window
/// expiry — those have no state to roll forward yet.
/// </para>
/// </remarks>
public sealed class GameRulesCatchUpTests
{
    private const string RegenReason = Harnesses.EnergyRegenReason;

    /// <summary>The shipped energy numbers — 120 (+2/level, cap 200), one point per 4 minutes.</summary>
    private static readonly EnergyTuning Shipped = EnergyTuning.Read(ProgressionDocuments.Shipped);

    /// <summary>
    /// One regeneration interval, read from the tuning rather than written as <c>4</c>: a literal
    /// here would keep passing after the dial moved.
    /// </summary>
    private static TimeSpan Interval => Shipped.RegenInterval;

    /// <summary>2026-08-12T09:41:08Z — the instant every fixture applies at (a Wednesday).</summary>
    private static readonly DateTimeOffset Now = Worlds.NowUtc;

    /// <summary>2026-08-11T05:00Z — the game day <b>before</b> the one <see cref="Now"/> falls in.</summary>
    private static readonly DateTimeOffset PreviousGameDay = PlayerSnapshots.Wednesday.AddDays(-1);

    // ------------------------------------------------------------------ the events reach the log

    [Fact]
    public void Catch_up_energy_reaches_the_result_event_list()
    {
        var result = Apply(Snapshot(anchorAgo: 10 * Interval), Accepting);

        var accrual = result.Events.ShouldHaveSingleItem().ShouldBeOfType<CurrencyChanged>();

        accrual.Id.ShouldBe(CurrencyId.ENERGY);
        accrual.Delta.ShouldBe(10L, "40 minutes at one point per four minutes is ten points (10 §3).");
        accrual.Reason.ShouldBe(RegenReason, "A5 — 21 §8.3 groups income_attribution.csv by this column.");
        accrual.Sequence.ShouldBe(1, "Apply stamps the combined list from 1 (30 §7).");

        result.NewState.Player.Energy.ShouldBe(new EnergyBanks(10, 0));
    }

    /// <summary>
    /// Catch-up's events come first in the one list <c>Apply</c> stamps: they happened before the
    /// command the player sent. An accrual stamped after the spend it funded would claim the player
    /// paid with Energy they did not yet have.
    /// </summary>
    [Fact]
    public void Catch_up_events_are_stamped_before_the_handlers()
    {
        var result = Apply(
            Snapshot(anchorAgo: 10 * Interval),
            Worlds.MetaTable((_, input) =>
                HandlerResult.Accept(input.Player.MoveCurrency(CurrencyId.CROWNS, 5L, "fixture_grant"))));

        result.Events.Count.ShouldBe(2);

        var first = result.Events[0].ShouldBeOfType<CurrencyChanged>();
        first.Id.ShouldBe(CurrencyId.ENERGY);
        first.Reason.ShouldBe(RegenReason);
        first.Sequence.ShouldBe(1);

        var second = result.Events[1].ShouldBeOfType<CurrencyChanged>();
        second.Id.ShouldBe(CurrencyId.CROWNS);
        second.Reason.ShouldBe("fixture_grant");
        second.Sequence.ShouldBe(2);
    }

    /// <summary>
    /// The handler is handed a slice the catch-up has already advanced. Read from inside the
    /// handler, because a catch-up running after would produce the same <c>NewState</c> and a
    /// different game.
    /// </summary>
    [Fact]
    public void The_handler_is_handed_a_slice_the_catch_up_has_already_advanced()
    {
        var anchor = Now - (10 * Interval);
        EnergyBanks? seenEnergy = null;
        DateTimeOffset? seenAnchor = null;
        DateTimeOffset? seenDailyBoundary = null;

        Apply(
            Snapshot(anchorAgo: 10 * Interval, dailyPeriodStartUtc: PreviousGameDay),
            Worlds.MetaTable((_, input) =>
            {
                seenEnergy = input.Player.Energy;
                seenAnchor = input.Player.EnergyAnchorUtc;
                seenDailyBoundary = input.Player.DailyPeriodStartUtc;
                return HandlerResult.Accept();
            }));

        seenEnergy.ShouldBe(new EnergyBanks(10, 0), "the handler must see the regenerated banks, not the stored ones.");
        seenAnchor.ShouldBe(anchor + (10 * Interval), "…and the anchor the accrual moved.");
        seenDailyBoundary.ShouldBe(
            PlayerSnapshots.Wednesday,
            "…and the game day the command is actually in, which is what M1-09 keys 'first BEGIN_SESSION of the day' off.");
    }

    // ------------------------------------------------------------------ the accrual, A1

    /// <summary>
    /// A command sent less than one regeneration interval after the last accrual accrues nothing
    /// and emits no event — there is no zero row.
    /// </summary>
    [Fact]
    public void A_command_inside_one_regen_interval_accrues_nothing_and_emits_nothing()
    {
        var anchor = Now - Interval + TimeSpan.FromSeconds(1);

        var result = Apply(
            PlayerSnapshots.With(
                energyAnchorUtc: anchor,
                lastAppliedAtUtc: PlayerSnapshots.Midmorning,
                dailyPeriodStartUtc: PreviousGameDay,
                dailyCounters: PlayerSnapshots.Counters(("ad_caps", 3))),
            Accepting);

        result.Events.ShouldBeEmpty("a partial unit is not a movement, so there is no 21 §8.3 row to write.");
        result.NewState.Player.Energy.ShouldBe(new EnergyBanks(0, 0));
        result.NewState.Player.EnergyAnchorUtc.ShouldBe(
            anchor, "the anchor must not move over a partial unit — that is how the remainder gets discarded (A1).");

        // Evidence the catch-up ran at all, not merely that this assertion is vacuous.
        result.NewState.Player.DailyPeriodStartUtc.ShouldBe(PlayerSnapshots.Wednesday);
        result.NewState.Player.DailyCount("ad_caps").ShouldBe(0);
    }

    /// <summary>
    /// When the accrual ran but both banks were already full, the anchor still advances and the
    /// row is still published with <c>Delta</c> zero — stopping the clock would hand the player the
    /// same span again later.
    /// </summary>
    [Fact]
    public void A_saturated_accrual_still_advances_the_anchor_and_still_reports()
    {
        var full = new EnergyBanks(
            EnergyMath.MaxEnergy(Shipped, 1), EnergyMath.ReserveCapacity(Shipped, 1));
        var anchor = Now - (10 * Interval);

        var result = Apply(Snapshot(anchorAgo: 10 * Interval, energy: full), Accepting);

        var accrual = result.Events.ShouldHaveSingleItem().ShouldBeOfType<CurrencyChanged>();
        accrual.Id.ShouldBe(CurrencyId.ENERGY);
        accrual.Delta.ShouldBe(0L, "A6 — the row is published even when there was nowhere to put the points.");
        accrual.Reason.ShouldBe(RegenReason);

        result.NewState.Player.Energy.ShouldBe(full);
        result.NewState.Player.EnergyAnchorUtc.ShouldBe(
            anchor + (10 * Interval),
            "the clock does not stop because the tank is full — an anchor left behind re-grants the span.");
    }

    /// <summary>
    /// The anchor advances by <c>wholeUnits × interval</c> and never to <c>NowUtc</c>; the
    /// remainder stays banked in the gap. The obvious alternative — floor the elapsed time, then
    /// set the anchor to now — discards the remainder every call.
    /// <see cref="Many_small_advances_accrue_exactly_what_one_big_one_does"/> is the randomised form.
    /// </summary>
    [Fact]
    public void The_anchor_advances_by_whole_units_and_never_to_now()
    {
        var remainder = TimeSpan.FromSeconds(90);
        var anchor = Now - (10 * Interval) - remainder;

        var result = Apply(
            PlayerSnapshots.With(energyAnchorUtc: anchor, lastAppliedAtUtc: PlayerSnapshots.Midmorning),
            Accepting);

        var moved = result.NewState.Player.EnergyAnchorUtc;

        moved.ShouldBe(anchor + (10 * Interval));
        moved.ShouldBeLessThan(Now, "the anchor is never moved to the instant it was asked about (A1).");
        (Now - moved).ShouldBe(remainder, "the remainder survives in the gap between the anchor and now.");
        result.NewState.Player.Energy.Energy.ShouldBe(10);
    }

    /// <summary>
    /// Randomised through <c>Apply</c>: N commands over sub-intervals summing to T leave exactly
    /// the banks and anchor that one command over T leaves. A saturated case cannot distinguish a
    /// correct accrual from one that discards its remainder, so spans are sampled against each
    /// case's own headroom and the discriminating count is asserted, not assumed. Seeded with a
    /// constant so a failure reproduces on every machine.
    /// </summary>
    [Fact]
    public void Many_small_advances_accrue_exactly_what_one_big_one_does()
    {
        var random = new Random(Seed);
        var quoted = new List<string>();
        var failures = 0;
        var discriminating = 0;
        var saturated = 0;

        for (var i = 0; i < Cases; i++)
        {
            var legendLevel = random.Next(1, 61);
            var max = EnergyMath.MaxEnergy(Shipped, legendLevel);
            var reserveCapacity = EnergyMath.ReserveCapacity(Shipped, legendLevel);
            var start = new EnergyBanks(random.Next(0, max + 1), random.Next(0, reserveCapacity + 1));

            var total = SampleSpan(random, start, max, reserveCapacity, saturating: i % 5 == 0);
            var cuts = Cuts(random, total);

            var snapshot = PlayerSnapshots.With(
                legendLevel: legendLevel,
                energy: start,
                energyAnchorUtc: SplitBase,
                lastAppliedAtUtc: SplitBase);

            var whole = Advance(snapshot, new[] { total });
            var split = Advance(snapshot, cuts);

            if (IsDiscriminating(start, whole.Banks, max, reserveCapacity))
            {
                discriminating++;
            }
            else if (whole.Banks.Energy >= max && whole.Banks.Reserve >= reserveCapacity)
            {
                saturated++;
            }

            if (split.Banks == whole.Banks && split.AnchorAdvance == whole.AnchorAdvance)
            {
                continue;
            }

            failures++;
            if (quoted.Count < QuotedFailures)
            {
                quoted.Add(Describe(legendLevel, start, total, cuts, split, whole));
            }
        }

        // A saturated pair absorbs any difference in what was accrued, so a suite of saturated
        // cases is a suite that passes under the defect — hence the floor below.
        discriminating.ShouldBeGreaterThan(
            Cases * 6 / 10,
            $"only {discriminating} of {Cases} randomised cases accrued something WITHOUT filling both " +
            "banks. The rest prove nothing about the remainder — either the span sampling has drifted, " +
            "or the catch-up is not accruing at all.");

        // One case in five is meant to reach the saturating regime and cover the Reserve cap under
        // splitting; break that branch and nothing else here would notice.
        saturated.ShouldBeGreaterThan(
            Cases / 10,
            $"only {saturated} of {Cases} randomised cases filled both banks, so the saturating branch " +
            "of the span sampling has stopped reaching the Reserve cap.");

        failures.ShouldBe(
            0,
            $"{failures} of {Cases} randomised splits disagreed with the single command over the same " +
            "span. N commands must leave exactly what one command leaves: if the anchor is advanced to " +
            "NowUtc rather than by wholeUnits × the regeneration interval, the remainder is discarded " +
            "once per command and an active player regenerates less than an idle one (A1). First " +
            "offenders:" + Environment.NewLine + string.Join(Environment.NewLine, quoted.Select(q => "  " + q)));
    }

    // ------------------------------------------------------------------ the clamp

    /// <summary>
    /// An accrual anchor persisted in the future (host clock skew) is clamped, not thrown on: the
    /// command returns a result, the player is neither charged nor paid, and the anchor stays put.
    /// The clamp lives in <c>AdvanceTime</c>, not <c>EnergyMath.Accrue</c>, which refuses a
    /// negative span deliberately so a persistence defect stays distinguishable from skew.
    /// </summary>
    [Fact]
    public void An_anchor_in_the_future_clamps_instead_of_throwing()
    {
        var future = Now.AddDays(1);

        var snapshot = PlayerSnapshots.With(
            energy: new EnergyBanks(7, 0),
            energyAnchorUtc: future,
            lastAppliedAtUtc: PlayerSnapshots.Midmorning,
            dailyPeriodStartUtc: PreviousGameDay,
            dailyCounters: PlayerSnapshots.Counters(("ad_caps", 3)));

        var result = Should.NotThrow(() => Apply(snapshot, Accepting));

        result.Accepted.ShouldBeTrue();
        result.Events.ShouldBeEmpty("a clamped span accrues nothing, so there is no 21 §8.3 row.");
        result.NewState.Player.Energy.ShouldBe(
            new EnergyBanks(7, 0), "a backwards clock costs the player nothing and grants them nothing.");
        result.NewState.Player.EnergyAnchorUtc.ShouldBe(
            future, "the anchor only ever moves forwards, and the accrual it would have moved was zero.");

        // Evidence the catch-up ran and clamped, rather than simply not running.
        result.NewState.Player.DailyPeriodStartUtc.ShouldBe(PlayerSnapshots.Wednesday);
        result.NewState.Player.DailyCount("ad_caps").ShouldBe(0);
    }

    // ------------------------------------------------------------------ the 05:00 UTC day

    /// <summary>
    /// A command sent inside the game day already in force leaves the day's counters exactly where
    /// they were. The guard is <c>&gt;=</c>, not <c>&gt;</c>, which delegates the equal-boundary
    /// case to <c>Player.ResetDailyCounters</c>' own no-op; a <c>&gt;</c> guard would shadow it and
    /// quietly make that defence unreachable.
    /// </summary>
    [Fact]
    public void A_command_inside_the_open_game_day_keeps_the_days_counters()
    {
        var result = Apply(
            PlayerSnapshots.With(
                energyAnchorUtc: Now - (10 * Interval),
                lastAppliedAtUtc: PlayerSnapshots.Midmorning,
                dailyPeriodStartUtc: PlayerSnapshots.Wednesday,
                dailyCounters: PlayerSnapshots.Counters(("ad_caps", 3)),
                weeklyPeriodStartUtc: PlayerSnapshots.Monday,
                weeklyCounters: PlayerSnapshots.Counters(("guild_quest_contributions", 5))),
            Accepting);

        result.Events.ShouldHaveSingleItem().ShouldBeOfType<CurrencyChanged>().Reason.ShouldBe(RegenReason);

        result.NewState.Player.DailyCount("ad_caps").ShouldBe(3, "the game day has not turned over.");
        result.NewState.Player.WeeklyCount("guild_quest_contributions").ShouldBe(5, "nor has the game week.");
        result.NewState.Player.DailyPeriodStartUtc.ShouldBe(PlayerSnapshots.Wednesday);
        result.NewState.Player.WeeklyPeriodStartUtc.ShouldBe(PlayerSnapshots.Monday);
    }

    [Fact]
    public void A_crossed_game_day_clears_the_daily_counters_and_records_the_boundary()
    {
        var result = Apply(
            PlayerSnapshots.With(
                lastAppliedAtUtc: PlayerSnapshots.Midmorning,
                dailyPeriodStartUtc: PreviousGameDay,
                dailyCounters: PlayerSnapshots.Counters(("ad_caps", 3), ("dungeon_entries", 1)),
                weeklyPeriodStartUtc: PlayerSnapshots.Monday,
                weeklyCounters: PlayerSnapshots.Counters(("guild_quest_contributions", 5))),
            Accepting);

        result.NewState.Player.DailyCounters.ShouldBeEmpty();
        result.NewState.Player.DailyPeriodStartUtc.ShouldBe(
            PlayerSnapshots.Wednesday, "the boundary now in force is the latest 05:00 UTC at or before NowUtc.");

        result.NewState.Player.WeeklyCount("guild_quest_contributions").ShouldBe(
            5, "a day boundary is not a week boundary (A2).");
        result.NewState.Player.WeeklyPeriodStartUtc.ShouldBe(PlayerSnapshots.Monday);
    }

    /// <summary>
    /// A host clock that went backwards neither throws out of <c>Apply</c> nor reopens a period
    /// the player has already been counted against: <c>AdvanceTime</c>'s <c>&gt;=</c> guard covers
    /// the backwards case, and the aggregate's no-op covers the equal one.
    /// </summary>
    [Fact]
    public void A_clock_that_went_backwards_neither_throws_nor_reopens_a_period()
    {
        var dayAhead = PlayerSnapshots.Wednesday.AddDays(1);
        var weekAhead = PlayerSnapshots.Monday.AddDays(7);

        weekAhead.DayOfWeek.ShouldBe(DayOfWeek.Monday, "the fixture must be a legal week boundary (A2).");

        var snapshot = PlayerSnapshots.With(
            energyAnchorUtc: Now - (10 * Interval),
            lastAppliedAtUtc: PlayerSnapshots.Midmorning,
            dailyPeriodStartUtc: dayAhead,
            dailyCounters: PlayerSnapshots.Counters(("ad_caps", 3)),
            weeklyPeriodStartUtc: weekAhead,
            weeklyCounters: PlayerSnapshots.Counters(("guild_quest_contributions", 5)));

        var result = Should.NotThrow(() => Apply(snapshot, Accepting));

        result.Accepted.ShouldBeTrue();
        result.Events.ShouldHaveSingleItem().ShouldBeOfType<CurrencyChanged>().Reason.ShouldBe(RegenReason);

        result.NewState.Player.DailyPeriodStartUtc.ShouldBe(dayAhead, "a period already in force is not reopened.");
        result.NewState.Player.WeeklyPeriodStartUtc.ShouldBe(weekAhead);
        result.NewState.Player.DailyCount("ad_caps").ShouldBe(3);
        result.NewState.Player.WeeklyCount("guild_quest_contributions").ShouldBe(5);

        // The rule the guard prevents from firing: an unguarded ResetDailyCounters on the same
        // aggregate and boundary throws here.
        Should.Throw<ArgumentOutOfRangeException>(
                  () => Worlds.Rehydrated(snapshot).ResetDailyCounters(PlayerSnapshots.Wednesday))
              .Message.ShouldMatchWildcard("*handing back every cap*");
    }

    /// <summary>
    /// 180 game days crossed offline are applied by the next command, in one call, with no job and
    /// no timer: the accrual, the daily boundary and the weekly one all move at once, in one step
    /// rather than a 180-iteration loop.
    /// </summary>
    [Fact]
    public void A_hundred_and_eighty_days_offline_are_applied_by_the_next_command()
    {
        var later = Now.AddDays(180);

        later.DayOfWeek.ShouldBe(
            DayOfWeek.Monday, "the fixture instant is checked against the calendar, not assumed (2027-02-08).");

        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Accepting,
            new WorldSlice(
                Worlds.Rehydrated(PlayerSnapshots.With(
                    lastAppliedAtUtc: PlayerSnapshots.Midmorning,
                    dailyPeriodStartUtc: PlayerSnapshots.Wednesday,
                    dailyCounters: PlayerSnapshots.Counters(("ad_caps", 3)),
                    weeklyPeriodStartUtc: PlayerSnapshots.Monday,
                    weeklyCounters: PlayerSnapshots.Counters(("guild_quest_contributions", 5)))),
                null),
            new Worlds.MetaFixtureCommand(),
            Worlds.Context with { NowUtc = later });

        var player = result.NewState.Player;

        player.DailyPeriodStartUtc.ShouldBe(new DateTimeOffset(2027, 2, 8, 5, 0, 0, TimeSpan.Zero));
        player.DailyCounters.ShouldBeEmpty();
        player.WeeklyPeriodStartUtc.ShouldBe(new DateTimeOffset(2027, 2, 8, 5, 0, 0, TimeSpan.Zero));
        player.WeeklyCounters.ShouldBeEmpty();

        player.Energy.ShouldBe(
            new EnergyBanks(EnergyMath.MaxEnergy(Shipped, 1), EnergyMath.ReserveCapacity(Shipped, 1)),
            "180 days is 64,800 points at one per four minutes; 28 C2's cascade fills the bar, then the Reserve.");

        player.EnergyAnchorUtc.ShouldBe(
            PlayerSnapshots.Midmorning.AddDays(180),
            "180 days is a whole number of four-minute units, so the anchor lands exactly there (A1).");

        result.Events.ShouldHaveSingleItem().ShouldBeOfType<CurrencyChanged>().Delta.ShouldBe(
            (long)EnergyMath.MaxEnergy(Shipped, 1) + EnergyMath.ReserveCapacity(Shipped, 1),
            "overflow into the Reserve is a movement within ONE currency, so 21 §8.3 sees one row.");
    }

    // ------------------------------------------------------------------ the Monday game week

    /// <summary>
    /// A crossed game week resets on the Monday at 05:00 UTC, not on the seventh day after the
    /// last reset. The weekday is asserted, not just the date: <c>Player.ResetWeeklyCounters</c>
    /// refuses any other weekday.
    /// </summary>
    [Fact]
    public void A_crossed_game_week_resets_on_the_Monday_at_0500_UTC()
    {
        var previousWeek = PlayerSnapshots.Monday.AddDays(-7);

        previousWeek.DayOfWeek.ShouldBe(DayOfWeek.Monday, "the fixture must be a real Monday (2026-08-03).");

        var result = Apply(
            PlayerSnapshots.With(
                lastAppliedAtUtc: PlayerSnapshots.Midmorning,
                dailyPeriodStartUtc: PlayerSnapshots.Wednesday,
                weeklyPeriodStartUtc: previousWeek,
                weeklyCounters: PlayerSnapshots.Counters(("guild_quest_contributions", 5))),
            Accepting);

        var boundary = result.NewState.Player.WeeklyPeriodStartUtc;

        boundary.ShouldBe(PlayerSnapshots.Monday);
        boundary.DayOfWeek.ShouldBe(DayOfWeek.Monday);
        boundary.TimeOfDay.ShouldBe(TimeSpan.FromHours(5));
        boundary.Offset.ShouldBe(TimeSpan.Zero);

        result.NewState.Player.WeeklyCounters.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ rejection

    /// <summary>
    /// A rejected command discards the catch-up with the working clone, and that costs the player
    /// nothing: the next accepted command accrues the same span from the same anchors. Correct only
    /// because the catch-up is idempotent in elapsed time — every boundary it moves is derived from
    /// <c>NowUtc</c> and the stored anchors, never consumed.
    /// </summary>
    [Fact]
    public void A_rejected_command_discards_the_catch_up_and_costs_the_player_nothing()
    {
        var anchor = Now - (10 * Interval);
        var snapshot = PlayerSnapshots.With(
            energyAnchorUtc: anchor,
            lastAppliedAtUtc: PlayerSnapshots.Midmorning,
            dailyPeriodStartUtc: PreviousGameDay,
            dailyCounters: PlayerSnapshots.Counters(("ad_caps", 3)));

        var state = new WorldSlice(Worlds.Rehydrated(snapshot), null);

        var refused = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.MetaTable((_, _) => HandlerResult.Reject(RejectionReason.COOLDOWN_ACTIVE)),
            state,
            new Worlds.MetaFixtureCommand(),
            Worlds.Context);

        refused.Accepted.ShouldBeFalse();
        refused.NewState.ShouldBeSameAs(state, "P4: a rejection returns the caller's own slice.");

        state.Player.Energy.ShouldBe(new EnergyBanks(0, 0), "the catch-up was discarded with the clone.");
        state.Player.EnergyAnchorUtc.ShouldBe(anchor);
        state.Player.DailyPeriodStartUtc.ShouldBe(PreviousGameDay);
        state.Player.DailyCount("ad_caps").ShouldBe(3);

        // Nothing was lost by discarding it: the next accepted command at the same instant applies
        // exactly what the refused one did.
        var accepted = SlayIdleRepeat.Core.GameRules.Execute(
            Accepting, state, new Worlds.MetaFixtureCommand(), Worlds.Context);

        accepted.NewState.Player.Energy.ShouldBe(new EnergyBanks(10, 0));
        accepted.NewState.Player.EnergyAnchorUtc.ShouldBe(anchor + (10 * Interval));
        accepted.NewState.Player.DailyPeriodStartUtc.ShouldBe(PlayerSnapshots.Wednesday);
        accepted.NewState.Player.DailyCount("ad_caps").ShouldBe(0);
    }

    /// <summary>A rejected command carries no events, catch-up's included.</summary>
    [Fact]
    public void A_rejected_command_carries_no_catch_up_events()
    {
        var snapshot = Snapshot(anchorAgo: 10 * Interval);
        var state = new WorldSlice(Worlds.Rehydrated(snapshot), null);

        var refused = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.MetaTable((_, _) => HandlerResult.Reject(RejectionReason.CAP_REACHED)),
            state,
            new Worlds.MetaFixtureCommand(),
            Worlds.Context);

        refused.Events.ShouldBeEmpty();
        refused.NewState.ShouldBeSameAs(state);
        state.Player.Energy.ShouldBe(new EnergyBanks(0, 0));

        // Contrast: there was a catch-up event to carry.
        Apply(snapshot, Accepting).Events
            .ShouldHaveSingleItem()
            .ShouldBeOfType<CurrencyChanged>()
            .Reason.ShouldBe(RegenReason);
    }

    // ------------------------------------------------------------------ the run's TTL

    /// <summary>
    /// The catch-up never writes <c>Run.LastAppliedAtUtc</c>: the run's sliding TTL is measured
    /// from the last accepted command to that run, not from an unrelated meta command that happened
    /// to touch the player's clock.
    /// </summary>
    [Fact]
    public void Catch_up_does_not_slide_the_runs_TTL()
    {
        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Accepting,
            new WorldSlice(Worlds.Rehydrated(Snapshot(anchorAgo: 10 * Interval)), Worlds.NewRun()),
            new Worlds.MetaFixtureCommand(),
            Worlds.Context);

        result.Events.ShouldHaveSingleItem().ShouldBeOfType<CurrencyChanged>().Reason.ShouldBe(RegenReason);

        result.NewState.Run!.LastAppliedAtUtc.ShouldBe(
            RunSnapshots.Midmorning,
            "14 §16.3's run TTL slides only on a CommandKind.Run command, and never from the catch-up.");

        result.NewState.Player.LastAppliedAtUtc.ShouldBe(
            Now, "…while the player's anchor advances as it does on every accepted command.");
    }

    // ------------------------------------------------------------------ fixtures

    /// <summary>A table with one accepting meta handler that produces no events of its own.</summary>
    private static readonly CommandDispatch Accepting = Worlds.MetaTable((_, _) => HandlerResult.Accept());

    /// <summary>Fixed, so a failure of the randomised property reproduces exactly.</summary>
    private const int Seed = 20_260_812;

    /// <summary>
    /// How many randomised splits to check. Smaller than <c>EnergyAccrualPropertyTests</c>' 2,000
    /// because every case here is up to thirteen <c>Apply</c> round trips, each of which snapshots
    /// and re-validates the aggregate.
    /// </summary>
    private const int Cases = 200;

    /// <summary>How many offending splits to quote before the message stops being readable.</summary>
    private const int QuotedFailures = 5;

    /// <summary>
    /// 2026-08-12T05:00Z — where the randomised split runs start. A 05:00 UTC boundary, so the day
    /// and week resets a long span crosses are legal ones.
    /// </summary>
    private static readonly DateTimeOffset SplitBase = PlayerSnapshots.Wednesday;

    /// <summary>A player row with the accrual anchor set <paramref name="anchorAgo"/> before <see cref="Now"/>.</summary>
    private static PlayerSnapshot Snapshot(
        TimeSpan anchorAgo, EnergyBanks? energy = null, DateTimeOffset? dailyPeriodStartUtc = null) =>
        PlayerSnapshots.With(
            energy: energy,
            energyAnchorUtc: Now - anchorAgo,
            lastAppliedAtUtc: PlayerSnapshots.Midmorning,
            dailyPeriodStartUtc: dailyPeriodStartUtc);

    /// <summary>Applies one meta command at <see cref="Now"/> over a player-only slice.</summary>
    private static CommandResult Apply(PlayerSnapshot snapshot, CommandDispatch table) =>
        SlayIdleRepeat.Core.GameRules.Execute(
            table,
            new WorldSlice(Worlds.Rehydrated(snapshot), null),
            new Worlds.MetaFixtureCommand(),
            Worlds.Context);

    /// <summary>
    /// Applies one command per offset, in order, and answers what the sequence left behind.
    /// </summary>
    /// <param name="snapshot">The starting row. Its anchor and applied instant are <see cref="SplitBase"/>.</param>
    /// <param name="offsets">Ascending offsets from <see cref="SplitBase"/>, each one command.</param>
    private static (EnergyBanks Banks, TimeSpan AnchorAdvance) Advance(
        PlayerSnapshot snapshot, IReadOnlyList<TimeSpan> offsets)
    {
        var state = new WorldSlice(Worlds.Rehydrated(snapshot), null);

        foreach (var offset in offsets)
        {
            state = SlayIdleRepeat.Core.GameRules.Execute(
                Accepting,
                state,
                new Worlds.MetaFixtureCommand(),
                Worlds.Context with { NowUtc = SplitBase + offset }).NewState;
        }

        return (state.Player.Energy, state.Player.EnergyAnchorUtc - SplitBase);
    }

    /// <summary>
    /// A span to accrue over. Four cases in five land in the band where the two banks still have
    /// headroom — the only band a discarded remainder is visible in at all — and the fifth runs
    /// anywhere up to forty days, so the saturating regime and the Reserve cap are covered.
    /// </summary>
    private static TimeSpan SampleSpan(
        Random random, EnergyBanks start, int max, int reserveCapacity, bool saturating)
    {
        if (saturating)
        {
            return TimeSpan.FromTicks(random.NextInt64(0, TimeSpan.FromDays(40).Ticks));
        }

        // One unit past the headroom, so the boundary at which the banks fill is itself sampled.
        var headroom = max - start.Energy + reserveCapacity - start.Reserve + 1;

        return TimeSpan.FromTicks(random.NextInt64(0, headroom * Interval.Ticks));
    }

    /// <summary>
    /// True when the case can tell a correct accrual from one that discards its remainder: it
    /// accrued something, and it did <em>not</em> end with both banks full — a full pair absorbs any
    /// difference and reports the same answer either way.
    /// </summary>
    private static bool IsDiscriminating(
        EnergyBanks start, EnergyBanks after, int max, int reserveCapacity) =>
        (after.Energy > start.Energy || after.Reserve > start.Reserve) &&
        !(after.Energy >= max && after.Reserve >= reserveCapacity);

    /// <summary>Ascending offsets that cut <paramref name="total"/> into 2..12 commands.</summary>
    private static TimeSpan[] Cuts(Random random, TimeSpan total)
    {
        var count = random.Next(2, 13);
        var cuts = new TimeSpan[count];

        for (var i = 0; i < count - 1; i++)
        {
            cuts[i] = TimeSpan.FromTicks(total.Ticks == 0 ? 0 : random.NextInt64(0, total.Ticks + 1));
        }

        cuts[count - 1] = total;
        Array.Sort(cuts);

        return cuts;
    }

    private static string Describe(
        int legendLevel,
        EnergyBanks start,
        TimeSpan total,
        IReadOnlyList<TimeSpan> cuts,
        (EnergyBanks Banks, TimeSpan AnchorAdvance) split,
        (EnergyBanks Banks, TimeSpan AnchorAdvance) whole) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"level {legendLevel} from ({start.Energy}, {start.Reserve}) over {Render(total)} " +
            $"cut at [{string.Join(", ", cuts.Select(Render))}]: " +
            $"split gives ({split.Banks.Energy}, {split.Banks.Reserve}) anchor {Render(split.AnchorAdvance)}, " +
            $"whole gives ({whole.Banks.Energy}, {whole.Banks.Reserve}) anchor {Render(whole.AnchorAdvance)}");

    private static string Render(TimeSpan span) => span.ToString("c", CultureInfo.InvariantCulture);
}
