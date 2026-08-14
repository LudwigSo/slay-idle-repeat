using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Stats;

/// <summary>
/// 🔒 The <b>residue</b> of `18` §8 — the handful of claims that no public entry point can express
/// and no <see cref="SimulationResult"/> can report.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Read <see cref="StatAggregationTests"/> first.</b> That suite is where `18` §8 is actually
/// pinned: every case there runs a real fight through <see cref="CombatSimulator.SimulateDuel"/> and
/// reads the aggregated stat back out of the combat log. This file exists because four things are
/// left over, and each one is here for a stated reason rather than for convenience:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>The argument contract.</b> <c>Aggregate</c> refuses a null block, list, cap set or seam
///     set. A fight cannot pass any of those — <see cref="CombatSimulator"/> builds all four itself
///     — so the guard has no public caller to fail for.
///   </item>
///   <item>
///     <b><c>SkippedNonCombatStatEffects</c>.</b> The list of effects the pipeline declined to apply
///     is a field of the internal <c>AggregatedStats</c>, and no <c>CombatEvent</c> carries it.
///     <see cref="StatAggregationTests.A_stat_op_on_a_non_combat_stat_leaves_the_combat_block_alone"/>
///     pins the half a fight can see; this pins the report itself, which is the half that stops a
///     "+X% Gold Gain" affix from vanishing silently.
///   </item>
///   <item>
///     <b>The step-6 conversion rounding.</b> It needs a seam that returns a delta with a fifth
///     decimal place, and <c>StatAggregationSeams</c> is internal by design — `30` §11.2 exports the
///     simulator, not the DSL's seams.
///   </item>
///   <item>
///     <b>"Every stat is rounded".</b> A fight observes one stat at a time; the claim is about all
///     fourteen at once.
///   </item>
/// </list>
/// <para>
/// ⚠️ <b>Nothing in this file re-asserts arithmetic <see cref="StatAggregationTests"/> already
/// covers.</b> If a case here starts to look like it could be read out of a log, it belongs there.
/// </para>
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
    /// Discriminating, unlike a step-6 case with nothing after it: a delta of <c>0.00005</c> onto a
    /// base of 1.0 rounds to <c>1.0001</c> at step 6 and doubles to <c>2.0002</c>. Carried unrounded
    /// into step 7 it is <c>2.0001</c>. The seam supplies the deltas, so this is the one step whose
    /// input is not already 4-dp by construction — and the reason it cannot be written as a fight:
    /// an authored <c>STAT_CONVERT</c> goes through the real conversion, which has no way to emit a
    /// fifth decimal place on demand.
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
    /// 🔒 <b>Every</b> stat in the result is 4-dp, not merely the one a given fight happens to read.
    /// </summary>
    [Fact]
    public void Every_stat_in_the_result_is_rounded_to_four_places()
    {
        var result = Uncapped(
            StatFixtures.Block((StatId.ATK, 3.0)),
            StatFixtures.Effect("A_MULT", EffectOp.STAT_MULT, StatId.ATK, 1.000_000_1));

        result.Final.Values.Select(v => v.Value).ShouldAllBe(v => StatRounding.IsRounded(v));
        result.Final.Values.Count().ShouldBe(14, "ShouldAllBe passes on an empty collection");
    }

    /// <summary>
    /// 🔒 <c>ALL_COMBAT</c> reaches every one of the fourteen and nothing else — the whole block at
    /// once, which is the part a single fight's single stat reading cannot show.
    /// </summary>
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
    /// 🔒 A stat op on one of `18` §2.1's 12 non-combat stats is <b>reported</b> rather than dropped.
    /// </summary>
    /// <remarks>
    /// Silently ignoring it is the failure mode: a resolver that hands the whole build to the
    /// aggregator and never asks what was left behind would lose every economy affix in the game
    /// with nothing going red. No <c>CombatEvent</c> carries this list, which is why the claim is
    /// here rather than in <see cref="StatAggregationTests"/>.
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

    /// <summary>🔒 A <c>null</c> <em>in</em> the effect list is refused by name rather than skipped.</summary>
    /// <remarks>
    /// ⚠️ <b>This one is here because the public entry points do not yet reach it, and that is worth
    /// recording rather than working around.</b> Handing
    /// <c>CombatSimulator.SimulateDuel</c> an <c>attackerEffects</c> list with a <c>null</c> element
    /// throws a bare <see cref="NullReferenceException"/> from the wrapping in <c>DuelFight</c>,
    /// <em>before</em> this guard runs — so the named refusal below is real, but a caller outside
    /// <c>Core</c> never sees it. Steering S2 makes the message the deliverable, so the entry points
    /// arguably owe the same <see cref="ArgumentNullException"/> this method already produces. Until
    /// that is decided, this test pins the guard where the guard actually is; asserting the
    /// <see cref="NullReferenceException"/> at the public seam instead would encode the gap as
    /// intended behaviour.
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
