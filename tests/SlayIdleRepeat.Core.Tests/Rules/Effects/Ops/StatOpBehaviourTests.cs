using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Ops;

/// <summary>
/// 🔒 `18` §8 steps 6 and 9 — <c>STAT_CONVERT</c> and all three <c>STAT_CAP_OVERRIDE</c> kinds,
/// through the real aggregation.
/// </summary>
/// <remarks>
/// Both ops were unimplementable as authored (R6) and were closed under `18` §10 by adding
/// <c>toStat</c>, <c>STAT_MAX</c> and <c>REDIRECT_EXCESS</c>. These are the numeric tests §10 step 3
/// requires, and they run through <see cref="StatAggregation.Aggregate"/> rather than against the
/// seam directly, so the step's <em>position</em> in the ten-step order is inside the assertion.
/// </remarks>
public sealed class StatOpBehaviourTests
{
    /// <summary>
    /// 🔒 `06` — <c>PK_TURTLE</c>: <em>"convert 20% of DEF into ATK"</em>. Two signed deltas, off the
    /// <b>post-step-5</b> DEF.
    /// </summary>
    [Fact]
    public void PK_TURTLE_moves_20_percent_of_DEF_into_ATK()
    {
        var result = StatAggregation.Aggregate(
            StatFixtures.Block((StatId.ATK, 100.0), (StatId.DEF, 500.0)),
            [Convert("PK_TURTLE_T1", StatId.DEF, StatId.ATK, 0.20)],
            StatCaps.None,
            StatAggregationSeams.Strict);

        result.Final[StatId.DEF].ShouldBe(400.0, "500 - 100");
        result.Final[StatId.ATK].ShouldBe(200.0, "100 + 100");
    }

    /// <summary>
    /// 🔒 `06` — <c>PK_JUGGERNAUT</c>: <em>"convert 8% of Max HP into ATK"</em>. The same op with a
    /// different <b>source</b>, which is what fixes <c>stat</c> as the source and <c>toStat</c> as
    /// the destination.
    /// </summary>
    [Fact]
    public void PK_JUGGERNAUT_moves_8_percent_of_MAX_HP_into_ATK()
    {
        var result = StatAggregation.Aggregate(
            StatFixtures.Block((StatId.ATK, 100.0), (StatId.MAX_HP, 2500.0)),
            [Convert("PK_JUGGERNAUT_T1", StatId.MAX_HP, StatId.ATK, 0.08)],
            StatCaps.None,
            StatAggregationSeams.Strict);

        result.Final[StatId.MAX_HP].ShouldBe(2300.0);
        result.Final[StatId.ATK].ShouldBe(300.0, "100 + 0.08 x 2500");
    }

    /// <summary>
    /// 🔒 `18` §8 step 6 reads <b>post-step-5</b> values, so two conversions off one source both take
    /// their percentage of the same number — not of each other's output.
    /// </summary>
    [Fact]
    public void Two_conversions_off_one_source_both_read_the_same_post_step_5_value()
    {
        var result = StatAggregation.Aggregate(
            StatFixtures.Block((StatId.ATK, 0.0), (StatId.DEF, 1000.0)),
            [
                Convert("A_CONVERT", StatId.DEF, StatId.ATK, 0.10),
                Convert("B_CONVERT", StatId.DEF, StatId.ATK, 0.10),
            ],
            StatCaps.None,
            StatAggregationSeams.Strict);

        result.Final[StatId.ATK].ShouldBe(
            200.0, "100 + 100; a sequential reading would give 100 + 90 = 190");
        result.Final[StatId.DEF].ShouldBe(800.0);
    }

    /// <summary>Step 5 runs first: the conversion takes its percentage of the boosted DEF.</summary>
    [Fact]
    public void A_conversion_reads_DEF_after_step_5_has_multiplied_it()
    {
        var result = StatAggregation.Aggregate(
            StatFixtures.Block((StatId.ATK, 0.0), (StatId.DEF, 100.0)),
            [
                StatFixtures.Effect("A_PCT", EffectOp.STAT_ADD_PCT, StatId.DEF, 1.0),
                Convert("B_CONVERT", StatId.DEF, StatId.ATK, 0.50),
            ],
            StatCaps.None,
            StatAggregationSeams.Strict);

        result.Final[StatId.ATK].ShouldBe(100.0, "0.50 x the post-step-5 DEF of 200");
    }

