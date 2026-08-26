using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// The conditional standing-effect bucket: a standing stat effect may carry a context gate — a
/// condition reading the current target or the attacker — and is re-aggregated per target inside one
/// attack resolution.
/// </summary>
/// <remarks>
/// <para>
/// The rule under test is subject-presence, not satisfiability: a standing effect whose condition
/// reads the current target (or the attacker) is active only in an evaluation context that carries
/// that subject, and there it is evaluated normally. Ambient re-aggregation carries neither subject,
/// so such an effect contributes nothing between attacks; within one attack resolution the
/// attacker's stats are re-aggregated with the defender as current target and the defender's with
/// the attacker in context.
/// </para>
/// <para>
/// Before this bucket existed, both halves failed in different ways, and both were verified before
/// fixing: a <c>TARGET_IS_ELITE</c>-gated standing effect made every battle throw during
/// re-aggregation (the ambient context carries no current target and the condition evaluator
/// refuses to answer without one), and an <c>ATTACKER_IS_*</c>-gated standing damage-taken
/// multiplier read false on every ambient pass and was silently inert.
/// </para>
/// </remarks>
public sealed class ContextGatedStandingEffectTests
{
    private const double HeroAtk = 100.0;

    private const double EnemyAtk = 100.0;

    /// <summary>+25% damage — under the multiplier reading, ×(1 + 0.25) on the base 1.0.</summary>
    private const double DamageBonus = 0.25;

    /// <summary>−15% damage taken — ×(1 − 0.15) on the damage-taken multiplier's base 1.0.</summary>
    private const double DamageReduction = -0.15;

    // ────────────────────────────────────────────────── the target-gated half (damage vs elites)

    /// <summary>D47 claim 1: a target-gated standing effect used to THROW during re-aggregation.</summary>
    /// <remarks>
    /// The fight must complete and the gate must simply not contribute against a target outside it —
    /// a hero carrying a damage-vs-Elites affix into an ordinary fight is the common case, not the
    /// edge.
    /// </remarks>
    [Fact]
    public void A_battle_with_a_target_gated_standing_effect_completes_and_the_gate_stays_off_target()
    {
        var result = HeroHits(elite: false, TargetGatedDamage());

        result.ShouldBe(
            HeroAtk,
            "the target is no elite, so the gated +25% contributes nothing and the hit carries the " +
            "bare attack — before the bucket existed this fight did not even complete: ambient " +
            "re-aggregation carries no current target and threw out of the condition evaluator");
    }

    /// <summary>The same effect against a target inside the gate applies in full.</summary>
    [Fact]
    public void A_target_gated_damage_bonus_applies_against_a_target_satisfying_the_gate()
    {
        HeroHits(elite: true, TargetGatedDamage()).ShouldBe(
            HeroAtk * (1.0 + DamageBonus),
            "against an elite the gate holds, so the standing +25% joins the attacker's " +
            "re-aggregation for exactly this swing: 100 × 1.25");
    }

    /// <summary>The control: an ungated bonus of the same magnitude applies against anyone.</summary>
    /// <remarks>
    /// Without this arm, the two cases above could be satisfied by a pipeline that dropped every
    /// <c>DMG_PCT</c> effect on the floor against non-elites for some unrelated reason.
    /// </remarks>
    [Fact]
    public void An_ungated_damage_bonus_still_applies_against_any_target()
    {
        var ungated = StandingEffect("TEST_UNGATED_DMG", StatId.DMG_PCT, DamageBonus, condition: null);

        HeroHits(elite: false, ungated).ShouldBe(
            HeroAtk * (1.0 + DamageBonus),
            "an ungated standing effect is the path that always worked, and it must not have moved");
    }

