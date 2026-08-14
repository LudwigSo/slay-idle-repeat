using Shouldly;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Handlers;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// 🔒 `30` §2.3 — <em>"succeeds as a no-op. <b>Its daily effects are idempotent per game day.</b>"</em>
/// </summary>
/// <remarks>
/// 🔴 A handler that re-grants on every command of the day and one that grants exactly once are
/// <b>indistinguishable</b> to any test sending one command per day — and that shape would leave a
/// free player receiving a full Energy refill several times an hour, against `10` §3.2's whole
/// free-player budget. So these tests send <em>many</em> commands inside one game day.
/// <para>
/// 🔒 Measured, not claimed (steering <b>S1</b>): with the early-return deleted, five of the seven
/// go red. The two that stay green are about a <b>new</b> game day — they assert the counter is
/// <em>cleared</em>, which is what stops a "fix" that simply never grants at all.
/// </para>
/// <para>
/// 🔒 The keying is legal only because M1-08's catch-up clears the daily counters <em>before</em> the
/// handler runs, so the handler sets the marker itself, after the catch-up, in the same command —
/// see <see cref="The_marker_is_set_by_the_handler_and_cleared_by_the_day_boundary"/>.
/// </para>
/// </remarks>
public sealed class BeginSessionIdempotenceTests
{
/// <summary>
/// 🔒 The headline: a second <c>BEGIN_SESSION</c> in the same game day grants <b>nothing</b>.
/// </summary>
/// <remarks>
/// The second command is applied to the <b>first's result</b> — re-sending against the original
/// slice would be two first commands, the one shape that cannot see this defect.
/// </remarks>
    [Fact]
    public void A_second_BEGIN_SESSION_in_the_same_game_day_grants_nothing()
    {
        var first = BeginSessions.Send(BeginSessions.Slice());

        first.Accepted.ShouldBeTrue();
        first.NewState.Player.Energy.ShouldBe(
            new EnergyBanks(BeginSessions.Tuning.MaxEnergyAt(1), 0),
            "the first BEGIN_SESSION of the game day pays 10 §3.1's free refill, to full.");

        var second = BeginSessions.Send(first.NewState, BeginSessions.Morning.AddMinutes(1));

        second.Accepted.ShouldBeTrue("30 §2.3: the repeat SUCCEEDS as a no-op, it is not refused.");
        second.NewState.Player.Energy.ShouldBe(
            first.NewState.Player.Energy,
            "the second call of the game day grants nothing — 30 §2.3's daily effects are idempotent " +
            "per game day, and a refill paid twice is 10 §3.2's free-player budget doubled.");

        // 🔴 S1 — THE ASSERTION ABOVE CANNOT FAIL ON ITS OWN, and this one is why the test is here.
        // Measured by mutation: deleting the early-return in BeginSession.Handle left this test
        // GREEN, because the first call fills the bar and EnergyMath.RefillToFull is deficit-only —
        // so the second grant is zero and the two banks compare equal. What the broken handler DID
        // produce is the row below: `CurrencyChanged { Sequence = 1, Id = ENERGY, Delta = 0,
        // Reason = daily_free_refill }`. A zero-delta refill is invisible in the state and loud in
        // 21 §8.3's income_attribution.csv, so the event list is where this claim is decidable.
        second.Events.ShouldBeEmpty(
            "…and it grants nothing OBSERVABLY: a no-op publishes no 30 §7 row. Without this line the " +
            "test's name promises more than it delivers — a handler with no idempotence at all passes " +
            "the energy comparison above, because a refill to a full bar is a grant of zero.");
    }

/// <summary>
/// 🔒 <b>Ten</b> commands inside one game day pay <b>one</b> refill, spread across the day rather
/// than sent in one instant.
/// </summary>
/// <remarks>
/// ⚠️ The clock advances between commands and stays inside the 05:00 UTC day, ruling out an
/// implementation keyed on the <em>instant</em>, or on <c>LastAppliedAtUtc</c> not having moved.
/// </remarks>
    [Fact]
    public void Ten_commands_in_one_game_day_pay_one_refill()
    {
        var state = BeginSessions.Slice();
        var refills = 0;

        // Ten commands at one-hour intervals from 09:41, so the last is at 18:41 — still before the
        // next 05:00 UTC boundary, and therefore all ten are in ONE game day.
        for (var hour = 0; hour < 10; hour++)
        {
            var result = BeginSessions.Send(state, BeginSessions.Morning.AddHours(hour));

            result.Accepted.ShouldBeTrue($"command {hour} must succeed; 30 §2.3 refuses none of them.");
            refills += result.Events.Count(e => IsRefill(e));
            state = result.NewState;
        }

        refills.ShouldBe(
            1,
            "10 §3.1 authors the free refill as '1/day' and 30 §2.3 makes BEGIN_SESSION's daily " +
            "effects idempotent per game day. Ten is what tells 'once per day' from 'once per " +
            "command' — a suite that sent one command per day would report success on both.");

        state.Player.DailyCount(BeginSession.DailyRunCounter).ShouldBe(
            1,
            "…and the marker counts the DAY, not the calls. Ten would mean the handler ran its daily " +
            "block ten times and the counter was merely being incremented alongside it.");
    }

/// <summary>
/// 🔒 The repeat call is a no-op <b>in the event list too</b>, not merely in the state.
/// </summary>
/// <remarks>
/// A no-op that still published a zero-delta refill row would put a "free refill" line in front of
/// the player several times a day and a phantom row in `21` §8.3's <c>income_attribution.csv</c>.
/// ⚠️ The opposite ruling from <b>A6</b>'s zero-delta <c>energy_regen</c> row, and deliberately:
/// there the accrual ran and moved the anchor, here the daily block did not run at all.
/// </remarks>
    [Fact]
    public void The_second_call_of_the_day_produces_no_events_at_all()
    {
        var first = BeginSessions.Send(BeginSessions.Slice());
        var second = BeginSessions.Send(first.NewState, BeginSessions.Morning.AddMinutes(1));

        first.Events.ShouldNotBeEmpty("the first call pays the refill, so it has a row to publish.");
        second.Events.ShouldBeEmpty(
            "a no-op moved nothing, so there is no 21 §8.3 row to write and no 14 §2.4 beat to play.");
    }

/// <summary>
/// 🔒 The calendar advances <b>at most once per game day</b> (`19` G), even when the player claims
/// the newly opened day and re-sends <c>BEGIN_SESSION</c>.
/// </summary>
/// <remarks>
/// 🔴 The re-claim is the whole test. The natural version — advance once, re-send, assert the day did
/// not move — stayed green with the early-return deleted, because the advance clears
/// <c>LoginCalendarDayClaimed</c> and `19` G's own pause rule stops the second advance regardless.
/// That proves the <em>calendar's</em> guard and says nothing about the <em>day's</em>.
/// <para>
/// A player whose calendar day is open <b>and claimed</b> inside a game day the handler already ran
/// is the only state where the two guards disagree — reachable the moment <c>CLAIM_CALENDAR</c>
/// lands (M4-09): claim in the morning, re-open the app in the evening.
/// </para>
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
            4,
            "19 G advances 'at most once per game day' — and here 19 G's PAUSE also stops it, because " +
            "day 4 is unclaimed. Two guards, one rule; the next assertion is the one that separates " +
            "them.");

        // 🔒 The player claims day 4 later the same game day — M4-09's CLAIM_CALENDAR, stood in for by
        // the persisted row — and re-sends. The day's marker is already set, so only the per-game-day
        // idempotence can refuse this one.
        var claimedAgain = BeginSessions.Slice(
            loginCalendarDay: 4,
            loginCalendarDayClaimed: true,
            dailyCounters: PlayerSnapshots.Counters((BeginSession.DailyRunCounter, 1)));

        var third = BeginSessions.Send(claimedAgain, BeginSessions.Morning.AddHours(9));

        third.NewState.Player.LoginCalendarDay.ShouldBe(
            4,
            "19 G: 'it advances AT MOST ONCE PER GAME DAY'. A player who claims in the morning and " +
            "re-opens the app in the evening must not walk two rows of the 28-day table in one day — " +
            "which would let a determined player finish the cycle in a fraction of the time and " +
            "collect the day-28 S-tier chest four weeks early.");
        third.NewState.Player.LoginCalendarDayClaimed.ShouldBeTrue(
            "…and the claim they made is still theirs to be paid for, untouched.");
    }

