using Shouldly;
using SlayIdleRepeat.BalanceHarness.Rules;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.BalanceHarness;

/// <summary>
/// 🔒 `02` §4.3's <c>EnemyPower(i)</c> — the one formula the harness computes itself, pinned against
/// the document's literals.
/// </summary>
/// <remarks>
/// Every expected value below is written as a <b>literal</b> rather than as an expression over
/// <see cref="NodePower"/>'s own constants. An assertion built from the constants it is checking is
/// the identity function with extra steps: it stays green when the constant is wrong.
/// </remarks>
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
        // 🔒 `03` §1.1: "Spine nodes are numbered 0..41 in walk order across the three stages
        // (12 + 14 + 16); the boss node is 42." The three counts are asserted here too, because 42 is
        // only correct if they still sum to it.
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
        // par × (1 + 0.035 × 42) × 2.20 = par × 2.47 × 2.20 = par × 5.434, written out.
        NodePower.BossPower(par).ShouldBe(expected, tolerance: 0.001);
    }

    [Fact]
    public void The_stage_multiplier_is_applied_exactly_once()
    {
        // 🔴 `05` §6.3 / `17` §1: "do not multiply by 2.20 again." The discriminating control is the
        // doubly-multiplied value: BossPower must NOT equal it, and the gap must be exactly 2.20×.
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

        // Second shape — the growth term is linear in the index, so node 0 is the bare stage value and
        // node 42 is 2.47× it. A reader that dropped the +1 would make node 0 zero.
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
