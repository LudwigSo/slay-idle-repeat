using Shouldly;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// 🔒 <c>BattlePlan</c> refuses a fight whose `05` §4 / §4.1 📐 constants would run a different game.
/// </summary>
/// <remarks>
/// <para>
/// Every value below produces a <b>legal-looking log</b> rather than an error if it is waved through,
/// which is the reason the check exists and the reason it is worth a test of its own:
/// </para>
/// <list type="bullet">
///   <item>A zero or negative <c>wardCapPct</c> clips every grant to nothing while `05` §4.1's
///   <c>Shield</c> event still fires on every one — a replay full of shields that absorb nothing.</item>
///   <item>A zero <c>flatConstant</c> makes `05` §4 step 3's fraction <c>effDef/effDef = 1</c>
///   against any defender with DEF, so every hit in the game is mitigated to its 10% floor, and
///   <c>0/0</c> against a defender without — which `05` §1.1's rounding refuses as a NaN three
///   layers later, naming the wrong thing.</item>
/// </list>
/// <para>
/// ⚠️ Both are <b>refused</b> rather than clamped. `05` §4's own sanity check — DEF 120 mitigating
/// 0.46 at attacker level 1 — is arithmetic on the shipped pair, and a clamp would silently
/// substitute a game nobody balanced.
/// </para>
/// </remarks>
public sealed class SimulatorConstantTests
{
    /// <summary>🔒 `05` §4.1 — <c>wardCapPct</c> is strictly positive and finite.</summary>
    /// <remarks>
    /// <c>0.0</c> is the row that matters: it is the one value <c>game-data/schema/combat_caps.schema.json</c>
    /// forbids by <c>exclusiveMinimum</c> and the one a <c>&lt; 0</c> guard would let through, while
    /// being the value that deletes every shield in the game.
    /// </remarks>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_ward_cap_that_would_delete_every_shield_is_refused(double wardCapPct) =>
        Should.Throw<ArgumentOutOfRangeException>(() => Simulate(wardCapPct: wardCapPct))
            .Message.ShouldContain("wardCapPct", Case.Sensitive);

    /// <summary>🔒 `05` §4 — the mitigation dials are a positive flat term and a non-negative slope.</summary>
    /// <remarks>
    /// The <c>(120, -1)</c> row is the negative control on the asymmetry: `05` §4's slope may be 0 in
    /// principle (a game where defence does not decay) but never negative, which would make
    /// mitigation <em>fall</em> as the attacker levels.
    /// </remarks>
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

    /// <summary>🔒 A slope of exactly 0 is legal: `05` §4 states no lower bound above it.</summary>
    /// <remarks>
    /// The boundary case, and it is what stops the guard from being tightened past what `05` §4 says.
    /// <c>perLevel = 0</c> is a game in which the <c>20 × attackerLevel</c> term is switched off —
    /// unbalanced, and not malformed.
    /// </remarks>
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