    /// <summary>
    /// 🔒 `05` §1.1 rounds <b>results</b>, not authored values: the conversion's fraction reaches the
    /// multiplication unrounded, and only the product is rounded.
    /// </summary>
    /// <remarks>
    /// The other five `18` §8 steps pass <c>EffectiveValue</c> through unrounded and round after the
    /// accumulation; step 6 rounding its fraction first would have made this pipeline the odd one
    /// out. <c>0.123456 × 1000</c> is <c>123.456</c>; pre-rounding the fraction to <c>0.1235</c>
    /// gives <c>123.5</c>.
    /// </remarks>
    [Fact]
    public void A_conversion_fraction_reaches_the_multiplication_unrounded()
    {
        var result = StatAggregation.Aggregate(
            StatFixtures.Block((StatId.ATK, 0.0), (StatId.DEF, 1000.0)),
            [Convert("PK_PRECISE", StatId.DEF, StatId.ATK, 0.123456)],
            StatCaps.None,
            StatAggregationSeams.Strict);

        result.Final[StatId.ATK].ShouldBe(
            123.456, "pre-rounding the fraction to 0.1235 would have given 123.5");
    }

    /// <summary>
    /// 🔒 <c>toStat</c> belongs to <see cref="StatCapKind.REDIRECT_EXCESS"/> alone — a raise and a
    /// heal ceiling send nothing anywhere, so a destination on one is a key that means nothing.
    /// </summary>
    /// <remarks>
    /// ⚠️ Enforced here rather than in <c>effect.schema.json</c>: <c>JsonSchemaValidator</c>
    /// implements neither <c>not</c> nor <c>if</c>/<c>then</c>/<c>else</c>, so a conditional-required
    /// rule is not expressible there. Recorded as a known limit of the schema.
    /// </remarks>
    [Theory]
    [InlineData(StatCapKind.STAT_MAX)]
    [InlineData(StatCapKind.HEAL_CEILING)]
    public void A_toStat_on_a_cap_override_that_is_not_a_redirect_is_refused(StatCapKind kind)
    {
        var control = StatFixtures.Effect("TAL_X", EffectOp.STAT_CAP_OVERRIDE, StatId.CRIT, 0.90) with
        {
            CapKind = kind,
        };

        StatAggregation.Aggregate(
            StatFixtures.Block((StatId.CRIT, 0.60)), [control], StatFixtures.Caps(),
            StatAggregationSeams.Strict);

        var borrowed = control with { ToStat = StatId.CDMG };

        Should.Throw<EffectContextException>(
                  () => StatAggregation.Aggregate(
                      StatFixtures.Block((StatId.CRIT, 0.60)), [borrowed], StatFixtures.Caps(),
                      StatAggregationSeams.Strict))
              .Message.ShouldContain("names a toStat", Case.Sensitive);
    }

    /// <summary>A conversion onto its own source moves nothing and is refused.</summary>
    [Fact]
    public void A_conversion_whose_source_and_destination_are_the_same_stat_is_refused()
    {
        Should.Throw<EffectContextException>(
                  () => StatAggregation.Aggregate(
                      StatFixtures.Block((StatId.DEF, 100.0)),
                      [Convert("PK_X", StatId.DEF, StatId.DEF, 0.20)],
                      StatCaps.None,
                      StatAggregationSeams.Strict))
              .Message.ShouldContain("converts DEF into itself", Case.Sensitive);
    }

    /// <summary>A conversion with no destination names the `18` §10 key that was missing.</summary>
    [Fact]
    public void A_conversion_with_no_toStat_names_the_key_18_10_added()
    {
        var noDestination = StatFixtures.Effect("PK_TURTLE_T1", EffectOp.STAT_CONVERT, StatId.DEF, 0.20);

        Should.Throw<EffectContextException>(
                  () => StatAggregation.Aggregate(
                      StatFixtures.Block((StatId.DEF, 100.0)), [noDestination], StatCaps.None,
                      StatAggregationSeams.Strict))
              .Message.ShouldContain("names no toStat", Case.Sensitive);
    }

