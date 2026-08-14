using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// 🔒 `05` §4's <c>ReflectDamage</c> — <em>"a non-attack damage event: no dodge, crit, block or
/// floor; reduced by the receiver's <c>DR%</c> and <c>DAMAGE_TAKEN_MULT</c>; absorbed by the
/// receiver's wards; triggers no lifesteal and — anti-loop rule — never triggers the receiver's
/// thorns in turn."</em>
/// </summary>
/// <remarks>
/// Every case drives it the way a fight does: through `05` §4 step 10, off a real attack, rather
/// than by calling the internal method. The one thing that cannot be reached that way is a reflect
/// <em>of</em> a reflect, which is the anti-loop rule and is asserted by its absence.
/// </remarks>
public sealed class ReflectDamageTests
{
    /// <summary>ATK 100 against DEF 120 at level 1 — `05` §4's sanity check, so the basis is 53.85.</summary>
    private const double Basis = 53.85;

    /// <summary>
    /// 🔒 `05` §4 step 10 — the reflect is <c>basis × defender.THORN</c>, off the <b>pre-absorption</b>
    /// basis.
    /// </summary>
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
    /// 🔒 `05` §4.2 / R4 — <c>REFLECT</c> <em>"adds to <c>THORN</c> for its duration"</em>, and the
    /// aggregated <c>THORNS</c> stat and the additions are one number.
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
    /// 🔒 The reflect is <em>"reduced by the receiver's <c>DR%</c> and
    /// <c>DAMAGE_TAKEN_MULT</c>"</em> — and by nothing else the attack pipeline applies.
    /// </summary>
    /// <remarks>
    /// The receiver's DEF is 5000 and its PEN is irrelevant, because a reflect takes no mitigation:
    /// if step 3 ran on it, the 26.925 would come out near zero. The 0.9 DR row is separately the
    /// <b>no-floor</b> proof — 2.6925 is well under 10% of any <c>raw</c> in this fight, and `05`
    /// §4's floor would have lifted it.
    /// </remarks>
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

    /// <summary>🔒 <em>"absorbed by the receiver's wards"</em>.</summary>
    /// <remarks>
    /// The second row is the negative control that the pool is really consulted rather than merely
    /// present: a 10-point ward leaves 16.925 to reach HP, which no other rule in `05` §4 would
    /// produce.
    /// </remarks>
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
    /// 🔒 The anti-loop rule — <em>"never triggers the receiver's thorns in turn"</em> (`05` §3.1
    /// lists it beside the death-save rule).
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Both sides carry thorns</b>, which is the only shape in which the rule is observable.
    /// The defender's 0.5 reflects 26.925 at the hero; if that reflect triggered the hero's own 0.5,
    /// the enemy would take a further 13.46 — so the enemy's HP is the assertion, and it must show
    /// exactly the attack and nothing after it. Without the rule this fight does not terminate at
    /// all until the cascade guard trips.
    /// </remarks>
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

    /// <summary>🔒 <em>"triggers no lifesteal"</em>.</summary>
    /// <remarks>
    /// The defender has both thorns and lifesteal and is at 1 HP, so a reflect that leeched would be
    /// unmissable. The attacker's own lifesteal is 0, so the only heal `05` §4 could produce here is
    /// the one the rule forbids — and the log carries none.
    /// </remarks>
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
    /// 🔒 <em>"no dodge, crit, block"</em> — the reflect takes <b>no draw</b>, so a thorned fight
    /// consumes exactly the attack's three.
    /// </summary>
    /// <remarks>
    /// 🔒 The determinism half of the anti-loop rule. A reflect that ran the three steps would make
    /// the stream's position depend on whether the defender happened to carry thorns, and `11` §6
    /// re-runs the duel from the seed alone.
    /// </remarks>
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

    /// <summary>
    /// 🔒 `05` §7 — the reflect <em>"is itself a <c>Hit</c> on the attacker"</em>
    /// (<c>CombatEventType</c>'s emission sequence), so the replayer has a number to draw.
    /// </summary>
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

        // 🔒 The reflect names the THORNS HOLDER as its source. `05` §8 makes the log the replay, so
        // a Hit with CombatActor.None here would draw as damage arriving from nowhere.
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

    /// <summary>`05` §1's block — see <see cref="AttackPipelineBench.Stats"/> for the two defaults.</summary>
    private static ActorStats Stats(double maxHp, (StatId Stat, double Value)[] rest) =>
        AttackPipelineBench.Stats(maxHp, rest);
}
