using Shouldly;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Handlers;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// 🔒 `30` §2.3 — <em>"otherwise: succeeds as a no-op. <b>Its daily effects are idempotent per game
/// day.</b>"</em>
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>The failure mode this file exists to make impossible, stated first because every test below
/// is shaped by it.</b> A handler that re-grants on every command of the day and one that grants
/// exactly once are <b>indistinguishable</b> to any test that sends one command per day. That is not
/// a hypothetical: it is the natural way to write this suite, it would leave a free player receiving
/// a full Energy refill several times an hour, and `10` §3.2's whole free-player budget rests on the
/// refill being <b>1/day</b>. So the tests here send <em>many</em> commands inside one game day, and
/// each one names what it would fail on.
/// </para>
/// <para>
/// 🔒 <b>Which of these fail if idempotence is removed — MEASURED, not claimed</b> (steering
/// <b>S1</b>). The early-return in <c>BeginSession.Handle</c> was deleted, the suite run, the output
/// recorded, and the mutation reverted. <b>Five of the seven</b> go red:
/// <see cref="A_second_BEGIN_SESSION_in_the_same_game_day_grants_nothing"/>,
/// <see cref="Ten_commands_in_one_game_day_pay_one_refill"/> (<c>refills should be 1 but was 10</c>),
/// <see cref="The_second_call_of_the_day_produces_no_events_at_all"/>,
/// <see cref="A_repeat_call_does_not_advance_the_calendar_a_second_time"/> and
/// <see cref="Energy_spent_during_the_day_is_not_topped_back_up"/> (<c>should be EnergyBanks { 4
/// (+0) } but was EnergyBanks { 120 (+0) }</c>) — the last being the defect as a player would exploit
/// it rather than as a counter.
/// </para>
/// <para>
/// 🔴 <b>Two of those five only bite because the first measurement caught them not biting.</b> The
/// original <see cref="A_second_BEGIN_SESSION_in_the_same_game_day_grants_nothing"/> compared the two
/// Energy banks and stayed <b>green</b> under the mutation — the first call fills the bar and
/// <c>EnergyMath.RefillToFull</c> is deficit-only, so the second grant is <em>zero</em> and the banks
/// compare equal. The original
/// <see cref="A_repeat_call_does_not_advance_the_calendar_a_second_time"/> stayed green too, because
/// `19` G's own pause rule stops the second advance whether or not idempotence exists. Both now carry
/// the assertion that separates them, and each says so at the line. <b>This is what a suite looks
/// like before S1 is applied to it: two tests whose names were exactly right and whose assertions
/// were true of the defect.</b>
/// </para>
/// <para>
/// The two that stay green are the ones about a <b>new</b> game day, and that asymmetry is the point:
/// they assert the counter is <em>cleared</em>, which is the opposite direction and is what stops a
/// "fix" that simply never grants at all.
/// </para>
/// <para>
/// 🔒 <b>The keying is not arbitrary and the tests know it.</b> M1-08's catch-up <b>clears the daily
/// counters before the handler runs</b>, so the counter this keys on is only usable because the
/// handler sets it <em>itself</em>, after the catch-up, in the same command —
/// <see cref="The_marker_is_set_by_the_handler_and_cleared_by_the_day_boundary"/> asserts both halves
/// of that directly rather than leaving it inferred from behaviour.
/// </para>
/// </remarks>
public sealed class BeginSessionIdempotenceTests
{
    /// <summary>
    /// 🔒 The headline: a second <c>BEGIN_SESSION</c> in the same game day grants <b>nothing</b>.
    /// </summary>
    /// <remarks>
    /// Driven through <c>GameRules.Apply</c> over the production table, and the second command is
    /// applied to the <b>first's result</b> — re-sending against the original slice would be two
    /// first commands, which is exactly the shape that cannot see this defect.
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
    /// 🔒 <b>Ten</b> commands inside one game day pay <b>one</b> refill, and the intermediate ones
    /// are spread across the day rather than sent in one instant.
    /// </summary>
    /// <remarks>
    /// ⚠️ The clock advances between commands and stays inside the 05:00 UTC day, which is what makes
    /// this different from the pair above: it rules out an implementation that keyed idempotence on
    /// the <em>instant</em> rather than on the game day, and one that keyed it on
    /// <c>LastAppliedAtUtc</c> not having moved.
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
    /// `14` §2.4 replays the list as the animation script and `14` §7.1 appends it to the economy log.
    /// A no-op that still published a zero-delta refill row would put a "free refill" line in front of
    /// the player several times a day and a phantom row in `21` §8.3's <c>income_attribution.csv</c>.
    /// ⚠️ Note this is the opposite ruling from <b>A6</b>'s zero-delta <c>energy_regen</c> row, and
    /// deliberately: there the accrual <em>ran</em> and moved the anchor, here the daily block did not
    /// run at all.
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
    /// 🔒 The calendar advances <b>at most once per game day</b> (`19` G), even when the player
    /// claims the newly opened day and re-sends <c>BEGIN_SESSION</c> the same day.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>The second half is the whole test, and S1 is how that was discovered.</b> The natural
    /// version — advance once, re-send, assert the day did not move — was measured against a handler
    /// with the early-return deleted and stayed <b>green</b>: the advance clears
    /// <c>LoginCalendarDayClaimed</c>, so `19` G's own pause rule stops the second advance whether
    /// or not the idempotence exists. That is genuinely two independent guards over one rule, which
    /// is good — and it means the natural test proves the <em>calendar's</em> guard and says nothing
    /// about the <em>day's</em>.
    /// </para>
    /// <para>
    /// So the second half re-claims. A player whose calendar day is open <b>and claimed</b> inside a
    /// game day the handler has already run is the only state where the two guards disagree, and it
    /// is reachable the moment <c>CLAIM_CALENDAR</c> lands (M4-09): claim in the morning, re-open the
    /// app in the evening. Without per-game-day idempotence that player advances twice in one day.
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
    /// 🔒 The player-visible shape of the defect: Energy <b>spent</b> during the day is not topped
    /// back up by re-sending <c>BEGIN_SESSION</c>.
    /// </summary>
    /// <remarks>
    /// The four tests above would all still pass against a handler that re-granted only when there
    /// was a deficit — because they leave the bar full, and <c>EnergyMath.RefillToFull</c> is
    /// deficit-only, so a repeat grant of zero is invisible in both the state and the event list. This
    /// is the one that sees it: the fixture opens <b>below</b> maximum on the second call, which is
    /// where a real player is after a run, and a re-grant would be free Energy on demand.
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
    /// 🔒 A <c>BEGIN_SESSION</c> on the <b>next</b> game day grants again — so "idempotent per game
    /// day" is not "granted once ever".
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>This is the test that stays green if idempotence is removed</b>, and that is why it is
    /// here: without it, the whole file could be satisfied by a handler that granted <em>nothing</em>,
    /// and "the second call grants nothing" would be trivially true.
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
    /// 🔒 The mechanism itself, asserted directly: the marker is written by the handler and cleared
    /// by the 05:00 UTC boundary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 It is asserted rather than inferred because M1-08's finding — <b>catch-up clears the daily
    /// counters before the handler runs</b> — is the one fact that makes this key legal, and the
    /// legality is not visible in the behavioural tests above. Both halves are checked: the boundary
    /// clears it (so a new day grants), and a command that crosses no boundary leaves it standing (so
    /// the same day does not).
    /// </para>
    /// <para>
    /// ⚠️ The "preserved" half is driven by a <b>non-BEGIN_SESSION</b> command as well, because the
    /// catch-up runs on every command and a counter cleared by some other command's catch-up would
    /// re-open the day. There is no other handled command in M1, so it is driven through the
    /// <c>internal</c> <c>GameRules.Execute</c> door the domain suite uses — the same table shape
    /// <c>GameRulesCatchUpTests</c> drives.
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
    /// <b>twice</b> — and the day it was wrong about is answered as a no-op when real time reaches it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>The M1-09 code review predicted this case, and driving it found something the review had
    /// not: the "clock corrected backwards" leg is UNREACHABLE.</b>
    /// <c>Player.MarkApplied</c> refuses a <c>NowUtc</c> earlier than <c>LastAppliedAtUtc</c>
    /// outright, so a corrected clock does not quietly lose a day — <c>Apply</c> raises
    /// <c>ArgumentOutOfRangeException</c> before any handler decides anything. That is asserted below
    /// rather than assumed, and it is <b>carried forward</b>: M1-08 clamped the <em>energy</em>
    /// backwards-clock path explicitly for `30` §2.1 <b>P3</b> reasons (<em>"a backwards clock costs
    /// the player nothing and grants them nothing"</em>) and left this one throwing, so the two halves
    /// of one decision disagree. Neither the guard nor the clamp is M1-09's to move.
    /// </para>
    /// <para>
    /// 🔒 <b>What that leaves, and it is the direction that matters:</b> nothing is ever granted a
    /// second time. A forward skew pays the day it believes it is in, and real time arriving at that
    /// day finds the marker already set. The cost is one day's grants received early rather than on
    /// the day; the loop recovers at the next boundary.
    /// </para>
    /// <para>
    /// ⚠️ <b>It is not a property of the idempotence key.</b> What the skew pins forward is
    /// <c>Player.DailyPeriodStartUtc</c>, so a persisted "the game day I last ran on" compared against
    /// that field behaves identically — at the cost of a <c>SchemaVersion</c> field. Asserted here so
    /// whoever revisits the key can see the alternative buys nothing.
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

        // 🔴 The operator corrects the clock — and this is a DEFECT, not a lost day. Pinned on the
        // message (steering S2) because several things in Apply raise this exception type.
        var corrected = Should.Throw<ArgumentOutOfRangeException>(
            () => BeginSessions.Send(skewed.NewState, BeginSessions.Morning.AddMinutes(1)),
            "a NowUtc behind LastAppliedAtUtc is refused by Player.MarkApplied before any rule runs.");

        corrected.Message.ShouldContain(
            "The last command was applied at",
            Case.Sensitive,
            "⚠️ CARRIED FORWARD: 30 §2.1's P3 forbids an exception out of Apply for anything but a " +
            "caller or domain defect, and M1-08 clamped the ENERGY backwards-clock path for exactly " +
            "that reason while this guard still throws. The two halves of one ruling disagree; " +
            "whichever way it is settled, it is M1-05's guard and M1-08's clamp, not M1-09's handler.");

        // Real time reaches the day the skewed command already claimed.
        var caughtUp = BeginSessions.Send(
            skewed.NewState, Worlds.NextDay(BeginSessions.Morning).AddHours(1));

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
