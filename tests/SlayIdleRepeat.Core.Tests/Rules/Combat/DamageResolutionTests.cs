using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>The ten steps of the attack pipeline, one case per step, read out of a public fight.</summary>
/// <remarks>
/// Every case runs <see cref="CombatSimulator.SimulateDuel"/> and asserts on the events in
/// <see cref="SimulationResult.Log"/>. What no log can show is in
/// <see cref="AttackPipelineInternalTests"/>.
/// <para>
/// Ceilings are authored, not switched off: a case needing a certainty passes a caps override
/// authoring that ceiling at 1.0. <c>NextDouble()</c> is in <c>[0,1)</c>, so 1.0 always fires and
/// 0.0 never does.
/// </para>
/// </remarks>
public sealed class DamageResolutionTests
{
    /// <summary>ATK 100 makes step 2's <c>raw</c> equal the multiplier, in percent.</summary>
    private const double Atk = 100.0;

    private const double SanityCheckDef = 120.0;

    /// <summary>Sanity check: <c>120 / (120 + 120 + 20 × 1)</c> = 0.4615, so 53.85 lands.</summary>
    private const double SanityCheckHit = 53.85;

    private static IReadOnlyDictionary<StatId, decimal> Uncapped(StatId stat) =>
        new Dictionary<StatId, decimal> { [stat] = 1.0m };

    // ══════════════════════════════════════════════════════ steps 1-5: the three draws

