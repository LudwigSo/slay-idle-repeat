using Shouldly;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// Combat constants that would run a different game are refused rather than clamped. Every value
/// below produces a legal-looking log if waved through: a zero <c>wardCapPct</c> clips every grant
/// to nothing while <c>Shield</c> still fires, and a zero mitigation flat term makes the mitigation
/// fraction <c>1</c> against any defender with DEF and <c>0/0</c> against one without.
/// </summary>
public sealed class SimulatorConstantTests
{
    /// <summary>
    /// The mitigation dials are content, so the refusal is observable at a public entry point: the
    /// flat term must be positive and the slope non-negative.
    /// </summary>
    [Theory]
    [InlineData(0.0, 20.0)]
    [InlineData(-120.0, 20.0)]
    [InlineData(120.0, -1.0)]
    public void Mitigation_dials_a_content_document_authors_wrong_are_refused(
        double flat, double perLevel) =>
        Should.Throw<ArgumentOutOfRangeException>(() => Duel(((decimal)flat, (decimal)perLevel)))
            .Message.ShouldContain("mitigation", Case.Insensitive);

    /// <summary>
    /// A slope of exactly 0 is legal — the boundary that stops the guard from being tightened too
    /// far: it switches off the level term, which is unbalanced but not malformed.
    /// </summary>
    [Fact]
    public void A_zero_per_level_slope_is_legal() =>
        Should.NotThrow(() => Duel((120.0m, 0.0m)));

    /// <summary>
    /// <c>wardCapPct</c> is strictly positive and finite. <c>0.0</c> is the row that matters: it is
    /// the value that deletes every shield in the game, and the one a <c>&lt; 0</c> guard would let
    /// through.
    /// </summary>
    /// <remarks>
    /// Internal plan seam: the caps fixture authors no <c>wardCapPct</c> knob and JSON cannot carry
    /// a non-finite number, so no public entry point's content can deliver these values.
    /// </remarks>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_ward_cap_that_would_delete_every_shield_is_refused(double wardCapPct) =>
        Should.Throw<ArgumentOutOfRangeException>(() => Simulate(wardCapPct: wardCapPct))
            .Message.ShouldContain("wardCapPct", Case.Sensitive);

    /// <summary>Non-finite mitigation dials, unreachable through content for the same reason.</summary>
    [Theory]
    [InlineData(double.NaN, 20.0)]
    [InlineData(120.0, double.PositiveInfinity)]
    public void Non_finite_mitigation_dials_are_refused(double flat, double perLevel) =>
        Should.Throw<ArgumentOutOfRangeException>(
                () => Simulate(mitigation: new MitigationConstants(flat, perLevel)))
            .Message.ShouldContain("mitigation", Case.Insensitive);

    /// <summary>One public duel whose content authors the given mitigation pair.</summary>
    private static SimulationResult Duel((decimal Flat, decimal PerLevel) mitigation) =>
        PublicFightBench.Duel(
            PublicFightBench.Stats(1000.0),
            PublicFightBench.Stats(1000.0),
            mitigation: mitigation);

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
