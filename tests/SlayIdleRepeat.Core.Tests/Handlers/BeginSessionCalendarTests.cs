using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// 🔒 `19` Part G — the 28-day login calendar's <b>advancement rule</b>, driven through
/// <c>GameRules.Apply</c>: <em>"advances at <c>BEGIN_SESSION</c> … at most once per game day, and only
/// when the currently open day has been claimed; a missed or unclaimed day pauses the calendar.
/// Nothing is skipped or lost."</em>
/// </summary>
/// <remarks>
/// 🔒 This advances the <b>pointer</b> and never pays out — the payout is <c>CLAIM_CALENDAR</c>'s
/// (M4-09) and the 28 reward rows are read by nothing today. There is no test here asserting a reward,
/// and its absence is the deferral rather than a gap.
/// <para>
/// ⚠️ Nothing in M1 can claim a day, so the <em>claimed</em> fixtures are built through
/// <c>Player.Rehydrate</c> — the same door the Postgres adapter uses, not a shortcut around a missing
/// command. The <b>pause</b> arm needs no such help: it is the state every M1 player is in.
/// </para>
/// <para>
/// 🔒 The "at most once per game day" half is <c>BeginSessionIdempotenceTests</c>' — the same mechanism
/// as the daily idempotence, and stating it in both places would be two rules over one mechanism.
/// </para>
/// </remarks>
public sealed class BeginSessionCalendarTests
{
    /// <summary>
    /// 🔒 The pause: an <b>unclaimed</b> open day does not advance.
    /// </summary>
    /// <remarks>
    /// ⚠️ It also asserts the command did something else — the refill was paid — so the claim is "the
    /// calendar stayed put while the daily block ran", not "nothing happened". Without that, a handler
    /// that ignored <c>BEGIN_SESSION</c> entirely would pass.
    /// </remarks>
    [Fact]
    public void An_unclaimed_open_day_pauses_the_calendar()
    {
        var result = BeginSessions.Send(
            BeginSessions.Slice(loginCalendarDay: 5, loginCalendarDayClaimed: false));

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.LoginCalendarDay.ShouldBe(
            5,
            "19 G: 'a missed day — or an unclaimed one — pauses the calendar. Nothing is skipped or " +
            "lost.' Advancing past an unclaimed day would silently destroy a reward the player is " +
            "owed, and the loss would be invisible — the pointer would simply be further along than " +
            "the payouts.");
        result.NewState.Player.LoginCalendarDayClaimed.ShouldBeFalse("…and it is still unclaimed.");

        result.Events.ShouldNotBeEmpty(
            "positive evidence that the daily block DID run: the calendar being paused is a decision " +
            "the handler took, not a handler that never executed.");
    }

    /// <summary>🔒 The advance: a <b>claimed</b> open day moves to the next, which opens unclaimed.</summary>
    [Fact]
    public void A_claimed_open_day_advances_and_the_new_day_opens_unclaimed()
    {
        var result = BeginSessions.Send(
            BeginSessions.Slice(loginCalendarDay: 5, loginCalendarDayClaimed: true));

        result.NewState.Player.LoginCalendarDay.ShouldBe(6);
        result.NewState.Player.LoginCalendarDayClaimed.ShouldBeFalse(
            "the day that just opened has not been claimed, which is what pauses the calendar again " +
            "until CLAIM_CALENDAR (M4-09) arrives.");
    }

    /// <summary>
    /// 🔒 `19` G — <em>"after day 28 it restarts at day 1"</em>, and the wrap point is read from the
    /// tuning rather than written as a literal.
    /// </summary>
    /// <remarks>
    /// <c>[InlineData]</c> over <c>TuningDocuments.ShippedCycleDays</c>, so a test that restated 28
    /// could not keep passing after the data moved — the reason that fixture is a <c>const</c>.
    /// </remarks>
    [Theory]
    [InlineData(TuningDocuments.ShippedCycleDays, LoginCalendarTuning.FirstDay)]
    [InlineData(TuningDocuments.ShippedCycleDays - 1, TuningDocuments.ShippedCycleDays)]
    [InlineData(LoginCalendarTuning.FirstDay, LoginCalendarTuning.FirstDay + 1)]
    public void The_cycle_restarts_at_day_one_after_the_last_day(int openDay, int expected)
    {
        var result = BeginSessions.Send(
            BeginSessions.Slice(loginCalendarDay: openDay, loginCalendarDayClaimed: true));

        result.NewState.Player.LoginCalendarDay.ShouldBe(
            expected,
            "19 G runs a 28-day cycle and 'every cycle pays identically — nothing is " +
            "first-cycle-exclusive'. A calendar that stopped at 28 would end a returning player's " +
            "daily reward permanently.");
    }

