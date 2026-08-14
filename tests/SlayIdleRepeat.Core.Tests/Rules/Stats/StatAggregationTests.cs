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
/// <para>
/// 🔒 <b>Every case here runs through <see cref="CombatSimulator.SimulateDuel"/> — a public entry
/// point — and reads the answer out of <see cref="SimulationResult.Log"/>.</b> An aggregated stat is
/// not a number a caller can ask for; it is a number a caller can <em>observe</em>, as the damage
/// the build produces. <see cref="PublicFightBench.AggregatedAtk"/> is the reading: against a
/// defender with <c>DEF = 0</c>, `05` §4's mitigation is exactly 0 and the <c>Hit</c> event carries
/// the attacker's post-`18`-§8 <c>ATK</c> itself, at the same 4 decimal places `05` §1.1 rounds
/// everything else to.
/// </para>
/// <para>
/// 🔒 <b>Why that matters more than the shorter test it replaces.</b> These cases previously drove
/// the <c>internal</c> <c>StatAggregation.Aggregate</c> directly and asserted on
/// <c>AggregatedStats.Final</c>. That pinned the calculator but not the wiring: an aggregation that
/// was correct in isolation and never reached — or reached with the wrong effect set, or at the
/// wrong point in the tick — passed every one of them. Read through a real fight, each case now
/// fails if <em>either</em> half breaks. The `18` §8 arithmetic that genuinely has no public
/// reading is in <see cref="StatAggregationInternalTests"/>, with the reason stated per case.
/// </para>
/// <para>
/// ⚠️ The caps are `05` §1's shipped ceilings unless a case authors otherwise, because they arrive
/// as <b>content</b> (<c>content/combat_caps.json</c>) rather than as an argument. A case that needs
/// a different ceiling passes a different document — see <c>StatFixtures.CombatCapsSnapshot</c>.
/// </para>
/// </remarks>
public sealed class StatAggregationTests
{
    /// <summary>The attacker's aggregated <c>ATK</c>, as the fight's one <c>Hit</c> reports it.</summary>
    private static double Atk(double baseAtk, params EffectDefinition[] effects) =>
        PublicFightBench.AggregatedAtk(baseAtk, effects);

    /// <summary>One `18` §2.1 stat op.</summary>
    private static EffectDefinition Effect(string id, EffectOp op, StatId stat, double value) =>
        StatFixtures.Effect(id, op, stat, value);

    // ───────────────────────────────────────────────────────────── R1 · STAT_MULT is Π(value)

    /// <summary>
    /// 🔴 <c>STAT_MULT</c>'s <c>value</c> <b>is</b> the multiplier. `05` §1.1's
    /// <c>Π (1 + Multiplicative(stat))</c> term is an erratum.
    /// </summary>
    /// <remarks>
    /// <para>
    /// `05` §3.1's <c>SYS_ENRAGE</c> is <em>"<c>PERIODIC {interval: 1.0, startDelay: 70.0}</c> →
    /// <c>STAT_MULT ATK ×1.08</c>, multiplicative stacking, uncapped"</em>, captioned <em>"+8% ATK
    /// per second"</em>. Three seconds into the enrage that is ×1.08³ = 1.259712, which against a
    /// base of 100 is 125.9712.
    /// </para>
    /// <para>
    /// Under `05` §1.1's literal <c>(1 + v)</c> reading the same three ticks would be ×2.08³ =
    /// 8.998912, i.e. 899.8912 — the boss's attack up nine-fold in three seconds, and up
    /// twenty-thousand-fold by the time `05` §3's 90 s timeout arrives. `18` §8 step 7 says
    /// <em>"product"</em>, and it is right.
    /// </para>
    /// </remarks>
    [Fact]
    public void SYS_ENRAGE_stacks_multiplicatively_on_the_value_not_on_one_plus_the_value()
    {
        var enraged = Atk(
            100.0,
            Effect("SYS_ENRAGE_1", EffectOp.STAT_MULT, StatId.ATK, 1.08),
            Effect("SYS_ENRAGE_2", EffectOp.STAT_MULT, StatId.ATK, 1.08),
            Effect("SYS_ENRAGE_3", EffectOp.STAT_MULT, StatId.ATK, 1.08));

        enraged.ShouldBe(
            125.9712,
            "05 §3.1: three seconds of SYS_ENRAGE is 100 x 1.08^3 = 125.9712");

        enraged.ShouldNotBe(
            899.8912,
            "899.8912 is 100 x 2.08^3 — what 05 §1.1's literal Pi(1 + v) term would produce, and a " +
            "boss that one-shots the hero three seconds into the enrage");
    }

