using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Conditions;
using SlayIdleRepeat.Core.Rules.Effects.Values;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Values;

/// <summary>
/// The <c>valueScale</c> gap, closed. <c>valueScale</c> accepts any condition function, and some of
/// those take an argument, while <see cref="ValueScale"/> once carried only
/// <c>fn</c>/<c>per</c>/<c>cap</c> — so scales driven by an argument-taking function were unexpressible.
/// </summary>
/// <remarks>
/// Closed by giving <see cref="ValueScale"/> the same three optional argument keys a condition term
/// already carries. No new vocabulary: the names, types and meanings are the condition layer's.
/// <see cref="ValueScale"/> cannot carry <c>ConditionArguments</c> itself, since it is <c>public</c> in
/// <c>Core.Content.Effects</c> and production forbids <c>Content</c> naming <c>Rules</c>. The evaluator
/// converts the scale into the same type the condition path uses, so there is one argument type in the
/// codebase and one place that reads it.
/// </remarks>
public sealed class ValueScaleArgumentTests
{
    /// <summary>A scale over <c>STATUS_STACKS</c> — one step per stack. <c>SUNDER</c> stacks to 5.</summary>
    [Fact]
    public void A_scale_over_STATUS_STACKS_reads_the_status_it_names()
    {
        var perStack = Scaled(
            new ValueScale
            {
                Fn = ConditionFunction.STATUS_STACKS,
                Per = 1,
                Cap = null,
                StatusId = "SUNDER",
            },
            value: 0.05);

        ValueScaleEvaluator.EffectiveValue(perStack, WithStatus("SUNDER", 3)).ShouldBe(
            0.15, "three stacks of SUNDER at 0.05 a stack");

        ValueScaleEvaluator.EffectiveValue(perStack, WithStatus("BURN", 3)).ShouldBe(
            0.0, "the scale names SUNDER, and the actor carries none");
    }

    /// <summary>A scale over <c>DIE_FACE_COUNT</c> — the die face kinds, counted on the run's dice.</summary>
    [Fact]
    public void A_scale_over_DIE_FACE_COUNT_reads_the_face_kind_it_names()
    {
        var perStar = Scaled(
            new ValueScale
            {
                Fn = ConditionFunction.DIE_FACE_COUNT,
                Per = 1,
                Cap = null,
                FaceKind = "Star",
            },
            value: 0.03);

        ValueScaleEvaluator.EffectiveValue(perStar, WithFaces("Star", 4)).ShouldBe(0.12);

        ValueScaleEvaluator.EffectiveValue(perStar, WithFaces("Pip", 4)).ShouldBe(
            0.0, "the scale names Star, and the run's dice carry none");
    }

    /// <summary>
    /// A scale over <c>PERK_COUNT</c>, whose category is optional — the same key is optional here
    /// too, and its absence counts every perk.
    /// </summary>
    [Fact]
    public void A_scale_over_PERK_COUNT_honours_the_optional_category()
    {
        var run = EffectTestBattle.Run() with
        {
            PerksByCategory = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["offense"] = 3,
                ["defense"] = 2,
            },
        };

        var everyPerk = Scaled(
            new ValueScale { Fn = ConditionFunction.PERK_COUNT, Per = 1 }, value: 0.01);

        var offenceOnly = Scaled(
            new ValueScale { Fn = ConditionFunction.PERK_COUNT, Per = 1, Category = "offense" },
            value: 0.01);