/// <summary>
/// 🔒 The player-visible shape of the defect: Energy <b>spent</b> during the day is not topped back
/// up by re-sending <c>BEGIN_SESSION</c>.
/// </summary>
/// <remarks>
/// The four tests above all pass against a handler that re-granted only on a deficit, because they
/// leave the bar full and <c>EnergyMath.RefillToFull</c> is deficit-only. This fixture opens
/// <b>below</b> maximum on the second call — where a real player is after a run.
/// </remarks>
    [Fact]
    public void Energy_spent_during_the_day_is_not_topped_back_up()
    {
        var max = BeginSessions.Tuning.MaxEnergyAt(1);
        var first = BeginSessions.Send(BeginSessions.Slice());

        first.NewState.Player.Energy.ShouldBe(new EnergyBanks(max, 0));

        // The player spends most of the tank on runs. Modelled by rehydrating the same player from a
        // row with the same calendar and the same daily counter and a drained bar, because M1 has no
        // command that spends Energy — START_RUN is deferred to M3-15.
        var spent = BeginSessions.Slice(
            energy: new EnergyBanks(4, 0),
            dailyCounters: PlayerSnapshots.Counters((BeginSession.DailyRunCounter, 1)));

        // ⚠️ Two minutes later, INSIDE the regeneration interval. Six hours later would accrue 90
        // Energy and the exact-banks assertion below would be measuring the catch-up: the first draft
        // of this test did exactly that, expected 4, found 94, and would have been "fixed" by
        // loosening the assertion — which is how a test stops being about the thing it names.
        var second = BeginSessions.Send(spent, BeginSessions.Morning.AddMinutes(2));

        second.NewState.Player.Energy.ShouldBe(
            new EnergyBanks(4, 0),
            "the day's refill was already taken, so re-sending BEGIN_SESSION after spending must NOT " +
            "top the bar back up. This is the defect as a player would exploit it: 10 §3's Energy " +
            "paces the whole game, and a refill on demand removes the pacing entirely.");
        second.Events.ShouldBeEmpty("nothing moved, so nothing is attributed.");
    }

    // ------------------------------------------------------------------ the other direction: a new day

