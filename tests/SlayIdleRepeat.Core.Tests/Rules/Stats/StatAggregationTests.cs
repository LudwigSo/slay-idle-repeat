using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Stats;

/// <summary>
/// 🔒 `18` §8 — the resolution order, <em>"must be implemented exactly, or builds will produce
/// different numbers on client and server."</em>
/// </summary>
/// <remarks>
/// Every case runs a real fight through <see cref="CombatSimulator.SimulateDuel"/>: against a
/// defender with <c>DEF = 0</c> the mitigation is exactly 0, so the <c>Hit</c> event carries the
/// attacker's post-`18`-§8 <c>ATK</c> itself, at the 4 decimals `05` §1.1 rounds to. Caps and dials
/// arrive as content. What no fight can report is in <see cref="StatAggregationInternalTests"/>.
/// </remarks>
public sealed class StatAggregationTests
{
    /// <summary>The attacker's aggregated <c>ATK</c>, as the fight's one <c>Hit</c> reports it.</summary>
    private static double Atk(double baseAtk, params EffectDefinition[] effects) =>
        PublicFightBench.AggregatedAtk(baseAtk, effects);

    private static EffectDefinition Effect(string id, EffectOp op, StatId stat, double value) =>
        StatFixtures.Effect(id, op, stat, value);

    // ───────────────────────────────────────────────────────────── R1 · STAT_MULT is Π(value)

    /// <summary>
    /// 🔴 <c>STAT_MULT</c>'s <c>value</c> <b>is</b> the multiplier. `05` §1.1's
    /// <c>Π (1 + Multiplicative(stat))</c> term is an erratum; `18` §8 step 7 says "product".
    /// </summary>
    [Fact]
    public void SYS_ENRAGE_stacks_multiplicatively_on_the_value_not_on_one_plus_the_value()
    {
        var enraged = Atk(
            100.0,
            Effect("SYS_ENRAGE_1", EffectOp.STAT_MULT, StatId.ATK, 1.08),
            Effect("SYS_ENRAGE_2", EffectOp.STAT_MULT, StatId.ATK, 1.08),
            Effect("SYS_ENRAGE_3", EffectOp.STAT_MULT, StatId.ATK, 1.08));

        enraged.ShouldBe(
            125.9712, "05 §3.1: three seconds of SYS_ENRAGE is 100 x 1.08^3");

        enraged.ShouldNotBe(
            899.8912,
            "899.8912 is 100 x 2.08^3 — what the literal Pi(1 + v) reading gives, and a boss that " +
            "one-shots the hero three seconds into the enrage");
    }

    /// <summary>🔴 The same erratum from `18` §9.1's side: <c>ALL_COMBAT ×2.0</c> is ×2, not ×3.</summary>
    [Fact]
    public void CP_GLASS_HEART_doubles_every_combat_stat_exactly()
    {
        var doubled = Atk(390.0, StatFixtures.AllCombatEffect("CP_GLASS_HEART_MULT", EffectOp.STAT_MULT, 2.0));

        doubled.ShouldBe(780.0, "390 x 2");
        doubled.ShouldNotBe(1170.0, "390 x 3 is what Pi(1 + v) would give");
    }

    /// <summary>🔒 The 2.0 is data — `18` §9.1's pre-agreed downgrade to ×1.6 is a one-number edit.</summary>
    [Fact]
    public void The_glass_heart_multiplier_is_data_and_the_pre_agreed_downgrade_is_a_with()
    {
        var effect = StatFixtures.AllCombatEffect("CP_GLASS_HEART_MULT", EffectOp.STAT_MULT, 2.0);

        Atk(100.0, effect).ShouldBe(200.0);
        Atk(100.0, effect with { Value = 1.6 }).ShouldBe(160.0);
    }

    /// <summary>
    /// 🔒 `18` §9.1 — the step-8 <c>STAT_SET</c> writes <c>MAX_HP</c> and nothing else. The
    /// <c>MAX_HP</c> half is read as a ward ceiling in
    /// <see cref="The_ward_cap_reads_the_post_step_7_max_hp_not_the_value_step_8_wrote"/>.
    /// </summary>
    [Fact]
    public void CP_GLASS_HEART_sets_max_hp_after_the_multiplier_and_leaves_everything_else_doubled() =>
        Atk(
            390.0,
            StatFixtures.AllCombatEffect("CP_GLASS_HEART_MULT", EffectOp.STAT_MULT, 2.0),
            Effect("CP_GLASS_HEART_SET_HP", EffectOp.STAT_SET, StatId.MAX_HP, 1.0))
            .ShouldBe(780.0, "the set writes MAX_HP only — everything else is still doubled");

