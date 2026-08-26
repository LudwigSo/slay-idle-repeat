using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Conditions;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Conditions;

/// <summary>
/// The classification behind the conditional standing-effect bucket: which contextual subjects — the
/// current target, the attacker — a condition tree reads, anywhere in its nesting.
/// </summary>
/// <remarks>
/// The classification is over the tree's <em>vocabulary</em>, not its truth value: a negated or
/// disjoined target read still makes the tree a target-reading one, because the bucket's rule is
/// subject-presence — the effect waits for a context that carries the subject, and only there is the
/// tree evaluated at all.
/// </remarks>
public sealed class ConditionSubjectsTests
{
    /// <summary>An ungated effect reads no subject.</summary>
    [Fact]
    public void A_null_condition_reads_nothing()
    {
        var subjects = ConditionSubjects.Of(null);

        subjects.ReadsTarget.ShouldBeFalse();
        subjects.ReadsAttacker.ShouldBeFalse();
    }

    /// <summary>Every ambient function leaves both flags off.</summary>
    [Theory]
    [InlineData(ConditionFunction.SELF_HP_PCT)]
    [InlineData(ConditionFunction.SELF_MISSING_HP_PCT)]
    [InlineData(ConditionFunction.ENEMY_COUNT)]
    [InlineData(ConditionFunction.BATTLE_TIME)]
    [InlineData(ConditionFunction.IS_PVP)]
    [InlineData(ConditionFunction.GOLD_HELD)]
    public void An_ambient_function_reads_neither_subject(ConditionFunction fn)
    {
        var subjects = ConditionSubjects.Of(Term(fn));

        subjects.ReadsTarget.ShouldBeFalse();
        subjects.ReadsAttacker.ShouldBeFalse();
    }

    /// <summary>The three target functions read the current target.</summary>
    [Theory]
    [InlineData(ConditionFunction.TARGET_HP_PCT)]
    [InlineData(ConditionFunction.TARGET_IS_ELITE)]
    [InlineData(ConditionFunction.TARGET_IS_BOSS)]
    public void A_target_function_reads_the_target(ConditionFunction fn)
    {
        var subjects = ConditionSubjects.Of(Term(fn));

        subjects.ReadsTarget.ShouldBeTrue();
        subjects.ReadsAttacker.ShouldBeFalse();
    }

    /// <summary>The three attacker functions read the attacker.</summary>
    [Theory]
    [InlineData(ConditionFunction.ATTACKER_IS_ELITE)]
    [InlineData(ConditionFunction.ATTACKER_IS_BOSS)]
    [InlineData(ConditionFunction.ATTACKER_IS_SUMMON)]
    public void An_attacker_function_reads_the_attacker(ConditionFunction fn)
    {
        var subjects = ConditionSubjects.Of(Term(fn));

        subjects.ReadsTarget.ShouldBeFalse();
        subjects.ReadsAttacker.ShouldBeTrue();
    }

    /// <summary>A read is found at any nesting depth, through every combinator.</summary>
    [Fact]
    public void A_target_read_is_found_through_nested_combinators()
    {
        var tree = EffectCondition.All(
            Term(ConditionFunction.IS_PVP),
            EffectCondition.Any(
                Term(ConditionFunction.SELF_HP_PCT),
                EffectCondition.Not(Term(ConditionFunction.TARGET_IS_ELITE))));

        ConditionSubjects.Of(tree).ReadsTarget.ShouldBeTrue(
            "the read sits under all → any → not, and the classification is over the whole tree");
    }

    /// <summary>A tree can read both subjects at once.</summary>
    [Fact]
    public void A_tree_reading_both_subjects_reports_both()
    {
        var tree = EffectCondition.All(
            Term(ConditionFunction.TARGET_HP_PCT),
            Term(ConditionFunction.ATTACKER_IS_SUMMON));

        var subjects = ConditionSubjects.Of(tree);

        subjects.ReadsTarget.ShouldBeTrue();
        subjects.ReadsAttacker.ShouldBeTrue();
    }

    // ─────────────────────────────────────────────────────────── the subject-presence rule itself

    /// <summary>A target-reading tree is carried only by a context with a current target.</summary>
    [Fact]
    public void A_target_reading_tree_is_carried_only_where_the_context_has_a_target()
    {
        var tree = Term(ConditionFunction.TARGET_IS_ELITE);
        var hero = EffectTestBattle.Hero();
        var enemy = EffectTestBattle.Enemy("E1", 1);

        var ambient = EffectTestBattle.Context(hero, hero, enemy);

        ConditionSubjects.CarriedBy(tree, ambient).ShouldBeFalse(
            "ambient re-aggregation carries no current target, so the tree's subject is absent");

        ConditionSubjects.CarriedBy(tree, ambient with { CurrentTarget = enemy }).ShouldBeTrue(
            "an attack resolution names its target, so the tree can be evaluated there");
    }

    /// <summary>An attacker-reading tree is carried only by a context with an attacker.</summary>
    [Fact]
    public void An_attacker_reading_tree_is_carried_only_where_the_context_has_an_attacker()
    {
        var tree = Term(ConditionFunction.ATTACKER_IS_BOSS);
        var hero = EffectTestBattle.Hero();
        var enemy = EffectTestBattle.Enemy("E1", 1);

        var ambient = EffectTestBattle.Context(hero, hero, enemy);

        ConditionSubjects.CarriedBy(tree, ambient).ShouldBeFalse();
        ConditionSubjects.CarriedBy(tree, ambient with { Attacker = enemy }).ShouldBeTrue();
    }

    /// <summary>An ambient tree — and an ungated effect — is carried by every context.</summary>
    [Fact]
    public void An_ambient_tree_is_carried_by_a_bare_context()
    {
        var hero = EffectTestBattle.Hero();
        var ambient = EffectTestBattle.Context(hero, hero);

        ConditionSubjects.CarriedBy(null, ambient).ShouldBeTrue();
        ConditionSubjects.CarriedBy(Term(ConditionFunction.SELF_HP_PCT), ambient).ShouldBeTrue();
    }

    /// <summary>A both-subject tree needs both subjects present.</summary>
    [Fact]
    public void A_both_subject_tree_needs_both_subjects_present()
    {
        var tree = EffectCondition.All(
            Term(ConditionFunction.TARGET_IS_BOSS),
            Term(ConditionFunction.ATTACKER_IS_ELITE));
        var hero = EffectTestBattle.Hero();
        var enemy = EffectTestBattle.Enemy("E1", 1);

        var ambient = EffectTestBattle.Context(hero, hero, enemy);

        ConditionSubjects.CarriedBy(tree, ambient with { CurrentTarget = enemy }).ShouldBeFalse(
            "the attacker is still absent");

        ConditionSubjects.CarriedBy(
                tree, ambient with { CurrentTarget = enemy, Attacker = enemy })
            .ShouldBeTrue();
    }

    /// <summary>
    /// A term over the function. Booleans read <c>1</c>/<c>0</c>, so <c>eq 1</c> is a valid
    /// comparison for every function here — and the classification never evaluates it anyway.
    /// </summary>
    private static EffectCondition Term(ConditionFunction fn) =>
        EffectCondition.Of(new ConditionTerm
        {
            Fn = fn,
            Comparator = ConditionComparator.EQ,
            Value = 1.0,
        });
}
