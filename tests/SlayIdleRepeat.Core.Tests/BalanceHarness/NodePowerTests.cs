using Shouldly;
using SlayIdleRepeat.BalanceHarness.Rules;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.BalanceHarness;

/// <summary>
/// <c>EnemyPower(i)</c> — the one formula the harness computes itself. Every expected value below is
/// a literal rather than an expression over <see cref="NodePower"/>'s own constants, since the latter
/// would stay green even if the constant were wrong.
/// </summary>
[Collection(WallClockSensitive.Name)]
public sealed class NodePowerTests
{
    [Fact]
    public void The_transcribed_constants_are_02_4_3_s()
    {
        NodePower.PerNodePowerGrowth.ShouldBe(0.035);
        NodePower.Stage1Multiplier.ShouldBe(1.00);
        NodePower.Stage2Multiplier.ShouldBe(1.15);
        NodePower.Stage3Multiplier.ShouldBe(1.35);
        NodePower.BossStageMultiplier.ShouldBe(2.20);
    }

    [Fact]
    public void The_boss_node_index_is_03_1_1_s_forty_two()
    {
        // The three stage counts are asserted too, because 42 is only correct if they still sum to it.
        NodePower.BossNodeIndex.ShouldBe(42);
        (12 + 14 + 16).ShouldBe(NodePower.BossNodeIndex);
    }

    [Theory]
    [InlineData(1000.0, 5434.0)]
    [InlineData(4000.0, 21736.0)]
    [InlineData(64000.0, 347776.0)]
    [InlineData(2048000.0, 11128832.0)]
    public void The_boss_node_power_is_par_times_five_point_four_three_four(double par, double expected)
    {
        // par x (1 + 0.035 x 42) x 2.20 = par x 2.47 x 2.20 = par x 5.434, written out.
        NodePower.BossPower(par).ShouldBe(expected, tolerance: 0.001);
    }

    [Fact]
    public void The_stage_multiplier_is_applied_exactly_once()
    {
        // The discriminating control is the doubly-multiplied value: BossPower must not equal it, and
        // the gap must be exactly 2.20x.
        var once = NodePower.BossPower(1000.0);
        var twice = 1000.0 * (1.0 + (0.035 * 42)) * 2.20 * 2.20;

        once.ShouldBe(5434.0, tolerance: 0.001);
        twice.ShouldBe(11954.8, tolerance: 0.001);
        once.ShouldNotBe(twice);
        (twice / once).ShouldBe(2.20, tolerance: 1e-9);
    }

    [Fact]
    public void The_node_form_and_the_boss_form_agree_at_node_forty_two()
    {
        NodePower.NodeEnemyPower(1000.0, 42, 2.20).ShouldBe(NodePower.BossPower(1000.0));

        // The growth term is linear in the index, so node 0 is the bare stage value — a reader that
        // dropped the +1 would make node 0 zero.
        NodePower.NodeEnemyPower(1000.0, 0, 1.00).ShouldBe(1000.0);
        NodePower.NodeEnemyPower(1000.0, 42, 1.00).ShouldBe(2470.0, tolerance: 0.001);
        NodePower.NodeEnemyPower(1000.0, 12, 1.15).ShouldBe(1633.0, tolerance: 0.001);
    }

    [Fact]
    public void A_non_positive_par_power_is_refused_rather_than_producing_a_zero_boss()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => NodePower.BossPower(0.0));
        Should.Throw<ArgumentOutOfRangeException>(() => NodePower.BossPower(-1.0));
        Should.Throw<ArgumentOutOfRangeException>(() => NodePower.BossPower(double.NaN));
    }
}