    /// <summary>
    /// The gate is subject-presence, not satisfiability: a <c>not(TARGET_IS_ELITE)</c> gate is also
    /// inactive in a target-less context, and evaluates normally where a target exists.
    /// </summary>
    /// <remarks>
    /// This is the arm that separates the two readings. Under a satisfiability rule, an ambient pass
    /// would answer <c>not(…)</c> as true with no target in hand and apply an anti-elite bonus to the
    /// whole fight; under the subject-presence rule the tree reads a target, so it waits for one.
    /// </remarks>
    [Theory]
    [InlineData(false, HeroAtk * (1.0 + DamageBonus))]
    [InlineData(true, HeroAtk)]
    public void A_negated_target_gate_applies_exactly_against_targets_outside_it(
        bool elite, double expectedHit)
    {
        var antiElite = StandingEffect(
            "TEST_ANTI_ELITE_DMG",
            StatId.DMG_PCT,
            DamageBonus,
            EffectCondition.Not(EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.TARGET_IS_ELITE,
                Comparator = ConditionComparator.EQ,
                Flag = true,
            })));

        HeroHits(elite, antiElite).ShouldBe(
            expectedHit,
            "a negated gate applies exactly against targets outside it — never ambiently, and " +
            "never against the target it names");
    }

    /// <summary>
    /// The re-aggregation is per target within one attack resolution, not once per tick: a live HP
    /// gate crosses mid-fight and only the swings after the crossing carry the bonus.
    /// </summary>
    /// <remarks>
    /// The executioner shape. The target opens at full health (first swing unboosted), the first
    /// hit takes it below the threshold, and the second swing — twenty ticks later, against the
    /// same target — reads the target's LIVE hit points and lands boosted.
    /// </remarks>
    [Fact]
    public void A_live_hp_gate_is_read_at_swing_time_against_the_targets_current_hp()
    {
        var executioner = StandingEffect(
            "TEST_EXECUTIONER_DMG",
            StatId.DMG_PCT,
            DamageBonus,
            EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.TARGET_HP_PCT,
                Comparator = ConditionComparator.LT,
                Value = 0.75,
            }));

        var hero = BattleTestBench.Hero(
            AttackPipelineBench.Stats(1_000_000.0, (StatId.ATK, HeroAtk)),
            effects: [new HeldEffect(executioner)]);

        // 300 Max HP: the first hit of 100 leaves 200/300 ≈ 0.6667 < 0.75, and the fight ends with
        // the target still standing so both swings are observable. ATK 0: the enemy soaks silently.
        var enemy = BattleTestBench.Enemy(0, AttackPipelineBench.Stats(300.0));

        var result = CombatSimulator.Simulate(BattleTestBench.Plan(
            [hero, enemy],
            rules: CombatRules.PvE with { MaxTicks = 21 }));

        result.ValuesBy(CombatEventType.Hit, CombatActor.Hero).ShouldBe(
            new[] { HeroAtk, HeroAtk * (1.0 + DamageBonus) },
            "the gate reads the target's live HP at each swing: full health first (no bonus), " +
            "below three quarters at the second swing (boosted) — a gate read once per fight or " +
            "at plan time would produce two equal hits");
    }

    // ────────────────────────────────────────────── the attacker-gated half (DR vs elites/bosses)

    /// <summary>D47 claim 2: an attacker-gated standing DR used to be silently inert.</summary>
    /// <remarks>
    /// <c>ATTACKER_IS_*</c> reads false in a context with no attacker, which was every ambient
    /// re-aggregation — so the effect was filtered on every pass and no hit was ever reduced. The
    /// defender's stats must instead re-aggregate with the attacker in context at the moment a hit
    /// resolves.
    /// </remarks>
    [Fact]
    public void An_attacker_gated_standing_dr_reduces_a_hit_from_an_attacker_inside_the_gate()
    {
        EnemyHitOnHero(EliteEnemy(), AttackerGatedReduction()).ShouldBe(
            EnemyAtk * (1.0 + DamageReduction),
            "the attacker is an elite, so the −15% joins the defender's re-aggregation for this " +
            "hit: 100 × 0.85 — before the bucket existed this read 100, the silent-inertness half " +
            "of D47");
    }

    /// <summary>The same gate names bosses too, through the <c>any</c> combinator.</summary>
    [Fact]
    public void The_any_combinator_fires_the_same_gate_for_a_boss_attacker()
    {
        EnemyHitOnHero(BossEnemy(), AttackerGatedReduction()).ShouldBe(
            EnemyAtk * (1.0 + DamageReduction),
            "the gate is any(elite, boss), and this attacker satisfies its second arm");
    }

    /// <summary>A hit from an attacker outside the gate is not reduced.</summary>
    [Fact]
    public void An_attacker_outside_the_gate_hits_for_the_full_amount()
    {
        EnemyHitOnHero(OrdinaryEnemy(), AttackerGatedReduction()).ShouldBe(
            EnemyAtk,
            "an ordinary enemy satisfies neither arm of the gate, so the hit lands unreduced");
    }

    /// <summary>
    /// The re-aggregation is per attacker within one tick: two enemies swing in the same tick, and
    /// only the one inside the gate is reduced.
    /// </summary>
    /// <remarks>
    /// The arm that separates per-pair re-aggregation from a once-per-tick shortcut: both hits land
    /// in tick 0, so a defender block computed once for the tick would either reduce both or
    /// neither. Two ticks rather than one, so the ambient refresh after the swings also completes
    /// with the gated effect still held.
    /// </remarks>
    [Fact]
    public void Two_attackers_in_one_tick_are_gated_independently()
    {
        var hero = BattleTestBench.Hero(
            AttackPipelineBench.Stats(1_000_000.0, (StatId.ATK, HeroAtk)),
            effects: [new HeldEffect(AttackerGatedReduction())]);

        var elite = EliteEnemy();
        var ordinary = BattleTestBench.Enemy(
            1, AttackPipelineBench.Stats(1_000_000.0, (StatId.ATK, EnemyAtk)));

        var result = CombatSimulator.Simulate(BattleSimulationPlan(hero, elite, ordinary));

        result.ValuesBy(CombatEventType.Hit, CombatActor.Enemy(0)).ShouldBe(
            new[] { EnemyAtk * (1.0 + DamageReduction) },
            "the elite's hit is reduced by the gate");

        result.ValuesBy(CombatEventType.Hit, CombatActor.Enemy(1)).ShouldBe(
            new[] { EnemyAtk },
            "the ordinary enemy's hit, in the same tick, is not — a defender block computed once " +
            "per tick could not tell the two apart");
    }

    /// <summary>A two-tick plan over the given roster.</summary>
    private static BattlePlan BattleSimulationPlan(ActorPlan hero, params ActorPlan[] enemies) =>
        BattleTestBench.Plan(
            new[] { hero }.Concat(enemies),
            rules: CombatRules.PvE with { MaxTicks = 2 });

    /// <summary>The control: an ungated reduction of the same magnitude reduces every hit.</summary>
    [Fact]
    public void An_ungated_reduction_still_reduces_a_hit_from_anyone()
    {
        var ungated = StandingEffect("TEST_UNGATED_DR", StatId.DR_PCT, DamageReduction, condition: null);

        EnemyHitOnHero(OrdinaryEnemy(), ungated).ShouldBe(
            EnemyAtk * (1.0 + DamageReduction),
            "an ungated standing DR is the path that always worked, and it must not have moved");
    }

    // ──────────────────────────────────────────────────────────── the off-gate byte-identity pin

    /// <summary>
    /// Off its gate, a context-gated standing effect leaves the fight byte-identical to a fight
    /// that never held it.
    /// </summary>
    /// <remarks>
    /// <see cref="SimulationResult.LogHash"/> covers every event of the fight, so this is the
    /// strongest single statement that the gate contributes nothing outside its subject — no stat
    /// drift, no extra event, no changed draw.
    /// </remarks>
    [Fact]
    public void Off_its_gate_a_context_gated_effect_leaves_the_log_hash_untouched()
    {
        var without = Fight(OrdinaryEnemy());
        var with = Fight(OrdinaryEnemy(), TargetGatedDamage(), AttackerGatedReduction());

        with.LogHash.ShouldBe(
            without.LogHash,
            "neither gate holds against an ordinary enemy, so holding both effects must not move a " +
            "single byte of the fight");
    }

    // ────────────────────────────────────────────────────────────────────────────────── fixtures

    /// <summary>The damage-vs-Elites shape: a standing +25% damage gated on the target being an elite.</summary>
    private static EffectDefinition TargetGatedDamage() =>
        StandingEffect(
            "TEST_GATED_DMG_VS_ELITES",
            StatId.DMG_PCT,
            DamageBonus,
            EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.TARGET_IS_ELITE,
                Comparator = ConditionComparator.EQ,
                Flag = true,
            }));

    /// <summary>The Ironvow shape: a standing −15% damage taken gated on any(elite, boss) attacker.</summary>
    private static EffectDefinition AttackerGatedReduction() =>
        StandingEffect(
            "TEST_GATED_DR_VS_ELITES",
            StatId.DR_PCT,
            DamageReduction,
            EffectCondition.Any(
                EffectCondition.Of(new ConditionTerm
                {
                    Fn = ConditionFunction.ATTACKER_IS_ELITE,
                    Comparator = ConditionComparator.EQ,
                    Flag = true,
                }),
                EffectCondition.Of(new ConditionTerm
                {
                    Fn = ConditionFunction.ATTACKER_IS_BOSS,
                    Comparator = ConditionComparator.EQ,
                    Flag = true,
                })));

    private static EffectDefinition StandingEffect(
        string id, StatId stat, double value, EffectCondition? condition) =>
        new()
        {
            Id = id,
            Op = EffectOp.STAT_ADD_PCT,
            Stat = StatSelector.Of(stat),
            Trigger = EffectDefaults.Always,
            Target = EffectTarget.SELF,
            Condition = condition,
            Value = value,
        };

    private static ActorPlan OrdinaryEnemy() => Enemy();

    private static ActorPlan EliteEnemy() => Enemy() with { IsElite = true };

    private static ActorPlan BossEnemy() => Enemy() with { IsBoss = true };

    /// <summary>An enemy that swings for a flat 100 and soaks whatever the hero deals.</summary>
    private static ActorPlan Enemy() =>
        BattleTestBench.Enemy(0, AttackPipelineBench.Stats(1_000_000.0, (StatId.ATK, EnemyAtk)));

    /// <summary>One tick of PvE: both sides swing exactly once at tick 0.</summary>
    /// <remarks>
    /// A boss-flagged enemy needs a phase controller to enter the fight at all; the recording one
    /// satisfies that requirement without registering any phase mechanic, so the boss arm measures
    /// exactly what the elite arm does.
    /// </remarks>
    private static SimulationResult Fight(ActorPlan enemy, params EffectDefinition[] heroEffects)
    {
        var hero = BattleTestBench.Hero(
            AttackPipelineBench.Stats(1_000_000.0, (StatId.ATK, HeroAtk)),
            effects: heroEffects.Select(e => new HeldEffect(e)).ToArray());

        return CombatSimulator.Simulate(BattleTestBench.Plan(
            [hero, enemy],
            enemy.IsBoss ? PhasedButUnscripted : null,
            rules: CombatRules.PvE with { MaxTicks = 1 }));
    }

    /// <summary>The real pipeline, plus the phase controller a boss requires — no phase registered.</summary>
    private static BattleSeams PhasedButUnscripted(BattleServices services) =>
        BattleSeams.For(services) with { Phases = new RecordingPhases(services) };

    /// <summary>The hero's one hit on the enemy.</summary>
    private static double HeroHits(bool elite, params EffectDefinition[] heroEffects) =>
        Fight(elite ? EliteEnemy() : OrdinaryEnemy(), heroEffects)
            .ValuesBy(CombatEventType.Hit, CombatActor.Hero)
            .Single();

    /// <summary>The enemy's one hit on the hero, with the hero holding the given effects.</summary>
    private static double EnemyHitOnHero(ActorPlan enemy, params EffectDefinition[] heroEffects) =>
        Fight(enemy, heroEffects)
            .ValuesBy(CombatEventType.Hit, CombatActor.Enemy(0))
            .Single();
}