    /// <summary>A dodge logs MISS and ends the attack — nothing after it runs.</summary>
    [Fact]
    public void Step_1_a_dodge_logs_MISS_and_ends_the_attack()
    {
        var fight = PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk)),
            Block(5000.0, (StatId.DEF, SanityCheckDef), (StatId.DODGE, 1.0)),
            capOverrides: Uncapped(StatId.DODGE));

        fight.AttackerSequence().ShouldBe(new[] { CombatEventType.Attack, CombatEventType.Miss });
        fight.EventsBy(CombatEventType.Hit, CombatActor.Hero).ShouldBeEmpty(
            "step 1 returns, so nothing downstream of it emits");
    }

    /// <summary>The negative control: at <c>DODGE = 0</c> the same swing lands.</summary>
    [Fact]
    public void Step_1_at_zero_dodge_the_swing_always_lands()
    {
        var fight = PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk)),
            Block(5000.0, (StatId.DEF, SanityCheckDef), (StatId.DODGE, 0.0)));

        fight.EventsBy(CombatEventType.Miss, CombatActor.Hero).ShouldBeEmpty();
        fight.AttackerHit().ShouldBe(SanityCheckHit);
    }

    /// <summary>
    /// Steps 2 and 3's mitigation formula. The level row is the discriminating one: a pipeline
    /// dropping the <c>20 × attackerLevel</c> term would still pass the level-1 rows.
    /// </summary>
    [Theory]
    [InlineData(120.0, 1, 53.85)]     // mitigation 120/260 = 0.4615
    [InlineData(600.0, 1, 18.92)]     // mitigation 600/740 = 0.8108
    [InlineData(120.0, 10, 72.73)]    // 120/(120+120+200) = 0.2727 — the level term, alone
    public void Steps_2_and_3_are_05_4s_own_sanity_check(double def, int level, double expected) =>
        PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk)),
            Block(50_000.0, (StatId.DEF, def)),
            attackerLevel: level)
            .AttackerHit()
            .ShouldBe(expected);

    /// <summary>
    /// The two mitigation dials come from content data, not a literal, and travel from the document
    /// into the arithmetic through the public entry point.
    /// </summary>
    [Theory]
    [InlineData(120.0, 20.0, 53.85)]   // the shipped pair: 120/(120+120+20)
    [InlineData(240.0, 40.0, 70.0)]    // doubled: 120/(120+240+40) = 0.30
    [InlineData(60.0, 10.0, 36.84)]    // halved:  120/(120+60+10)  = 0.6316
    public void Step_3_reads_both_dials_from_the_content_document_and_not_from_a_literal(
        double flat, double perLevel, double expected) =>
        PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk)),
            Block(50_000.0, (StatId.DEF, SanityCheckDef)),
            mitigation: ((decimal)flat, (decimal)perLevel))
            .AttackerHit()
            .ShouldBe(expected);

    /// <summary>A crit multiplies damage by <c>(1 + attacker.CDMG)</c>.</summary>
    [Fact]
    public void Step_4_a_crit_multiplies_by_one_plus_CDMG()
    {
        var fight = PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk), (StatId.CRIT, 1.0), (StatId.CDMG, 0.5)),
            Block(50_000.0, (StatId.DEF, SanityCheckDef)),
            capOverrides: Uncapped(StatId.CRIT));

        fight.AttackerHit().ShouldBe(80.775, "53.85 x 1.5");
        fight.AttackerSequence().ShouldBe(new[]
        {
            CombatEventType.Attack, CombatEventType.Crit, CombatEventType.Hit,
        });
    }

    /// <summary>A block halves the hit.</summary>
    [Fact]
    public void Step_5_a_block_halves_the_hit()
    {
        var fight = PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk)),
            Block(50_000.0, (StatId.DEF, SanityCheckDef), (StatId.BLOCK, 1.0)),
            capOverrides: Uncapped(StatId.BLOCK));

        fight.AttackerHit().ShouldBe(26.925, "53.85 x 0.5");
        fight.AttackerSequence().ShouldBe(new[]
        {
            CombatEventType.Attack, CombatEventType.Block, CombatEventType.Hit,
        });
    }

    /// <summary>
    /// The three draws are taken in step order — dodge, crit, block. Thresholds are picked strictly
    /// between two adjacent draws of the same seeded stream, so reading the wrong draw inverts the
    /// outcome.
    /// </summary>
    [Fact]
    public void The_three_draws_are_taken_in_dodge_then_crit_then_block_order()
    {
        var stream = new DeterministicRng(PublicFightBench.Seed, RngStreams.Combat);
        var dodgeDraw = stream.NextDouble();
        var critDraw = stream.NextDouble();
        var blockDraw = stream.NextDouble();

        var dodgeThreshold = Between(dodgeDraw, critDraw);
        var critBlockThreshold = Between(critDraw, blockDraw);

        var dodged = PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk)),
            Block(50_000.0, (StatId.DEF, SanityCheckDef), (StatId.DODGE, dodgeThreshold)),
            capOverrides: Uncapped(StatId.DODGE))
            .EventsBy(CombatEventType.Miss, CombatActor.Hero)
            .Count == 1;

        dodged.ShouldBe(
            dodgeDraw < dodgeThreshold, "step 1 reads draw 0; reading draw 1 inverts this");

        (critDraw < critBlockThreshold).ShouldNotBe(
            blockDraw < critBlockThreshold,
            "the case only discriminates if the two draws fall on opposite sides");

        var fight = PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk), (StatId.CRIT, critBlockThreshold)),
            Block(50_000.0, (StatId.DEF, SanityCheckDef), (StatId.BLOCK, critBlockThreshold)),
            capOverrides: new Dictionary<StatId, decimal>
            {
                [StatId.CRIT] = 1.0m,
                [StatId.BLOCK] = 1.0m,
            });

        (fight.EventsBy(CombatEventType.Crit, CombatActor.Hero).Count == 1).ShouldBe(
            critDraw < critBlockThreshold, "step 4 reads draw 1");
        (fight.EventsBy(CombatEventType.Block, CombatActor.Hero).Count == 1).ShouldBe(
            blockDraw < critBlockThreshold, "step 5 reads draw 2");
    }

    /// <summary>
    /// <c>FORCE_CRIT_NEXT</c> decides the crit-step outcome. That it still draws is
    /// <c>AttackPipelineInternalTests</c>' half — the RNG position is not in the log.
    /// </summary>
    [Fact]
    public void A_FORCE_CRIT_NEXT_charge_crits_a_swing_the_attackers_own_CRIT_never_would()
    {
        var fight = PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk), (StatId.CRIT, 0.0), (StatId.CDMG, 0.5)),
            Block(50_000.0, (StatId.DEF, SanityCheckDef)),
            attackerEffects: [Charge("EFF_FORCE_CRIT", EffectOp.FORCE_CRIT_NEXT, null)],
            durationSeconds: 1.05);

        fight.ValuesBy(CombatEventType.Hit, CombatActor.Hero).ShouldBe(
            new[] { 80.775, SanityCheckHit },
            "the charge crits the first swing (53.85 x 1.5) and is then spent");

        fight.EventsBy(CombatEventType.Crit, CombatActor.Hero).Count.ShouldBe(
            1, "CRIT is 0, so nothing but the charge could produce one");
    }

    // ══════════════════════════════════════════════════════ steps 6-8

    /// <summary>
    /// `16` D46: <c>DMG%</c> is a multiplier consumed bare — <c>raw = ATK × attackMultiplier ×
    /// DMG%</c>, never <c>× (1 + DMG%)</c>.
    /// </summary>
    /// <remarks>
    /// 1.15 is the discriminating row: bare gives 61.9275 (115 × 0.5385), the additive reading
    /// 115.7775. The 0.5 row is a damage CUT below identity, unreachable under <c>(1 + x)</c>.
    /// </remarks>
    [Theory]
    [InlineData(1.15, 61.9275)]
    [InlineData(0.5, 26.925)]
    [InlineData(1.0, 53.85)]
    public void Step_2_consumes_DMG_PCT_bare_as_the_damage_multiplier(
        double dmgPct, double expected) =>
        PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk), (StatId.DMG_PCT, dmgPct)),
            Block(50_000.0, (StatId.DEF, SanityCheckDef)))
            .AttackerHit()
            .ShouldBe(expected);

    /// <summary>
    /// `16` D46: <c>DR%</c> is the damage-taken multiplier consumed bare — <c>dmg × DR%</c>, never
    /// <c>dmg × (1 − DR%)</c>.
    /// </summary>
    /// <remarks>
    /// Every row separates the two readings: bare 0.45 gives 24.2325 where subtractive gives
    /// 29.6175; 0.9 gives 48.465 vs 5.385; 1.2 amplifies (64.62), which <c>(1 − x)</c> cannot
    /// produce at all. 0.5 is deliberately absent — it is the readings' fixed point.
    /// </remarks>
    [Theory]
    [InlineData(0.45, 24.2325)]
    [InlineData(0.9, 48.465)]
    [InlineData(1.2, 64.62)]
    public void Step_6_consumes_DR_PCT_bare_as_the_damage_taken_multiplier(
        double dr, double expected) =>
        PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk)),
            Block(50_000.0, (StatId.DEF, SanityCheckDef), (StatId.DR_PCT, dr)))
            .AttackerHit()
            .ShouldBe(expected);

    /// <summary>
    /// Both the bare <c>DR%</c> multiplier and the <c>DAMAGE_TAKEN_MULT</c> product apply together —
    /// D46 re-signs the stat and does NOT merge it into the op. The three rows separate DR alone,
    /// the product alone, and both.
    /// </summary>
    [Theory]
    [InlineData(0.9, 1.0, 1.0, 48.465)]
    [InlineData(1.0, 0.5, 0.5, 13.4625)]
    [InlineData(0.9, 0.5, 1.0, 24.2325)]
    public void Step_6_applies_both_DR_and_the_DAMAGE_TAKEN_MULT_product(
        double dr, double firstMult, double secondMult, double expected) =>
        PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk)),
            Block(50_000.0, (StatId.DEF, SanityCheckDef), (StatId.DR_PCT, dr)),
            defenderEffects:
            [
                Charge("EFF_A", EffectOp.DAMAGE_TAKEN_MULT, firstMult),
                Charge("EFF_B", EffectOp.DAMAGE_TAKEN_MULT, secondMult),
            ])
            .AttackerHit()
            .ShouldBe(expected);

    /// <summary>
    /// Step 9 floors the aggregated <c>DR%</c> at the authored damage-taken floor — `05` §1's 0.60
    /// reduction cap re-expressed under D46 as <c>1.0 − 0.6 = 0.4</c>.
    /// </summary>
    /// <remarks>
    /// −0.75 aggregates to 1.0 × 0.25, under the floor, so the fight reads 0.4 (21.54). −0.45
    /// lands at 0.55, above the floor, and passes through untouched (29.6175) — the negative
    /// control that separates a floor from a constant.
    /// </remarks>
    [Theory]
    [InlineData(-0.75, 21.54)]
    [InlineData(-0.45, 29.6175)]
    public void Step_9_floors_the_aggregated_damage_taken_multiplier(
        double pctAdd, double expected) =>
        PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk)),
            Block(50_000.0, (StatId.DEF, SanityCheckDef), (StatId.DR_PCT, 1.0)),
            defenderEffects:
            [
                PublicFightBench.Effect("EFF_DR", EffectOp.STAT_ADD_PCT, StatId.DR_PCT, pctAdd),
            ])
            .AttackerHit()
            .ShouldBe(expected);

    /// <summary>The floor binds a base authored below it too, not only an aggregation that sinks past it.</summary>
    [Fact]
    public void Step_9s_floor_binds_a_base_authored_below_it() =>
        PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk)),
            Block(50_000.0, (StatId.DEF, SanityCheckDef), (StatId.DR_PCT, 0.25)))
            .AttackerHit()
            .ShouldBe(21.54, "the floor reads 0.4 whatever authored the undershoot");

    /// <summary>
    /// The floor binds the stat, not the damage a fight computes from it — <c>DAMAGE_TAKEN_MULT</c>
    /// still multiplies below <c>0.4 × dmg</c>.
    /// </summary>
    [Fact]
    public void The_floor_binds_the_stat_and_not_the_DAMAGE_TAKEN_MULT_product() =>
        PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk)),
            Block(50_000.0, (StatId.DEF, SanityCheckDef), (StatId.DR_PCT, 0.25)),
            defenderEffects: [Charge("EFF_A", EffectOp.DAMAGE_TAKEN_MULT, 0.5)])
            .AttackerHit()
            .ShouldBe(10.77, "53.85 × 0.4 (floored stat) × 0.5 (the op, unfloored)");

    /// <summary>
    /// Damage never falls below 10% of raw. At DEF 100 000 mitigation alone would leave 0.12, so the
    /// 10.0 can only be the floor; the second row is the negative control below the floor's reach.
    /// </summary>
    [Theory]
    [InlineData(100_000.0, 10.0)]
    [InlineData(SanityCheckDef, SanityCheckHit)]
    public void Step_7_floors_the_hit_at_ten_percent_of_raw(double def, double expected) =>
        PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk)),
            Block(50_000.0, (StatId.DEF, def)))
            .AttackerHit()
            .ShouldBe(expected);

    /// <summary>
    /// The floor applies before absorption and is never re-applied after: a fully absorbed hit deals
    /// 0 HP. The floor is 10 — ward 4 leaves 6, ward 10 absorbs it exactly and leaves 0, ward 40 is
    /// over-shielded and still 0.
    /// </summary>
    [Theory]
    [InlineData(4.0, 6.0)]
    [InlineData(10.0, 0.0)]
    [InlineData(40.0, 0.0)]
    public void The_step_7_floor_applies_before_absorption_and_is_never_re_applied(
        double ward, double expectedHpLost) =>
        PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk)),
            Block(50_000.0, (StatId.DEF, 100_000.0)),
            defenderEffects: [Shield("EFF_W", ward)])
            .AttackerHit()
            .ShouldBe(expectedHpLost);

    /// <summary>
    /// A lifesteal attacker still heals off a fully-warded hit, and thorns still reflect it: both
    /// read the pre-absorption basis, not the post-absorption 0. Two ticks are needed because a heal
    /// is clamped by the room the healer has, and the first swing's thorns reflect opens that room.
    /// </summary>
    [Fact]
    public void Step_8s_basis_is_pre_absorption_and_both_lifesteal_and_thorns_read_it()
    {
        var fight = PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk), (StatId.LIFESTEAL, 0.4)),
            Block(50_000.0, (StatId.DEF, SanityCheckDef), (StatId.THORNS, 0.5)),
            defenderEffects: [Shield("EFF_W", 5_000.0)],
            durationSeconds: 1.05);

        fight.ValuesBy(CombatEventType.Hit, CombatActor.Hero).ShouldAllBe(
            v => v == 0.0, "the ward absorbs both swings whole");

        fight.ValuesBy(CombatEventType.Hit, CombatActor.Enemy(0)).ShouldContain(
            26.925, "thorns reflects 53.85 x 0.5 off a hit that cost the defender no HP");

        fight.ValuesBy(CombatEventType.Heal, CombatActor.None).ShouldContain(
            21.54, "lifesteal heals 53.85 x 0.4 off the same fully-absorbed hit");
    }

    // ══════════════════════════════════════════════════════ step 9, and the emission sequence

    /// <summary>
    /// The per-attack emission sequence, with every member present in one swing. This is step order,
    /// not code line order — and order is inside <c>LogHash</c>, so two honest readings that
    /// disagree here disagree on the tamper check for an identical fight.
    /// </summary>
    [Fact]
    public void The_per_attack_emission_sequence_is_attack_crit_block_wardbroken_then_hit()
    {
        var fight = PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk), (StatId.CRIT, 1.0), (StatId.CDMG, 0.5)),
            Block(50_000.0, (StatId.DEF, SanityCheckDef), (StatId.BLOCK, 1.0)),
            defenderEffects: [Shield("EFF_W", 1.0)],
            capOverrides: new Dictionary<StatId, decimal>
            {
                [StatId.CRIT] = 1.0m,
                [StatId.BLOCK] = 1.0m,
            });

        fight.AttackerSequence().ShouldBe(new[]
        {
            CombatEventType.Attack,
            CombatEventType.Crit,
            CombatEventType.Block,
            CombatEventType.Hit,
        });

        fight.Log.Select(e => e.Type).ShouldContain(
            CombatEventType.WardBroken, "the 1-point pool is emptied by the hit that lands on it");
    }

    /// <summary><c>WardBroken</c> fires only when damage empties the pool, not when it survives or was never granted.</summary>
    [Theory]
    [InlineData(1000.0, 0)]
    [InlineData(53.85, 1)]
    [InlineData(0.0, 0)]
    public void WardBroken_fires_only_when_damage_empties_the_pool(double ward, int expected) =>
        PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk)),
            Block(50_000.0, (StatId.DEF, SanityCheckDef)),
            defenderEffects: ward > 0.0 ? [Shield("EFF_W", ward)] : [])
            .Log.Count(e => e.Type == CombatEventType.WardBroken)
            .ShouldBe(expected);

    // ══════════════════════════════════════════════════════ AttackMultiplier

    /// <summary>
    /// The multiplier arrives unmultiplied by ATK — multiplying by ATK at the op too would square
    /// the attacker's power, which is why the rows are 0.4 and 2.0 rather than 1.0.
    /// </summary>
    [Theory]
    [InlineData(0.4, 21.54)]
    [InlineData(2.0, 107.7)]
    public void The_AttackMultiplier_scales_raw_and_is_not_itself_a_damage_amount(
        double multiplier, double expected) =>
        PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk)),
            Block(50_000.0, (StatId.DEF, SanityCheckDef)),
            attackerEffects: [Charge("PK_OPENER", EffectOp.ATTACK_MULT_NEXT, multiplier)])
            .AttackerHit()
            .ShouldBe(expected);

    /// <summary>The attack multiplier resets to 1.0 after every resolved attack.</summary>
    [Fact]
    public void An_ATTACK_MULT_NEXT_charge_applies_to_the_next_swing_and_then_resets_to_1() =>
        PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk)),
            Block(50_000.0, (StatId.DEF, SanityCheckDef)),
            attackerEffects: [Charge("PK_OPENER", EffectOp.ATTACK_MULT_NEXT, 3.0)],
            durationSeconds: 1.05)
            .ValuesBy(CombatEventType.Hit, CombatActor.Hero)
            .ShouldBe(
                new[] { 161.55, SanityCheckHit },
                "x3 then x1 — a factor of three apart, which no other rule in 05 §4 produces");

    // ══════════════════════════════════════════════════════ helpers

    private static ActorStats Block(double maxHp, params (StatId Stat, double Value)[] rest) =>
        PublicFightBench.Stats(maxHp, rest);

    /// <summary>A one-charge flow effect on its holder, granted at battle start.</summary>
    private static EffectDefinition Charge(string id, EffectOp op, double? value) =>
        new()
        {
            Id = id,
            Op = op,
            Value = value,
            ValueMode = value is null ? null : ValueMode.FLAT,
            Target = EffectTarget.SELF,
            Charges = 1,
            Trigger = new EffectTrigger { Kind = TriggerKind.ON_BATTLE_START },
        };

    /// <summary>A ward grant of a flat amount, on its holder, at battle start.</summary>
    private static EffectDefinition Shield(string id, double amount) =>
        new()
        {
            Id = id,
            Op = EffectOp.SHIELD,
            Value = amount,
            ValueMode = ValueMode.FLAT,
            Target = EffectTarget.SELF,
            Trigger = new EffectTrigger { Kind = TriggerKind.ON_BATTLE_START },
        };

    /// <summary>
    /// A 4-decimal value strictly between two draws. Rounded because <c>ActorStats</c> refuses an
    /// unrounded value; the guard proves the rounding did not land on either draw.
    /// </summary>
    private static double Between(double left, double right)
    {
        var midpoint = StatRounding.Round((left + right) / 2.0);

        midpoint.ShouldBeInRange(
            Math.Min(left, right) + 1e-9,
            Math.Max(left, right) - 1e-9,
            "the two draws must be separable at four decimal places for the case to discriminate");

        return midpoint;
    }
}
