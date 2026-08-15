using Shouldly;
using SlayIdleRepeat.BalanceHarness.Rules;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.BalanceHarness;

/// <summary>
/// The harness's restatement of the four boss duration numbers, pinned against both the literals and
/// <c>Core</c>'s own copy. This suite is the only assembly that can see both copies, so the drift it
/// forbids is otherwise undetectable.
/// </summary>
[Collection(WallClockSensitive.Name)]
public sealed class DurationBandsTests
{
    [Fact]
    public void The_four_numbers_are_17_1_s_literals()
    {
        // Literals, not expressions over the constants under test.
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
        // A nested pair of intervals, not four independent values.
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