    /// <summary>
    /// 🔴 The same erratum from the other side: `18` §9.1 authors
    /// <c>{"op":"STAT_MULT","stat":"ALL_COMBAT","value":2.0}</c> and captions it <em>"×2 all
    /// stats"</em>. Under <c>(1 + v)</c> it would be ×3.
    /// </summary>
    [Fact]
    public void CP_GLASS_HEART_doubles_every_combat_stat_exactly()
    {
        var doubled = Atk(
            390.0,
            StatFixtures.AllCombatEffect("CP_GLASS_HEART_MULT", EffectOp.STAT_MULT, 2.0));

        doubled.ShouldBe(780.0, "390 x 2");
        doubled.ShouldNotBe(1170.0, "390 x 3 is what Pi(1 + v) would give");
    }

    /// <summary>
    /// 🔒 The number 2.0 is data. `18` §9.1's pre-agreed downgrade to ×1.6 must be a one-number edit.
    /// </summary>
    [Fact]
    public void The_glass_heart_multiplier_is_data_and_the_pre_agreed_downgrade_is_a_with()
    {
        var effect = StatFixtures.AllCombatEffect("CP_GLASS_HEART_MULT", EffectOp.STAT_MULT, 2.0);

        Atk(100.0, effect).ShouldBe(200.0);
        Atk(100.0, effect with { Value = 1.6 }).ShouldBe(160.0);
    }

    /// <summary>
    /// 🔒 `18` §9.1: <em>"<c>MAX_HP</c> is set <b>after</b> all multipliers (step 8), so ×2 never
    /// applies to it."</em> The whole <c>CP_GLASS_HEART</c> perk, both effects, as authored.
    /// </summary>
    /// <remarks>
    /// The <c>MAX_HP</c> half is read as the <b>ward ceiling</b> rather than as a stat, because that
    /// is where `05` §4.1 makes the post-step-7 value observable — see
    /// <see cref="The_ward_cap_reads_the_post_step_7_max_hp_not_the_value_step_8_wrote"/>. What this
    /// case pins is the other half: the ×2 reaches every combat stat, and the set does not undo it.
    /// </remarks>
    [Fact]
    public void CP_GLASS_HEART_sets_max_hp_after_the_multiplier_and_leaves_everything_else_doubled()
    {
        var atk = Atk(
            390.0,
            StatFixtures.AllCombatEffect("CP_GLASS_HEART_MULT", EffectOp.STAT_MULT, 2.0),
            Effect("CP_GLASS_HEART_SET_HP", EffectOp.STAT_SET, StatId.MAX_HP, 1.0));

        atk.ShouldBe(780.0, "the set writes MAX_HP and nothing else — everything else is still doubled");
    }

    // ─────────────────────────────────────────── 05 §4.1 · the post-step-7 Max HP, read as a ward

