using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Duration;
using SlayIdleRepeat.Core.Rules.Effects.Stacking;
using SlayIdleRepeat.Core.Rules.Stats;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Stacking;

/// <summary>
/// 🔒 <b>The acceptance test for M2-06.</b> `05` §3.1 fixes <c>SYS_ENRAGE</c> as
/// <c>PERIODIC {interval: 1.0, startDelay: 70.0}</c> → <c>STAT_MULT ATK ×1.08</c>,
/// <b>multiplicative stacking, uncapped, <c>BATTLE</c> scope</b>. Under R1 —
/// <c>STAT_MULT</c>'s value <em>is</em> the multiplier — three seconds of enrage against a 100 ATK
/// boss is <c>100 × 1.08³ = 125.9712</c>.
/// </summary>
/// <remarks>
/// M2-07 already pins the same number for three <em>separately authored</em> <c>STAT_MULT</c>
/// effects. What is pinned here is the stacking path: <b>one</b> effect id with three
/// <c>MULTIPLICATIVE</c> stacks has to reach the same answer, because `05` §3.1 authors
/// <c>SYS_ENRAGE</c> as one effect that stacks — not as N effects.
/// </remarks>
public sealed class SysEnrageStackingTests
{
    /// <summary>
    /// The pin: three seconds of enrage is 125.9712, and it is not 899.8912.
    /// </summary>
    [Fact]
    public void Three_seconds_of_SYS_ENRAGE_is_125_9712_through_the_stacking_path()
    {
        var enraged = AtkAfterEnrage(seconds: 3);

        enraged.ShouldBe(
            125.9712,
            "05 §3.1 x 18 §8 step 7 under R1: 100 x 1.08^3 = 125.9712");

        enraged.ShouldNotBe(
            899.8912,
            "899.8912 is 100 x 2.08^3 — 05 §1.1's literal Pi(1 + Multiplicative) reading, which R1 " +
            "rules an erratum and which would have the boss one-shot the hero three seconds into enraging");
    }

    /// <summary>
    /// 🔒 <b>The stack combiner must NOT round to 4 dp, and this is the case that shows why.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// `05` §1.1's accumulation points are <em>"after each damage calculation, each heal, and each
    /// stat aggregation step"</em>. The product of a multiplicative stack set is none of the three:
    /// `18` §8 <b>step 7</b> is the accumulation point, and <c>StatAggregation</c> already rounds
    /// there. Rounding the combined multiplier as well is not belt-and-braces — it is a second,
    /// earlier accumulation point that `05` §1.1 does not authorise, and it changes the answer.
    /// </para>
    /// <para>
    /// <c>1.08³ = 1.259712</c> exactly; rounded to 4 dp it is <c>1.2597</c>, and
    /// <c>100 × 1.2597 = 125.97</c> — the enrage quietly weakened in the fourth decimal place, every
    /// second, for the rest of the fight.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_combined_multiplier_is_not_rounded_before_it_reaches_step_7()
    {
        var combined = EnrageStacks(seconds: 3).CombinedValue;

        combined.ShouldBe(1.259712, 1e-15, "1.08^3");

        Math.Round(combined, 4).ShouldBe(
            1.2597, "the value a combiner that rounded at 4 dp would hand on");

        (100.0 * Math.Round(combined, 4)).ShouldBe(
            125.97,
            1e-12,
            "125.97 is what a double-rounded enrage produces — 0.0012 ATK short of 05 §3.1's number, " +
            "compounding once a second");

        AtkAfterEnrage(seconds: 3).ShouldNotBe(
            125.97, "18 §8 step 7 is the accumulation point, and it is the only one on this path");
    }

    /// <summary>
    /// `05` §3.1's <c>SYS_ENRAGE</c> is <em>"uncapped"</em>, so the enrage keeps compounding for the
    /// rest of the fight rather than plateauing after one stack.
    /// </summary>
    [Fact]
    public void SYS_ENRAGE_keeps_compounding_because_it_is_uncapped()
    {
        AtkAfterEnrage(seconds: 1).ShouldBe(108.0);
        AtkAfterEnrage(seconds: 2).ShouldBe(116.64);
        AtkAfterEnrage(seconds: 3).ShouldBe(125.9712);

        AtkAfterEnrage(seconds: 2).ShouldNotBe(
            108.0, "a maxStacks of 1 would freeze the enrage after its first tick");
    }

    /// <summary>
    /// `05` §3.1 gives <c>SYS_ENRAGE</c> <c>BATTLE</c> scope, which is a battle-bounded scope: the
    /// enrage does not follow the hero out of the fight.
    /// </summary>
    [Fact]
    public void SYS_ENRAGE_is_BATTLE_scoped()
    {
        DurationScopes.OutlivesTheBattle(DurationScope.BATTLE).ShouldBeFalse();
    }

    // ───────────────────────────────────────────── fixtures

    private static EffectStackSet EnrageStacks(int seconds)
    {
        // 05 §3.1: PERIODIC {interval: 1.0, startDelay: 70.0} — one application per second, uncapped.
        var stacks = EffectStackSet.Empty(
            "SYS_ENRAGE",
            new EffectStacking { Mode = StackingMode.MULTIPLICATIVE, MaxStacks = null });

        for (var i = 0; i < seconds; i++)
        {
            stacks = stacks.Apply(1.08).Stacks;
        }

        return stacks;
    }

    private static double AtkAfterEnrage(int seconds)
    {
        var enrage = new EffectDefinition
        {
            Id = "SYS_ENRAGE",
            Op = EffectOp.STAT_MULT,
            Stat = StatSelector.Of(StatId.ATK),
            Value = EnrageStacks(seconds).CombinedValue,
            Trigger = new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 1.0, StartDelay = 70.0 },
            Target = EffectTarget.SELF,
            Duration = new EffectDuration { Scope = DurationScope.BATTLE },
            Stacking = new EffectStacking { Mode = StackingMode.MULTIPLICATIVE, MaxStacks = null },
        };

        var boss = ActorStats.From(
            StatIds.Combat.ToDictionary(stat => stat, stat => stat == StatId.ATK ? 100.0 : 0.0));

        return StatAggregation
            .Aggregate(boss, [enrage], StatCaps.None, StatAggregationSeams.Strict)
            .Final[StatId.ATK];
    }
}
