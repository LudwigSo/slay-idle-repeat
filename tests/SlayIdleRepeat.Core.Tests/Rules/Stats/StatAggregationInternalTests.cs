using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Stats;

/// <summary>
/// 🔒 The <b>residue</b> of `18` §8 — what no public entry point can express and no
/// <see cref="SimulationResult"/> can report. <see cref="StatAggregationTests"/> is where `18` §8 is
/// actually pinned.
/// </summary>
/// <remarks>
/// Four things are left over: the argument contract (a fight builds all four arguments itself, so
/// the guard has no public caller to fail for), <c>SkippedNonCombatStatEffects</c> (no
/// <c>CombatEvent</c> carries it), the step-6 conversion rounding (it needs an internal seam), and
/// "every stat is rounded" (a fight observes one stat at a time).
/// </remarks>
public sealed class StatAggregationInternalTests
{
    private static AggregatedStats Uncapped(ActorStats baseStats, params EffectDefinition[] effects) =>
        StatAggregation.Aggregate(baseStats, effects, StatCaps.None, StatAggregationSeams.Strict);

    /// <summary>
    /// 🔒 Step 6 rounds <b>before</b> step 7 multiplies, so a conversion delta with a fifth decimal
    /// place cannot be magnified by a later multiplier.
    /// </summary>
    /// <remarks>
    /// The seam supplies the deltas, so this is the one step whose input is not already 4-dp — and
    /// the reason it cannot be a fight: a real <c>STAT_CONVERT</c> cannot emit a fifth decimal place
    /// on demand.
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

    /// <summary>🔒 <b>Every</b> stat is 4-dp, not merely the one a given fight reads.</summary>
    [Fact]
    public void Every_stat_in_the_result_is_rounded_to_four_places()
    {
        var result = Uncapped(
            StatFixtures.Block((StatId.ATK, 3.0)),
            StatFixtures.Effect("A_MULT", EffectOp.STAT_MULT, StatId.ATK, 1.000_000_1));

        result.Final.Values.Select(v => v.Value).ShouldAllBe(v => StatRounding.IsRounded(v));
        result.Final.Values.Count().ShouldBe(14, "ShouldAllBe passes on an empty collection");
    }

    /// <summary>🔒 <c>ALL_COMBAT</c> reaches all fourteen and nothing else — the whole block at once.</summary>
    [Fact]
    public void ALL_COMBAT_reaches_every_one_of_the_fourteen_and_nothing_else()
    {
        var baseStats = ActorStats.From(StatIds.Combat.ToDictionary(stat => stat, _ => 3.0));

        var result = Uncapped(baseStats, StatFixtures.AllCombatEffect("A_MULT", EffectOp.STAT_MULT, 2.0));

        result.Final.Values.Count().ShouldBe(14);
        result.Final.Values.ShouldAllBe(v => v.Value == 6.0);
        result.SkippedNonCombatStatEffects.ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 A non-combat stat op is <b>reported</b> rather than dropped: silently ignoring it would
    /// lose every economy affix in the game with nothing going red.
    /// </summary>
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

    /// <summary>🔒 A <c>null</c> <em>in</em> the effect list is refused by name rather than skipped.</summary>
    /// <remarks>
    /// ⚠️ <b>Known gap.</b> <c>SimulateDuel</c> with a <c>null</c> element throws a bare
    /// <see cref="NullReferenceException"/> from <c>DuelFight</c>'s wrapping, before this guard runs,
    /// so a caller outside <c>Core</c> never sees the named refusal. Pinned here rather than at the
    /// public seam, which would encode the gap as intended behaviour.
    /// </remarks>
    [Fact]
    public void A_null_effect_in_the_list_is_refused() =>
        Should.Throw<ArgumentNullException>(() => StatAggregation.Aggregate(
            StatFixtures.Zeroed(), [null!], StatCaps.None, StatAggregationSeams.Strict));

    /// <summary>🔒 The four arguments are all required — a guard no fight can trip.</summary>
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

        public IReadOnlyList<StatDelta> RedirectCappedExcess(
            IReadOnlyList<EffectDefinition> overrides, ActorStats preCap, StatCaps effective,
            IEffectValueReader values) => [];

        public double? HealCeilingFraction(
            IReadOnlyList<EffectDefinition> overrides, IEffectValueReader values) => null;
    }
}
