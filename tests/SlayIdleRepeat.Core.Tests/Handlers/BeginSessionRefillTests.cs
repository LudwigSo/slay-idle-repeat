using Shouldly;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Handlers;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Economy;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// 🔒 `10` §3.1 — the <b>daily free Energy refill</b>: <em>"first login of the day — to full,
/// 1/day"</em>, and the `30` §7 attribution row it must publish.
/// </summary>
/// <remarks>
/// 🔴 The refill is a currency movement whose return value has to be carried all the way to
/// <c>CommandResult.Events</c>, and <c>A_currency_event_is_never_discarded_at_its_call_site</c> can
/// only see the <em>popped</em> version of that mistake, never the assigned-and-forgotten one — so it
/// is asserted here, on the result.
/// <para>
/// 🔒 The amount is <c>EnergyMath.RefillToFull</c>'s ruling, not this suite's: it is <b>deficit-only</b>,
/// so a full bar grants nothing. ⚠️ The rival "fill both banks" reading is a <b>live contradiction
/// registered for a ruling</b>, not a settled rule. These tests assert the handler <em>calls</em> that
/// rule; they do not restate its arithmetic and do not declare a winner.
/// </para>
/// </remarks>
public sealed class BeginSessionRefillTests
{
    /// <summary>🔒 An empty player is refilled to their Max Energy on the first call of the day.</summary>
    [Fact]
    public void The_first_BEGIN_SESSION_of_the_day_refills_the_bar_to_full()
    {
        var result = BeginSessions.Send(BeginSessions.Slice(energy: new EnergyBanks(0, 0)));

        result.NewState.Player.Energy.ShouldBe(
            new EnergyBanks(BeginSessions.Tuning.MaxEnergyAt(1), 0),
            "10 §3.1 authors the daily free refill as 'to full'.");
    }

    /// <summary>
    /// 🔒 The refill fills the <b>bar</b> to this level's Max Energy and leaves the <b>Reserve</b>
    /// exactly where it was, on both sides of the cap.
    /// </summary>
    /// <remarks>
    /// ⚠️ The Legend Levels span the point Max Energy stops growing (first reached at level <b>41</b>,
    /// not 40), which is what this theory uniquely contributes — and exactly why the expectation must
    /// not be computed by the rule under test.
    /// <para>
    /// 🔒 It used to assert against <c>EnergyMath.RefillToFull</c> itself, which could not distinguish
    /// the handler <em>calling</em> the rule from restating it, and could not fail for any bug inside
    /// <c>RefillToFull</c>. The independent expectation also keeps the deficit-only cascade under test —
    /// a refill that topped the Reserve up would move the second component off its input value of 3.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    [InlineData(41)]
    [InlineData(200)]
    public void The_refill_fills_the_bar_to_this_levels_max_and_leaves_the_reserve_alone(int legendLevel)
    {
        var before = new EnergyBanks(7, 3);

        var result = BeginSessions.Send(BeginSessions.Slice(energy: before, legendLevel: legendLevel));

        result.NewState.Player.Energy.ShouldBe(
            new EnergyBanks(BeginSessions.Tuning.MaxEnergyAt(legendLevel), 3),
            $"'to full' at Legend Level {legendLevel} fills the BAR to this level's Max Energy and " +
            "leaves the Reserve at the 3 it came in with. 40/41/200 straddle 10 §3's cap of 200, " +
            "first reached at 41.");
    }

    /// <summary>
    /// 🔒 M1-10's deficit-only ruling, at the boundary it is about: a player already at maximum
    /// receives <b>nothing</b>, and nothing overflows into the Reserve.
    /// </summary>
    /// <remarks>
    /// ⚠️ The Reserve assertion is the load-bearing half. `28` C2 lists daily refills among the
    /// sources that overflow into the Reserve, and under the deficit-only reading they never can —
    /// <c>EnergyMath.RefillToFull</c>'s remarks state that cost plainly. A refill that banked a second
    /// tank here would be the rival reading shipping silently.
    /// </remarks>
    [Fact]
    public void A_player_at_maximum_receives_nothing_and_overflows_nothing()
    {
        var max = BeginSessions.Tuning.MaxEnergyAt(1);
        var result = BeginSessions.Send(BeginSessions.Slice(energy: new EnergyBanks(max, 0)));

        result.NewState.Player.Energy.ShouldBe(
            new EnergyBanks(max, 0),
            "'to full' of a bar that is already full is ZERO (M1-10). The rival reading — top the " +
            "Reserve up too — would grant 240 here instead of 0, on a 10 §3.2 free-player day of " +
            "roughly 480. See EnergyMath.RefillToFull: the conflict between 10 §3.1/§3.2 and 28 C2 " +
            "is registered for a ruling, and only the `deficit` expression there changes if it goes " +
            "the other way.");

        // 🔒 …AND THE ZERO-DELTA ROW IS PUBLISHED, NOT FILTERED — recorded assumption A6's ruling,
        // one command on. This is the only fixture in the suite that produces a zero deficit, so
        // without these three lines a handler carrying `if (delta == 0) return Accept();` passes
        // every test in the repository. 21 §8.3 needs to see the refill was TAKEN: a missing row and
        // a row of zero are the difference between "the player did not log in" and "the player
        // logged in full", which is the engagement question the report answers.
        var row = result.Events.OfType<CurrencyChanged>().ShouldHaveSingleItem();

        row.Reason.ShouldBe(BeginSession.DailyRefillReason);
        row.Delta.ShouldBe(0, "nothing moved — and the row says so, rather than being absent.");
    }