    // ─────────────────────────────────────────── 05 §4.1 · the post-step-7 Max HP, read as a ward

    /// <summary>
    /// 🔒 `05` §4.1 — the ward cap is <c>wardCapPct ×</c> Max HP <b>as it stood after step 7</b>,
    /// which is what keeps <c>CP_GLASS_HEART</c>'s re-based shields functional (`18` §9.1).
    /// </summary>
    /// <remarks>
    /// A <c>SHIELD</c> grant is clamped to the cap and the clamped amount is what `05` §7's
    /// <c>Shield</c> event carries, so an over-large grant reports the ceiling itself.
    /// </remarks>
    [Fact]
    public void The_ward_cap_reads_the_post_step_7_max_hp_not_the_value_step_8_wrote()
    {
        var granted = PublicFightBench.Duel(
            PublicFightBench.Stats(2950.0, (StatId.ATK, 100.0)),
            PublicFightBench.Stats(500_000.0),
            attackerEffects:
            [
                StatFixtures.AllCombatEffect("CP_GLASS_HEART_MULT", EffectOp.STAT_MULT, 2.0),
                Effect("CP_GLASS_HEART_SET_HP", EffectOp.STAT_SET, StatId.MAX_HP, 1.0),
                Shield("CP_GLASS_HEART_WARD", 999_999.0),
            ])
            .ValuesBy(CombatEventType.Shield, CombatActor.None)
            .Single();

        granted.ShouldBe(5900.0, "2950 x 2, read after step 7 and before step 8's STAT_SET");
        granted.ShouldNotBe(
            1.0, "1.0 is the final MAX_HP — reading it would cap every shield on the build at 1 HP");
    }

    /// <summary>🔒 The negative control: with nothing to set or cap it, the two readings agree.</summary>
    [Fact]
    public void The_post_step_7_max_hp_is_the_final_value_when_nothing_sets_or_caps_it() =>
        PublicFightBench.Duel(
            PublicFightBench.Stats(2950.0, (StatId.ATK, 100.0)),
            PublicFightBench.Stats(500_000.0),
            attackerEffects:
            [
                Effect("A_FLAT", EffectOp.STAT_ADD_FLAT, StatId.MAX_HP, 500.0),
                Shield("B_WARD", 999_999.0),
            ])
            .ValuesBy(CombatEventType.Shield, CombatActor.None)
            .Single()
            .ShouldBe(3450.0, "2950 + 500, with nothing at step 7 or 8 to make the two differ");

    // ───────────────────────────────────────────────────────────── steps 4, 5 and their order

    /// <summary>`05` §1.1 — <c>(Base + Σ FlatAdd) × (1 + Σ PctAdd)</c>.</summary>
    [Fact]
    public void Flat_adds_land_before_percent_and_the_percent_bucket_is_additive()
    {
        var result = Atk(
            100.0,
            Effect("A_FLAT_1", EffectOp.STAT_ADD_FLAT, StatId.ATK, 30.0),
            Effect("A_FLAT_2", EffectOp.STAT_ADD_FLAT, StatId.ATK, 20.0),
            Effect("B_PCT_1", EffectOp.STAT_ADD_PCT, StatId.ATK, 0.12),
            Effect("B_PCT_2", EffectOp.STAT_ADD_PCT, StatId.ATK, 0.08));

        result.ShouldBe(180.0, "(100 + 30 + 20) x (1 + 0.12 + 0.08)");
        result.ShouldNotBe(
            188.0, "188 is 100 x 1.12 x 1.08 + 50 — percent before flat, or multiplicatively");
    }

