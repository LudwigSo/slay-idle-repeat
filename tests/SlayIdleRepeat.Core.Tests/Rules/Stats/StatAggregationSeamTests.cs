using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Stats;

/// <summary>
/// The three seams M2-07 leaves open in `18` §8, and the refusals that stand in for them until
/// M2-03, M2-05 and M2-06 land.
/// </summary>
/// <remarks>
/// 🔒 Every one of these throws rather than guessing, and each case pins <b>which</b> refusal fired
/// and which task owns it. A seam that quietly no-opped would be indistinguishable from a seam that
/// worked, and the difference only shows up as a balance bug months later.
/// </remarks>
public sealed class StatAggregationSeamTests
{
    // ────────────────────────────────────────────── step 2 · the condition gate (M2-05)

    [Fact]
    public void An_unconditional_effect_passes_the_step_2_gate()
    {
        UnconditionalEffectsOnly.Instance
            .IsActive(StatFixtures.Effect("PK_SHARP_EDGE_I", EffectOp.STAT_ADD_PCT, StatId.ATK, 0.12))
            .ShouldBeTrue();
    }

    /// <summary>
    /// 🔒 A conditional effect is <b>refused</b>, not admitted and not skipped. Both quiet answers
    /// are balance bugs: admitting gives <c>PK_EXECUTIONER</c>'s +25% against a full-health target,
    /// skipping deletes <c>PK_BERSERK</c> from the build.
    /// </summary>
    [Fact]
    public void A_conditional_effect_is_refused_by_the_step_2_gate_until_M2_05()
    {
        var executioner = new EffectDefinition
        {
            Id = "PK_EXECUTIONER_I",
            Op = EffectOp.STAT_ADD_PCT,
            Stat = StatSelector.Of(StatId.DMG_PCT),
            Value = 0.25,
            Condition = EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.TARGET_HP_PCT,
                Comparator = ConditionComparator.LT,
                Value = 0.30,
            }),
        };

        var thrown = Should.Throw<NotSupportedException>(
            () => StatAggregation.Aggregate(
                StatFixtures.Zeroed(), [executioner], StatCaps.None, StatAggregationSeams.Strict));

        thrown.Message.ShouldContain("18 §8 step 2", Case.Sensitive);
        thrown.Message.ShouldContain("PK_EXECUTIONER_I", Case.Sensitive);
        thrown.Message.ShouldContain("M2-05", Case.Sensitive);
    }

    /// <summary>The seam really is a seam: a double replaces the whole of step 2.</summary>
    [Fact]
    public void A_condition_gate_double_decides_step_2_instead()
    {
        var effect = StatFixtures.Effect("PK_ANY", EffectOp.STAT_ADD_PCT, StatId.ATK, 1.0);

        var admitted = StatAggregation.Aggregate(
            StatFixtures.Block((StatId.ATK, 100.0)),
            [effect],
            StatCaps.None,
            StatAggregationSeams.Strict with { Conditions = new FixedGate(true) });

        var rejected = StatAggregation.Aggregate(
            StatFixtures.Block((StatId.ATK, 100.0)),
            [effect],
            StatCaps.None,
            StatAggregationSeams.Strict with { Conditions = new FixedGate(false) });

        admitted.Final[StatId.ATK].ShouldBe(200.0);
        rejected.Final[StatId.ATK].ShouldBe(100.0);
    }

    // ───────────────────────────────────────── value · valueScale and valueMode (M2-06/M2-03)

    [Fact]
    public void An_authored_value_with_no_scale_is_read_as_written()
    {
        AuthoredEffectValue.Instance
            .EffectiveValue(StatFixtures.Effect("PK_X", EffectOp.STAT_MULT, StatId.ATK, 1.08))
            .ShouldBe(1.08);
    }

    [Fact]
    public void A_valueScale_is_refused_until_the_state_reading_exists()
    {
        var berserk = new EffectDefinition
        {
            Id = "PK_BERSERK_I",
            Op = EffectOp.STAT_ADD_PCT,
            Stat = StatSelector.Of(StatId.ATK),
            Value = 0.01,
            ValueScale = new ValueScale { Fn = ConditionFunction.SELF_MISSING_HP_PCT, Per = 0.01, Cap = 45 },
        };

        var thrown = Should.Throw<NotSupportedException>(
            () => AuthoredEffectValue.Instance.EffectiveValue(berserk));

        thrown.Message.ShouldContain("PK_BERSERK_I", Case.Sensitive);
        thrown.Message.ShouldContain("18 §1.1", Case.Sensitive);
        thrown.Message.ShouldContain("M2-05", Case.Sensitive);
    }

    /// <summary>
    /// <c>FLAT</c> is the one <c>valueMode</c> `18` puts on a stat op — §9.1's
    /// <c>STAT_SET MAX_HP</c>. The other seven are damage- and heal-relative.
    /// </summary>
    [Fact]
    public void FLAT_is_accepted_and_the_other_seven_value_modes_are_refused()
    {
        var flat = StatFixtures.Effect("CP_X", EffectOp.STAT_SET, StatId.MAX_HP, 1.0) with
        {
            ValueMode = ValueMode.FLAT,
        };

        AuthoredEffectValue.Instance.EffectiveValue(flat).ShouldBe(1.0);

        foreach (var mode in Enum.GetValues<ValueMode>().Where(m => m != ValueMode.FLAT))
        {
            var effect = StatFixtures.Effect("CP_X", EffectOp.STAT_SET, StatId.MAX_HP, 1.0) with
            {
                ValueMode = mode,
            };

            Should.Throw<NotSupportedException>(() => AuthoredEffectValue.Instance.EffectiveValue(effect))
                  .Message.ShouldContain("M2-03", Case.Sensitive);
        }
    }

    [Fact]
    public void A_stat_op_with_no_value_is_refused_rather_than_read_as_zero()
    {
        var broken = new EffectDefinition
        {
            Id = "PK_NO_VALUE",
            Op = EffectOp.STAT_MULT,
            Stat = StatSelector.Of(StatId.ATK),
        };

        Should.Throw<ArgumentException>(() => AuthoredEffectValue.Instance.EffectiveValue(broken))
              .Message.ShouldContain("PK_NO_VALUE", Case.Sensitive);
    }

    // ───────────────────────────────────────────── steps 6 and 9 · op behaviour (M2-03)

    [Fact]
    public void No_conversions_and_no_cap_overrides_is_a_no_op()
    {
        UnimplementedStatOps.Instance.Convert([], StatFixtures.Zeroed(), AuthoredEffectValue.Instance).ShouldBeEmpty();

        var declared = StatFixtures.Caps();

        UnimplementedStatOps.Instance.OverrideCaps([], declared, AuthoredEffectValue.Instance).ShouldBeSameAs(
            declared, "with no overrides, step 9 uses 05 §1's table unchanged");
    }

    [Fact]
    public void A_STAT_CONVERT_is_refused_until_M2_03_rules_on_which_stat_is_the_source()
    {
        var turtle = StatFixtures.Effect("PK_TURTLE_I", EffectOp.STAT_CONVERT, StatId.DEF, 0.10);

        var thrown = Should.Throw<NotSupportedException>(
            () => StatAggregation.Aggregate(
                StatFixtures.Zeroed(), [turtle], StatCaps.None, StatAggregationSeams.Strict));

        thrown.Message.ShouldContain("18 §8 step 6", Case.Sensitive);
        thrown.Message.ShouldContain("PK_TURTLE_I", Case.Sensitive);
        thrown.Message.ShouldContain("M2-03", Case.Sensitive);
    }

    [Fact]
    public void A_STAT_CAP_OVERRIDE_is_refused_until_M2_03_rules_on_HEAL_CEILING_and_Perfect_Strike()
    {
        var avatarOfWar = StatFixtures.Effect("TAL_AVATAR_OF_WAR", EffectOp.STAT_CAP_OVERRIDE, StatId.MAX_HP, 0.80)
            with
        { CapKind = StatCapKind.HEAL_CEILING };

        var thrown = Should.Throw<NotSupportedException>(
            () => StatAggregation.Aggregate(
                StatFixtures.Zeroed(), [avatarOfWar], StatCaps.None, StatAggregationSeams.Strict));

        thrown.Message.ShouldContain("18 §8 step 9", Case.Sensitive);
        thrown.Message.ShouldContain("HEAL_CEILING", Case.Sensitive);
        thrown.Message.ShouldContain("Perfect Strike", Case.Sensitive);
        thrown.Message.ShouldContain("M2-03", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 Step 6's <em>"reads post-step-5 values"</em> is enforced by the pipeline, not trusted to
    /// the seam: the block handed over is frozen, so a second conversion cannot see the first one's
    /// output however the implementation is written.
    /// </summary>
    [Fact]
    public void The_conversion_seam_reads_the_frozen_post_step_5_block()
    {
        var conversions = new RecordingConversions(
            [new StatDelta(StatId.ATK, 5.0), new StatDelta(StatId.ATK, 7.0)]);

        var result = StatAggregation.Aggregate(
            StatFixtures.Block((StatId.ATK, 10.0), (StatId.DEF, 100.0)),
            [
                StatFixtures.Effect("A_PCT", EffectOp.STAT_ADD_PCT, StatId.ATK, 1.0),
                StatFixtures.Effect("PK_JUGGERNAUT_I", EffectOp.STAT_CONVERT, StatId.DEF, 0.05),
                StatFixtures.Effect("PK_TURTLE_I", EffectOp.STAT_CONVERT, StatId.DEF, 0.10),
            ],
            StatCaps.None,
            StatAggregationSeams.Strict with { Ops = conversions });

        conversions.Seen.ShouldNotBeNull();
        conversions.Seen![StatId.ATK].ShouldBe(20.0, "step 5 doubled ATK before step 6 was asked");
        conversions.Seen[StatId.DEF].ShouldBe(100.0);

        // 🔒 BOTH conversions arrive in one call, against ONE block, in effect-id order — so the
        // second cannot see the first's output however the seam is implemented. The pipeline hands
        // over a frozen ActorStats rather than a mutable accumulator; that is the enforcement.
        conversions.Calls.ShouldBe(1);
        conversions.Ids.ShouldBe(["PK_JUGGERNAUT_I", "PK_TURTLE_I"]);

        result.Final[StatId.ATK].ShouldBe(32.0, "20 + 5 + 7 — both deltas applied by the pipeline");
    }

    /// <summary>The cap-override seam really replaces step 9's table.</summary>
    [Fact]
    public void A_cap_override_double_replaces_the_step_9_table()
    {
        var result = StatAggregation.Aggregate(
            StatFixtures.Block((StatId.CRIT, 0.90)),
            [StatFixtures.Effect("TAL_PERFECT_STRIKE", EffectOp.STAT_CAP_OVERRIDE, StatId.CRIT, 0.90)],
            StatFixtures.Caps(),
            StatAggregationSeams.Strict with { Ops = new RaiseCritCap() });

        result.Final[StatId.CRIT].ShouldBe(0.90, "the seam raised 05 §1's 0.75 ceiling");
    }

    private sealed class FixedGate(bool answer) : IEffectConditionGate
    {
        public bool IsActive(EffectDefinition effect) => answer;
    }

    private sealed class RecordingConversions(IReadOnlyList<StatDelta> deltas) : IStatOpBehaviour
    {
        internal ActorStats? Seen { get; private set; }

        internal IReadOnlyList<string> Ids { get; private set; } = [];

        internal int Calls { get; private set; }

        public IReadOnlyList<StatDelta> Convert(
            IReadOnlyList<EffectDefinition> conversions, ActorStats postAdditive, IEffectValueReader values)
        {
            Calls++;
            Seen = postAdditive;
            Ids = conversions.Select(e => e.Id).ToArray();

            return conversions.Count == 0 ? [] : deltas;
        }

        public StatCaps OverrideCaps(
            IReadOnlyList<EffectDefinition> overrides, StatCaps declared, IEffectValueReader values) => declared;
    }

    private sealed class RaiseCritCap : IStatOpBehaviour
    {
        public IReadOnlyList<StatDelta> Convert(
            IReadOnlyList<EffectDefinition> conversions, ActorStats postAdditive, IEffectValueReader values) => [];

        public StatCaps OverrideCaps(
            IReadOnlyList<EffectDefinition> overrides, StatCaps declared, IEffectValueReader values) =>
            overrides.Count == 0 ? declared : declared.With(StatId.CRIT, 0.90);
    }
}
