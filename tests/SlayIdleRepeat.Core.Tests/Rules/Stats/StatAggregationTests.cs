using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Stats;

/// <summary>
/// 🔒 `18` §8 — the resolution order, <em>"must be implemented exactly, or builds will produce
/// different numbers on client and server."</em>
/// </summary>
public sealed class StatAggregationTests
{
    private static AggregatedStats Aggregate(ActorStats baseStats, params EffectDefinition[] effects) =>
        StatAggregation.Aggregate(baseStats, effects, StatFixtures.Caps(), StatAggregationSeams.Strict);

    private static AggregatedStats Uncapped(ActorStats baseStats, params EffectDefinition[] effects) =>
        StatAggregation.Aggregate(baseStats, effects, StatCaps.None, StatAggregationSeams.Strict);

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
        var enraged = Uncapped(
            StatFixtures.Block((StatId.ATK, 100.0)),
            StatFixtures.Effect("SYS_ENRAGE_1", EffectOp.STAT_MULT, StatId.ATK, 1.08),
            StatFixtures.Effect("SYS_ENRAGE_2", EffectOp.STAT_MULT, StatId.ATK, 1.08),
            StatFixtures.Effect("SYS_ENRAGE_3", EffectOp.STAT_MULT, StatId.ATK, 1.08));

        enraged.Final[StatId.ATK].ShouldBe(
            125.9712,
            "05 §3.1: three seconds of SYS_ENRAGE is 100 x 1.08^3 = 125.9712");

        enraged.Final[StatId.ATK].ShouldNotBe(
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
        var baseStats = StatFixtures.Block(
            (StatId.MAX_HP, 2950.0), (StatId.ATK, 390.0), (StatId.DEF, 195.0), (StatId.ASPD, 1.0));

        var glassHeart = Uncapped(
            baseStats,
            StatFixtures.AllCombatEffect("CP_GLASS_HEART_MULT", EffectOp.STAT_MULT, 2.0));

        glassHeart.Final[StatId.ATK].ShouldBe(780.0, "390 x 2");
        glassHeart.Final[StatId.DEF].ShouldBe(390.0, "195 x 2");
        glassHeart.Final[StatId.ASPD].ShouldBe(2.0, "1.0 x 2");
        glassHeart.Final[StatId.MAX_HP].ShouldBe(5900.0, "2950 x 2");

        glassHeart.Final[StatId.ATK].ShouldNotBe(1170.0, "390 x 3 is what Pi(1 + v) would give");
    }

    /// <summary>
    /// 🔒 The number 2.0 is data. `18` §9.1's pre-agreed downgrade to ×1.6 must be a one-number edit.
    /// </summary>
    [Fact]
    public void The_glass_heart_multiplier_is_data_and_the_pre_agreed_downgrade_is_a_with()
    {
        var effect = StatFixtures.AllCombatEffect("CP_GLASS_HEART_MULT", EffectOp.STAT_MULT, 2.0);
        var baseStats = StatFixtures.Block((StatId.ATK, 100.0));

        Uncapped(baseStats, effect).Final[StatId.ATK].ShouldBe(200.0);
        Uncapped(baseStats, effect with { Value = 1.6 }).Final[StatId.ATK].ShouldBe(160.0);
    }

    /// <summary>
    /// 🔒 `18` §9.1: <em>"<c>MAX_HP</c> is set <b>after</b> all multipliers (step 8), so ×2 never
    /// applies to it."</em> The whole <c>CP_GLASS_HEART</c> perk, both effects, as authored.
    /// </summary>
    [Fact]
    public void CP_GLASS_HEART_sets_max_hp_to_one_after_the_multiplier_rather_than_to_two()
    {
        var result = Uncapped(
            StatFixtures.Block((StatId.MAX_HP, 2950.0), (StatId.ATK, 390.0)),
            StatFixtures.AllCombatEffect("CP_GLASS_HEART_MULT", EffectOp.STAT_MULT, 2.0),
            new EffectDefinition
            {
                Id = "CP_GLASS_HEART_SET_HP",
                Op = EffectOp.STAT_SET,
                Stat = StatSelector.Of(StatId.MAX_HP),
                Value = 1.0,
                ValueMode = ValueMode.FLAT,
            });

        result.Final[StatId.MAX_HP].ShouldBe(1.0, "the set lands at step 8, after step 7's x2");
        result.Final[StatId.MAX_HP].ShouldNotBe(2.0, "the x2 must not reach a value written after it");
        result.Final[StatId.ATK].ShouldBe(780.0, "everything else is still doubled");
    }