    /// <summary>
    /// 🔒 R5 — `18` §8 step 1's <em>"(in draft order)"</em> is the collection order; the closing
    /// <em>"effect-id order"</em> is the application order at steps 6–8. Arrival order cannot reach
    /// the arithmetic.
    /// </summary>
    [Fact]
    public void Aggregation_does_not_depend_on_the_order_the_effects_arrive_in()
    {
        EffectDefinition[] effects =
        [
            Effect("A_FLAT", EffectOp.STAT_ADD_FLAT, StatId.ATK, 17.0),
            Effect("B_PCT", EffectOp.STAT_ADD_PCT, StatId.ATK, 0.13),
            Effect("C_MULT", EffectOp.STAT_MULT, StatId.ATK, 1.07),
            Effect("D_MULT", EffectOp.STAT_MULT, StatId.ATK, 1.19),
        ];

        var forwards = Atk(100.0, effects);

        forwards.ShouldBe(168.343, "(100 + 17) x 1.13 = 132.21, x 1.07 = 141.4647, x 1.19");

        Atk(100.0, effects.Reverse().ToArray()).ShouldBe(forwards);
        Atk(100.0, [effects[2], effects[0], effects[3], effects[1]]).ShouldBe(forwards);
    }

    /// <summary>🔒 <c>STAT_SET</c> is last-writer-wins at step 8, and the ids decide who is last.</summary>
    [Fact]
    public void Steps_6_to_8_apply_in_ascending_ordinal_effect_id_order()
    {
        var setLow = Effect("PK_A_SET", EffectOp.STAT_SET, StatId.ATK, 10.0);
        var setHigh = Effect("PK_B_SET", EffectOp.STAT_SET, StatId.ATK, 20.0);

        Atk(5.0, setHigh, setLow).ShouldBe(20.0, "PK_B_SET sorts last");
        Atk(5.0, setLow, setHigh).ShouldBe(20.0, "same answer from the other input order");
    }

    /// <summary>
    /// 🔒 The comparer is <b>ordinal</b>: <c>"PK_A"</c> sorts after <c>"PKA"</c> ordinally
    /// (<c>'_'</c> is U+005F, <c>'A'</c> is U+0041) and before it under <c>en-US</c> collation.
    /// </summary>
    [Fact]
    public void The_order_is_ordinal_so_underscores_sort_where_their_code_unit_says() =>
        Atk(
            50.0,
            Effect("PK_A", EffectOp.STAT_SET, StatId.ATK, 1.0),
            Effect("PKA", EffectOp.STAT_SET, StatId.ATK, 2.0))
            .ShouldBe(
                1.0,
                "PK_A sorts LAST ordinally. 2.0 is the ambient-collation answer — a machine whose " +
                "culture decided this would fight a different fight from the server's");

    // ─────────────────────────────────────────────────────── rounding, at every step not just 10

    /// <summary>
    /// 🔒 `05` §1.1 rounds <em>"at every accumulation point"</em>, not only at step 10 — and the two
    /// readings give different numbers, so they are not interchangeable.
    /// </summary>
    [Fact]
    public void Rounding_at_step_5_and_rounding_only_at_step_10_are_different_answers()
    {
        var roundedOnlyAtTheEnd = Math.Round(1.0 * (1.0 + 0.123_456) * 2.0, 4);
        roundedOnlyAtTheEnd.ShouldBe(2.2469, "1.123456 x 2 = 2.246912, rounded once at the end");

        var result = Atk(
            1.0,
            Effect("A_PCT", EffectOp.STAT_ADD_PCT, StatId.ATK, 0.123_456),
            Effect("B_MULT", EffectOp.STAT_MULT, StatId.ATK, 2.0));

        result.ShouldBe(2.247, "step 5 rounds 1.123456 to 1.1235, then step 7 doubles it");
        result.ShouldNotBe(roundedOnlyAtTheEnd);
    }

    [Fact]
    public void Step_4_rounds_before_step_5_multiplies()
    {
        var result = Atk(
            0.0,
            Effect("A_FLAT", EffectOp.STAT_ADD_FLAT, StatId.ATK, 1.234_56),
            Effect("B_PCT", EffectOp.STAT_ADD_PCT, StatId.ATK, 1.0));

        result.ShouldBe(2.4692, "1.23456 -> 1.2346 at step 4, then x2");
        result.ShouldNotBe(2.4691, "2.46912 rounded once would be 2.4691");
    }

    /// <summary>
    /// 🔒 Steps 8–10 are terminal, so <em>which</em> of them rounds is not observable — only that
    /// one of them does.
    /// </summary>
    [Fact]
    public void A_STAT_SET_of_an_unrounded_value_lands_rounded()
    {
        var result = Atk(5.0, Effect("A_SET", EffectOp.STAT_SET, StatId.ATK, 1.234_56));

        result.ShouldBe(1.2346);
        StatRounding.IsRounded(result).ShouldBeTrue();
    }