/// <summary>
/// 🔒 A <c>BEGIN_SESSION</c> on the <b>next</b> game day grants again — "idempotent per game day" is
/// not "granted once ever".
/// </summary>
/// <remarks>
/// ⚠️ The test that stays green if idempotence is removed, and that is why it is here: without it the
/// file could be satisfied by a handler that granted <em>nothing</em>.
/// </remarks>
    [Fact]
    public void A_BEGIN_SESSION_on_the_next_game_day_grants_again()
    {
        var max = BeginSessions.Tuning.MaxEnergyAt(1);
        var first = BeginSessions.Send(BeginSessions.Slice());

        first.NewState.Player.Energy.ShouldBe(new EnergyBanks(max, 0));

        // 24 hours later — past the 05:00 UTC boundary, so a NEW game day, and the catch-up clears
        // the marker on the way in.
        var nextDay = BeginSessions.Send(first.NewState, Worlds.NextDay(BeginSessions.Morning));

        // 🔴 S1 — COUNTED BY REASON, not by "the list is not empty". Twenty-four hours is 360
        // regeneration intervals, so GameRules.AdvanceTime publishes an energy_regen row whatever
        // this handler does — A6 is explicit that the anchor moves and the row goes out even on a
        // full tank. `ShouldNotBeEmpty` was therefore true of a handler that granted NOTHING, which
        // is precisely the case this test exists to rule out. Third instance of the regen-interval
        // trap on this branch.
        nextDay.Events.Count(IsRefill).ShouldBe(
            1,
            "a new game day pays the free refill again — 10 §3.1 authors it as daily, not as a " +
            "one-off, and 19 G's calendar runs a 28-day cycle that a once-ever grant could never " +
            "walk.");
        nextDay.NewState.Player.DailyCount(BeginSession.DailyRunCounter).ShouldBe(
            1,
            "the marker was cleared at the boundary and set again, so it counts THIS day's calls.");
    }