    // ─────────────────────────────────────────── 05 §4.1 · the post-step-7 Max HP M2-09 reads

    /// <summary>
    /// 🔒 `05` §4.1 — the ward pool cap is <c>wardCapPct × "the actor's Max HP as it stood after
    /// `18` §8 step 7"</c> (post-multiplier, pre-<c>STAT_SET</c>), <em>"which is what keeps
    /// <c>CP_GLASS_HEART</c>'s re-based shields functional (`18` §9.1)"</em>.
    /// </summary>
    /// <remarks>
    /// Reading <see cref="AggregatedStats.Final"/> instead would cap every shield on that build at
    /// 1 HP, and `18` §9.1 says in as many words that shields <em>"are the build's entire survival
    /// mechanism"</em>. The two numbers differ by a factor of 5900 in this case, which is why the
    /// intermediate is exposed rather than recomputed by whoever needs it.
    /// </remarks>
    [Fact]
    public void The_post_step_7_max_hp_is_exposed_because_05_section_4_1s_ward_cap_reads_it()
    {
        var result = Uncapped(
            StatFixtures.Block((StatId.MAX_HP, 2950.0)),
            StatFixtures.AllCombatEffect("CP_GLASS_HEART_MULT", EffectOp.STAT_MULT, 2.0),
            new EffectDefinition
            {
                Id = "CP_GLASS_HEART_SET_HP",
                Op = EffectOp.STAT_SET,
                Stat = StatSelector.Of(StatId.MAX_HP),
                Value = 1.0,
                ValueMode = ValueMode.FLAT,
            });

        result.PostMultiplierMaxHp.ShouldBe(5900.0, "2950 x 2, read before step 8's STAT_SET");
        result.Final[StatId.MAX_HP].ShouldBe(1.0);
        result.PostMultiplierMaxHp.ShouldNotBe(result.Final[StatId.MAX_HP]);
    }

    [Fact]
    public void The_post_step_7_max_hp_is_the_final_value_when_nothing_sets_or_caps_it()
    {
        var result = Aggregate(
            StatFixtures.Block((StatId.MAX_HP, 2950.0)),
            StatFixtures.Effect("A_FLAT", EffectOp.STAT_ADD_FLAT, StatId.MAX_HP, 500.0));

        result.PostMultiplierMaxHp.ShouldBe(3450.0);
        result.Final[StatId.MAX_HP].ShouldBe(3450.0);
    }

    // ───────────────────────────────────────────────────────────── steps 4, 5 and their order