    // ────────────────────────────────────────────────────────────────────── step 9 · the caps

    /// <summary>
    /// 🔒 `05` §1.1 — <em>"caps are applied after all aggregation"</em>.
    /// </summary>
    /// <remarks>
    /// <c>DR_PCT</c> is the cap these cases read because it is the one capped stat whose ceiling is
    /// <em>deterministically</em> visible: `05` §4 step 6 multiplies the hit by <c>(1 − DR%)</c>. The
    /// other five are draw thresholds, visible only as a rate across many swings. The raw is 100
    /// throughout, so each expectation is the percentage that survived.
    /// </remarks>
    [Fact]
    public void Caps_are_applied_after_all_aggregation() =>
        Mitigated(
            Effect("A_PCT", EffectOp.STAT_ADD_PCT, StatId.DR_PCT, 30.0),
            Effect("B_MULT", EffectOp.STAT_MULT, StatId.DR_PCT, 5.0))
            .ShouldBe(40.0, "DR% caps at 0.60 however large the build gets");

    /// <summary>🔒 A step-8 set below the ceiling means the cap binds nothing at all.</summary>
    [Fact]
    public void A_cap_binds_the_end_of_the_pipeline_not_an_intermediate() =>
        Mitigated(
            Effect("A_MULT", EffectOp.STAT_MULT, StatId.DR_PCT, 2.0),
            Effect("B_SET", EffectOp.STAT_SET, StatId.DR_PCT, 0.30))
            .ShouldBe(70.0, "the set lands after step 7's x2 and below the 0.60 cap");

    /// <summary>🔒 Step 9 runs after step 8, so a <c>STAT_SET</c> above the ceiling is still clamped.</summary>
    [Fact]
    public void A_STAT_SET_above_the_ceiling_is_still_capped()
    {
        var landed = Mitigated(Effect("A_SET", EffectOp.STAT_SET, StatId.DR_PCT, 0.90));

        landed.ShouldBe(40.0, "DR% caps at 0.60");
        landed.ShouldNotBe(10.0, "10 is an uncapped 0.90 — the cap applied before step 8");
    }

    /// <summary>🔒 …and after step 7: a sub-1 multiplier brings an over-cap intermediate back down.</summary>
    [Fact]
    public void A_cap_is_not_applied_to_an_intermediate_a_later_step_brings_back_down()
    {
        var landed = Mitigated(
            Effect("A_PCT", EffectOp.STAT_ADD_PCT, StatId.DR_PCT, 30.0),
            Effect("B_MULT", EffectOp.STAT_MULT, StatId.DR_PCT, 0.5));

        landed.ShouldBe(40.0, "0.05 x 31 = 1.55, x 0.5 = 0.775, then capped to 0.60");
        landed.ShouldNotBe(70.0, "70 is a 0.30 DR% — capped at step 5 and then halved by step 7");
    }

    /// <summary>🔒 `05` §1 caps <c>THORNS</c> nowhere, and the reflected damage says so.</summary>
    [Fact]
    public void An_uncapped_stat_is_never_bound() =>
        PublicFightBench.Duel(
            PublicFightBench.Stats(500_000.0, (StatId.ATK, 100.0)),
            PublicFightBench.Stats(500_000.0, (StatId.THORNS, 0.10)),
            defenderEffects: [Effect("A_MULT", EffectOp.STAT_MULT, StatId.THORNS, 40.0)])
            .ValuesBy(CombatEventType.Hit, CombatActor.Enemy(0))
            .ShouldContain(400.0, "0.10 x 40 = 4.0, and 4.0 x the 100 hit is four times the hit");

    // ──────────────────────────────────────────────────────────────────────── the whole order

    /// <summary>One block through all ten steps, so that a step moving would change the answer.</summary>
    [Fact]
    public void The_ten_steps_run_in_the_order_18_section_8_states() =>
        Atk(
            100.0,
            Effect("A_FLAT", EffectOp.STAT_ADD_FLAT, StatId.ATK, 50.0),
            Effect("B_PCT", EffectOp.STAT_ADD_PCT, StatId.ATK, 0.20),
            Effect("C_MULT", EffectOp.STAT_MULT, StatId.ATK, 1.50))
            .ShouldBe(270.0, "step 4: 100 + 50 = 150 · step 5: x 1.20 = 180 · step 7: x 1.50");

