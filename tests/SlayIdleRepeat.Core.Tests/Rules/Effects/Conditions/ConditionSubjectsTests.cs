using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
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

    /// <summary>The three functions that read the current target.</summary>
    private static readonly ConditionFunction[] TargetReaders =
    [
        ConditionFunction.TARGET_HP_PCT,
        ConditionFunction.TARGET_IS_ELITE,
        ConditionFunction.TARGET_IS_BOSS,
    ];

    /// <summary>The three functions that read the attacker.</summary>
    private static readonly ConditionFunction[] AttackerReaders =
    [
        ConditionFunction.ATTACKER_IS_ELITE,
        ConditionFunction.ATTACKER_IS_BOSS,
        ConditionFunction.ATTACKER_IS_SUMMON,
    ];

    /// <summary>
    /// The partition is total: every declared function classifies as exactly what the two lists
    /// above say, and every function outside them reads neither subject.
    /// </summary>
    /// <remarks>
    /// Exhaustive over the enum rather than a sample: an ambient function misclassified as
    /// subject-reading would silently convert its strict-gate refusal into inertness, and a
    /// subject function misclassified as ambient would throw or misfire mid-battle. A function
    /// added to the vocabulary lands in this loop on its own and forces a classification
    /// decision here.
    /// </remarks>
    [Fact]
    public void Every_declared_function_classifies_per_the_partition()
    {
        foreach (var fn in Enum.GetValues<ConditionFunction>())
        {
            var subjects = ConditionSubjects.Of(Term(fn));

            subjects.ReadsTarget.ShouldBe(
                TargetReaders.Contains(fn),
                fn + " must classify as " +
                (TargetReaders.Contains(fn) ? "reading" : "not reading") + " the target");
            subjects.ReadsAttacker.ShouldBe(
                AttackerReaders.Contains(fn),
                fn + " must classify as " +
                (AttackerReaders.Contains(fn) ? "reading" : "not reading") + " the attacker");
        }
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

    /// <summary>A target-reading tree is carried exactly by a context with a current target.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void A_target_reading_tree_is_carried_only_where_the_context_has_a_target(
        bool hasTarget, bool carried)
    {
        var tree = Term(ConditionFunction.TARGET_IS_ELITE);
        var hero = EffectTestBattle.Hero();
        var enemy = EffectTestBattle.Enemy("E1", 1);

        var context = EffectTestBattle.Context(hero, hero, enemy) with
        {
            CurrentTarget = hasTarget ? enemy : null,
        };

        context.Carries(ConditionSubjects.Of(tree)).ShouldBe(
            carried,
            "a target-reading tree waits for a context that names its target — ambient " +
            "re-aggregation carries none, an attack resolution carries one");
    }

    /// <summary>An attacker-reading tree is carried exactly by a context with an attacker.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void An_attacker_reading_tree_is_carried_only_where_the_context_has_an_attacker(
        bool hasAttacker, bool carried)
    {
        var tree = Term(ConditionFunction.ATTACKER_IS_BOSS);
        var hero = EffectTestBattle.Hero();
        var enemy = EffectTestBattle.Enemy("E1", 1);

        var context = EffectTestBattle.Context(hero, hero, enemy) with
        {
            Attacker = hasAttacker ? enemy : null,
        };

        context.Carries(ConditionSubjects.Of(tree)).ShouldBe(carried);
    }

    /// <summary>An ungated effect is carried by every context.</summary>
    [Fact]
    public void A_null_condition_is_carried_by_a_bare_context()
    {
        var hero = EffectTestBattle.Hero();

        EffectTestBattle.Context(hero, hero).Carries(ConditionSubjects.Of(null)).ShouldBeTrue();
    }

    /// <summary>An ambient tree is carried by a context with neither subject.</summary>
    [Fact]
    public void An_ambient_tree_is_carried_by_a_bare_context()
    {
        var hero = EffectTestBattle.Hero();

        EffectTestBattle.Context(hero, hero)
            .Carries(ConditionSubjects.Of(Term(ConditionFunction.SELF_HP_PCT)))
            .ShouldBeTrue();
    }

    /// <summary>A both-subject tree needs both subjects present — either alone is not enough.</summary>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void A_both_subject_tree_needs_both_subjects_present(
        bool hasTarget, bool hasAttacker, bool carried)
    {
        var tree = EffectCondition.All(
            Term(ConditionFunction.TARGET_IS_BOSS),
            Term(ConditionFunction.ATTACKER_IS_ELITE));
        var hero = EffectTestBattle.Hero();
        var enemy = EffectTestBattle.Enemy("E1", 1);

        var context = EffectTestBattle.Context(hero, hero, enemy) with
        {
            CurrentTarget = hasTarget ? enemy : null,
            Attacker = hasAttacker ? enemy : null,
        };

        context.Carries(ConditionSubjects.Of(tree)).ShouldBe(carried);
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
