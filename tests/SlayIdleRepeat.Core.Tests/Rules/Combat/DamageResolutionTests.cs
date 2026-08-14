using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// 🔒 `05` §4 — the ten steps of the attack pipeline, one case per step, read out of a public fight.
/// </summary>
/// <remarks>
/// Every case runs <see cref="CombatSimulator.SimulateDuel"/> and asserts on the events in
/// <see cref="SimulationResult.Log"/> — the same surface `05` §8 replays and `11` §6 hashes. What no
/// log can show is in <see cref="AttackPipelineInternalTests"/>.
/// <para>
/// ⚠️ Ceilings are authored, not switched off: `05` §1 caps <c>DODGE</c> at 0.50 and <c>BLOCK</c> at
/// 0.60, so a case needing a certainty passes a <c>combat_caps.json</c> authoring that ceiling at
/// 1.0. <c>NextDouble()</c> is in <c>[0,1)</c>, so 1.0 always fires and 0.0 never does.
/// </para>
/// </remarks>
public sealed class DamageResolutionTests
{
    /// <summary>ATK 100 makes step 2's <c>raw</c> equal the multiplier, in percent.</summary>
    private const double Atk = 100.0;

    private const double SanityCheckDef = 120.0;

    /// <summary>`05` §4's sanity check: <c>120 / (120 + 120 + 20 × 1)</c> = 0.4615, so 53.85 lands.</summary>
    private const double SanityCheckHit = 53.85;

    private static IReadOnlyDictionary<StatId, decimal> Uncapped(StatId stat) =>
        new Dictionary<StatId, decimal> { [stat] = 1.0m };

    // ══════════════════════════════════════════════════════ steps 1-5: the three draws

