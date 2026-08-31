using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// The claims no <see cref="SimulationResult"/> can report and no public entry point can provoke.
/// <see cref="DamageResolutionTests"/> is where the ten attack-pipeline steps are pinned.
/// </summary>
/// <remarks>
/// Three groups: draw discipline (<c>DeterministicRng.Position</c> appears in no
/// <c>CombatEvent</c>, yet a spent or skipped draw desynchronises a client for the rest of the
/// fight); the non-attack damage routes, whose whole observable difference is the ward pool's
/// balance; and two guards against a caller the public entry points cannot be.
/// <para>
/// These use <see cref="AttackPipelineBench"/>, which reaches through <c>BattlePlan.Seams</c> to the
/// internal pipeline inside a real fight — the sanctioned last resort, not the default.
/// </para>
/// </remarks>
public sealed class AttackPipelineInternalTests
{
    private const double Atk = 100.0;
    private const double SanityCheckDef = 120.0;

    // ══════════════════════════════════════════════════════ the draw discipline

    /// <summary>
    /// A resolved attack advances the RNG stream by exactly 3, a dodged one by 1. Both rows are
    /// needed: always-three passes the first, lazy-draw passes the second.
    /// </summary>
    [Theory]
    [InlineData(0.0, 3UL)]
    [InlineData(1.0, 1UL)]
    public void A_resolved_attack_draws_three_times_and_a_dodged_one_draws_once(
        double dodge, ulong expectedDraws) =>
        Fight(
            attacker: Block(500.0, (StatId.ATK, Atk)),
            defender: Block(5000.0, (StatId.DEF, SanityCheckDef), (StatId.DODGE, dodge)),
            body: p =>
            {
                p.Services.Rng.Position.ShouldBe(
                    0UL, "nothing in this fight draws before the probe does");

                p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X");

                p.Services.Rng.Position.ShouldBe(expectedDraws);
            });

    /// <summary>
    /// <c>FORCE_CRIT_NEXT</c> decides the crit outcome and never skips its draw: a skipped draw
    /// would make the RNG position a function of the attacker's flow state, desyncing any client
    /// that had not observed the charge.
    /// </summary>
    [Fact]
    public void A_forced_crit_crits_without_skipping_step_4s_draw() =>
        Fight(
            attacker: Block(500.0, (StatId.ATK, Atk), (StatId.CRIT, 0.0), (StatId.CDMG, 0.5)),
            defender: Block(5000.0, (StatId.DEF, SanityCheckDef)),
            body: p =>
            {
                p.Hero.Flow.GrantForcedCrits(1);

                p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X").Crit.ShouldBeTrue();
                p.Services.Rng.Position.ShouldBe(3UL, "step 4 drew even though the outcome was fixed");

                // The charge is spent: the next swing is an ordinary one, and still draws three.
                p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X").Crit.ShouldBeFalse();
                p.Services.Rng.Position.ShouldBe(6UL);
            });

    // ══════════════════════════════════════════════════════ the other two damage routes

    /// <summary><c>DAMAGE_TRUE</c> bypasses everything: no DR%, damage-taken multiplier, floor or wards.</summary>
    [Fact]
    public void DAMAGE_TRUE_bypasses_DR_the_damage_taken_multiplier_and_the_ward_pool() =>
        Fight(
            defender: Block(5000.0, (StatId.DEF, SanityCheckDef), (StatId.DR_PCT, 0.5)),
            body: p =>
            {
                p.Enemy().Flow.AddDamageTakenMultiplier(0.5, "EFF_A");
                p.Pipeline.GrantWard(p.Enemy(), 1000.0, null, "EFF_W");

                p.Pipeline.DealTrueDamage(p.Enemy(), 100.0, "EFF_TRUE");

                p.Enemy().CurrentHp.ShouldBe(4900.0, "none of the three touched it");
                p.Enemy().Wards.Total.ShouldBe(1000.0, "the pool is untouched, not merely bypassed");
                p.Services.Rng.Position.ShouldBe(0UL, "a non-attack damage event draws nothing");
            });

