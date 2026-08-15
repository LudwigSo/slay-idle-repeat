using Shouldly;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// <c>BattlePlan</c> refuses a fight whose combat constants would run a different game. Every value
/// below produces a legal-looking log rather than an error if waved through: a zero
/// <c>wardCapPct</c> clips every grant to nothing while the <c>Shield</c> event still fires, and a
/// zero <c>flatConstant</c> makes the mitigation fraction <c>1</c> against any defender with DEF and
/// <c>0/0</c> against one without. Both are refused rather than clamped, since a clamp would silently
/// substitute a game nobody balanced.
/// </summary>
public sealed class SimulatorConstantTests
{
    /// <summary>
    /// <c>wardCapPct</c> is strictly positive and finite. <c>0.0</c> is the row that matters: it is
    /// the value that deletes every shield in the game, and the one a <c>&lt; 0</c> guard would let
    /// through.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_ward_cap_that_would_delete_every_shield_is_refused(double wardCapPct) =>
        Should.Throw<ArgumentOutOfRangeException>(() => Simulate(wardCapPct: wardCapPct))
            .Message.ShouldContain("wardCapPct", Case.Sensitive);

    /// <summary>
    /// The mitigation dials are a positive flat term and a non-negative slope: the slope may be 0
    /// (defence never decays) but never negative, which would make mitigation fall as the attacker
    /// levels.
    /// </summary>
    [Theory]
    [InlineData(0.0, 20.0)]
    [InlineData(-120.0, 20.0)]
    [InlineData(120.0, -1.0)]
    [InlineData(double.NaN, 20.0)]
    [InlineData(120.0, double.PositiveInfinity)]
    public void Mitigation_dials_that_would_run_a_different_game_are_refused(double flat, double perLevel) =>
        Should.Throw<ArgumentOutOfRangeException>(
                () => Simulate(mitigation: new MitigationConstants(flat, perLevel)))
            .Message.ShouldContain("mitigation", Case.Insensitive);

    /// <summary>The shipped values are accepted — the positive control for both cases above.</summary>
    [Fact]
    public void The_shipped_constants_are_accepted() =>
        Should.NotThrow(() => Simulate());

    /// <summary>
    /// A slope of exactly 0 is legal — the boundary case that stops the guard from being tightened
    /// too far: it switches off the level term, which is unbalanced but not malformed.
    /// </summary>
    [Fact]
    public void A_zero_per_level_slope_is_legal() =>
        Should.NotThrow(() => Simulate(mitigation: new MitigationConstants(120.0, 0.0)));

    private static SimulationResult Simulate(
        double wardCapPct = StatFixtures.WardCapPct, MitigationConstants? mitigation = null)
    {
        var plan = BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(AttackPipelineBench.Stats(1000.0), 1),
                BattleTestBench.Enemy(0, AttackPipelineBench.Stats(1000.0)),
            },
            rules: new CombatRules(1, OnKillTriggersFire: true));

        return CombatSimulator.Simulate(plan with
        {
            WardCapPct = wardCapPct,
            Mitigation = mitigation ?? StatFixtures.Mitigation(),
        });
    }
}