        ValueScaleEvaluator.EffectiveValue(everyPerk, WithRun(run)).ShouldBe(0.05);
        ValueScaleEvaluator.EffectiveValue(offenceOnly, WithRun(run)).ShouldBe(0.03);
    }

    /// <summary>
    /// The gap the extension closes, stated as the failure it used to be: a scale over
    /// <c>STATUS_STACKS</c> that names no status is refused, loudly, rather than counting every
    /// status or reading zero — the same refusal <c>ConditionEvaluator</c> already gives a condition
    /// term with no <c>statusId</c>, reached through the same code.
    /// </summary>
    [Fact]
    public void A_scale_over_STATUS_STACKS_that_names_no_status_is_refused()
    {
        var argumentless = Scaled(
            new ValueScale { Fn = ConditionFunction.STATUS_STACKS, Per = 1 }, value: 0.05);

        var thrown = Should.Throw<EffectContextException>(
            () => ValueScaleEvaluator.EffectiveValue(argumentless, WithStatus("SUNDER", 3)));

        thrown.Token.ShouldBe(nameof(ConditionFunction.STATUS_STACKS));
        thrown.Message.ShouldContain("18 §4");
    }

    /// <summary>
    /// One argument type, not two. The scale's keys reach the evaluator as the same
    /// <c>ConditionArguments</c> a condition term produces, so a function can never read an argument
    /// one way from a condition and another way from a scale.
    /// </summary>
    [Fact]
    public void A_scale_and_a_condition_term_carrying_the_same_argument_read_the_same_number()
    {
        var context = WithStatus("SUNDER", 4);

        var viaScale = ValueScaleEvaluator.Steps(
            new ValueScale { Fn = ConditionFunction.STATUS_STACKS, Per = 1, StatusId = "SUNDER" },
            context);

        var viaCondition = ConditionArguments.Of(
            new ConditionTerm
            {
                Fn = ConditionFunction.STATUS_STACKS,
                Comparator = ConditionComparator.GTE,
                Value = 0,
                StatusId = "SUNDER",
            });

        viaScale.ShouldBe(4);
        ConditionEvaluator.Read(ConditionFunction.STATUS_STACKS, viaCondition, context).ShouldBe(4.0);
    }

    /// <summary>
    /// The three keys are all optional, so every scale already authored keeps working untouched —
    /// <c>PK_BERSERK</c> and <c>PK_HOARD</c> name no argument and need none. Reading the three
    /// properties back off the record they were never set on cannot fail for any behavioural bug;
    /// what can is the conversion: an argumentless scale has to reach the shared seam as
    /// <see cref="ConditionArguments.None"/>, so <c>SELF_MISSING_HP_PCT</c> is read against no
    /// argument rather than a manufactured one.
    /// </summary>
    [Fact]
    public void The_argument_keys_are_optional()
    {
        var berserk = new ValueScale
        {
            Fn = ConditionFunction.SELF_MISSING_HP_PCT, Per = 0.01, Cap = 45,
        };

        ConditionArguments.Of(berserk).ShouldBe(
            ConditionArguments.None, "18 §1.1's two authored scales name no argument and need none");
    }

    // ───────────────────────────────────────────── fixtures

    private static EffectDefinition Scaled(ValueScale scale, double value) =>
        new()
        {
            Id = "PK_UNDER_TEST",
            Op = EffectOp.STAT_ADD_PCT,
            Stat = StatSelector.Of(StatId.ATK),
            Value = value,
            Trigger = new EffectTrigger { Kind = TriggerKind.ALWAYS },
            Target = EffectTarget.SELF,
            ValueScale = scale,
        };

    private static EffectEvaluationContext WithStatus(string statusId, int stacks)
    {
        var hero = EffectTestBattle.Hero() with
        {
            Statuses = new Dictionary<string, int>(StringComparer.Ordinal) { [statusId] = stacks },
        };

        return EffectTestBattle.Context(hero, hero);
    }

    private static EffectEvaluationContext WithFaces(string faceKind, int count)
    {
        var run = EffectTestBattle.Run() with
        {
            DieFacesByKind = new Dictionary<string, int>(StringComparer.Ordinal) { [faceKind] = count },
        };

        return WithRun(run);
    }

    private static EffectEvaluationContext WithRun(RunStateReading run)
    {
        var hero = EffectTestBattle.Hero();

        return EffectTestBattle.Context(hero, hero) with { Run = run };
    }
}
