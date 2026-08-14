using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// 🔒 `05` §4 — the ten steps of the attack pipeline, one case per step, read out of a
/// <b>public</b> fight.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Every case runs through <see cref="CombatSimulator.SimulateDuel"/> and asserts on the
/// <c>CombatEvent</c>s in <see cref="SimulationResult.Log"/>.</b> That is the same surface `05` §8
/// has the client replay and `11` §6 has the backend recompute <c>LogHash</c> over, so a case that
/// goes red here is a case a real consumer would have seen go wrong. The residue that no log can
/// show — the draw-stream positions, the pre-absorption <c>Basis</c>, `05` §4.2's non-attack damage
/// routes — is in <see cref="AttackPipelineInternalTests"/>, with a stated reason per case.
/// </para>
/// <para>
/// 🔒 <b>The worked arithmetic below is `05` §4's own sanity check.</b> <em>"With <c>DEF = 120</c>
/// and <c>attackerLevel = 1</c>, mitigation = <c>120/(120+140)</c> = 0.46. With <c>DEF = 600</c>,
/// mitigation = <c>600/(600+140)</c> = 0.81."</em> Both appear as literal expectations, which is
/// what makes the dials and the formula assertable at the same time.
/// </para>
/// <para>
/// ⚠️ <b>Ceilings are authored, not switched off.</b> `05` §1 caps <c>DODGE</c> at 0.50 and
/// <c>BLOCK</c> at 0.60, and <c>NextDouble()</c> is in <c>[0,1)</c> — so a stat of <c>1.0</c> only
/// fires every time if the ceiling lets it survive `18` §8 step 9. The cases that need a certainty
/// pass a <c>content/combat_caps.json</c> authoring that ceiling at 1.0, because that is how a
/// public fight receives its constants: as data.
/// </para>
/// </remarks>
public sealed class DamageResolutionTests
{
    /// <summary>ATK 100 makes `05` §4 step 2's <c>raw</c> equal the multiplier, in percent.</summary>
    private const double Atk = 100.0;

    /// <summary>`05` §4's own sanity-check DEF.</summary>
    private const double SanityCheckDef = 120.0;

    /// <summary>`05` §4's sanity check: <c>120 / (120 + 120 + 20 × 1)</c> = 0.4615, so 53.85 lands.</summary>
    private const double SanityCheckHit = 53.85;

    /// <summary>A ceiling of 1.0 for one stat, so a <c>1.0</c> roll threshold survives step 9.</summary>
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
    /// 🔒 `05` §4 steps 2 and 3, and `05` §4's own two-row sanity check — <em>"with <c>DEF = 120</c>
    /// … 0.46. With <c>DEF = 600</c> … 0.81."</em>
    /// </summary>
    /// <remarks>
    /// The <c>attackerLevel</c> row is the third, and it is the one that discriminates: the
    /// <c>20 × attacker.Level</c> term is what `05` §4 says <em>"means defense must keep growing to
    /// stay relevant"</em>, and a pipeline that dropped it would still pass both of `05`'s own rows,
    /// because both are stated at level 1.
    /// </remarks>
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
    /// 🔒 `05` §4 step 3's two constants come from <c>content/combat_caps.json#/mitigation</c> — the
    /// same fight, two different dial pairs, two different numbers.
    /// </summary>
    /// <remarks>
    /// 🔒 This is the case that would go red if the <c>120</c> and the <c>20</c> were ever written
    /// into the formula: the second row would then produce the first row's answer. `05` §4 calls
    /// them <em>"the two most important balance dials in the game"</em> and asks for them in data;
    /// <c>MitigationDialRuleTests</c> is the static half of the same claim.
    /// <para>
    /// 🔒 Read through the public entry point, the row now proves something the internal form could
    /// not: that the dials travel all the way from the <b>content document</b> a caller supplies
    /// into `05` §4's arithmetic. A pipeline that read them correctly from a <c>BattlePlan</c> but
    /// ignored the snapshot would pass the old case and fail this one.
    /// </para>
    /// </remarks>
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
    /// 🔒 The three draws are taken in `05` §4's step order — <b>dodge, crit, block</b> — and the
    /// case is built so that any other assignment produces a different, wrong answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This reads the fight's first three draws from a second stream over the same seed (which is
    /// what determinism means) and picks a threshold <b>strictly between</b> two adjacent draws. With
    /// <c>CRIT</c> and <c>BLOCK</c> both at the crit/block threshold, the two outcomes must come out
    /// <b>opposite</b>; a pipeline that read block before crit would produce exactly the inverted
    /// pair, and one that read the same draw for both would produce a matching pair.
    /// </para>
    /// <para>
    /// The dodge half is the same construction one draw earlier: the threshold sits between draws 0
    /// and 1, so reading draw 1 for the dodge flips the outcome from "lands" to "missed".
    /// </para>
    /// <para>
    /// ⚠️ The ceilings are authored at 1.0 so that step 9 cannot move a threshold this case chose:
    /// the shipped <c>BLOCK</c> cap of 0.60 sits below the crit/block threshold, and a clamped
    /// threshold would still give the right answer here for the wrong reason.
    /// </para>
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