    /// <summary>
    /// 🔒 `05` §4.1 — the ward pool cap is <c>wardCapPct × "the actor's Max HP as it stood after
    /// `18` §8 step 7"</c> (post-multiplier, pre-<c>STAT_SET</c>), <em>"which is what keeps
    /// <c>CP_GLASS_HEART</c>'s re-based shields functional (`18` §9.1)"</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The ward pool is where the intermediate becomes observable.</b> A <c>SHIELD</c> grant is
    /// clamped to the pool cap and the clamped amount is what `05` §7's <c>Shield</c> event carries,
    /// so a fight that granted a shield larger than the ceiling reports the ceiling itself. The base
    /// block is <c>CP_GLASS_HEART</c>'s: <c>MAX_HP</c> 2950, doubled at step 7 and then <em>set to
    /// 1</em> at step 8.
    /// </para>
    /// <para>
    /// Reading the final <c>MAX_HP</c> instead would cap every shield on that build at 1, and `18`
    /// §9.1 says in as many words that shields <em>"are the build's entire survival mechanism"</em>.
    /// The two readings differ by a factor of 5900 here, which is the whole reason the intermediate
    /// exists rather than being recomputed by whoever needs it.
    /// </para>
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

        granted.ShouldBe(
            5900.0, "2950 x 2, read after 18 §8 step 7 and before step 8's STAT_SET");
        granted.ShouldNotBe(
            1.0, "1.0 is the final MAX_HP — reading it would cap every shield on the build at 1 HP");
    }

    /// <summary>
    /// 🔒 …and when nothing sets or caps <c>MAX_HP</c>, the post-step-7 value <b>is</b> the final one.
    /// The negative control on the case above: the intermediate is not a second, permanently
    /// different number.
    /// </summary>
    [Fact]
    public void The_post_step_7_max_hp_is_the_final_value_when_nothing_sets_or_caps_it()
    {
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
    }

    // ───────────────────────────────────────────────────────────── steps 4, 5 and their order

    /// <summary>
    /// `05` §1.1's surviving half: <c>(Base + Σ FlatAdd) × (1 + Σ PctAdd)</c>. Flat first, percent
    /// buckets additive with each other, then one multiplication.
    /// </summary>
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
            188.0, "188 is 100 x 1.12 x 1.08 + 50 — percent applied before flat, or multiplicatively");
    }

    /// <summary>
    /// 🔒 R5 — `18` §8 step 1's <em>"(in draft order)"</em> is the <b>collection</b> order; the
    /// section's closing <em>"effect-id order, not draft order"</em> is the <b>application</b> order
    /// at steps 6, 7 and 8. They are not in conflict, and the proof is that the order effects arrive
    /// in cannot reach the arithmetic.
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

        forwards.ShouldBe(
            168.343, "(100 + 17) x 1.13 = 132.21, x 1.07 = 141.4647 -> 141.4647, x 1.19 = 168.34299...");

        Atk(100.0, effects.Reverse().ToArray()).ShouldBe(forwards);
        Atk(100.0, [effects[2], effects[0], effects[3], effects[1]]).ShouldBe(forwards);
    }

    /// <summary>
    /// 🔒 Application order is ordinal and it is observable. <c>STAT_SET</c> is last-writer-wins at
    /// step 8, so the two effects' ids — not the order they were handed over — decide the answer.
    /// </summary>
    [Fact]
    public void Steps_6_to_8_apply_in_ascending_ordinal_effect_id_order()
    {
        var setLow = Effect("PK_A_SET", EffectOp.STAT_SET, StatId.ATK, 10.0);
        var setHigh = Effect("PK_B_SET", EffectOp.STAT_SET, StatId.ATK, 20.0);

        Atk(5.0, setHigh, setLow)
            .ShouldBe(20.0, "18 §8 step 8: last writer wins, and PK_B_SET sorts last");

        Atk(5.0, setLow, setHigh)
            .ShouldBe(20.0, "the same answer from the other input order — the sort is what decides");
    }

    /// <summary>
    /// 🔒 `18` §8's comparer is <b>ordinal</b>, exhibited on the pair the section's doc comment calls
    /// out: <c>"PK_A"</c> sorts <em>after</em> <c>"PKA"</c> ordinally (<c>'_'</c> is U+005F, <c>'A'</c>
    /// is U+0041) and <em>before</em> it under <c>en-US</c> collation.
    /// </summary>
    /// <remarks>
    /// 🔒 The discriminating half is that the answer is 1.0 rather than 2.0: under the ambient
    /// collation <c>PKA</c> would sort last and write 2.0 instead. A machine whose culture decided
    /// this would produce a different fight from the server's for the same build, which is the
    /// failure `11` §6's <c>LogHash</c> comparison would report as tampering.
    /// </remarks>
    [Fact]
    public void The_order_is_ordinal_so_underscores_sort_where_their_code_unit_says()
    {
        var pkA = Effect("PK_A", EffectOp.STAT_SET, StatId.ATK, 1.0);
        var pka = Effect("PKA", EffectOp.STAT_SET, StatId.ATK, 2.0);

        Atk(50.0, pkA, pka)
            .ShouldBe(1.0, "PK_A sorts LAST ordinally, so its value is the last writer");
    }

    // ─────────────────────────────────────────────────────── rounding, at every step not just 10

    /// <summary>
    /// 🔒 `05` §1.1 rounds <em>"at every accumulation point … each stat aggregation step"</em>, not
    /// only at `18` §8 step 10. This is a case where the two give different answers.
    /// </summary>
    /// <remarks>
    /// Base 1.0, a percent bucket of 0.123456, then a ×2 at step 7.
    /// <list type="bullet">
    /// <item>Rounding at step 5: <c>1 × 1.123456 → 1.1235</c>, then <c>× 2 = 2.2470</c>.</item>
    /// <item>Rounding only at step 10: <c>1.123456 × 2 = 2.246912 → 2.2469</c>.</item>
    /// </list>
    /// The pipeline must produce the first. A tenth of a milli-unit is not the point — the point is
    /// that the two are different numbers, so "round at the end" and "round at every step" are not
    /// interchangeable readings of `05` §1.1, and a client and a server that chose differently would
    /// diverge. Both readings survive the trip through the fight intact, because the <c>Hit</c> is
    /// rounded to the same four places the stat is.
    /// </remarks>
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

    /// <summary>🔒 Step 4 rounds before step 5 multiplies.</summary>
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
    /// 🔒 A <c>STAT_SET</c> value that is not 4-dp lands rounded. Steps 8, 9 and 10 are <b>terminal</b>
    /// — nothing after them magnifies a difference — so which of the three rounds it is not
    /// observable, and only that <em>one</em> of them does is.
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
    /// 🔒 `05` §1.1: <em>"caps are applied <b>after</b> all aggregation"</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b><c>DR_PCT</c> is the cap these cases read, and it is chosen because it is the one capped
    /// stat whose ceiling is <em>deterministically</em> visible in the log.</b> `05` §4 step 6
    /// multiplies the hit by <c>(1 − DR%)</c>, so a defender whose <c>DR_PCT</c> aggregates above the
    /// 0.60 ceiling takes 40% of the raw hit and one that was capped early takes a different, wrong
    /// fraction. The other five capped stats (<c>CRIT</c>, <c>DODGE</c>, <c>BLOCK</c>, …) are draw
    /// thresholds, so their ceilings are only visible as a rate across many swings.
    /// </para>
    /// <para>The attacker's raw is 100 throughout, which makes each expectation the percentage that survived.</para>
    /// </remarks>
    [Fact]
    public void Caps_are_applied_after_all_aggregation()
    {
        Mitigated(
            Effect("A_PCT", EffectOp.STAT_ADD_PCT, StatId.DR_PCT, 30.0),
            Effect("B_MULT", EffectOp.STAT_MULT, StatId.DR_PCT, 5.0))
            .ShouldBe(40.0, "05 §1 caps DR% at 0.60 however large the build gets, so 40% of 100 lands");
    }

    /// <summary>
    /// 🔒 A cap applied before step 7 would bind the multiplier's input instead of its output — and
    /// a <c>STAT_SET</c> below the ceiling means the cap binds nothing at all.
    /// </summary>
    [Fact]
    public void A_cap_binds_the_end_of_the_pipeline_not_an_intermediate()
    {
        Mitigated(
            Effect("A_MULT", EffectOp.STAT_MULT, StatId.DR_PCT, 2.0),
            Effect("B_SET", EffectOp.STAT_SET, StatId.DR_PCT, 0.30))
            .ShouldBe(70.0, "step 8's set lands after step 7's x2 and below the 0.60 cap, so 70% lands");
    }

    /// <summary>
    /// 🔒 Step 9 is <b>after</b> step 8, and the two orders give different answers. A
    /// <c>STAT_SET</c> above the ceiling is clamped; a cap applied before the set would be
    /// overwritten by it and the actor would carry an uncapped DR%.
    /// </summary>
    [Fact]
    public void A_STAT_SET_above_the_ceiling_is_still_capped()
    {
        var landed = Mitigated(Effect("A_SET", EffectOp.STAT_SET, StatId.DR_PCT, 0.90));

        landed.ShouldBe(40.0, "05 §1 caps DR% at 0.60, and step 9 runs after step 8");
        landed.ShouldNotBe(10.0, "10 is what an uncapped 0.90 would leave — a cap applied before step 8");
    }

    /// <summary>
    /// 🔒 And step 9 is after step 7 in the direction that matters: a multiplier <em>below</em> 1
    /// brings an over-cap intermediate back under the ceiling, so a cap applied at step 5 would bind
    /// a value the final block never holds.
    /// </summary>
    [Fact]
    public void A_cap_is_not_applied_to_an_intermediate_a_later_step_brings_back_down()
    {
        var landed = Mitigated(
            Effect("A_PCT", EffectOp.STAT_ADD_PCT, StatId.DR_PCT, 30.0),
            Effect("B_MULT", EffectOp.STAT_MULT, StatId.DR_PCT, 0.5));

        landed.ShouldBe(40.0, "0.05 x 31 = 1.55, x 0.5 = 0.775, then capped to 0.60, so 40% lands");
        landed.ShouldNotBe(
            70.0, "70 is a 0.30 DR% — the cap applied at step 5 (0.60) and then halved by step 7");
    }

    /// <summary>
    /// 🔒 An uncapped stat is never bound. `05` §1 caps <c>THORNS</c> nowhere, and the reflected
    /// damage is what says so.
    /// </summary>
    /// <remarks>
    /// The attacker's hit is 100, so a <c>THORNS</c> of 4.0 reflects 400 — four times the hit that
    /// caused it, which no ceiling in `05` §1 would allow if one applied.
    /// </remarks>
    [Fact]
    public void An_uncapped_stat_is_never_bound()
    {
        PublicFightBench.Duel(
            PublicFightBench.Stats(500_000.0, (StatId.ATK, 100.0)),
            PublicFightBench.Stats(500_000.0, (StatId.THORNS, 0.10)),
            defenderEffects: [Effect("A_MULT", EffectOp.STAT_MULT, StatId.THORNS, 40.0)])
            .ValuesBy(CombatEventType.Hit, CombatActor.Enemy(0))
            .ShouldContain(400.0, "0.10 x 40 = 4.0 uncapped, and 4.0 x the 100 hit is 400 reflected");
    }

    // ──────────────────────────────────────────────────────────────────────── the whole order

    /// <summary>
    /// One block through all ten steps, with a stat touched by every op the strict seams implement,
    /// so that a step moving would change the answer.
    /// </summary>
    [Fact]
    public void The_ten_steps_run_in_the_order_18_section_8_states()
    {
        Atk(
            100.0,
            Effect("A_FLAT", EffectOp.STAT_ADD_FLAT, StatId.ATK, 50.0),
            Effect("B_PCT", EffectOp.STAT_ADD_PCT, StatId.ATK, 0.20),
            Effect("C_MULT", EffectOp.STAT_MULT, StatId.ATK, 1.50))
            .ShouldBe(270.0, "step 4: 100 + 50 = 150 · step 5: x 1.20 = 180 · step 7: x 1.50 = 270");
    }

    /// <summary>An empty effect list leaves the base block alone.</summary>
    [Fact]
    public void An_empty_effect_list_leaves_the_base_block_alone() =>
        Atk(137.5).ShouldBe(137.5);

    /// <summary>Ops that are not `18` §2.1 stat ops change no stat, and are not an error.</summary>
    [Fact]
    public void An_op_that_is_not_a_stat_op_changes_nothing() =>
        Atk(
            100.0,
            new EffectDefinition { Id = "PK_CLEAVE", Op = EffectOp.DAMAGE, Value = 0.40 },
            new EffectDefinition { Id = "PK_FLURRY", Op = EffectOp.EXTRA_ATTACK, Value = 1 })
            .ShouldBe(100.0);

    /// <summary>
    /// 🔒 A stat op naming one of `18` §2.1's 12 non-combat stats is legitimate authored content — a
    /// "+X% Gold Gain" affix — and is simply not the actor block's subject. The fight runs, and the
    /// combat stats it does name are unaffected.
    /// </summary>
    /// <remarks>
    /// ⚠️ That the skipped effect is <b>reported</b> rather than silently dropped is
    /// <c>AggregatedStats.SkippedNonCombatStatEffects</c>, which no fight publishes — see
    /// <c>StatAggregationInternalTests</c> for that half.
    /// </remarks>
    [Fact]
    public void A_stat_op_on_a_non_combat_stat_leaves_the_combat_block_alone() =>
        Atk(
            100.0,
            Effect("GEAR_GOLD_AFFIX", EffectOp.STAT_ADD_PCT, StatId.GOLD_PCT, 0.15),
            Effect("PK_SHARP_EDGE_I", EffectOp.STAT_ADD_PCT, StatId.ATK, 0.12))
            .ShouldBe(112.0);

    /// <summary>
    /// 🔒 A non-combat stat op never reaches the value reader. `18` §1.1's <c>valueScale</c> on a
    /// "+1% Gold per 100 Gold held" affix (<c>PK_HOARD</c>'s shape) is legitimate authored content;
    /// the actor block does not hold <c>GOLD_PCT</c>, so valuing it would throw about M2-06 for a
    /// stat this pipeline was never going to write.
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
    /// 🔒 The step-2 gate is asked only of `18` §2.1's six stat ops. A conditional <c>DAMAGE</c>
    /// clause changes no stat, so refusing it here would make the pipeline unusable against any real
    /// build for a reason that has nothing to do with the stat pipeline.
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

    /// <summary>
    /// 🔒 A stat op with no <c>stat</c> selector is refused by name rather than ignored, and the
    /// refusal reaches a caller outside <c>Core</c> as an exception from the fight.
    /// </summary>
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
    /// The hit that lands on a defender holding <paramref name="defenderEffects"/>, against a raw
    /// of 100 — so the number <em>is</em> the percentage `05` §4 step 6 let through.
    /// </summary>
    /// <remarks>
    /// The defender's base <c>DR_PCT</c> is `05` §2's 0.0 plus the 0.05 these cases build on, and its
    /// <c>DEF</c> is 0 so that step 3's mitigation cannot dilute step 6's fraction.
    /// </remarks>
    private static double Mitigated(params EffectDefinition[] defenderEffects) =>
        PublicFightBench.Duel(
            PublicFightBench.Stats(500.0, (StatId.ATK, 100.0)),
            PublicFightBench.Stats(500_000.0, (StatId.DR_PCT, 0.05)),
            defenderEffects: defenderEffects)
        .AttackerHit();

    /// <summary>A `05` §4.1 ward grant of a flat amount, on the holder, at battle start.</summary>
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
