using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// <c>ReflectDamage</c>: a non-attack damage event — no dodge, crit, block or floor; reduced by the
/// receiver's <c>DR%</c> and <c>DAMAGE_TAKEN_MULT</c>; absorbed by wards; triggers no lifesteal and,
/// as an anti-loop rule, never triggers the receiver's thorns in turn.
/// </summary>
/// <remarks>
/// Internal bench seam: the cases stage exact ward totals and multipliers and read the RNG stream
/// position, none of which <c>SimulateDuel</c> can set or its log show. Every case still drives the
/// reflect the way a fight does, off a real attack, rather than calling the internal method
/// directly; the one thing unreachable that way is a reflect of a reflect — the anti-loop rule,
/// asserted by its absence.
/// </remarks>
public sealed class ReflectDamageTests
{
    /// <summary>ATK 100 against DEF 120 at level 1: the basis is 53.85.</summary>
    private const double Basis = 53.85;

    /// <summary>The reflect is <c>basis × defender.THORN</c>, off the pre-absorption basis.</summary>
    [Fact]
    public void Thorns_reflect_a_fraction_of_step_8s_basis_back_at_the_attacker() =>
        Fight(
            attackerRest: new[] { (StatId.ATK, 100.0) },
            defenderRest: new[] { (StatId.DEF, 120.0), (StatId.THORNS, 0.5) },
            body: p =>
            {
                p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X").Basis.ShouldBe(Basis);

                p.Hero.CurrentHp.ShouldBe(1000.0 - 26.925, "53.85 x 0.5");
            });

    /// <summary>
    /// <c>REFLECT</c> adds to <c>THORN</c> for its duration, and the aggregated <c>THORNS</c> stat
    /// and the additions are one number.
    /// </summary>
    [Theory]
    [InlineData(0.0, 0.5, 26.925)]
    [InlineData(0.2, 0.3, 26.925)]
    [InlineData(0.5, 0.0, 26.925)]
    public void The_REFLECT_op_adds_to_the_aggregated_THORNS_stat(
        double stat, double added, double expected) =>
        Fight(
            attackerRest: new[] { (StatId.ATK, 100.0) },
            defenderRest: new[] { (StatId.DEF, 120.0), (StatId.THORNS, stat) },
            body: p =>
            {
                if (added > 0.0)
                {
                    p.Pipeline.AddThorns(p.Enemy(), added, null, "EFF_R");
                }

                p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X");

                p.Hero.CurrentHp.ShouldBe(1000.0 - expected);
            });

    /// <summary>
    /// The reflect is reduced by the receiver's <c>DR%</c> and <c>DAMAGE_TAKEN_MULT</c> and nothing
    /// else the attack pipeline applies: DEF/PEN are irrelevant since a reflect takes no mitigation,
    /// and the 0.9 DR row separately proves there is no floor (2.6925 is well under 10% of raw).
    /// </summary>
    [Theory]
    [InlineData(0.0, 1.0, 26.925)]
    [InlineData(0.5, 1.0, 13.4625)]
    [InlineData(0.9, 1.0, 2.6925)]
    [InlineData(0.0, 0.5, 13.4625)]
    public void The_reflect_takes_DR_and_the_damage_taken_multiplier_but_no_mitigation_and_no_floor(
        double dr, double damageTakenMult, double expected) =>
        Fight(
            attackerRest: new[] { (StatId.ATK, 100.0), (StatId.DEF, 5000.0), (StatId.DR_PCT, dr) },
            defenderRest: new[] { (StatId.DEF, 120.0), (StatId.THORNS, 0.5) },
            body: p =>
            {
                if (damageTakenMult != 1.0)
                {
                    p.Hero.Flow.AddDamageTakenMultiplier(damageTakenMult, "EFF_D");
                }

                p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X");

                p.Hero.CurrentHp.ShouldBe(1000.0 - expected);
            });

    /// <summary>
    /// Absorbed by the receiver's wards: the second row is the negative control that the pool is
    /// really consulted rather than merely present.
    /// </summary>
    [Theory]
    [InlineData(100.0, 0.0, 73.075)]
    [InlineData(10.0, 16.925, 0.0)]
    public void The_reflect_is_absorbed_by_the_receivers_wards(
        double ward, double expectedHpLost, double expectedWardLeft) =>
        Fight(
            attackerRest: new[] { (StatId.ATK, 100.0) },
            defenderRest: new[] { (StatId.DEF, 120.0), (StatId.THORNS, 0.5) },
            body: p =>
            {
                p.Pipeline.GrantWard(p.Hero, ward, null, "EFF_W");

                p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X");

                p.Hero.CurrentHp.ShouldBe(1000.0 - expectedHpLost);
                p.Hero.Wards.Total.ShouldBe(expectedWardLeft);
            });