    /// <summary>
    /// <c>DAMAGE_MAXHP_PCT</c>: DR% and the damage-taken multiplier do apply and wards absorb,
    /// unless the caller marks it as bypassing wards — a self-inflicted cost (like a drawback perk)
    /// must not be silently deleted by the holder's own ward.
    /// </summary>
    [Theory]
    [InlineData(false, 5000.0, 950.0)]   // absorbed: the pool pays the 50, HP is untouched
    [InlineData(true, 4950.0, 1000.0)]   // bypass: the drawback reaches HP, the pool is untouched
    public void DAMAGE_MAXHP_PCT_applies_DR_and_is_absorbed_unless_it_is_a_self_inflicted_cost(
        bool bypassesWards, double expectedHp, double expectedWard) =>
        Fight(
            defender: Block(5000.0, (StatId.DEF, SanityCheckDef), (StatId.DR_PCT, 0.5)),
            body: p =>
            {
                p.Pipeline.GrantWard(p.Enemy(), 1000.0, null, "EFF_W");

                // 100, halved by DR% to 50.
                p.Pipeline.DealMaxHpPctDamage(p.Enemy(), 100.0, bypassesWards, "CP_BLOOD_PRICE", source: null);

                p.Enemy().CurrentHp.ShouldBe(expectedHp);
                p.Enemy().Wards.Total.ShouldBe(expectedWard);
                p.Services.Rng.Position.ShouldBe(0UL, "no dodge, no crit, no block — so no draws");
            });

    // ══════════════════════════════════════════════════════ refusals

    /// <summary>
    /// A non-finite number is refused by name rather than carried through the pipeline: a NaN
    /// compares false against every bound, so it would otherwise surface three layers later naming
    /// the serialiser instead of the effect that produced it.
    /// </summary>
    [Fact]
    public void A_non_finite_number_is_refused_naming_the_effect_that_produced_it() =>
        Fight(body: p =>
        {
            var refused = Should.Throw<EffectContextException>(
                () => p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), double.NaN, "PK_GAMBLER"));

            refused.Message.ShouldContain("PK_GAMBLER", Case.Sensitive);
            refused.Message.ShouldContain("AttackMultiplier", Case.Sensitive);

            Should.Throw<EffectContextException>(
                () => p.Pipeline.Heal(p.Enemy(), double.PositiveInfinity, "PK_TRANSFUSION"))
                .Message.ShouldContain("PK_TRANSFUSION", Case.Sensitive);
        });

    /// <summary>A battle has one roster and one view of it — a foreign actor view is refused.</summary>
    [Fact]
    public void A_foreign_actor_view_is_refused() =>
        Fight(body: p =>
            Should.Throw<InvalidOperationException>(
                    () => p.Pipeline.Heal(new ForeignView(), 1.0, "EFF_X"))
                .Message.ShouldContain("one roster", Case.Sensitive));

    /// <summary>An <c>IEffectActorView</c> that is not a <c>BattleActor</c>.</summary>
    private sealed class ForeignView : IEffectActorView
    {
        public string Id => "FOREIGN";

        public int Index => 99;

        public BattleSide Side => BattleSide.ENEMY;

        public EffectActorKind Kind => EffectActorKind.ENEMY;

        public bool IsAlive => true;

        public double CurrentHp => 1.0;

        public double MaxHp => 1.0;

        public bool IsElite => false;

        public bool IsBoss => false;

        public bool IsSummon => false;

        public string? OwnerId => null;

        public int StatusStacks(string statusId) => 0;
    }

    // ══════════════════════════════════════════════════════ helpers

    private static ActorStats Block(double maxHp, params (StatId Stat, double Value)[] rest) =>
        AttackPipelineBench.Stats(maxHp, rest);

    /// <summary>One probe fight: a hero, one enemy, nobody swinging, the probe in slot 1.</summary>
    private static AttackProbe Fight(
        Action<AttackProbe> body,
        ActorStats? attacker = null,
        ActorStats? defender = null,
        int attackerLevel = 1) =>
        AttackPipelineBench.Run(
            new[]
            {
                BattleTestBench.Hero(attacker ?? Block(500.0, (StatId.ATK, Atk)), attackerLevel),
                BattleTestBench.Enemy(0, defender ?? Block(5000.0, (StatId.DEF, SanityCheckDef))),
            },
            body,
            battleSeed: 1UL);
}
