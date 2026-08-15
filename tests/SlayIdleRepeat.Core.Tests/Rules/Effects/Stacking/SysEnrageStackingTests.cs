using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Duration;
using SlayIdleRepeat.Core.Rules.Effects.Stacking;
using SlayIdleRepeat.Core.Rules.Stats;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Stacking;

/// <summary>
/// <c>SYS_ENRAGE</c> is <c>PERIODIC {interval: 1.0, startDelay: 70.0}</c> → <c>STAT_MULT ATK ×1.08</c>,
/// multiplicative stacking, uncapped, <c>BATTLE</c> scope. Three seconds against a 100 ATK boss is
/// <c>100 × 1.08³ = 125.9712</c>.
/// </summary>
/// <remarks>
/// <c>StatAggregationTests</c> pins the same number for three separately authored effects. What is
/// pinned here is the stacking path: one effect id with three <c>MULTIPLICATIVE</c> stacks has to reach
/// the same answer, because <c>SYS_ENRAGE</c> is authored as one effect that stacks.
/// </remarks>
public sealed class SysEnrageStackingTests
{
    /// <summary>The pin: three seconds of enrage is 125.9712, and it is not 899.8912.</summary>
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
    /// The stack combiner must NOT round to 4 dp, and this is the case that shows why. The rounded
    /// accumulation points are after each damage calculation, each heal, and each stat aggregation
    /// step; the product of a multiplicative stack set is none of the three. Rounding the combined
    /// multiplier as well is not belt-and-braces — it is a second, earlier accumulation point that
    /// changes the answer: <c>1.08³ = 1.259712</c> exactly, but rounded to 4 dp it is <c>1.2597</c>,
    /// and <c>100 × 1.2597 = 125.97</c> — the enrage quietly weakened in the fourth decimal place,
    /// every second, for the rest of the fight.
    /// </summary>
    [Fact]
    public void The_combined_multiplier_is_not_rounded_before_it_reaches_step_7()
    {
        var combined = EnrageStacks(seconds: 3).CombinedValue;

        combined.ShouldBe(1.259712, 1e-15, "1.08^3");

        // The property this test is named for, stated directly rather than demonstrated: the
        // combiner's answer still carries digits past the fourth decimal place. A combiner that
        // rounded would return 1.2597, which IS its own 4-dp rounding.
        combined.ShouldNotBe(
            Math.Round(combined, 4),
            "a combiner that rounded at 4 dp would hand on 1.2597, and 100 x 1.2597 is 125.97 — " +
            "0.0012 ATK short of 05 §3.1's number, compounding once a second");

        AtkAfterEnrage(seconds: 3).ShouldNotBe(
            125.97, "18 §8 step 7 is the accumulation point, and it is the only one on this path");
    }

    /// <summary><c>SYS_ENRAGE</c> is uncapped, so it keeps compounding for the rest of the fight rather than plateauing after one stack.</summary>
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
    /// <c>SYS_ENRAGE</c> has <c>BATTLE</c> scope, and therefore does not follow the hero out of the
    /// fight. The claim is about this effect's duration, so it is read off the effect rather than
    /// asserted of the <c>BATTLE</c> scope in the abstract — that second reading is
    /// <c>DurationEvaluatorTests.A_battle_bounded_scope_does_not_outlive_the_battle</c>'s, and
    /// restating it here would pass unchanged if <c>SYS_ENRAGE</c> were re-authored as <c>RUN</c>.
    /// </summary>
    [Fact]
    public void SYS_ENRAGE_is_BATTLE_scoped()
    {
        var enrage = EnrageEffect(seconds: 3);

        enrage.Duration.ShouldNotBeNull("05 §3.1 authors the enrage with a scope");
        enrage.Duration.Scope.ShouldBe(
            DurationScope.BATTLE, "05 §3.1: 'multiplicative stacking, uncapped, BATTLE scope'");

        DurationEvaluator.Evaluate(
            new EffectApplication
            {
                EffectId = enrage.Id,
                Duration = enrage.Duration,
                AppliedAtSeconds = 70.0,
            },
            new DurationProbe { BattleTimeSeconds = 90.0, BattleEnded = true }).Reason.ShouldBe(
            DurationEndReason.BattleEnded,
            "the enrage ends with the fight it belongs to, rather than outliving it (A4's scopes do)");
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

    /// <summary>The built-in <c>SYS_ENRAGE</c>, with <paramref name="seconds"/> stacks on it.</summary>
    private static EffectDefinition EnrageEffect(int seconds) =>
        new()
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

    private static double AtkAfterEnrage(int seconds)
    {
        var enrage = EnrageEffect(seconds);

        var boss = ActorStats.From(
            StatIds.Combat.ToDictionary(stat => stat, stat => stat == StatId.ATK ? 100.0 : 0.0));

        return StatAggregation
            .Aggregate(boss, [enrage], StatCaps.None, StatAggregationSeams.Strict)
            .Final[StatId.ATK];
    }
}
