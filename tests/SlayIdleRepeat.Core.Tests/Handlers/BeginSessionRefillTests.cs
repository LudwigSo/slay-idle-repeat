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
/// <para>
/// 🔴 <b>The headline is M1-08's finding, one task on.</b> M1-08 proved that dropping the catch-up's
/// events left the architecture suite green at 62/62 while `21` §8.3's
/// <c>income_attribution.csv</c> silently lost every regeneration row. This handler's refill is
/// exactly that shape — a currency movement produced by an aggregate mutator whose return value has
/// to be carried all the way to <c>CommandResult.Events</c> — and
/// <c>A_currency_event_is_never_discarded_at_its_call_site</c> can only see the <em>popped</em>
/// version of the mistake, never the assigned-and-forgotten one. So it is asserted here, on the
/// result, in <see cref="The_refills_CurrencyChanged_reaches_the_result_event_list"/>.
/// </para>
/// <para>
/// 🔒 <b>The amount is M1-10's ruling, not this task's.</b> <c>EnergyMath.RefillToFull</c> is
/// <b>deficit-only</b>: it grants <c>max(0, max − energy)</c>, so a full bar grants nothing and
/// overflows nothing. The rival "fill both banks" reading makes the refill 400 rather than 120 and
/// swings `10` §3.2's free-player daily budget by about 2.3×; `28` C2's source list is the erratum
/// and <c>EnergyMath.RefillToFull</c> carries the whole argument. These tests assert the handler
/// <em>calls</em> that rule — they do not restate its arithmetic, which would be a second
/// transcription to keep in step.
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
    /// 🔒 The amount is <c>EnergyMath.RefillToFull</c>'s, at every Legend Level — asserted against the
    /// rule rather than against a transcribed number.
    /// </summary>
    /// <remarks>
    /// ⚠️ The Legend Levels span the point Max Energy stops growing (`10` §3's cap of 200, first
    /// reached at level <b>41</b>, not 40 — see <c>EnergyTuning.MaxEnergyAt</c>), so the sweep covers
    /// both sides of the cap rather than three points on the same slope.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    [InlineData(41)]
    [InlineData(200)]
    public void The_refill_is_exactly_what_EnergyMath_answers(int legendLevel)
    {
        var before = new EnergyBanks(7, 3);

        var result = BeginSessions.Send(BeginSessions.Slice(energy: before, legendLevel: legendLevel));

        result.NewState.Player.Energy.ShouldBe(
            EnergyMath.RefillToFull(BeginSessions.Tuning, legendLevel, before),
            "30 §11.5 puts computation in Rules/: the handler calls EnergyMath and never restates it. " +
            "A second transcription is how the free refill and the level-up refill start disagreeing.");
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
            "Reserve up too — makes this refill 400 rather than 120 and swings 10 §3.2's free-player " +
            "daily budget by about 2.3x.");
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
    /// command does the two at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Ordered: the catch-up's accrual first, the refill second.</b> `30` §7's <c>Sequence</c>
    /// orders one command's list, `14` §7.1 appends it to the economy log and `14` §2.4 replays it as
    /// the animation script — and the accrual genuinely happened first, before the handler was even
    /// built. A refill stamped ahead of the regeneration that preceded it would tell the log the
    /// player was topped up from a bar they had not yet refilled into.
    /// </para>
    /// <para>
    /// ⚠️ <b>It is the SHARPEST test of the two tokens being separate.</b> If <c>daily_free_refill</c>
    /// and <c>energy_regen</c> were one token, `10` §3.2's budget could not tell the free player's
    /// flat daily income from their idle accrual — the exact question `21` §8.3 exists to answer —
    /// and every other test in this file would still pass.
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
            "energy_regen",
            "the catch-up ran BEFORE the handler and its row is prepended (GameRules.Combine).");
        rows[0].Delta.ShouldBe(10, "ten whole intervals is ten Energy (A1).");

        rows[1].Reason.ShouldBe(
            BeginSession.DailyRefillReason,
            "…and the refill is attributed separately, because 10 §3.2 budgets the two apart.");
        rows[1].Delta.ShouldBe(
            BeginSessions.Tuning.MaxEnergyAt(1) - 10,
            "the refill tops up what the accrual left, so it grants the REMAINING deficit — proof " +
            "that the handler saw the caught-up banks rather than the stored ones.");

        rows.Select(r => r.Sequence).ShouldBe(new[] { 1, 2 }, "stamped in the order they happened.");
    }
}