    /// <summary>
    /// A conversion across the 26-stat / 14-stat boundary is refused: "20% of GOLD_PCT into ATK"
    /// would add a real ATK bonus out of a stat the actor block does not hold.
    /// </summary>
    [Fact]
    public void A_conversion_naming_a_non_combat_stat_is_refused()
    {
        var crossBoundary = StatFixtures.Effect("PK_X", EffectOp.STAT_CONVERT, StatId.DEF, 0.20) with
        {
            ToStat = StatId.GOLD_PCT,
        };

        Should.Throw<ArgumentException>(
                  () => StatAggregation.Aggregate(
                      StatFixtures.Block((StatId.DEF, 100.0)), [crossBoundary], StatCaps.None,
                      StatAggregationSeams.Strict))
              .Message.ShouldContain("26 stats", Case.Sensitive);
    }

    // ───────────────────────────────────────────── step 9 · the three cap kinds

    /// <summary>
    /// 🔒 <c>STAT_MAX</c> — §2.1's <em>"raise"</em>, added under `18` §10 because the one authored
    /// <c>capKind</c> raised nothing.
    /// </summary>
    [Fact]
    public void A_STAT_MAX_override_replaces_05_1s_ceiling_on_that_stat()
    {
        var raise = StatFixtures.Effect("TAL_X", EffectOp.STAT_CAP_OVERRIDE, StatId.CRIT, 0.90) with
        {
            CapKind = StatCapKind.STAT_MAX,
        };

        var capped = StatAggregation.Aggregate(
            StatFixtures.Block((StatId.CRIT, 0.88)), [], StatFixtures.Caps(), StatAggregationSeams.Strict);

        capped.Final[StatId.CRIT].ShouldBe(0.75, "the control: 05 §1's ceiling, unraised");

        var raised = StatAggregation.Aggregate(
            StatFixtures.Block((StatId.CRIT, 0.88)), [raise], StatFixtures.Caps(),
            StatAggregationSeams.Strict);

        raised.Final[StatId.CRIT].ShouldBe(0.88, "the override is the only edit between the two");
    }

    /// <summary>
    /// 🔒 <c>REDIRECT_EXCESS</c> — `09` §4's <em>Perfect Strike</em>: <em>"crit chance above the 75%
    /// cap converts to crit damage"</em>. CRIT is still capped; the overshoot lands on CDMG.
    /// </summary>
    /// <remarks>
    /// ⚠️ The ratio below is a <b>test</b> ratio, not `09`'s. `09` §4 words it "at 1:4" and never says
    /// which way round; it is the effect's authored <c>value</c> and M3's talent catalogue owns it
    /// (steering S6). What is asserted here is the arithmetic — <c>excess × value</c> — not a number
    /// this task invented.
    /// </remarks>
    [Fact]
    public void A_REDIRECT_EXCESS_override_caps_the_stat_and_moves_the_overshoot_to_toStat()
    {
        var perfectStrike = StatFixtures.Effect(
            "TAL_PERFECT_STRIKE", EffectOp.STAT_CAP_OVERRIDE, StatId.CRIT, 4.0) with
        {
            CapKind = StatCapKind.REDIRECT_EXCESS,
            ToStat = StatId.CDMG,
        };

        var result = StatAggregation.Aggregate(
            StatFixtures.Block((StatId.CRIT, 0.90), (StatId.CDMG, 0.50)),
            [perfectStrike],
            StatFixtures.Caps(),
            StatAggregationSeams.Strict);

        result.Final[StatId.CRIT].ShouldBe(0.75, "05 §1's cap still binds — the redirect is not a raise");
        result.Final[StatId.CDMG].ShouldBe(1.10, "0.50 + (0.90 - 0.75) x 4.0");
    }

    /// <summary>An uncapped stat has no overshoot, so a redirect off one moves nothing.</summary>
    [Fact]
    public void A_REDIRECT_EXCESS_off_an_uncapped_stat_moves_nothing()
    {
        var redirect = StatFixtures.Effect("TAL_X", EffectOp.STAT_CAP_OVERRIDE, StatId.ATK, 1.0) with
        {
            CapKind = StatCapKind.REDIRECT_EXCESS,
            ToStat = StatId.CDMG,
        };

        var result = StatAggregation.Aggregate(
            StatFixtures.Block((StatId.ATK, 900.0), (StatId.CDMG, 0.50)),
            [redirect],
            StatFixtures.Caps(),
            StatAggregationSeams.Strict);

        result.Final[StatId.ATK].ShouldBe(900.0);
        result.Final[StatId.CDMG].ShouldBe(0.50);
    }

