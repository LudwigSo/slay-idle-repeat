using Shouldly;
using SlayIdleRepeat.BalanceHarness.Rules;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.BalanceHarness;

/// <summary>
/// 🔒 The harness's restatement of `17` §1's four duration numbers, pinned against the document's
/// literals <b>and</b> against <c>Core</c>'s own copy.
/// </summary>
/// <remarks>
/// 🔴 This suite is the only assembly that can see both copies: <c>BossDurationGuardrails</c> is
/// <c>internal</c> to <c>SlayIdleRepeat.Core</c>, which grants <c>InternalsVisibleTo</c> here and
/// nowhere else, and <c>DurationBands</c> is the harness's public restatement of it. So the drift
/// this file forbids is undetectable anywhere but here — which is exactly why the duplication is
/// reported in M2-16a's notes rather than treated as harmless.
/// </remarks>
[Collection(WallClockSensitive.Name)]
public sealed class DurationBandsTests
{
    [Fact]
    public void The_four_numbers_are_17_1_s_literals()
    {
        // Literals, not expressions over the constants under test. `17` §1: "Duration — 35-60 s at par
        // power. Balance guardrail: never below 12 s, never above 70 s (05 §9)."
        DurationBands.ParMinSeconds.ShouldBe(35.0);
        DurationBands.ParMaxSeconds.ShouldBe(60.0);
        DurationBands.HardFloorSeconds.ShouldBe(12.0);
        DurationBands.HardCeilingSeconds.ShouldBe(70.0);
    }

    [Fact]
    public void The_harness_copy_and_Core_s_copy_do_not_drift()
    {
        DurationBands.ParMinSeconds.ShouldBe(BossDurationGuardrails.ParMinSeconds);
        DurationBands.ParMaxSeconds.ShouldBe(BossDurationGuardrails.ParMaxSeconds);
        DurationBands.HardFloorSeconds.ShouldBe(BossDurationGuardrails.HardFloorSeconds);
        DurationBands.HardCeilingSeconds.ShouldBe(BossDurationGuardrails.HardCeilingSeconds);
    }

    [Fact]
    public void The_band_sits_strictly_inside_the_hard_bounds()
    {
        // A second shape on the four numbers: they are not four independent values but a nested pair
        // of intervals, and a transposition that swapped a band edge with a bound would pass the
        // literal case above only if it also changed the literal.
        DurationBands.HardFloorSeconds.ShouldBeLessThan(DurationBands.ParMinSeconds);
        DurationBands.ParMinSeconds.ShouldBeLessThan(DurationBands.ParMaxSeconds);
        DurationBands.ParMaxSeconds.ShouldBeLessThan(DurationBands.HardCeilingSeconds);
    }

    [Fact]
    public void The_tick_rate_is_05_3_s_twenty_hertz_and_the_cap_is_ninety_seconds()
    {
        DurationBands.TicksPerSecond.ShouldBe(20.0);
        DurationBands.MaxFightTicks.ShouldBe(1800);
        DurationBands.Seconds(DurationBands.MaxFightTicks).ShouldBe(90.0);

        DurationBands.Seconds(240).ShouldBe(12.0);
        DurationBands.Seconds(1400).ShouldBe(70.0);
        DurationBands.Seconds(1).ShouldBe(0.05);
    }
}