    /// <summary>
    /// 🔒 A full cycle walked one game day at a time, claiming each day: 28 advances return to day 1.
    /// </summary>
    /// <remarks>
    /// ⚠️ The per-step tests each advance a player <em>constructed</em> at the day they test, so none
    /// proves the sequence composes — an implementation that advanced correctly from any given day but
    /// reset the day on some other path satisfies all of them.
    /// <para>
    /// 🔴 The loop carries the <b>aggregate</b>, not just the day number. The first draft minted a fresh
    /// player each iteration, which made it 28 independent applications of <c>DayAfter</c> — the very
    /// shape it claims to improve on. The state that walks the cycle is now the same twenty-two
    /// snapshot fields throughout.
    /// </para>
    /// </remarks>
    [Fact]
    public void Twenty_eight_claimed_days_walk_the_whole_cycle_and_return_to_day_one()
    {
        var player = Worlds.Rehydrated(
            PlayerSnapshots.With(
                energyAnchorUtc: BeginSessions.Morning,
                lastAppliedAtUtc: BeginSessions.Morning,
                dailyPeriodStartUtc: BeginSessions.Today,
                loginCalendarDay: LoginCalendarTuning.FirstDay,
                loginCalendarDayClaimed: true));

        var at = BeginSessions.Morning;
        var seen = new List<int>();

        for (var i = 0; i < TuningDocuments.ShippedCycleDays; i++)
        {
            seen.Add(player.LoginCalendarDay);

            var result = BeginSessions.Send(new WorldSlice(player, null), at);

            // The player claims the newly opened day (M4-09's CLAIM_CALENDAR, stood in for by the
            // persisted row) — ONE field of the aggregate the command just returned, so everything
            // else walks the cycle unchanged.
            player = Worlds.Rehydrated(
                result.NewState.Player.ToSnapshot() with { LoginCalendarDayClaimed = true });

            at = Worlds.NextDay(at);
        }

        var day = player.LoginCalendarDay;

        seen.ShouldBe(
            Enumerable.Range(LoginCalendarTuning.FirstDay, TuningDocuments.ShippedCycleDays).ToArray(),
            ignoreOrder: false,
            customMessage: "19 G's table is days 1..28 in order — every one of them opens exactly once per cycle, " +
            "which is what 'nothing is skipped or lost' means when the player claims every day.");

        day.ShouldBe(
            LoginCalendarTuning.FirstDay,
            "…and the 28th advance restarts the cycle, rather than running to 29.");
    }

    /// <summary>
    /// 🔒 A player who never claims stays on day 1 <b>across many game days</b> — which is every M1
    /// player, because <c>CLAIM_CALENDAR</c> is deferred to M4-09.
    /// </summary>
    /// <remarks>
    /// This is the production behaviour <b>M1-11</b> will observe when it drives a multi-day player,
    /// and it is asserted here so that task does not read a motionless calendar as a bug. It also
    /// closes the direction the single-command pause test cannot: a handler that advanced on, say,
    /// every second day would pass that one.
    /// </remarks>
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
            LoginCalendarTuning.FirstDay,
            "seven game days, seven BEGIN_SESSIONs, no claim: 19 G's calendar is PAUSED, not slowed. " +
            "This is what every M1 player sees, because CLAIM_CALENDAR is deferred to M4-09.");
    }

    /// <summary>
    /// 🔒 The calendar advance <b>pays nothing</b>: it is a pointer move and no currency changes.
    /// </summary>
    /// <remarks>
    /// ⚠️ Stated as "the advance adds no row of its own", by comparing the event list of a command
    /// that advanced against one that did not. Asserting "no calendar event exists" would be true of
    /// every implementation, including one that granted 300 Crowns through the refill's row.
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
            "19 G's payout is CLAIM_CALENDAR's (M4-09), not BEGIN_SESSION's — 30 §2.3 is explicit " +
            "that 'claims stay explicit commands'. An advance that also paid would grant day 5's " +
            "+30 Energy and day 7's Pet Egg to a player who never tapped anything.");

        advanced.NewState.Player.Wallet.ShouldBe(
            paused.NewState.Player.Wallet,
            "…and no wallet row moved either, which is where 19 G's Crowns and Merge Dust would land.");
    }
}