    /// <summary>
    /// Anti-loop rule: a reflect never triggers the receiver's thorns in turn. Both sides carry
    /// thorns here, the only shape in which the rule is observable — without it, this fight would
    /// not terminate until the cascade guard trips.
    /// </summary>
    [Fact]
    public void A_reflect_never_triggers_the_receivers_thorns_in_turn() =>
        Fight(
            attackerRest: new[] { (StatId.ATK, 100.0), (StatId.THORNS, 0.5) },
            defenderRest: new[] { (StatId.DEF, 120.0), (StatId.THORNS, 0.5) },
            body: p =>
            {
                var result = p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X");

                result.HpLost.ShouldBe(Basis);
                p.Enemy().CurrentHp.ShouldBe(
                    5000.0 - Basis,
                    "the attack, and nothing reflected back off the hero's own thorns");
                p.Hero.CurrentHp.ShouldBe(1000.0 - 26.925);
            });

    /// <summary>
    /// A reflect triggers no lifesteal: the defender carries both thorns and lifesteal, so a reflect
    /// that leeched would be unmissable.
    /// </summary>
    [Fact]
    public void A_reflect_triggers_no_lifesteal()
    {
        var probe = Fight(
            attackerRest: new[] { (StatId.ATK, 100.0) },
            defenderRest: new[] { (StatId.DEF, 120.0), (StatId.THORNS, 0.5), (StatId.LIFESTEAL, 1.0) },
            body: p =>
            {
                p.Enemy().SetCurrentHp(1000.0);

                p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X");

                p.Enemy().CurrentHp.ShouldBe(
                    1000.0 - Basis, "the defender's lifesteal has nothing to leech off a reflect");
            });

        probe.EventsOf(CombatEventType.Heal).ShouldBeEmpty();
    }

    /// <summary>
    /// No dodge, crit or block roll on a reflect, so a thorned fight consumes exactly the attack's
    /// three draws — otherwise the RNG stream's position would depend on whether the defender
    /// happened to carry thorns, breaking seed-only replay.
    /// </summary>
    [Fact]
    public void A_reflect_consumes_no_draw() =>
        Fight(
            attackerRest: new[] { (StatId.ATK, 100.0) },
            defenderRest: new[] { (StatId.DEF, 120.0), (StatId.THORNS, 0.5) },
            body: p =>
            {
                p.Services.Rng.Position.ShouldBe(0UL);

                p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X");

                p.Services.Rng.Position.ShouldBe(3UL, "the attack's three, and nothing for the reflect");
            });

    /// <summary>The reflect is itself a <c>Hit</c> on the attacker, so the replayer has a number to draw.</summary>
    [Fact]
    public void The_reflect_emits_its_own_Hit_after_the_attacks()
    {
        var probe = Fight(
            attackerRest: new[] { (StatId.ATK, 100.0) },
            defenderRest: new[] { (StatId.DEF, 120.0), (StatId.THORNS, 0.5) },
            body: p => p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X"));

        var hits = probe.EventsOf(CombatEventType.Hit);

        hits.Count.ShouldBe(2);

        hits[0].Value.ShouldBe(Basis, "the attack");
        hits[0].SourceId.ShouldBe(CombatActor.Hero);
        hits[0].TargetId.ShouldBe(CombatActor.Enemy(0));

        hits[1].Value.ShouldBe(26.925, "the reflect");
        hits[1].TargetId.ShouldBe(CombatActor.Hero);

        // The reflect names the thorns holder as its source; CombatActor.None here would draw as
        // damage arriving from nowhere.
        hits[1].SourceId.ShouldBe(CombatActor.Enemy(0));
    }

    // ══════════════════════════════════════════════════════ helpers

    private static AttackProbe Fight(
        Action<AttackProbe> body,
        (StatId Stat, double Value)[] attackerRest,
        (StatId Stat, double Value)[] defenderRest) =>
        AttackPipelineBench.Run(
            new[]
            {
                BattleTestBench.Hero(Stats(1000.0, attackerRest), 1),
                BattleTestBench.Enemy(0, Stats(5000.0, defenderRest)),
            },
            body);

    /// <summary>See <see cref="AttackPipelineBench.Stats"/> for the two defaults.</summary>
    private static ActorStats Stats(double maxHp, (StatId Stat, double Value)[] rest) =>
        AttackPipelineBench.Stats(maxHp, rest);
}