    /// <summary>🔒 `05` §4 step 1 — <em>"log(MISS); return"</em>. Nothing after it runs.</summary>
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
    /// 🔒 `05` §4 steps 2 and 3, and §4's own sanity check. The level row is the discriminating one:
    /// both of `05`'s stated rows are at level 1, so a pipeline dropping the <c>20 × attackerLevel</c>
    /// term would still pass them.
    /// </summary>
    [Theory]
    [InlineData(120.0, 1, 53.85)]     // 05 §4: mitigation 120/260 = 0.4615
    [InlineData(600.0, 1, 18.92)]     // 05 §4: mitigation 600/740 = 0.8108
    [InlineData(120.0, 10, 72.73)]    // 120/(120+120+200) = 0.2727 — the level term, alone
    public void Steps_2_and_3_are_05_4s_own_sanity_check(double def, int level, double expected) =>
        PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk)),
            Block(50_000.0, (StatId.DEF, def)),
            attackerLevel: level)
            .AttackerHit()
            .ShouldBe(expected);

    /// <summary>
    /// 🔒 `05` §4 step 3's two dials come from <c>content/combat_caps.json#/mitigation</c> —
    /// <em>"the two most important balance dials in the game. Expose them in data."</em> Read through
    /// the public entry point, this also proves they travel from the document into the arithmetic.
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

    /// <summary>🔒 `05` §4 step 4 — <em>"if isCrit: dmg *= (1 + attacker.CDMG)"</em>.</summary>
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

    /// <summary>🔒 `05` §4 step 5 — <em>"if Rng.NextDouble() &lt; defender.BLOCK: dmg *= 0.5"</em>.</summary>
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
    /// 🔒 The three draws are taken in step order — dodge, crit, block.
    /// </summary>
    /// <remarks>
    /// The thresholds are picked strictly <em>between</em> two adjacent draws of the same seeded
    /// stream, so reading the wrong draw inverts the outcome. With <c>CRIT</c> and <c>BLOCK</c> at
    /// one threshold the two results must come out opposite; reading the same draw for both would
    /// match them, and reading them in the other order would invert the pair. The ceilings are
    /// authored at 1.0 so step 9 cannot move a threshold the case chose.
    /// </remarks>
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
    /// 🔒 `18` §2.4's <c>FORCE_CRIT_NEXT</c> decides step 4's outcome. That it still <em>draws</em>
    /// is <c>AttackPipelineInternalTests</c>' half — the position is not in the log.
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
    /// 🔒 `05` §4 step 6 — <b>both</b> <c>(1 − DR%)</c> and the <c>DAMAGE_TAKEN_MULT</c> product
    /// apply, and the product is the product rather than one of its factors. The three rows separate
    /// DR alone, the product alone, and both.
    /// </summary>
    /// <remarks>
    /// ⚠️ It does not observe the <em>order</em> of the two and does not claim to: multiplication
    /// commutes and no row differs in the fourth decimal between the two sequences.
    /// </remarks>
    [Theory]
    [InlineData(0.5, 1.0, 1.0, 26.925)]
    [InlineData(0.0, 0.5, 0.5, 13.4625)]
    [InlineData(0.5, 0.5, 1.0, 13.4625)]
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
    /// 🔒 `05` §4 step 7 — <em>"never less than 10% of raw"</em>. At DEF 100 000 the mitigation is
    /// 0.9988 and step 6 would leave 0.12, so the 10.0 can only be the floor; the second row is the
    /// negative control, one step under the floor's reach.
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
    /// 🔒 `05` §4.1 — <em>"the §4 step-7 floor applies before absorption; there is no re-floor after.
    /// A fully absorbed hit deals 0 HP damage — the floor exists to defeat mitigation stacking, not
    /// shields."</em>
    /// </summary>
    /// <remarks>
    /// The floor is 10. Ward 4 leaves 6 (flooring after absorption would deal 10); ward 10 absorbs it
    /// exactly and leaves 0 (a re-floor would deal 10 against a shield that just paid in full); ward
    /// 40 is over-shielded and still 0.
    /// </remarks>
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
    /// 🔒 `05` §4 step 8 / §4.1 — <em>"a lifesteal attacker still heals off a fully-warded hit, and
    /// thorns still reflect it."</em> Behind a 5000-point ward the defender loses zero HP, so
    /// anything reading the post-absorption number would heal 0 and reflect 0.
    /// </summary>
    /// <remarks>
    /// ⚠️ Two ticks, because a heal is clamped by the room the healer has and the attacker starts
    /// full. The defender's <c>THORNS</c> of 0.5 reflects 26.925 on the first swing, opening more
    /// room than the second swing's lifesteal wants — so the tick-20 heal is the unclamped number.
    /// </remarks>
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
    /// 🔒 The per-attack emission sequence, with every member present in one swing. It is `05` §4's
    /// <b>step</b> order, not its line order — and order is inside <c>LogHash</c>, so two honest
    /// readings that disagree here disagree on `11` §6's tamper check for an identical fight.
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

    /// <summary>
    /// 🔒 `05` §4.1 — <c>WardBroken</c> fires only when damage empties the pool, not when it survives
    /// or was never granted.
    /// </summary>
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
    /// 🔒 `05` §4 step 2 — the multiplier arrives unmultiplied by ATK. Multiplying by ATK at the op
    /// too would square the attacker's power; at ATK 100 the two readings differ by a factor of 100,
    /// which is why the rows are 0.4 and 2.0 rather than 1.0.
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

    /// <summary>
    /// 🔒 `05` §4 — <em>"it resets to 1.0 after every resolved attack"</em>. The claim spans the
    /// grant, <c>CombatFlowState</c>'s spending and step 2, so it needs a fight that swings twice.
    /// </summary>
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

    /// <summary>A one-charge `18` §2.4 flow effect on its holder, granted at battle start.</summary>
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

    /// <summary>A `05` §4.1 ward grant of a flat amount, on its holder, at battle start.</summary>
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
    /// unrounded value (`05` §1.1); the guard proves the rounding did not land on either draw.
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