    // ---------------------------------------------------------------- 30 §7 · the attribution row

    /// <summary>
    /// 🔒 The refill's <c>CurrencyChanged</c> reaches <c>CommandResult.Events</c> — stamped, attributed
    /// and with the right delta.
    /// </summary>
    /// <remarks>
    /// 🔴 This is the assertion M1-08's finding demands. The architecture suite cannot make it: its
    /// IL rule sees a <c>call</c> followed by <c>pop</c> and nothing else, so a handler that assigned
    /// the event to a local and quietly forgot it would leave all 62 rules green while `14` §7.1's
    /// economy log lost every free refill in the game.
    /// </remarks>
    [Fact]
    public void The_refills_CurrencyChanged_reaches_the_result_event_list()
    {
        var max = BeginSessions.Tuning.MaxEnergyAt(1);
        var result = BeginSessions.Send(BeginSessions.Slice(energy: new EnergyBanks(10, 0)));

        var refill = result.Events.OfType<CurrencyChanged>().ShouldHaveSingleItem();

        refill.Id.ShouldBe(CurrencyId.ENERGY);
        refill.Delta.ShouldBe(
            max - 10,
            "the delta is the deficit the rule actually granted — 21 §8.3 sums this column, so a row " +
            "carrying the wrong number is worse than no row at all.");
        // 🔒 Shouldly's string ShouldBe is exact and case-sensitive already — unlike its
        // ShouldContain, which defaults to Case.Insensitive. The token is the 21 §8.3 grouping key,
        // so nothing weaker than equality would do.
        refill.Reason.ShouldBe(
            BeginSession.DailyRefillReason,
            "21 §8.3 groups income_attribution.csv by this token.");
        refill.Sequence.ShouldBe(
            1,
            "Apply stamps the ordinal from 1; a handler that stamped its own is refused outright.");
    }

    /// <summary>
    /// 🔒 The refill's reason is <b>distinct</b> from regeneration's, and both rows survive when a
    /// command does the two at once — ordered, accrual first.
    /// </summary>
    /// <remarks>
    /// The accrual genuinely happened first, before the handler was built. A refill stamped ahead of it
    /// would tell the log the player was topped up from a bar they had not yet refilled into.
    /// <para>
    /// ⚠️ The sharpest test of the two tokens being separate: if they were one, `10` §3.2's budget could
    /// not tell the free player's flat daily income from their idle accrual — the exact question `21`
    /// §8.3 exists to answer — and every other test in this file would still pass.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_command_that_accrues_and_refills_publishes_both_rows_in_order()
    {
        var interval = BeginSessions.Tuning.RegenInterval;

        // The anchor is ten regeneration intervals back, so the catch-up accrues before the handler
        // runs. Everything else is the ordinary first-of-day fixture.
        var slice = new WorldSlice(
            Worlds.Rehydrated(PlayerSnapshots.With(
                energy: new EnergyBanks(0, 0),
                energyAnchorUtc: BeginSessions.Morning - (10 * interval),
                lastAppliedAtUtc: BeginSessions.Morning - (10 * interval),
                dailyPeriodStartUtc: BeginSessions.Today)),
            null);

        var result = BeginSessions.Send(slice);

        var rows = result.Events.OfType<CurrencyChanged>().ToArray();

        rows.Length.ShouldBe(2, "one accrual and one refill.");
        rows[0].Reason.ShouldBe(
            Harnesses.EnergyRegenReason,
            "the catch-up ran BEFORE the handler and its row is prepended (GameRules.Combine).");
        rows[0].Delta.ShouldBe(10, "ten whole intervals is ten Energy (A1).");

        // 🔒 Harnesses' transcription, NOT BeginSession's own constant. Harnesses explains why the
        // second copy is the deliberate kind: these tokens are what 21 §8.3's income_attribution.csv
        // groups by, so a test that read the production constant would keep passing after the token
        // was renamed under the dashboards' feet. Reading it here defeated that on one of two rows.
        rows[1].Reason.ShouldBe(
            Harnesses.DailyRefillReason,
            "…and the refill is attributed separately, because 10 §3.2 budgets the two apart.");
        rows[1].Delta.ShouldBe(
            BeginSessions.Tuning.MaxEnergyAt(1) - 10,
            "the refill tops up what the accrual left, so it grants the REMAINING deficit — proof " +
            "that the handler saw the caught-up banks rather than the stored ones.");

        rows.Select(r => r.Sequence).ShouldBe(new[] { 1, 2 }, "stamped in the order they happened.");
    }
}
