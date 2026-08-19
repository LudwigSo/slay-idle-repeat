using Shouldly;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Handlers;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Economy;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>The daily free Energy refill: first login of the day, to full, once per day.</summary>
/// <remarks>
/// The refill amount is EnergyMath.RefillToFull's ruling, not this suite's — it is deficit-only, so a
/// full bar grants nothing. These tests assert the handler calls that rule; they do not restate its
/// arithmetic.
/// </remarks>
public sealed class BeginSessionRefillTests
{
    /// <remarks>Legend Levels 40/41/200 straddle the point Max Energy stops growing (first reached at 41, not 40).</remarks>
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
            $"'to full' at Legend Level {legendLevel} fills the bar and leaves the Reserve at 3.");
    }

    /// <remarks>
    /// The Reserve assertion is load-bearing: under a rival "fill both banks" reading a refill here
    /// would silently bank a second tank instead of granting zero.
    /// </remarks>
    [Fact]
    public void A_player_at_maximum_receives_nothing_and_overflows_nothing()
    {
        var max = BeginSessions.Tuning.MaxEnergyAt(1);
        var result = BeginSessions.Send(BeginSessions.Slice(energy: new EnergyBanks(max, 0)));

        result.NewState.Player.Energy.ShouldBe(
            new EnergyBanks(max, 0),
            "'to full' of a bar that is already full is zero.");

        // The zero-delta row must still be published, not filtered: a missing row and a row of zero
        // are the difference between "did not log in" and "logged in full" for engagement reporting.
        var row = result.Events.OfType<CurrencyChanged>().ShouldHaveSingleItem();

        row.Reason.ShouldBe(BeginSession.DailyRefillReason);
        row.Delta.ShouldBe(0, "nothing moved — and the row says so, rather than being absent.");
    }

    [Fact]
    public void The_refills_CurrencyChanged_reaches_the_result_event_list()
    {
        var max = BeginSessions.Tuning.MaxEnergyAt(1);
        var result = BeginSessions.Send(BeginSessions.Slice(energy: new EnergyBanks(10, 0)));

        var refill = result.Events.OfType<CurrencyChanged>().ShouldHaveSingleItem();

        refill.Id.ShouldBe(CurrencyId.ENERGY);
        refill.Delta.ShouldBe(max - 10, "the delta is the deficit the rule actually granted.");
        refill.Reason.ShouldBe(BeginSession.DailyRefillReason);
        refill.Sequence.ShouldBe(1, "Apply stamps the ordinal from 1; a handler stamping its own is refused.");
    }

    [Fact]
    public void A_command_that_accrues_and_refills_publishes_both_rows_in_order()
    {
        var interval = BeginSessions.Tuning.RegenInterval;

        // The anchor is ten regeneration intervals back, so the catch-up accrues before the handler runs.
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
            "the catch-up ran before the handler and its row is prepended.");
        rows[0].Delta.ShouldBe(10, "ten whole intervals is ten Energy.");

        // Harnesses' own copy of the token, not BeginSession's — so a rename of the production
        // constant can't silently keep this test green while the attribution token drifted.
        rows[1].Reason.ShouldBe(
            Harnesses.DailyRefillReason, "…and the refill is attributed separately from the accrual.");
        rows[1].Delta.ShouldBe(
            BeginSessions.Tuning.MaxEnergyAt(1) - 10,
            "the refill tops up the remaining deficit, proof the handler saw the caught-up banks.");

        rows.Select(r => r.Sequence).ShouldBe(new[] { 1, 2 }, "stamped in the order they happened.");
    }
}