    /// <summary>
    /// `05` §1.1's surviving half: <c>(Base + Σ FlatAdd) × (1 + Σ PctAdd)</c>. Flat first, percent
    /// buckets additive with each other, then one multiplication.
    /// </summary>
    [Fact]
    public void Flat_adds_land_before_percent_and_the_percent_bucket_is_additive()
    {
        var result = Uncapped(
            StatFixtures.Block((StatId.ATK, 100.0)),
            StatFixtures.Effect("A_FLAT_1", EffectOp.STAT_ADD_FLAT, StatId.ATK, 30.0),
            StatFixtures.Effect("A_FLAT_2", EffectOp.STAT_ADD_FLAT, StatId.ATK, 20.0),
            StatFixtures.Effect("B_PCT_1", EffectOp.STAT_ADD_PCT, StatId.ATK, 0.12),
            StatFixtures.Effect("B_PCT_2", EffectOp.STAT_ADD_PCT, StatId.ATK, 0.08));

        result.Final[StatId.ATK].ShouldBe(180.0, "(100 + 30 + 20) x (1 + 0.12 + 0.08)");
        result.Final[StatId.ATK].ShouldNotBe(
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
        var baseStats = StatFixtures.Block((StatId.ATK, 100.0), (StatId.MAX_HP, 1000.0));

        EffectDefinition[] effects =
        [
            StatFixtures.Effect("A_FLAT", EffectOp.STAT_ADD_FLAT, StatId.ATK, 17.0),
            StatFixtures.Effect("B_PCT", EffectOp.STAT_ADD_PCT, StatId.ATK, 0.13),
            StatFixtures.Effect("C_MULT", EffectOp.STAT_MULT, StatId.ATK, 1.07),
            StatFixtures.Effect("D_MULT", EffectOp.STAT_MULT, StatId.ATK, 1.19),
            StatFixtures.Effect("E_SET", EffectOp.STAT_SET, StatId.MAX_HP, 1.0),
        ];

        var forwards = Uncapped(baseStats, effects);
        var backwards = Uncapped(baseStats, effects.Reverse().ToArray());
        var shuffled = Uncapped(baseStats, [effects[2], effects[0], effects[4], effects[3], effects[1]]);

        // Compared member by member: AggregatedStats is a record, and its
        // SkippedNonCombatStatEffects list would compare by REFERENCE, which is trivially unequal
        // across three calls and would make this rule pass for the wrong reason if it ever inverted.
        foreach (var other in (AggregatedStats[])[backwards, shuffled])
        {
            other.Final.ShouldBe(forwards.Final);
            other.PostMultiplierMaxHp.ShouldBe(forwards.PostMultiplierMaxHp);
            other.SkippedNonCombatStatEffects.ShouldBe(forwards.SkippedNonCombatStatEffects);
        }

        forwards.Final[StatId.ATK].ShouldBe(
            168.343, "(100 + 17) x 1.13 = 132.21, x 1.07 = 141.4647 -> 141.4647, x 1.19 = 168.34299...");
    }

    /// <summary>
    /// 🔒 Application order is ordinal and it is observable. Two multipliers whose product is not
    /// representable at 4 dp give different answers in the two orders, which is precisely why `18`
    /// §8 fixes the order at steps 6, 7 and 8 — and why <c>EffectOrder.IdComparer</c> is
    /// <c>Ordinal</c> rather than the ambient collation.
    /// </summary>
    [Fact]
    public void Steps_6_to_8_apply_in_ascending_ordinal_effect_id_order()
    {
        var setLow = StatFixtures.Effect("PK_A_SET", EffectOp.STAT_SET, StatId.ATK, 10.0);
        var setHigh = StatFixtures.Effect("PK_B_SET", EffectOp.STAT_SET, StatId.ATK, 20.0);

        Uncapped(StatFixtures.Block((StatId.ATK, 5.0)), setHigh, setLow)
            .Final[StatId.ATK]
            .ShouldBe(20.0, "18 §8 step 8: last writer wins, and PK_B_SET sorts last");

        Uncapped(StatFixtures.Block((StatId.ATK, 5.0)), setLow, setHigh)
            .Final[StatId.ATK]
            .ShouldBe(20.0, "the same answer from the other input order — the sort is what decides");
    }

    /// <summary>
    /// 🔒 `18` §8's ordinal comparer, exhibited on the pair the `18` §8 doc comment calls out:
    /// <c>"PK_A"</c> sorts <em>after</em> <c>"PKA"</c> ordinally (<c>'_'</c> is U+005F, <c>'A'</c> is
    /// U+0041) and <em>before</em> it under <c>en-US</c> collation.
    /// </summary>
    [Fact]
    public void The_order_is_ordinal_so_underscores_sort_where_their_code_unit_says()
    {
        var pkA = StatFixtures.Effect("PK_A", EffectOp.STAT_SET, StatId.ATK, 1.0);
        var pka = StatFixtures.Effect("PKA", EffectOp.STAT_SET, StatId.ATK, 2.0);

        EffectOrder.IdComparer.Compare("PK_A", "PKA").ShouldBeGreaterThan(0);

        Uncapped(StatFixtures.Block((StatId.ATK, 0.0)), pkA, pka)
            .Final[StatId.ATK]
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
    /// diverge.
    /// </remarks>
    [Fact]
    public void Rounding_at_step_5_and_rounding_only_at_step_10_are_different_answers()
    {
        var roundedOnlyAtTheEnd = Math.Round(1.0 * (1.0 + 0.123_456) * 2.0, 4);
        roundedOnlyAtTheEnd.ShouldBe(2.2469, "1.123456 x 2 = 2.246912, rounded once at the end");

        var result = Uncapped(
            StatFixtures.Block((StatId.ATK, 1.0)),
            StatFixtures.Effect("A_PCT", EffectOp.STAT_ADD_PCT, StatId.ATK, 0.123_456),
            StatFixtures.Effect("B_MULT", EffectOp.STAT_MULT, StatId.ATK, 2.0));

        result.Final[StatId.ATK].ShouldBe(2.247, "step 5 rounds 1.123456 to 1.1235, then step 7 doubles it");
        result.Final[StatId.ATK].ShouldNotBe(roundedOnlyAtTheEnd);
    }

    [Fact]
    public void Step_4_rounds_before_step_5_multiplies()
    {
        var result = Uncapped(
            StatFixtures.Block((StatId.ATK, 0.0)),
            StatFixtures.Effect("A_FLAT", EffectOp.STAT_ADD_FLAT, StatId.ATK, 1.234_56),
            StatFixtures.Effect("B_PCT", EffectOp.STAT_ADD_PCT, StatId.ATK, 1.0));

        result.Final[StatId.ATK].ShouldBe(2.4692, "1.23456 -> 1.2346 at step 4, then x2");
        result.Final[StatId.ATK].ShouldNotBe(2.4691, "2.46912 rounded once would be 2.4691");
    }

    /// <summary>
    /// 🔒 Step 6 rounds <b>before</b> step 7 multiplies, so a conversion delta with a fifth decimal
    /// place cannot be magnified by a later multiplier.
    /// </summary>
    /// <remarks>
    /// Discriminating, unlike a step-6 case with nothing after it: a delta of <c>0.00005</c> onto a
    /// base of 1.0 rounds to <c>1.0001</c> at step 6 and doubles to <c>2.0002</c>. Carried unrounded
    /// into step 7 it is <c>2.0001</c>. The seam supplies the deltas, so this is the one step whose
    /// input is not already 4-dp by construction.
    /// </remarks>
    [Fact]
    public void Step_6_rounds_the_conversion_deltas_before_step_7_multiplies()
    {
        var result = StatAggregation.Aggregate(
            StatFixtures.Block((StatId.ATK, 1.0)),
            [
                StatFixtures.Effect("A_CONVERT", EffectOp.STAT_CONVERT, StatId.DEF, 0.10),
                StatFixtures.Effect("B_MULT", EffectOp.STAT_MULT, StatId.ATK, 2.0),
            ],
            StatCaps.None,
            StatAggregationSeams.Strict with { Ops = new FixedConversion(new StatDelta(StatId.ATK, 0.000_05)) });

        result.Final[StatId.ATK].ShouldBe(2.0002, "1.00005 -> 1.0001 at step 6, then x2");
        result.Final[StatId.ATK].ShouldNotBe(2.0001, "2.0001 is 1.00005 x 2 rounded once afterwards");
    }

    /// <summary>
    /// 🔒 A <c>STAT_SET</c> value that is not 4-dp lands rounded. Steps 8, 9 and 10 are <b>terminal</b>
    /// — nothing after them magnifies a difference — so which of the three rounds it is not
    /// observable, and only that <em>one</em> of them does is.
    /// </summary>
    /// <remarks>
    /// Stated this way rather than as "step 8 rounds", because that claim would be untestable: the
    /// value arrives from <see cref="IEffectValueReader"/>, which is the only place in the pipeline an
    /// unrounded number can enter after step 7, and `18` §8's own step 10 would catch it regardless.
    /// The redundancy is deliberate (see the pipeline's remarks) and this case pins the outcome the
    /// redundancy exists to guarantee.
    /// </remarks>
    [Fact]
    public void A_STAT_SET_of_an_unrounded_value_lands_rounded()
    {
        var result = Uncapped(
            StatFixtures.Block((StatId.ATK, 5.0)),
            StatFixtures.Effect("A_SET", EffectOp.STAT_SET, StatId.ATK, 1.234_56));

        result.Final[StatId.ATK].ShouldBe(1.2346);
        StatRounding.IsRounded(result.Final[StatId.ATK]).ShouldBeTrue();
    }

    [Fact]
    public void Every_stat_in_the_result_is_rounded_to_four_places()
    {
        var result = Uncapped(
            StatFixtures.Block((StatId.ATK, 3.0)),
            StatFixtures.Effect("A_MULT", EffectOp.STAT_MULT, StatId.ATK, 1.000_000_1));

        result.Final.Values.Select(v => v.Value).ShouldAllBe(v => StatRounding.IsRounded(v));
        result.Final.Values.Count().ShouldBe(14, "ShouldAllBe passes on an empty collection");
    }

    // ────────────────────────────────────────────────────────────────────── step 9 · the caps

    [Fact]
    public void Caps_are_applied_after_all_aggregation()
    {
        var result = Aggregate(
            StatFixtures.Block((StatId.CRIT, 0.05)),
            StatFixtures.Effect("A_PCT", EffectOp.STAT_ADD_PCT, StatId.CRIT, 30.0),
            StatFixtures.Effect("B_MULT", EffectOp.STAT_MULT, StatId.CRIT, 5.0));

        result.Final[StatId.CRIT].ShouldBe(0.75, "05 §1 caps CRIT at 0.75, however large the build gets");
    }

    /// <summary>
    /// 🔒 `05` §1.1: <em>"caps are applied <b>after</b> all aggregation"</em> — not between steps.
    /// A cap applied before step 7 would bind the multiplier's input instead of its output.
    /// </summary>
    [Fact]
    public void A_cap_binds_the_end_of_the_pipeline_not_an_intermediate()
    {
        var result = Aggregate(
            StatFixtures.Block((StatId.DODGE, 0.40)),
            StatFixtures.Effect("A_MULT", EffectOp.STAT_MULT, StatId.DODGE, 2.0),
            StatFixtures.Effect("B_SET", EffectOp.STAT_SET, StatId.DODGE, 0.30));

        result.Final[StatId.DODGE].ShouldBe(
            0.30, "step 8's set lands after step 7's x2 and below the 0.50 cap, so the cap binds nothing");
    }

    /// <summary>
    /// 🔒 Step 9 is <b>after</b> step 8, and the two orders give different answers. A
    /// <c>STAT_SET</c> above the ceiling is clamped; a cap applied before the set would be
    /// overwritten by it and the actor would carry an uncapped dodge.
    /// </summary>
    [Fact]
    public void A_STAT_SET_above_the_ceiling_is_still_capped()
    {
        var result = Aggregate(
            StatFixtures.Block((StatId.DODGE, 0.02)),
            StatFixtures.Effect("A_SET", EffectOp.STAT_SET, StatId.DODGE, 0.90));

        result.Final[StatId.DODGE].ShouldBe(0.50, "05 §1 caps DODGE at 0.50, and step 9 runs after step 8");
        result.Final[StatId.DODGE].ShouldNotBe(0.90, "0.90 is what capping before step 8 would leave");
    }

    /// <summary>
    /// 🔒 And step 9 is after step 7 in the direction that matters: a multiplier <em>below</em> 1
    /// brings an over-cap intermediate back under the ceiling, so a cap applied at step 5 would bind
    /// a value the final block never holds.
    /// </summary>
    [Fact]
    public void A_cap_is_not_applied_to_an_intermediate_a_later_step_brings_back_down()
    {
        var result = Aggregate(
            StatFixtures.Block((StatId.CRIT, 0.05)),
            StatFixtures.Effect("A_PCT", EffectOp.STAT_ADD_PCT, StatId.CRIT, 30.0),
            StatFixtures.Effect("B_MULT", EffectOp.STAT_MULT, StatId.CRIT, 0.5));

        result.Final[StatId.CRIT].ShouldBe(0.75, "0.05 x 31 = 1.55, x 0.5 = 0.775, then capped to 0.75");
        result.Final[StatId.CRIT].ShouldNotBe(0.375, "0.375 is 0.75 x 0.5 — a cap applied at step 5 instead of step 9");
    }

    [Fact]
    public void An_uncapped_stat_is_never_bound()
    {
        var result = Aggregate(
            StatFixtures.Block((StatId.THORNS, 0.10)),
            StatFixtures.Effect("A_MULT", EffectOp.STAT_MULT, StatId.THORNS, 40.0));

        result.Final[StatId.THORNS].ShouldBe(4.0, "05 §1 caps THORN nowhere");
    }

    // ────────────────────────────────────────────────────── ALL_COMBAT and the non-combat stats

    [Fact]
    public void ALL_COMBAT_reaches_every_one_of_the_fourteen_and_nothing_else()
    {
        var baseStats = ActorStats.From(StatIds.Combat.ToDictionary(stat => stat, _ => 3.0));

        var result = Uncapped(
            baseStats,
            StatFixtures.AllCombatEffect("A_MULT", EffectOp.STAT_MULT, 2.0));

        result.Final.Values.Count().ShouldBe(14);
        result.Final.Values.ShouldAllBe(v => v.Value == 6.0);
        result.SkippedNonCombatStatEffects.ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 A stat op naming one of `18` §2.1's 12 non-combat stats is legitimate authored content —
    /// a "+X% Gold Gain" affix — and is simply not the actor block's subject. It is <b>reported</b>
    /// rather than dropped.
    /// </summary>
    /// <remarks>
    /// Silently ignoring it is the failure mode: a resolver that hands the whole build here and never
    /// asks what was left behind would lose every economy affix in the game with nothing going red.
    /// </remarks>
    [Fact]
    public void A_stat_op_on_a_non_combat_stat_is_reported_rather_than_silently_dropped()
    {
        var result = Uncapped(
            StatFixtures.Block((StatId.ATK, 100.0)),
            StatFixtures.Effect("GEAR_GOLD_AFFIX", EffectOp.STAT_ADD_PCT, StatId.GOLD_PCT, 0.15),
            StatFixtures.Effect("PK_SHARP_EDGE_I", EffectOp.STAT_ADD_PCT, StatId.ATK, 0.12));

        result.Final[StatId.ATK].ShouldBe(112.0);
        result.SkippedNonCombatStatEffects.ShouldBe(["GEAR_GOLD_AFFIX"]);
    }

    // ──────────────────────────────────────────────────────────────────────── the whole order

    /// <summary>
    /// One block through all ten steps, with a stat touched by every op the strict seams implement,
    /// so that a step moving would change the answer.
    /// </summary>
    [Fact]
    public void The_ten_steps_run_in_the_order_18_section_8_states()
    {
        var result = Aggregate(
            StatFixtures.Block((StatId.ATK, 100.0), (StatId.CRIT, 0.05), (StatId.MAX_HP, 1000.0)),
            StatFixtures.Effect("A_FLAT", EffectOp.STAT_ADD_FLAT, StatId.ATK, 50.0),
            StatFixtures.Effect("B_PCT", EffectOp.STAT_ADD_PCT, StatId.ATK, 0.20),
            StatFixtures.Effect("C_MULT", EffectOp.STAT_MULT, StatId.ATK, 1.50),
            StatFixtures.Effect("D_PCT_CRIT", EffectOp.STAT_ADD_PCT, StatId.CRIT, 30.0),
            StatFixtures.Effect("E_SET_HP", EffectOp.STAT_SET, StatId.MAX_HP, 1.0));

        // step 4: 100 + 50 = 150 · step 5: 150 x 1.20 = 180 · step 7: 180 x 1.50 = 270
        result.Final[StatId.ATK].ShouldBe(270.0);

        // step 5: 0.05 x 31 = 1.55 · step 9: capped to 0.75
        result.Final[StatId.CRIT].ShouldBe(0.75);

        // step 8 writes 1, and step 7's multipliers never touched MAX_HP
        result.Final[StatId.MAX_HP].ShouldBe(1.0);
        result.PostMultiplierMaxHp.ShouldBe(1000.0);
    }

    [Fact]
    public void An_empty_effect_list_leaves_the_base_block_alone()
    {
        var baseStats = StatFixtures.HeroCurve().At(60);

        var result = Aggregate(baseStats);

        result.Final.ShouldBe(baseStats);
        result.PostMultiplierMaxHp.ShouldBe(2950.0);
        result.SkippedNonCombatStatEffects.ShouldBeEmpty();
    }

    /// <summary>Ops that are not `18` §2.1 stat ops change no stat, and are not an error.</summary>
    [Fact]
    public void An_op_that_is_not_a_stat_op_changes_nothing()
    {
        var result = Aggregate(
            StatFixtures.Block((StatId.ATK, 100.0)),
            new EffectDefinition { Id = "PK_CLEAVE", Op = EffectOp.DAMAGE, Value = 0.40 },
            new EffectDefinition { Id = "PK_FLURRY", Op = EffectOp.EXTRA_ATTACK, Value = 1 });

        result.Final[StatId.ATK].ShouldBe(100.0);
    }

    [Fact]
    public void A_stat_op_with_no_stat_selector_is_refused_rather_than_ignored()
    {
        var thrown = Should.Throw<ArgumentException>(() => Aggregate(
            StatFixtures.Block((StatId.ATK, 100.0)),
            new EffectDefinition { Id = "PK_BROKEN", Op = EffectOp.STAT_ADD_PCT, Value = 0.12 }));

        thrown.Message.ShouldContain("PK_BROKEN", Case.Sensitive);
        thrown.Message.ShouldContain("no 'stat'", Case.Sensitive);
    }

    [Fact]
    public void A_null_effect_in_the_list_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => StatAggregation.Aggregate(
            StatFixtures.Zeroed(), [null!], StatCaps.None, StatAggregationSeams.Strict));
    }

    /// <summary>
    /// 🔒 A non-combat stat op never reaches the value reader. `18` §1.1's <c>valueScale</c> on a
    /// "+1% Gold per 100 Gold held" affix (<c>PK_HOARD</c>'s shape) is legitimate authored content;
    /// the actor block does not hold <c>GOLD_PCT</c>, so valuing it would throw about M2-06 for a
    /// stat this pipeline was never going to write.
    /// </summary>
    [Fact]
    public void A_non_combat_stat_op_is_skipped_before_its_value_is_ever_read()
    {
        var hoard = new EffectDefinition
        {
            Id = "PK_HOARD_I",
            Op = EffectOp.STAT_ADD_PCT,
            Stat = StatSelector.Of(StatId.GOLD_PCT),
            Value = 0.01,
            ValueScale = new ValueScale { Fn = ConditionFunction.GOLD_HELD, Per = 100, Cap = null },
        };

        var result = Uncapped(StatFixtures.Block((StatId.ATK, 100.0)), hoard);

        result.Final[StatId.ATK].ShouldBe(100.0);
        result.SkippedNonCombatStatEffects.ShouldBe(["PK_HOARD_I"]);
    }

    /// <summary>
    /// 🔒 The step-2 gate is asked only of `18` §2.1's six stat ops. A conditional <c>DAMAGE</c>
    /// clause changes no stat, so refusing it here would make the strict seams unusable against any
    /// real build for a reason that has nothing to do with the stat pipeline.
    /// </summary>
    [Fact]
    public void A_conditional_effect_outside_the_stat_family_does_not_reach_the_step_2_gate()
    {
        var cleave = new EffectDefinition
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
        };

        var result = Aggregate(StatFixtures.Block((StatId.ATK, 100.0)), cleave);

        result.Final[StatId.ATK].ShouldBe(100.0);
    }

