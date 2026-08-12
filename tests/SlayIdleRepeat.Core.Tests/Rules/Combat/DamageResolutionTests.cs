using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// 🔒 `05` §4 — the ten steps of <c>ResolveAttack</c>, one case per step, over the real pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Every case runs through <see cref="AttackPipelineBench"/>, which drives the pipeline
/// <c>BattleSeams.For</c> builds inside a real fight. The stats are uncapped
/// (<c>StatCaps.None</c>) so that one step at a time can be isolated: <c>NextDouble()</c> is in
/// <c>[0,1)</c>, so <c>DODGE = 0</c> never dodges and <c>DODGE = 1</c> always does, and `05` §1's
/// 0.50 ceiling would make the second unreachable.
/// </para>
/// <para>
/// 🔒 <b>The worked arithmetic below is `05` §4's own sanity check.</b> <em>"With <c>DEF = 120</c>
/// and <c>attackerLevel = 1</c>, mitigation = <c>120/(120+140)</c> = 0.46. With <c>DEF = 600</c>,
/// mitigation = <c>600/(600+140)</c> = 0.81."</em> Both appear as literal expectations, which is
/// what makes the dials and the formula assertable at the same time.
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

    // ══════════════════════════════════════════════════════ steps 1-5: the three draws

    /// <summary>🔒 `05` §4 step 1 — <em>"log(MISS); return"</em>. Nothing after it runs.</summary>
    [Fact]
    public void Step_1_a_dodge_logs_MISS_ends_the_attack_and_leaves_HP_untouched()
    {
        var probe = Fight(
            defender: Block(500.0, (StatId.DEF, SanityCheckDef), (StatId.DODGE, 1.0)),
            body: p =>
            {
                var result = p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X");

                result.Missed.ShouldBeTrue();
                result.Crit.ShouldBeFalse();
                result.Blocked.ShouldBeFalse();
                result.Basis.ShouldBe(0.0);
                result.HpLost.ShouldBe(0.0);
                p.Enemy().CurrentHp.ShouldBe(500.0);
            });

        probe.Sequence().ShouldBe(new[] { CombatEventType.Miss });
    }

    /// <summary>The negative control: at <c>DODGE = 0</c> the same swing lands.</summary>
    [Fact]
    public void Step_1_at_zero_dodge_the_swing_always_lands()
    {
        var probe = Fight(
            defender: Block(500.0, (StatId.DEF, SanityCheckDef), (StatId.DODGE, 0.0)),
            body: p => p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X")
                        .Missed.ShouldBeFalse());

        probe.EventsOf(CombatEventType.Miss).ShouldBeEmpty();
        probe.EventsOf(CombatEventType.Hit).Single().Value.ShouldBe(SanityCheckHit);
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
    public void Steps_2_and_3_are_05_4s_own_sanity_check(double def, int level, double expected)
    {
        Fight(
            attacker: Block(500.0, (StatId.ATK, Atk)),
            attackerLevel: level,
            defender: Block(5000.0, (StatId.DEF, def)),
            body: p => p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X")
                        .HpLost.ShouldBe(expected));
    }

    /// <summary>
    /// 🔒 `05` §4 step 3's two constants come from <c>content/combat_caps.json#/mitigation</c> — the
    /// same fight, two different dial pairs, two different numbers.
    /// </summary>
    /// <remarks>
    /// 🔒 This is the case that would go red if the <c>120</c> and the <c>20</c> were ever written
    /// into the formula: the second row would then produce the first row's answer. `05` §4 calls
    /// them <em>"the two most important balance dials in the game"</em> and asks for them in data;
    /// <c>MitigationDialRuleTests</c> is the static half of the same claim.
    /// </remarks>
    [Theory]
    [InlineData(120.0, 20.0, 53.85)]   // the shipped pair: 120/(120+120+20)
    [InlineData(240.0, 40.0, 70.0)]    // doubled: 120/(120+240+40) = 0.30
    [InlineData(60.0, 10.0, 36.84)]    // halved:  120/(120+60+10)  = 0.6316
    public void Step_3_reads_both_dials_from_the_plan_and_not_from_a_literal(
        double flat, double perLevel, double expected)
    {
        Fight(
            attacker: Block(500.0, (StatId.ATK, Atk)),
            defender: Block(5000.0, (StatId.DEF, SanityCheckDef)),
            mitigation: new MitigationConstants(flat, perLevel),
            body: p => p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X")
                        .HpLost.ShouldBe(expected));
    }

    /// <summary>🔒 `05` §4 step 4 — <em>"if isCrit: dmg *= (1 + attacker.CDMG)"</em>.</summary>
    [Fact]
    public void Step_4_a_crit_multiplies_by_one_plus_CDMG()
    {
        var probe = Fight(
            attacker: Block(500.0, (StatId.ATK, Atk), (StatId.CRIT, 1.0), (StatId.CDMG, 0.5)),
            defender: Block(5000.0, (StatId.DEF, SanityCheckDef)),
            body: p =>
            {
                var result = p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X");

                result.Crit.ShouldBeTrue();
                result.HpLost.ShouldBe(80.775, "53.85 x 1.5");
            });

        probe.Sequence().ShouldBe(new[] { CombatEventType.Crit, CombatEventType.Hit });
    }

    /// <summary>🔒 `05` §4 step 5 — <em>"if Rng.NextDouble() &lt; defender.BLOCK: dmg *= 0.5"</em>.</summary>
    [Fact]
    public void Step_5_a_block_halves_the_hit()
    {
        var probe = Fight(
            attacker: Block(500.0, (StatId.ATK, Atk)),
            defender: Block(5000.0, (StatId.DEF, SanityCheckDef), (StatId.BLOCK, 1.0)),
            body: p =>
            {
                var result = p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X");

                result.Blocked.ShouldBeTrue();
                result.HpLost.ShouldBe(26.925, "53.85 x 0.5");
            });

        probe.Sequence().ShouldBe(new[] { CombatEventType.Block, CombatEventType.Hit });
    }

    // ══════════════════════════════════════════════════════ the draw discipline

    /// <summary>
    /// 🔒 One resolved attack advances the combat stream by exactly <b>3</b>, and a dodged one by
    /// exactly <b>1</b>.
    /// </summary>
    /// <remarks>
    /// 🔒 <c>DeterministicRng.Position</c> is <em>"the entire persistable state of this stream"</em>,
    /// so a spent or skipped draw desynchronises a client from the server for the rest of the fight
    /// and every draw after it. Both rows are needed: a pipeline that always drew three would pass
    /// the first and fail the second, and one that drew lazily would pass the second and fail the
    /// first.
    /// </remarks>
    [Theory]
    [InlineData(0.0, 3UL)]
    [InlineData(1.0, 1UL)]
    public void A_resolved_attack_draws_three_times_and_a_dodged_one_draws_once(
        double dodge, ulong expectedDraws)
    {
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
    }

    /// <summary>
    /// 🔒 The three draws are taken in `05` §4's step order — <b>dodge, crit, block</b> — and the
    /// case is built so that any other assignment produces a different, wrong answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A count alone cannot see an order. This reads the fight's first three draws from a second
    /// stream over the same seed (which is what determinism means) and picks a threshold
    /// <b>strictly between</b> draws 1 and 2. With <c>CRIT</c> and <c>BLOCK</c> both at that
    /// threshold, the two outcomes must come out <b>opposite</b>; a pipeline that read block before
    /// crit would produce exactly the inverted pair, and one that read the same draw for both would
    /// produce a matching pair.
    /// </para>
    /// <para>
    /// The dodge half is the same construction one draw earlier: the threshold sits between draws 0
    /// and 1, so reading draw 1 for the dodge flips the outcome.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_three_draws_are_taken_in_dodge_then_crit_then_block_order()
    {
        const ulong seed = 1UL;

        var stream = new DeterministicRng(seed, RngStreams.Combat);
        var dodgeDraw = stream.NextDouble();
        var critDraw = stream.NextDouble();
        var blockDraw = stream.NextDouble();

        var dodgeThreshold = Between(dodgeDraw, critDraw);
        var critBlockThreshold = Between(critDraw, blockDraw);

        // ── the dodge half: the threshold separates draw 0 from draw 1.
        Fight(
            attacker: Block(500.0, (StatId.ATK, Atk)),
            defender: Block(5000.0, (StatId.DEF, SanityCheckDef), (StatId.DODGE, dodgeThreshold)),
            battleSeed: seed,
            body: p => p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X").Missed
                        .ShouldBe(
                            dodgeDraw < dodgeThreshold,
                            "step 1 reads draw 0; reading draw 1 inverts this"));

        // ── the crit/block half: one threshold, two draws, therefore two opposite outcomes.
        (critDraw < critBlockThreshold).ShouldNotBe(
            blockDraw < critBlockThreshold,
            "the case is only discriminating if the two draws fall on opposite sides");

        Fight(
            attacker: Block(500.0, (StatId.ATK, Atk), (StatId.CRIT, critBlockThreshold)),
            defender: Block(5000.0, (StatId.DEF, SanityCheckDef), (StatId.BLOCK, critBlockThreshold)),
            battleSeed: seed,
            body: p =>
            {
                var result = p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X");

                result.Crit.ShouldBe(
                    critDraw < critBlockThreshold, "step 4 reads draw 1");
                result.Blocked.ShouldBe(
                    blockDraw < critBlockThreshold, "step 5 reads draw 2");
            });
    }

    /// <summary>
    /// 🔒 `18` §2.4's <c>FORCE_CRIT_NEXT</c> decides the <b>outcome</b> of step 4 and never its
    /// <b>draw</b>.
    /// </summary>
    /// <remarks>
    /// 🔒 The rule this pins is the one that matters for `11` §6: if a forced crit skipped its draw,
    /// the stream's position would become a function of the attacker's flow state, and a client that
    /// had not observed the charge would diverge from the server on every draw thereafter. The
    /// attacker's own <c>CRIT</c> is <b>0</b>, so the crit can only have come from the charge — and
    /// the position still advances by three.
    /// </remarks>
    [Fact]
    public void A_forced_crit_crits_without_skipping_step_4s_draw()
    {
        Fight(
            attacker: Block(500.0, (StatId.ATK, Atk), (StatId.CRIT, 0.0), (StatId.CDMG, 0.5)),
            defender: Block(5000.0, (StatId.DEF, SanityCheckDef)),
            body: p =>
            {
                p.Hero.Flow.GrantForcedCrits(1);

                var forcedSwing = p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X");
                forcedSwing.Crit.ShouldBeTrue("the charge forces it; CRIT is 0");
                forcedSwing.HpLost.ShouldBe(80.775);
                p.Services.Rng.Position.ShouldBe(3UL, "step 4 drew even though the outcome was fixed");

                // The charge is spent: the next swing is an ordinary one, and still draws three.
                var nextSwing = p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X");
                nextSwing.Crit.ShouldBeFalse();
                nextSwing.HpLost.ShouldBe(SanityCheckHit);
                p.Services.Rng.Position.ShouldBe(6UL);
            });
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
    /// applied only one of them passes exactly one row. The <em>ascending effect-id</em> order the
    /// product is taken in is <c>CombatFlowState.DamageTakenMultiplier</c>'s and is pinned by
    /// <c>CombatFlowStateTests</c>.
    /// </remarks>
    [Theory]
    [InlineData(0.5, 1.0, 1.0, 26.925)]
    [InlineData(0.0, 0.5, 0.5, 13.4625)]
    [InlineData(0.5, 0.5, 1.0, 13.4625)]
    public void Step_6_applies_both_DR_and_the_DAMAGE_TAKEN_MULT_product(
        double dr, double firstMult, double secondMult, double expected)
    {
        Fight(
            attacker: Block(500.0, (StatId.ATK, Atk)),
            defender: Block(5000.0, (StatId.DEF, SanityCheckDef), (StatId.DR_PCT, dr)),
            body: p =>
            {
                // Ascending effect-id order is CombatFlowState's and is pinned by
                // CombatFlowStateTests; what is asserted here is that the pipeline consults the
                // PRODUCT rather than one of the factors.
                p.Enemy().Flow.AddDamageTakenMultiplier(firstMult, "EFF_A");
                p.Enemy().Flow.AddDamageTakenMultiplier(secondMult, "EFF_B");

                p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X").HpLost.ShouldBe(expected);
            });
    }

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
        Fight(
            attacker: Block(500.0, (StatId.ATK, Atk)),
            defender: Block(5000.0, (StatId.DEF, def)),
            body: p => p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X")
                        .HpLost.ShouldBe(expected));

    /// <summary>
    /// 🔒 `05` §4.1 — <em>"the §4 step-7 floor applies <b>before</b> absorption; there is no re-floor
    /// after. A fully absorbed hit deals 0 HP damage — the floor exists to defeat mitigation
    /// stacking, not shields."</em>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three rows over one fight shape, and each rules out a different wrong implementation:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>ward 4</b> — the floor is 10, so 6 reaches HP. A pipeline that floored
    ///   <em>after</em> absorption would deal 10.</item>
    ///   <item><b>ward 10</b> — the hit is exactly absorbed and <b>0</b> reaches HP. A re-floor
    ///   would deal 10 against a shield that had just paid for the whole hit.</item>
    ///   <item><b>ward 40</b> — over-shielded, still 0, and the <c>Basis</c> is still the floored 10
    ///   in every row, which is what step 8 promises lifesteal and thorns.</item>
    /// </list>
    /// </remarks>
    [Theory]
    [InlineData(4.0, 6.0)]
    [InlineData(10.0, 0.0)]
    [InlineData(40.0, 0.0)]
    public void The_step_7_floor_applies_before_absorption_and_is_never_re_applied(
        double ward, double expectedHpLost)
    {
        var probe = Fight(
            attacker: Block(500.0, (StatId.ATK, Atk)),
            defender: Block(5000.0, (StatId.DEF, 100_000.0)),
            body: p =>
            {
                p.Pipeline.GrantWard(p.Enemy(), ward, null, "EFF_W");

                var result = p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X");

                result.Basis.ShouldBe(10.0, "step 8's basis is the floored, pre-absorption number");
                result.HpLost.ShouldBe(expectedHpLost);
                p.Enemy().CurrentHp.ShouldBe(5000.0 - expectedHpLost);
            });

        probe.EventsOf(CombatEventType.Hit).Single().Value.ShouldBe(
            expectedHpLost, "`05` §7's Hit carries what was actually lost, after absorption");
    }

    /// <summary>
    /// 🔒 `05` §4 step 8 / §4.1 — <em>"a lifesteal attacker still heals off a fully-warded hit, and
    /// thorns still reflect it."</em>
    /// </summary>
    /// <remarks>
    /// 🔒 The discriminating shape: the defender loses <b>zero</b> HP, so anything reading step 9's
    /// <c>HpLost</c> would heal 0 and reflect 0. Both numbers below are fractions of step 8's
    /// <c>basis</c>, which is the whole claim.
    /// </remarks>
    [Fact]
    public void Step_8s_basis_is_pre_absorption_and_both_lifesteal_and_thorns_read_it()
    {
        Fight(
            attacker: Block(500.0, (StatId.ATK, Atk), (StatId.LIFESTEAL, 0.4)),
            defender: Block(5000.0, (StatId.DEF, SanityCheckDef), (StatId.THORNS, 0.2)),
            body: p =>
            {
                p.Hero.SetCurrentHp(1.0);
                p.Pipeline.GrantWard(p.Enemy(), 5000.0, null, "EFF_W");

                var result = p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X");

                result.HpLost.ShouldBe(0.0, "the ward absorbed the whole hit");
                result.Basis.ShouldBe(SanityCheckHit);
                p.Enemy().CurrentHp.ShouldBe(5000.0);

                // 53.85 x 0.4 = 21.54 healed, minus 53.85 x 0.2 = 10.77 reflected back.
                p.Hero.CurrentHp.ShouldBe(1.0 + 21.54 - 10.77);
            });
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
    public void The_per_attack_emission_sequence_is_crit_block_wardbroken_hit_then_heal()
    {
        var probe = Fight(
            attacker: Block(500.0, (StatId.ATK, Atk), (StatId.CRIT, 1.0), (StatId.CDMG, 0.5), (StatId.LIFESTEAL, 0.4)),
            defender: Block(5000.0, (StatId.DEF, SanityCheckDef), (StatId.BLOCK, 1.0)),
            body: p =>
            {
                p.Hero.SetCurrentHp(1.0);
                p.Pipeline.GrantWard(p.Enemy(), 1.0, null, "EFF_W");

                p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X");
            });

        probe.Sequence().ShouldBe(new[]
        {
            CombatEventType.Shield,
            CombatEventType.Crit,
            CombatEventType.Block,
            CombatEventType.WardBroken,
            CombatEventType.Hit,
            CombatEventType.Heal,
        });
    }

    /// <summary>
    /// 🔒 `05` §4.1 — <c>WardBroken</c> fires <b>only</b> when damage empties the pool, and not when
    /// the pool merely survives or was already empty.
    /// </summary>
    [Theory]
    [InlineData(1000.0, 0)]
    [InlineData(53.85, 1)]
    [InlineData(0.0, 0)]
    public void WardBroken_fires_only_when_damage_empties_the_pool(double ward, int expected)
    {
        var probe = Fight(
            attacker: Block(500.0, (StatId.ATK, Atk)),
            defender: Block(5000.0, (StatId.DEF, SanityCheckDef)),
            body: p =>
            {
                if (ward > 0.0)
                {
                    p.Pipeline.GrantWard(p.Enemy(), ward, null, "EFF_W");
                }

                p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X");
            });

        probe.EventsOf(CombatEventType.WardBroken).Count.ShouldBe(expected);
    }

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
    [InlineData(1.0, SanityCheckHit)]
    [InlineData(2.0, 107.7)]
    public void The_AttackMultiplier_scales_raw_and_is_not_itself_a_damage_amount(
        double multiplier, double expected) =>
        Fight(
            attacker: Block(500.0, (StatId.ATK, Atk)),
            defender: Block(5000.0, (StatId.DEF, SanityCheckDef)),
            body: p => p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), multiplier, "EFF_X")
                        .HpLost.ShouldBe(expected));

    /// <summary>
    /// 🔒 `05` §4 — <em>"it resets to 1.0 after every resolved attack"</em>, over the whole slot-4
    /// path: an <c>ATTACK_MULT_NEXT</c> charge granted at the pre-tick applies to the <b>first</b>
    /// swing and to no later one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>Written over the tick loop rather than over two direct calls, and the earlier form was a
    /// test that could not fail.</b> <c>attackMultiplier</c> is a parameter of
    /// <c>ResolveAttack</c>, so the pipeline structurally cannot carry it forward and <em>every</em>
    /// implementation of that signature passed. The claim `05` §4 actually makes spans three
    /// components — <c>PK_OPENER</c>'s grant (`18` §2.4), <c>CombatFlowState.ConsumeAttackMultiplier</c>'s
    /// composition and spending, and this pipeline's step 2 — and only the loop exercises all three.
    /// </para>
    /// <para>
    /// The hero swings at 1.0 ASPD, so tick 0 and tick 20 are its first two swings. <c>PK_OPENER</c>
    /// is ×3 for one charge, so the two hits must be 161.55 and 53.85 — a factor of three apart,
    /// which no other rule in `05` §4 would produce.
    /// </para>
    /// </remarks>
    [Fact]
    public void An_ATTACK_MULT_NEXT_charge_applies_to_the_next_swing_and_then_resets_to_1()
    {
        var opener = new HeldEffect(new EffectDefinition
        {
            Id = "PK_OPENER",
            Op = EffectOp.ATTACK_MULT_NEXT,
            Target = EffectTarget.SELF,
            Value = 3.0,
            Charges = 1,
            Trigger = new EffectTrigger { Kind = TriggerKind.ON_BATTLE_START },
        });

        var probe = AttackPipelineBench.Run(
            new[]
            {
                BattleTestBench.Hero(Block(500.0, (StatId.ATK, Atk)), 1, opener),
                BattleTestBench.Enemy(0, Block(50_000.0, (StatId.DEF, SanityCheckDef))),
            },
            static _ => { },
            maxTicks: 21,
            actorsMaySwing: true);

        // 🔒 Attack precedes the outcome events (`05` §7's per-attack emission sequence) and marks a
        // BASIC attack only — one per swing, none for anything else. Both actors swing on both ticks
        // (`05` §3.1's fixed initiative: hero, then enemies by index), so the whole log is four
        // Attack/Hit pairs interleaved.
        probe.Sequence().ShouldBe(new[]
        {
            CombatEventType.Attack, CombatEventType.Hit,
            CombatEventType.Attack, CombatEventType.Hit,
            CombatEventType.Attack, CombatEventType.Hit,
            CombatEventType.Attack, CombatEventType.Hit,
        });

        probe.EventsOf(CombatEventType.Hit)
            .Where(e => e.SourceId == CombatActor.Hero)
            .Select(e => e.Value)
            .ShouldBe(new[] { 161.55, SanityCheckHit });
    }

    // ══════════════════════════════════════════════════════ 05 §4.2's other two routes

    /// <summary>
    /// 🔒 `05` §4.2 — <c>DAMAGE_TRUE</c> <em>"bypasses everything … no <c>DR%</c>,
    /// <c>DAMAGE_TAKEN_MULT</c>, floor or wards"</em>, and `05` §4.1's bypass class (a).
    /// </summary>
    [Fact]
    public void DAMAGE_TRUE_bypasses_DR_the_damage_taken_multiplier_and_the_ward_pool()
    {
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
    }

    /// <summary>
    /// 🔒 `05` §4.2 — <c>DAMAGE_MAXHP_PCT</c>: <em>"<c>DR%</c> and <c>DAMAGE_TAKEN_MULT</c>
    /// <b>do</b> apply; wards absorb"</em> — unless `05` §4.1's bypass class <b>(b)</b> says
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// 🔒 R12 — the bypass flag is <c>EffectTagging.IsSelfInflictedCost</c>'s answer, read once at
    /// the op and reported here, never re-derived. The two rows are the two answers, over the same
    /// ward: <em>"wards must not silently delete perk drawbacks"</em> (<c>CP_BLOOD_PRICE</c>).
    /// </remarks>
    [Theory]
    [InlineData(false, 5000.0, 950.0)]   // absorbed: the pool pays the 50, HP is untouched
    [InlineData(true, 4950.0, 1000.0)]   // `05` §4.1 (b): the drawback reaches HP, the pool is untouched
    public void DAMAGE_MAXHP_PCT_applies_DR_and_is_absorbed_unless_it_is_a_self_inflicted_cost(
        bool bypassesWards, double expectedHp, double expectedWard) =>
        Fight(
            defender: Block(5000.0, (StatId.DEF, SanityCheckDef), (StatId.DR_PCT, 0.5)),
            body: p =>
            {
                p.Pipeline.GrantWard(p.Enemy(), 1000.0, null, "EFF_W");

                // 100, halved by DR% to 50.
                p.Pipeline.DealMaxHpPctDamage(p.Enemy(), 100.0, bypassesWards, "CP_BLOOD_PRICE");

                p.Enemy().CurrentHp.ShouldBe(expectedHp);
                p.Enemy().Wards.Total.ShouldBe(expectedWard);
                p.Services.Rng.Position.ShouldBe(0UL, "no dodge, no crit, no block — so no draws");
            });

    // ══════════════════════════════════════════════════════ the S6 refusals

    /// <summary>
    /// 🔒 A non-finite number is refused by name rather than carried through the ten steps.
    /// </summary>
    /// <remarks>
    /// A NaN compares <c>false</c> against every bound in `05` §4 — the dodge test, the floor, the
    /// ward cap — so it passes through all of them and is refused by <c>CombatLog</c> three layers
    /// later, naming the serialiser rather than the effect. The message is the deliverable
    /// (steering S2), so the effect id is asserted and not merely the type.
    /// </remarks>
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

    /// <summary>
    /// 🔒 A view that is not this battle's actor is refused — <c>BattleFlowSink</c>'s guard and its
    /// reason: a battle has one roster and one view of it.
    /// </summary>
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

    /// <summary>`05` §1's block — see <see cref="AttackPipelineBench.Stats"/> for the two defaults.</summary>
    private static ActorStats Block(double maxHp, params (StatId Stat, double Value)[] rest) =>
        AttackPipelineBench.Stats(maxHp, rest);

    /// <summary>One probe fight: a hero, one enemy, nobody swinging, the probe in slot 1.</summary>
    private static AttackProbe Fight(
        Action<AttackProbe> body,
        ActorStats? attacker = null,
        ActorStats? defender = null,
        int attackerLevel = 1,
        MitigationConstants? mitigation = null,
        ulong battleSeed = 1UL) =>
        AttackPipelineBench.Run(
            new[]
            {
                BattleTestBench.Hero(attacker ?? Block(500.0, (StatId.ATK, Atk)), attackerLevel),
                BattleTestBench.Enemy(0, defender ?? Block(5000.0, (StatId.DEF, SanityCheckDef))),
            },
            body,
            mitigation: mitigation,
            battleSeed: battleSeed);

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