/// <summary>
/// 🔒 The mechanism itself: the marker is written by the handler and cleared by the 05:00 UTC
/// boundary.
/// </summary>
/// <remarks>
/// Asserted rather than inferred because catch-up clearing the daily counters before the handler
/// runs is the fact that makes this key legal, and that is invisible in the behavioural tests above.
/// <para>
/// ⚠️ The "preserved" half is driven by a <b>non-BEGIN_SESSION</b> command too, because the catch-up
/// runs on every command and a counter cleared by another command's catch-up would re-open the day.
/// </para>
/// </remarks>
    [Fact]
    public void The_marker_is_set_by_the_handler_and_cleared_by_the_day_boundary()
    {
        var first = BeginSessions.Send(BeginSessions.Slice());

        first.NewState.Player.DailyCount(BeginSession.DailyRunCounter).ShouldBe(
            1, "the handler sets the marker AFTER the catch-up, in the same command.");

        // Another command later the same day: its catch-up must NOT clear the marker.
        var interleaved = GameRules.Execute(
            Worlds.MetaTable((_, _) => HandlerResult.Accept()),
            first.NewState,
            new Worlds.MetaFixtureCommand(),
            Worlds.Context with { NowUtc = BeginSessions.Morning.AddHours(3) });

        interleaved.NewState.Player.DailyCount(BeginSession.DailyRunCounter).ShouldBe(
            1,
            "Player.ResetDailyCounters is a deliberate NO-OP on the boundary already in force " +
            "(M1-04's counter-wipe fix), so an unrelated command must leave the day's marker " +
            "standing. If it did not, every command would re-open the day and the refill would be " +
            "payable again immediately.");

        // And a command on the next game day must clear it.
        var nextDay = GameRules.Execute(
            Worlds.MetaTable((_, _) => HandlerResult.Accept()),
            interleaved.NewState,
            new Worlds.MetaFixtureCommand(),
            Worlds.Context with { NowUtc = Worlds.NextDay(BeginSessions.Morning) });

        nextDay.NewState.Player.DailyCount(BeginSession.DailyRunCounter).ShouldBe(
            0,
            "crossing 05:00 UTC clears the daily counters, which is what makes the marker mean 'this " +
            "game day' rather than 'ever'. 🔒 It is cleared by the CATCH-UP of an unrelated command — " +
            "so the next BEGIN_SESSION reads zero however the player got there, which is 30 §2.3's " +
            "'correctness never depends on BEGIN_SESSION arriving'.");
    }