    [Fact]
    public void The_arguments_are_all_required()
    {
        Should.Throw<ArgumentNullException>(() => StatAggregation.Aggregate(
            null!, [], StatCaps.None, StatAggregationSeams.Strict));
        Should.Throw<ArgumentNullException>(() => StatAggregation.Aggregate(
            StatFixtures.Zeroed(), null!, StatCaps.None, StatAggregationSeams.Strict));
        Should.Throw<ArgumentNullException>(() => StatAggregation.Aggregate(
            StatFixtures.Zeroed(), [], null!, StatAggregationSeams.Strict));
        Should.Throw<ArgumentNullException>(() => StatAggregation.Aggregate(
            StatFixtures.Zeroed(), [], StatCaps.None, null!));
    }

    /// <summary>A conversion seam that returns fixed deltas whatever it is handed.</summary>
    private sealed class FixedConversion(params StatDelta[] deltas) : IStatOpBehaviour
    {
        public IReadOnlyList<StatDelta> Convert(
            IReadOnlyList<EffectDefinition> conversions, ActorStats postAdditive, IEffectValueReader values) =>
            conversions.Count == 0 ? [] : deltas;

        public StatCaps OverrideCaps(
            IReadOnlyList<EffectDefinition> overrides, StatCaps declared, IEffectValueReader values) => declared;
    }
}
