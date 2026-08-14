using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Conditions;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Conditions;

/// <summary>
/// 🔒 `18` §4's seven comparators — <c>eq · neq · lt · lte · gt · gte · between</c> — and its three
/// combinators — <c>all · any · not</c>.
/// </summary>
public sealed class ConditionComparatorAndCombinatorTests
{
    /// <summary>
    /// A battle whose hero sits at exactly 50% HP, so <c>SELF_HP_PCT</c> reads <c>0.5</c> and every
    /// comparator can be exercised against a bound above it, at it, and below it.
    /// </summary>
    private static EffectEvaluationContext HalfHealthHero()
    {
        var hero = EffectTestBattle.Hero(currentHp: 50, maxHp: 100);

        return EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1));
    }

    private static bool Holds(ConditionComparator comparator, double bound) =>
        ConditionEvaluator.IsSatisfied(
            EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.SELF_HP_PCT,
                Comparator = comparator,
                Value = bound,
            }),
            HalfHealthHero());

    // ------------------------------------------------------------------ comparators

    /// <summary>
    /// Each comparator at the three points that distinguish it — below the reading, exactly on it,
    /// and above it. The boundary row is the one that separates <c>lt</c> from <c>lte</c>.
    /// </summary>
    [Theory]
    [InlineData(ConditionComparator.EQ, 0.4, false)]
    [InlineData(ConditionComparator.EQ, 0.5, true)]
    [InlineData(ConditionComparator.EQ, 0.6, false)]
    [InlineData(ConditionComparator.NEQ, 0.4, true)]
    [InlineData(ConditionComparator.NEQ, 0.5, false)]
    [InlineData(ConditionComparator.NEQ, 0.6, true)]
    [InlineData(ConditionComparator.LT, 0.4, false)]
    [InlineData(ConditionComparator.LT, 0.5, false)]
    [InlineData(ConditionComparator.LT, 0.6, true)]
    [InlineData(ConditionComparator.LTE, 0.4, false)]
    [InlineData(ConditionComparator.LTE, 0.5, true)]
    [InlineData(ConditionComparator.LTE, 0.6, true)]
    [InlineData(ConditionComparator.GT, 0.4, true)]
    [InlineData(ConditionComparator.GT, 0.5, false)]
    [InlineData(ConditionComparator.GT, 0.6, false)]
    [InlineData(ConditionComparator.GTE, 0.4, true)]
    [InlineData(ConditionComparator.GTE, 0.5, true)]
    [InlineData(ConditionComparator.GTE, 0.6, false)]
    public void Each_comparator_compares_the_reading_against_the_authored_value(
        ConditionComparator comparator,
        double bound,
        bool expected)
    {
        Holds(comparator, bound).ShouldBe(expected);
    }

    /// <summary>
    /// <c>between</c> is inclusive at both ends, per <see cref="ConditionTerm.RangeLow"/>'s and
    /// <see cref="ConditionTerm.RangeHigh"/>'s declarations.
    /// </summary>
    [Theory]
    [InlineData(0.5, 0.5, true)]
    [InlineData(0.4, 0.6, true)]
    [InlineData(0.5, 0.9, true)]
    [InlineData(0.1, 0.5, true)]
    [InlineData(0.6, 0.9, false)]
    [InlineData(0.1, 0.4, false)]
    public void BETWEEN_is_inclusive_at_both_bounds(double low, double high, bool expected)
    {
        ConditionEvaluator.IsSatisfied(
            EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.SELF_HP_PCT,
                Comparator = ConditionComparator.BETWEEN,
                RangeLow = low,
                RangeHigh = high,
            }),
            HalfHealthHero())
            .ShouldBe(expected);
    }

    /// <summary>
    /// `18` §7.10 writes <c>{"fn":"ATTACKER_IS_ELITE","op":"eq","value":true}</c> — a boolean
    /// comparison, carried by <see cref="ConditionTerm.Flag"/>.
    /// </summary>
    [Theory]
    [InlineData(ConditionComparator.EQ, true, true)]
    [InlineData(ConditionComparator.EQ, false, false)]
    [InlineData(ConditionComparator.NEQ, true, false)]
    [InlineData(ConditionComparator.NEQ, false, true)]
    public void A_boolean_function_compares_against_the_flag(
        ConditionComparator comparator,
        bool flag,
        bool expected)
    {
        var hero = EffectTestBattle.Hero();
        var elite = EffectTestBattle.Enemy("ELITE_A", 1) with { IsElite = true };

        ConditionEvaluator.IsSatisfied(
            EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.ATTACKER_IS_ELITE,
                Comparator = comparator,
                Flag = flag,
            }),
            EffectTestBattle.Context(hero, hero, elite) with { Attacker = elite })
            .ShouldBe(expected);
    }

    /// <summary>
    /// A boolean term may also be written numerically — <c>{"op":"eq","value":1}</c> — because
    /// <see cref="ConditionEvaluator.Read"/> reads every function as a number. Both spellings answer
    /// alike; neither is a special case.
    /// </summary>
    [Fact]
    public void A_boolean_function_also_compares_against_a_numeric_one_or_zero()
    {
        var hero = EffectTestBattle.Hero();
        var elite = EffectTestBattle.Enemy("ELITE_A", 1) with { IsElite = true };
        var battle = EffectTestBattle.Context(hero, hero, elite) with { Attacker = elite };

        ConditionEvaluator.IsSatisfied(
            EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.ATTACKER_IS_ELITE,
                Comparator = ConditionComparator.EQ,
                Value = 1,
            }),
            battle)
            .ShouldBeTrue();

        ConditionEvaluator.IsSatisfied(
            EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.ATTACKER_IS_ELITE,
                Comparator = ConditionComparator.EQ,
                Value = 0,
            }),
            battle)
            .ShouldBeFalse();
    }

    // ------------------------------------------------------------------ combinators

    /// <summary>
    /// `18` §4's worked combinator, verbatim:
    /// <c>{"all":[{"fn":"SELF_HP_PCT","op":"gte","value":1.0},{"fn":"ENEMY_COUNT","op":"eq","value":1}]}</c>.
    /// </summary>
    [Theory]
    [InlineData(100, 1, true)]
    [InlineData(100, 2, false)]
    [InlineData(99, 1, false)]
    [InlineData(99, 2, false)]
    public void ALL_holds_only_when_every_operand_holds(double heroHp, int enemies, bool expected)
    {
        var condition = EffectCondition.All(
            EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.SELF_HP_PCT,
                Comparator = ConditionComparator.GTE,
                Value = 1.0,
            }),
            EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.ENEMY_COUNT,
                Comparator = ConditionComparator.EQ,
                Value = 1,
            }));

        ConditionEvaluator.IsSatisfied(condition, Battle(heroHp, enemies)).ShouldBe(expected);
    }

    /// <summary><c>any</c> holds as soon as one operand does.</summary>
    [Theory]
    [InlineData(100, 1, true)]
    [InlineData(100, 2, true)]
    [InlineData(99, 1, true)]
    [InlineData(99, 2, false)]
    public void ANY_holds_when_at_least_one_operand_holds(double heroHp, int enemies, bool expected)
    {
        var condition = EffectCondition.Any(
            EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.SELF_HP_PCT,
                Comparator = ConditionComparator.GTE,
                Value = 1.0,
            }),
            EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.ENEMY_COUNT,
                Comparator = ConditionComparator.EQ,
                Value = 1,
            }));

        ConditionEvaluator.IsSatisfied(condition, Battle(heroHp, enemies)).ShouldBe(expected);
    }

    [Theory]
    [InlineData(100, false)]
    [InlineData(99, true)]
    public void NOT_inverts_its_operand(double heroHp, bool expected)
    {
        var condition = EffectCondition.Not(
            EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.SELF_HP_PCT,
                Comparator = ConditionComparator.GTE,
                Value = 1.0,
            }));

        ConditionEvaluator.IsSatisfied(condition, Battle(heroHp, 1)).ShouldBe(expected);
    }

    /// <summary>
    /// Combinators nest. `18` §9.3's skip idiom — <em>"gear affixes like <c>+X% Gold Gain</c> still
    /// need to be neutralised in duels — they are simply skipped"</em> — is
    /// <c>{"all":[{"not":{"fn":"IS_PVP","op":"eq","value":true}}, …]}</c>, and it must survive a
    /// combinator inside a combinator.
    /// </summary>
    [Fact]
    public void Combinators_nest_to_arbitrary_depth()
    {
        var notInADuel = EffectCondition.Not(
            EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.IS_PVP,
                Comparator = ConditionComparator.EQ,
                Flag = true,
            }));

        var holdingGold = EffectCondition.Of(new ConditionTerm
        {
            Fn = ConditionFunction.GOLD_HELD,
            Comparator = ConditionComparator.GTE,
            Value = 100,
        });

        // Three levels: All( Not( … ), Any( All( … ) ) ). Two would not distinguish an evaluator
        // that recursed from one that handled a single layer of nesting and stopped.
        var goldAffix = EffectCondition.All(notInADuel, EffectCondition.Any(EffectCondition.All(holdingGold)));

        var hero = EffectTestBattle.Hero();
        var inARun = EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1)) with
        {
            Run = EffectTestBattle.Run() with { GoldHeld = 500 },
        };

        ConditionEvaluator.IsSatisfied(goldAffix, inARun).ShouldBeTrue();

        var brokeButInARun = inARun with { Run = EffectTestBattle.Run() with { GoldHeld = 99 } };
        ConditionEvaluator.IsSatisfied(goldAffix, brokeButInARun).ShouldBeFalse();

        ConditionEvaluator.IsSatisfied(goldAffix, inARun with { IsPvp = true })
            .ShouldBeFalse("the affix is skipped in a duel before its gold clause is ever reached");
    }

    /// <summary>
    /// 🔒 <c>all</c> and <c>any</c> short-circuit — the duel skip above is only safe if a failed
    /// <c>not IS_PVP</c> stops the tree before an operand that would throw against a run-less duel
    /// context.
    /// </summary>
    /// <remarks>
    /// This is not an optimisation. `18` §9.3's whole mechanism is that a non-combat clause is
    /// <em>skipped</em> in a duel, and a duel context carries no <c>IRunStateView</c> (`05` §3.3), so
    /// an evaluator that read every operand before combining them would throw on exactly the effects
    /// the ruling exists to neutralise.
    /// </remarks>
    [Fact]
    public void ALL_short_circuits_so_the_IS_PVP_skip_never_reaches_the_run_state_it_guards()
    {
        var goldAffix = EffectCondition.All(
            EffectCondition.Not(
                EffectCondition.Of(new ConditionTerm
                {
                    Fn = ConditionFunction.IS_PVP,
                    Comparator = ConditionComparator.EQ,
                    Flag = true,
                })),
            EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.GOLD_HELD,
                Comparator = ConditionComparator.GTE,
                Value = 100,
            }));

        var duel = EffectTestBattle.Duel();
        duel.Run.ShouldBeNull("05 §3.3: a duel has no run");

        ConditionEvaluator.IsSatisfied(goldAffix, duel).ShouldBeFalse();
    }

    /// <summary>
    /// 🔒 And <c>any</c> short-circuits too — the same skip idiom is equally reachable through it,
    /// and a rule that held for one combinator and not the other would be half a rule.
    /// </summary>
    [Fact]
    public void ANY_short_circuits_so_a_satisfied_first_operand_never_reaches_the_rest()
    {
        var inADuelOrHoldingGold = EffectCondition.Any(
            EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.IS_PVP,
                Comparator = ConditionComparator.EQ,
                Flag = true,
            }),
            EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.GOLD_HELD,
                Comparator = ConditionComparator.GTE,
                Value = 100,
            }));

        var duel = EffectTestBattle.Duel();
        duel.Run.ShouldBeNull();

        // The first operand holds, so the second — which would throw against a run-less duel — is
        // never read. An evaluator that read every operand before combining them fails here.
        ConditionEvaluator.IsSatisfied(inADuelOrHoldingGold, duel).ShouldBeTrue();
    }

    /// <summary>An absent condition is an ungated effect — `18` §1's <c>"condition": null</c>.</summary>
    [Fact]
    public void A_null_condition_holds()
    {
        ConditionEvaluator.IsSatisfied(null, Battle(100, 1)).ShouldBeTrue();
    }

    private static EffectEvaluationContext Battle(double heroHp, int enemies)
    {
        var hero = EffectTestBattle.Hero(currentHp: heroHp, maxHp: 100);

        var roster = new List<IEffectActorView> { hero };
        for (var i = 0; i < enemies; i++)
        {
            roster.Add(EffectTestBattle.Enemy(
                "GRUNT_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                i + 1));
        }

        return EffectTestBattle.Context(hero, roster.ToArray());
    }
}