/// <summary>
/// 🔒 A host clock that jumps <b>forward</b> across 05:00 UTC pays the player <b>early</b>, never
/// <b>twice</b> — and the day it was wrong about is a no-op when real time reaches it.
/// </summary>
/// <remarks>
/// 🔒 What keeps the second payment away is not a throw: the skew pins <c>DailyPeriodStartUtc</c>
/// forward and <c>AdvanceTime</c>'s reset guard is <c>&gt;=</c>, so a corrected clock computes an
/// <em>earlier</em> boundary and clears nothing. Both facts are asserted, because "accepted" alone
/// would be weaker than the throwing assertion this replaced.
/// <para>
/// The cost is one day's grants received early; the loop recovers at the next boundary.
/// </para>
/// <para>
/// ⚠️ Not a property of the idempotence key — what the skew pins forward is
/// <c>Player.DailyPeriodStartUtc</c>, so a persisted "game day I last ran on" behaves identically, at
/// the cost of a <c>SchemaVersion</c> field. Asserted so whoever revisits the key sees the
/// alternative buys nothing.
/// </para>
/// </remarks>
    [Fact]
    public void A_forward_clock_jump_pays_early_and_never_twice()
    {
        // The clock is a day fast: the command lands in what it believes is the NEXT game day.
        var skewed = BeginSessions.Send(
            BeginSessions.Slice(), Worlds.NextDay(BeginSessions.Morning));

        skewed.Events.Count(IsRefill).ShouldBe(1, "the skewed command pays the day it thinks it is in.");
        skewed.NewState.Player.DailyPeriodStartUtc.ShouldBe(
            Worlds.NextDay(BeginSessions.Today),
            "…and the boundary it stored is the later one, which is what pins the day forward.");

        // 🔒 The operator corrects the clock. SETTLED IN M1-12, the way this test's own remark asked
        // for: it returns a RESULT. Until then this leg asserted an ArgumentOutOfRangeException out
        // of Apply and cited its own carried-forward note saying that violated 30 §2.1's P3 —
        // M1-09 pinned the behaviour it had rather than the behaviour the ruling required, which is
        // exactly the "whichever test was written first settles a ruling nobody made" that M1-11
        // declined to do by making VirtualClock forward-only.
        var corrected = BeginSessions.Send(skewed.NewState, BeginSessions.Morning.AddMinutes(1));

        corrected.Accepted.ShouldBeTrue(
            "30 §2.1's P3: every command on every state returns a result. GameRules.MarkApplied now " +
            "floors the instant it hands the aggregates, the same shape M1-08 gave the energy span " +
            "and the two reset guards. The aggregates still REFUSE a backwards anchor — see " +
            "GameRulesBackwardsClockTests for why the invariant stays in the model and the clamp " +
            "lives in Apply.");

        corrected.Events.Count(IsRefill).ShouldBe(
            0,
            "🔒 and the correction pays NOTHING. This is the leg that made the old throw look safe: " +
            "the skewed command already set this game day's marker, and the corrected clock does not " +
            "clear it — GameRules.AdvanceTime's reset guard is `dayStart >= DailyPeriodStartUtc`, and " +
            "a boundary pinned forward by the skew is later than the one a corrected clock computes. " +
            "Accepting the command is therefore not a second faucet.");

        corrected.NewState.Player.DailyPeriodStartUtc.ShouldBe(
            Worlds.NextDay(BeginSessions.Today),
            "…and the boundary stays where the skew put it, rather than being walked backwards. " +
            "If this regressed to Today, the next command would re-open the day and the refill " +
            "would be payable a second time — which is the outcome the throw used to prevent by " +
            "refusing to run at all.");

        corrected.NewState.Player.LastAppliedAtUtc.ShouldBe(
            skewed.NewState.Player.LastAppliedAtUtc,
            "the anchor is FLOORED at the stored instant, not moved backwards: 14 §16.3 measures the " +
            "run TTL from it, so skew must neither hold a run open nor expire one early.");

        // Real time reaches the day the skewed command already claimed.
        //
        // 🔒 Continued from `corrected`, not from `skewed`. Before M1-12 it had to be `skewed` —
        // the correction threw, so there was no state to carry forward — and leaving it there would
        // make the corrected command a dead end that this test's headline claim never passes
        // through. The chain is now skew → correction → catch-up → recovery, end to end.
        var caughtUp = BeginSessions.Send(
            corrected.NewState, Worlds.NextDay(BeginSessions.Morning).AddHours(1));

        caughtUp.Events.Count(IsRefill).ShouldBe(
            0,
            "🔒 THE DIRECTION THAT MATTERS: this day's grants were already paid by the skewed command, " +
            "so the player receives nothing now. Paid EARLY, never TWICE — 10 §3's Energy paces the " +
            "whole game, and a host with drifting clocks would otherwise be an Energy faucet.");

        // …and the day after that recovers, so the cost is bounded rather than permanent.
        var recovered = BeginSessions.Send(
            caughtUp.NewState, Worlds.NextDay(Worlds.NextDay(BeginSessions.Morning)));

        recovered.Events.Count(IsRefill).ShouldBe(
            1,
            "recovery is automatic on the next boundary. If this were 0 the skew would have stopped " +
            "the daily loop permanently, which is a defect rather than a bounded cost.");
    }

    /// <summary>Whether an event is the daily free refill (rather than a regeneration accrual).</summary>
    /// <remarks>
    /// 🔒 Matched on the <c>Reason</c>, not on the event <em>type</em>: both are
    /// <c>CurrencyChanged</c> for <c>ENERGY</c>, and `21` §8.3's whole report is grouped by this
    /// column — so a test that counted <c>CurrencyChanged</c>s would count an <c>energy_regen</c> row
    /// as a refill and would keep passing if the two tokens were merged.
    /// </remarks>
    private static bool IsRefill(DomainEvent produced) =>
        produced is CurrencyChanged { Id: CurrencyId.ENERGY } change &&
        string.Equals(change.Reason, BeginSession.DailyRefillReason, StringComparison.Ordinal);
}