        // ── the dodge half: the threshold separates draw 0 from draw 1.
        var dodged = PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk)),
            Block(50_000.0, (StatId.DEF, SanityCheckDef), (StatId.DODGE, dodgeThreshold)),
            capOverrides: Uncapped(StatId.DODGE))
            .EventsBy(CombatEventType.Miss, CombatActor.Hero)
            .Count == 1;

        dodged.ShouldBe(
            dodgeDraw < dodgeThreshold, "step 1 reads draw 0; reading draw 1 inverts this");

        // ── the crit/block half: one threshold, two draws, therefore two opposite outcomes.
        (critDraw < critBlockThreshold).ShouldNotBe(
            blockDraw < critBlockThreshold,
            "the case is only discriminating if the two draws fall on opposite sides");

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
    /// 🔒 `18` §2.4's <c>FORCE_CRIT_NEXT</c> decides the <b>outcome</b> of step 4 — the attacker's own
    /// <c>CRIT</c> is <b>0</b>, so the crit can only have come from the charge.
    /// </summary>
    /// <remarks>
    /// ⚠️ That the forced crit still <em>draws</em> — the half that matters for `11` §6, because a
    /// skipped draw makes the stream's position a function of the attacker's flow state — is
    /// <c>AttackPipelineInternalTests.A_forced_crit_crits_without_skipping_step_4s_draw</c>. The
    /// position is not in the log.
    /// </remarks>
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
            1, "one charge, one crit — CRIT is 0, so nothing else could produce one");
    }

    // ══════════════════════════════════════════════════════ steps 6-8

    /// <summary>
    /// 🔒 `05` §4 step 6 — <b>both</b> <c>(1 − DR%)</c> and the <c>DAMAGE_TAKEN_MULT</c> product are
    /// applied, and the product is the product rather than one of its factors.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>It does not observe the <em>order</em> of the two, and does not claim to.</b>
    /// Multiplication commutes and none of the rows differs in the fourth decimal between the two
    /// sequences, so a name promising "DR then the product" would be an assertion this case cannot
    /// make. What the three rows separate is DR alone, the product alone, and both — a pipeline that
    /// applied only one of them passes exactly one row.
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

    /// <summary>🔒 `05` §4 step 7 — <em>"never less than 10% of raw"</em>.</summary>
    /// <remarks>
    /// The DEF here is absurd on purpose: at 100 000 the mitigation is 0.9988 and step 6 would leave
    /// 0.12, so the 10.0 that lands can only be the floor. The second row is the negative control —
    /// the same fight one step under the floor's reach, where the mitigated number wins.
    /// </remarks>
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
    /// 🔒 `05` §4.1 — <em>"the §4 step-7 floor applies <b>before</b> absorption; there is no re-floor
    /// after. A fully absorbed hit deals 0 HP damage — the floor exists to defeat mitigation
    /// stacking, not shields."</em>
    /// </summary>
    /// <remarks>
    /// Three rows over one fight shape, and each rules out a different wrong implementation:
    /// <list type="bullet">
    ///   <item><b>ward 4</b> — the floor is 10, so 6 reaches HP. A pipeline that floored
    ///   <em>after</em> absorption would deal 10.</item>
    ///   <item><b>ward 10</b> — the hit is exactly absorbed and <b>0</b> reaches HP. A re-floor
    ///   would deal 10 against a shield that had just paid for the whole hit.</item>
    ///   <item><b>ward 40</b> — over-shielded, still 0.</item>
    /// </list>
    /// `05` §7's <c>Hit</c> carries what was actually lost, after absorption, which is what makes
    /// all three readable from the log.
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
    /// thorns still reflect it."</em>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 The discriminating shape: the defender loses <b>zero</b> HP behind a 5000-point ward, so
    /// anything reading step 9's post-absorption number would heal 0 and reflect 0. Both numbers
    /// asserted are fractions of step 8's pre-absorption <c>basis</c> of 53.85, which is the claim.
    /// </para>
    /// <para>
    /// ⚠️ <b>Two ticks, because a heal is clamped by the room the healer has.</b> `05` §4.1's heal is
    /// <c>min(amount × HEAL%, MaxHP − HP)</c>, and the attacker starts full — so the tick-0 lifesteal
    /// necessarily reports 0 and says nothing. The defender's <c>THORNS</c> of 0.5 reflects 26.925 on
    /// that first swing, which opens more room than the 21.54 the second swing's lifesteal wants, so
    /// the tick-20 heal is the unclamped number.
    /// </para>
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
            v => v == 0.0, "the 5000-point ward absorbs both swings whole");

        fight.ValuesBy(CombatEventType.Hit, CombatActor.Enemy(0)).ShouldContain(
            26.925, "thorns reflects 53.85 x 0.5 off a hit that cost the defender no HP at all");

        fight.ValuesBy(CombatEventType.Heal, CombatActor.None).ShouldContain(
            21.54, "lifesteal heals 53.85 x 0.4 off the same fully-absorbed hit");
    }

    // ══════════════════════════════════════════════════════ step 9, and the emission sequence

    /// <summary>
    /// 🔒 <c>CombatEventType</c>'s per-attack emission sequence, with every member present in one
    /// swing.
    /// </summary>
    /// <remarks>
    /// It is `05` §4's <b>step</b> order rather than its line order, and `05` §3.1 step 7 governs:
    /// events are appended <em>"at the moment each state change occurs"</em>. Order is inside
    /// <c>LogHash</c>, so two honest readings of §4 that disagree here would disagree on `11` §6's
    /// tamper check for an identical fight.
    /// </remarks>
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
    /// 🔒 `05` §4.1 — <c>WardBroken</c> fires <b>only</b> when damage empties the pool, and not when
    /// the pool merely survives or was never granted.
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
    /// 🔒 `05` §4 step 2 — the multiplier arrives <b>unmultiplied by ATK</b>, and step 2 is what
    /// applies it.
    /// </summary>
    /// <remarks>
    /// The failure this rules out is the one <c>DamageAndHealingOps</c> names: multiplying by ATK at
    /// the op as well would square the attacker's power, so <c>PK_CLEAVE</c> at <c>value: 0.40</c>
    /// would be 0.4 × ATK of flat damage rather than a ×0.4 attack. At ATK 100 those two readings
    /// differ by a factor of 100, which is why the rows are 0.4 and 2.0 rather than 1.0.
    /// </remarks>
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
    /// 🔒 `05` §4 — <em>"it resets to 1.0 after every resolved attack"</em>: an
    /// <c>ATTACK_MULT_NEXT</c> charge granted at battle start applies to the <b>first</b> swing and
    /// to no later one.
    /// </summary>
    /// <remarks>
    /// The claim `05` §4 makes spans three components — <c>PK_OPENER</c>'s grant (`18` §2.4),
    /// <c>CombatFlowState</c>'s composition and spending, and the pipeline's step 2 — and only a
    /// fight that swings twice exercises all three. The attacker swings at 1.0 ASPD, so tick 0 and
    /// tick 20 are its first two swings; <c>PK_OPENER</c> is ×3 for one charge, so the two hits must
    /// be 161.55 and 53.85, a factor of three apart, which no other rule in `05` §4 would produce.
    /// </remarks>
    [Fact]
    public void An_ATTACK_MULT_NEXT_charge_applies_to_the_next_swing_and_then_resets_to_1() =>
        PublicFightBench.Duel(
            Block(500.0, (StatId.ATK, Atk)),
            Block(50_000.0, (StatId.DEF, SanityCheckDef)),
            attackerEffects: [Charge("PK_OPENER", EffectOp.ATTACK_MULT_NEXT, 3.0)],
            durationSeconds: 1.05)
            .ValuesBy(CombatEventType.Hit, CombatActor.Hero)
            .ShouldBe(new[] { 161.55, SanityCheckHit });

    // ══════════════════════════════════════════════════════ helpers

    /// <summary>`05` §1's block — see <see cref="AttackPipelineBench.Stats"/> for the two defaults.</summary>
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
    /// A 4-decimal value strictly between two draws — the threshold that makes a draw-order case
    /// discriminating.
    /// </summary>
    /// <remarks>
    /// Rounded through <c>StatRounding</c> because <c>ActorStats</c> refuses an unrounded value
    /// (`05` §1.1), and the two draws are far enough apart that the rounding cannot land on either.
    /// </remarks>
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