    /// <summary>
    /// 🔒 <c>HEAL_CEILING</c> touches <b>no stat cap at all</b> — it bounds <c>Heal()</c> (`05` §4.3).
    /// Folding it into the table would cap <c>Avatar of War</c>'s Max HP at 0.8, i.e. delete the hero.
    /// </summary>
    [Fact]
    public void A_HEAL_CEILING_override_changes_no_stat_and_is_read_separately_by_the_healer()
    {
        var avatarOfWar = StatFixtures.Effect(
            "TAL_AVATAR_OF_WAR_CAP", EffectOp.STAT_CAP_OVERRIDE, StatId.MAX_HP, 0.80) with
        {
            CapKind = StatCapKind.HEAL_CEILING,
        };

        var result = StatAggregation.Aggregate(
            StatFixtures.Block((StatId.MAX_HP, 2400.0)),
            [avatarOfWar],
            StatFixtures.Caps(),
            StatAggregationSeams.Strict);

        result.Final[StatId.MAX_HP].ShouldBe(2400.0, "the hero's Max HP is untouched");

        StatOpBehaviour.Instance.HealCeilingFraction([avatarOfWar], AuthoredEffectValue.Instance)
                       .ShouldBe(0.80, "05 §4.3's bound, for M2-09 to read through the seam");
    }

    /// <summary>Two heal ceilings: the lowest binds, because a restriction cannot loosen another.</summary>
    [Fact]
    public void The_lowest_heal_ceiling_wins()
    {
        var loose = Ceiling("A_CEILING", 0.90);
        var tight = Ceiling("B_CEILING", 0.55);

        StatOpBehaviour.Instance.HealCeilingFraction([loose, tight], AuthoredEffectValue.Instance)
                       .ShouldBe(0.55);
        StatOpBehaviour.Instance.HealCeilingFraction([tight, loose], AuthoredEffectValue.Instance)
                       .ShouldBe(0.55);
    }

    /// <summary>A redirect with no destination discards the overshoot and reads as a cap raise.</summary>
    [Fact]
    public void A_REDIRECT_EXCESS_with_no_toStat_is_refused()
    {
        var broken = StatFixtures.Effect("TAL_X", EffectOp.STAT_CAP_OVERRIDE, StatId.CRIT, 4.0) with
        {
            CapKind = StatCapKind.REDIRECT_EXCESS,
        };

        Should.Throw<EffectContextException>(
                  () => StatAggregation.Aggregate(
                      StatFixtures.Block((StatId.CRIT, 0.90)), [broken], StatFixtures.Caps(),
                      StatAggregationSeams.Strict))
              .Message.ShouldContain("names no toStat", Case.Sensitive);
    }

    /// <summary>A cap override with no <c>capKind</c> names the three operations it could have been.</summary>
    [Fact]
    public void A_cap_override_with_no_capKind_is_refused()
    {
        var broken = StatFixtures.Effect("TAL_X", EffectOp.STAT_CAP_OVERRIDE, StatId.CRIT, 0.90);

        Should.Throw<EffectContextException>(
                  () => StatAggregation.Aggregate(
                      StatFixtures.Block((StatId.CRIT, 0.90)), [broken], StatFixtures.Caps(),
                      StatAggregationSeams.Strict))
              .Message.ShouldContain("names no capKind", Case.Sensitive);
    }

    private static EffectDefinition Convert(string id, StatId from, StatId to, double fraction) =>
        StatFixtures.Effect(id, EffectOp.STAT_CONVERT, from, fraction) with { ToStat = to };

    private static EffectDefinition Ceiling(string id, double fraction) =>
        StatFixtures.Effect(id, EffectOp.STAT_CAP_OVERRIDE, StatId.MAX_HP, fraction) with
        {
            CapKind = StatCapKind.HEAL_CEILING,
        };
}
