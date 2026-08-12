using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content.Effects;

/// <summary>`18` §4's condition trees — comparisons and the three combinators.</summary>
public sealed class EffectConditionTests
{
    /// <summary>`18` §4's worked condition, verbatim.</summary>
    [Fact]
    public void The_worked_all_condition_of_section_4_round_trips()
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

        condition.Kind.ShouldBe(ConditionKind.ALL);
        condition.Operands.Count.ShouldBe(2);
        condition.Operands[0].Term!.Fn.ShouldBe(ConditionFunction.SELF_HP_PCT);
        condition.Operands[1].Term!.Comparator.ShouldBe(ConditionComparator.EQ);
    }

    /// <summary>`18` §7.10's <c>PK_STALWART</c> — an <c>any</c> over the two attacker predicates.</summary>
    [Fact]
    public void PK_STALWART_gates_a_damage_taken_multiplier_on_an_any_over_attacker_predicates()
    {
        var stalwart = new EffectDefinition
        {
            Id = "PK_STALWART_T1",
            Op = EffectOp.DAMAGE_TAKEN_MULT,
            Value = 0.80,
            Trigger = new EffectTrigger { Kind = TriggerKind.ALWAYS },
            Target = EffectTarget.SELF,
            Condition = EffectCondition.Any(
                EffectCondition.Of(new ConditionTerm
                {
                    Fn = ConditionFunction.ATTACKER_IS_ELITE,
                    Comparator = ConditionComparator.EQ,
                    Flag = true,
                }),
                EffectCondition.Of(new ConditionTerm
                {
                    Fn = ConditionFunction.ATTACKER_IS_BOSS,
                    Comparator = ConditionComparator.EQ,
                    Flag = true,
                })),
        };

        stalwart.Condition!.Kind.ShouldBe(ConditionKind.ANY);
        stalwart.Condition.Operands.Select(o => o.Term!.Fn).ShouldBe(
        [
            ConditionFunction.ATTACKER_IS_ELITE,
            ConditionFunction.ATTACKER_IS_BOSS,
        ]);
        stalwart.Condition.Operands.ShouldAllBe(o => o.Term!.Flag == true);
        stalwart.Condition.Operands.Count.ShouldBe(2, "ShouldAllBe passes on an empty collection");
    }

    [Fact]
    public void Not_carries_exactly_one_operand()
    {
        var condition = EffectCondition.Not(
            EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.IS_PVP,
                Comparator = ConditionComparator.EQ,
                Flag = true,
            }));

        condition.Kind.ShouldBe(ConditionKind.NOT);
        condition.Operands.Count.ShouldBe(1);
    }

    /// <summary>
    /// 🔒 An empty <c>all</c> is vacuously true and an empty <c>any</c> vacuously false, so a
    /// combinator that lost its operands would gate nothing while still reading as a gate.
    /// </summary>
    [Theory]
    [InlineData("all")]
    [InlineData("any")]
    public void A_combinator_with_no_operands_is_rejected(string combinator)
    {
        var thrown = Should.Throw<ArgumentException>(
            () => combinator == "all" ? EffectCondition.All() : EffectCondition.Any());

        thrown.Message.ShouldContain("gates nothing", Case.Sensitive);
    }

    [Fact]
    public void A_null_term_or_operand_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() => EffectCondition.Of(null!));
        Should.Throw<ArgumentNullException>(() => EffectCondition.Not(null!));
        Should.Throw<ArgumentNullException>(() => EffectCondition.All(null!));
    }

    /// <summary>The `18` §4 comparator vocabulary is complete and lower-cases to the JSON tokens.</summary>
    [Fact]
    public void The_comparators_lower_case_to_the_tokens_18_writes()
    {
        Enum.GetNames<ConditionComparator>()
            .Select(n => n.ToLowerInvariant())
            .OrderBy(n => n, StringComparer.Ordinal)
            .ShouldBe(["between", "eq", "gt", "gte", "lt", "lte", "neq"]);
    }

    /// <summary>Combinators nest — the tree has no depth rule in `18`, so none is imposed here.</summary>
    [Fact]
    public void Combinators_nest()
    {
        var nested = EffectCondition.All(
            EffectCondition.Not(
                EffectCondition.Of(new ConditionTerm
                {
                    Fn = ConditionFunction.IS_PVP,
                    Comparator = ConditionComparator.EQ,
                    Flag = true,
                })),
            EffectCondition.Any(
                EffectCondition.Of(new ConditionTerm
                {
                    Fn = ConditionFunction.TARGET_HP_PCT,
                    Comparator = ConditionComparator.LT,
                    Value = 0.30,
                })));

        nested.Operands[0].Kind.ShouldBe(ConditionKind.NOT);
        nested.Operands[1].Kind.ShouldBe(ConditionKind.ANY);
    }
}