    [Fact]
    public void An_empty_effect_list_leaves_the_base_block_alone() =>
        Atk(137.5).ShouldBe(137.5);

    /// <summary>Ops outside `18` §2.1's stat family change no stat, and are not an error.</summary>
    [Fact]
    public void An_op_that_is_not_a_stat_op_changes_nothing() =>
        Atk(
            100.0,
            new EffectDefinition { Id = "PK_CLEAVE", Op = EffectOp.DAMAGE, Value = 0.40 },
            new EffectDefinition { Id = "PK_FLURRY", Op = EffectOp.EXTRA_ATTACK, Value = 1 })
            .ShouldBe(100.0);

    /// <summary>
    /// 🔒 A stat op on one of `18` §2.1's 12 non-combat stats — a "+X% Gold Gain" affix — is
    /// legitimate content and simply not this block's subject. That it is also <em>reported</em> is
    /// <see cref="StatAggregationInternalTests"/>'s half.
    /// </summary>
    [Fact]
    public void A_stat_op_on_a_non_combat_stat_leaves_the_combat_block_alone() =>
        Atk(
            100.0,
            Effect("GEAR_GOLD_AFFIX", EffectOp.STAT_ADD_PCT, StatId.GOLD_PCT, 0.15),
            Effect("PK_SHARP_EDGE_I", EffectOp.STAT_ADD_PCT, StatId.ATK, 0.12))
            .ShouldBe(112.0);

    /// <summary>
    /// 🔒 A non-combat stat op is skipped <b>before</b> its value is read: valuing
    /// <c>PK_HOARD</c>'s <c>valueScale</c> would throw for a stat the pipeline never writes.
    /// </summary>
    [Fact]
    public void A_non_combat_stat_op_is_skipped_before_its_value_is_ever_read() =>
        Atk(
            100.0,
            new EffectDefinition
            {
                Id = "PK_HOARD_I",
                Op = EffectOp.STAT_ADD_PCT,
                Stat = StatSelector.Of(StatId.GOLD_PCT),
                Value = 0.01,
                ValueScale = new ValueScale { Fn = ConditionFunction.GOLD_HELD, Per = 100, Cap = null },
            })
            .ShouldBe(100.0, "the fight runs rather than throwing about a stat it never writes");

    /// <summary>
    /// 🔒 The step-2 gate is asked only of the six stat ops — refusing a conditional <c>DAMAGE</c>
    /// here would make the pipeline unusable against any real build.
    /// </summary>
    [Fact]
    public void A_conditional_effect_outside_the_stat_family_does_not_reach_the_step_2_gate() =>
        Atk(
            100.0,
            new EffectDefinition
            {
                Id = "PK_CLEAVE_I",
                Op = EffectOp.DAMAGE,
                Value = 0.40,
                Condition = EffectCondition.Of(new ConditionTerm
                {
                    Fn = ConditionFunction.ENEMY_COUNT,
                    Comparator = ConditionComparator.GT,
                    Value = 1,
                }),
            })
            .ShouldBe(100.0);

    /// <summary>🔒 A stat op with no <c>stat</c> selector is refused by name, and the refusal reaches
    /// a caller outside <c>Core</c>.</summary>
    [Fact]
    public void A_stat_op_with_no_stat_selector_is_refused_rather_than_ignored()
    {
        var thrown = Should.Throw<ArgumentException>(() => Atk(
            100.0,
            new EffectDefinition { Id = "PK_BROKEN", Op = EffectOp.STAT_ADD_PCT, Value = 0.12 }));

        thrown.Message.ShouldContain("PK_BROKEN", Case.Sensitive);
        thrown.Message.ShouldContain("no 'stat'", Case.Sensitive);
    }

    // ══════════════════════════════════════════════════════════════════════════════ helpers

    /// <summary>
    /// The hit landing on a defender holding <paramref name="defenderEffects"/>, against a raw of
    /// 100 — so the number is the percentage step 6 let through. <c>DEF</c> is 0 so step 3 cannot
    /// dilute it.
    /// </summary>
    private static double Mitigated(params EffectDefinition[] defenderEffects) =>
        PublicFightBench.Duel(
            PublicFightBench.Stats(500.0, (StatId.ATK, 100.0)),
            PublicFightBench.Stats(500_000.0, (StatId.DR_PCT, 0.05)),
            defenderEffects: defenderEffects)
        .AttackerHit();

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
}
