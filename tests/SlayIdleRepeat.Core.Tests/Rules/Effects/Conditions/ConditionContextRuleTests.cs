using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Conditions;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Conditions;

/// <summary>
/// The uniform rule for a condition function that cannot be answered, and the floor under the
/// twenty-three functions themselves.
/// </summary>
/// <remarks>
/// Exactly one default is authored — the three <c>ATTACKER_IS_*</c> functions are <c>false</c> outside
/// an attacker context, pinned in <see cref="ConditionFunctionTests"/>. Everywhere else the subject's
/// absence is a failure rather than a substituted zero.
/// </remarks>
public sealed class ConditionContextRuleTests
{
    /// <summary>
    /// The three target-reading functions have no answer with no target. A substituted <c>0</c> would
    /// make <c>PK_EXECUTIONER</c> fire on an empty battlefield.
    /// </summary>
    [Theory]
    [InlineData(ConditionFunction.TARGET_HP_PCT)]
    [InlineData(ConditionFunction.TARGET_IS_ELITE)]
    [InlineData(ConditionFunction.TARGET_IS_BOSS)]
    public void A_target_function_with_no_target_fails_loudly(ConditionFunction function)
    {
        var hero = EffectTestBattle.Hero();
        var noTarget = EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1));

        noTarget.CurrentTarget.ShouldBeNull();

        var thrown = Should.Throw<EffectContextException>(
            () => ConditionEvaluator.Read(function, ConditionArguments.None, noTarget));

        thrown.Token.ShouldBe(function.ToString());
        thrown.Message.ShouldStartWith(EffectContextException.Marker, Case.Sensitive);
    }

    /// <summary>
    /// A run-state function against a context with no run throws rather than reading zero. Clauses
    /// with no duel meaning are meant to be skipped via <c>IS_PVP</c>, so one reaching this point means
    /// the content did not skip it — and a zero would hide that forever while quietly changing what
    /// the effect does.
    /// </summary>
    [Theory]
    [InlineData(ConditionFunction.PERK_COUNT)]
    [InlineData(ConditionFunction.DISTINCT_PERK_CATEGORIES)]
    [InlineData(ConditionFunction.PET_COUNT)]
    [InlineData(ConditionFunction.GOLD_HELD)]
    [InlineData(ConditionFunction.BATTLES_WON_THIS_RUN)]
    [InlineData(ConditionFunction.STAGE_INDEX)]
    [InlineData(ConditionFunction.CHAPTER)]
    [InlineData(ConditionFunction.TIER)]
    public void A_run_state_function_with_no_run_view_fails_loudly(ConditionFunction function)
    {
        var duel = EffectTestBattle.Duel();
        duel.Run.ShouldBeNull();

        var thrown = Should.Throw<EffectContextException>(
            () => ConditionEvaluator.Read(function, ConditionArguments.None, duel));

        thrown.Token.ShouldBe(function.ToString());
        thrown.Message.ShouldContain("18 §9.3", Case.Sensitive);
    }

    /// <summary>What the evaluator says when the subject it lacks is the run.</summary>
    private const string NoRunState = "carries no run state";

    /// <summary>
    /// The run-state functions are <b>exactly</b> those eight, derived rather than transcribed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>This case exists because two places outside it transcribe that same set by hand</b> —
    /// the theory above, and a filter in the Application tier that decides which perk rows are
    /// withheld from the draft because no fight can evaluate them. A run-scoped function added to
    /// the DSL and added to neither list is silent in both: the theory simply never asks about it,
    /// and the withholding filter reports a catalogue clean while a new unplayable row sits in the
    /// pool. So the set is read off the evaluator's own behaviour instead, by asking every function
    /// the enum declares and collecting the ones that fail for want of a run.
    /// </para>
    /// <para>
    /// ⚠️ <b>If this fails with a ninth name, the fix is not here.</b> Add the name to the list
    /// below, to the theory above, and to
    /// <c>Application.Tests</c>' <c>PerksDataTests.RunScopedConditionFunctions</c> — that last one is
    /// what keeps an unplayable perk row out of the draft, and it is the one with no other guard.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_run_state_function_is_exactly_one_of_these_eight()
    {
        var declared = Enum.GetValues<ConditionFunction>();

        declared.Length.ShouldBeGreaterThan(
            8,
            "floor: the sweep reads the enum, so an emptied or truncated vocabulary would make " +
            "every claim below vacuously true");

        var readsTheRun = new List<string>();

        foreach (var function in declared)
        {
            // Every function is asked against the same run-less duel. Most answer, and the ones
            // that throw for another reason — an absent status argument, say — are not collected:
            // the discriminator is the missing SUBJECT, not the failure.
            try
            {
                ConditionEvaluator.Read(function, ConditionArguments.None, EffectTestBattle.Duel());
            }
            catch (EffectContextException thrown)
                when (thrown.Message.Contains(NoRunState, StringComparison.Ordinal))
            {
                readsTheRun.Add(function.ToString());
            }
            catch (EffectContextException)
            {
                // Absent for a different reason. Pinned by this class's other cases.
            }
        }

        readsTheRun.ShouldBe(
            [
                nameof(ConditionFunction.PERK_COUNT),
                nameof(ConditionFunction.DISTINCT_PERK_CATEGORIES),
                nameof(ConditionFunction.PET_COUNT),
                nameof(ConditionFunction.GOLD_HELD),
                nameof(ConditionFunction.BATTLES_WON_THIS_RUN),
                nameof(ConditionFunction.STAGE_INDEX),
                nameof(ConditionFunction.CHAPTER),
                nameof(ConditionFunction.TIER),
            ],
            ignoreOrder: true,
            "a name here that is not in PerksDataTests.RunScopedConditionFunctions is a perk row " +
            "the draft can offer and no fight can evaluate — see this case's remarks");
    }

    /// <summary>
    /// A function keyed "by status id" has no answer without one — this is the failure that makes a
    /// missing argument visible instead of silently counting every status.
    /// </summary>
    [Theory]
    [InlineData(ConditionFunction.HAS_STATUS)]
    [InlineData(ConditionFunction.STATUS_STACKS)]
    public void A_function_that_needs_an_argument_fails_loudly_without_one(ConditionFunction function)
    {
        var hero = EffectTestBattle.Hero();
        var battle = EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1));

        var thrown = Should.Throw<EffectContextException>(
            () => ConditionEvaluator.Read(function, ConditionArguments.None, battle));

        thrown.Token.ShouldBe(function.ToString());
    }

    /// <summary>
    /// A <c>between</c> term missing a bound is malformed — half of a two-element range is not a
    /// range. Each of the three malformed-term rules below pins which one fired, not merely that
    /// something threw: an evaluator that threw for any term whose <c>Value</c> is null would pass all
    /// three while getting the flag case entirely wrong.
    /// </summary>
    [Theory]
    [InlineData(0.1, null)]
    [InlineData(null, 0.9)]
    [InlineData(null, null)]
    public void BETWEEN_without_both_bounds_fails_loudly(double? low, double? high)
    {
        var hero = EffectTestBattle.Hero();

        var thrown = Should.Throw<EffectContextException>(
            () => ConditionEvaluator.IsSatisfied(
                EffectCondition.Of(new ConditionTerm
                {
                    Fn = ConditionFunction.SELF_HP_PCT,
                    Comparator = ConditionComparator.BETWEEN,
                    RangeLow = low,
                    RangeHigh = high,
                }),
                EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1))));

        thrown.Token.ShouldBe(nameof(ConditionComparator.BETWEEN));
        thrown.Message.ShouldContain("bound", Case.Sensitive);
    }

    /// <summary>A comparator other than <c>between</c> with nothing to compare against is malformed.</summary>
    [Fact]
    public void A_comparison_with_neither_a_value_nor_a_flag_fails_loudly()
    {
        var hero = EffectTestBattle.Hero();

        var thrown = Should.Throw<EffectContextException>(
            () => ConditionEvaluator.IsSatisfied(
                EffectCondition.Of(new ConditionTerm
                {
                    Fn = ConditionFunction.SELF_HP_PCT,
                    Comparator = ConditionComparator.GTE,
                }),
                EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1))));

        thrown.Token.ShouldBe(nameof(ConditionComparator.GTE));
        thrown.Message.ShouldContain("nothing to compare against", Case.Sensitive);
    }

    /// <summary>
    /// An ordering comparator against a boolean flag is malformed — <c>{"op":"gt","value":true}</c>
    /// has no meaning, and answering it would mean inventing one.
    /// </summary>
    [Theory]
    [InlineData(ConditionComparator.LT)]
    [InlineData(ConditionComparator.LTE)]
    [InlineData(ConditionComparator.GT)]
    [InlineData(ConditionComparator.GTE)]
    [InlineData(ConditionComparator.BETWEEN)]
    public void An_ordering_comparator_against_a_flag_fails_loudly(ConditionComparator comparator)
    {
        var hero = EffectTestBattle.Hero();

        var thrown = Should.Throw<EffectContextException>(
            () => ConditionEvaluator.IsSatisfied(
                EffectCondition.Of(new ConditionTerm
                {
                    Fn = ConditionFunction.IS_PVP,
                    Comparator = comparator,
                    Flag = true,
                }),
                EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1))));

        thrown.Token.ShouldBe(comparator.ToString());
        thrown.Message.ShouldContain("orders a boolean", Case.Sensitive);
    }

    /// <summary>
    /// A combinator with no operands is rejected by the evaluator, not only by
    /// <see cref="EffectCondition.All"/>'s factory. The factory guard is bypassable:
    /// <see cref="EffectCondition.Operands"/> is an <c>init</c> property defaulting to an empty list,
    /// so an object initialiser (and a JSON deserialiser binding init properties directly) skips it. An
    /// empty <c>all</c> is silently vacuously true; an empty <c>any</c> would silently never fire.
    /// </summary>
    [Theory]
    [InlineData(ConditionKind.ALL)]
    [InlineData(ConditionKind.ANY)]
    [InlineData(ConditionKind.NOT)]
    public void A_combinator_with_no_operands_fails_loudly(ConditionKind kind)
    {
        var hero = EffectTestBattle.Hero();

        // Built by object initialiser precisely because that is the route around the factory.
        var emptied = new EffectCondition { Kind = kind };
        emptied.Operands.ShouldBeEmpty("the premise: the init property defaults to an empty list");

        var thrown = Should.Throw<EffectContextException>(
            () => ConditionEvaluator.IsSatisfied(
                emptied,
                EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1))));

        thrown.Token.ShouldBe(kind.ToString());
        thrown.Message.ShouldContain("no operands", Case.Sensitive);
    }

    /// <summary>
    /// A <c>null</c> operand inside a combinator is a hole in the tree, not an ungated effect.
    /// <c>IsSatisfied(null, …)</c> answers <c>true</c> — correctly, for an effect whose top-level
    /// condition is <c>null</c> — so letting that reach inside a combinator would make an <c>any</c>
    /// vacuously true, the same silent ungating one level down.
    /// </summary>
    [Fact]
    public void A_null_operand_inside_a_combinator_fails_loudly()
    {
        var hero = EffectTestBattle.Hero();

        var holed = new EffectCondition
        {
            Kind = ConditionKind.ANY,
            Operands = new EffectCondition[] { null! },
        };

        Should.Throw<EffectContextException>(
                () => ConditionEvaluator.IsSatisfied(
                    holed,
                    EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1))))
            .Message.ShouldContain("null", Case.Sensitive);
    }

    /// <summary>
    /// A term carrying both a numeric value and a boolean one is rejected rather than answered on the
    /// flag. Without the check the flag branch wins silently: the term below would hold at any
    /// non-zero HP, because a boolean comparison asks only whether the reading is non-zero, while the
    /// data plainly asked for exactly 50%.
    /// </summary>
    [Fact]
    public void A_term_carrying_both_a_value_and_a_flag_fails_loudly()
    {
        var hero = EffectTestBattle.Hero(currentHp: 13, maxHp: 100);

        var thrown = Should.Throw<EffectContextException>(
            () => ConditionEvaluator.IsSatisfied(
                EffectCondition.Of(new ConditionTerm
                {
                    Fn = ConditionFunction.SELF_HP_PCT,
                    Comparator = ConditionComparator.EQ,
                    Value = 0.5,
                    Flag = true,
                }),
                EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1))));

        thrown.Token.ShouldBe(nameof(ConditionFunction.SELF_HP_PCT));
        thrown.Message.ShouldContain("both a numeric value and a boolean one", Case.Sensitive);
    }

    /// <summary>
    /// An inverted <c>between</c> is rejected: it is satisfied by no reading at all, so the effect it
    /// gates could never fire — a content error that would never go red.
    /// </summary>
    [Fact]
    public void An_inverted_BETWEEN_range_fails_loudly()
    {
        var hero = EffectTestBattle.Hero(currentHp: 50, maxHp: 100);

        var thrown = Should.Throw<EffectContextException>(
            () => ConditionEvaluator.IsSatisfied(
                EffectCondition.Of(new ConditionTerm
                {
                    Fn = ConditionFunction.SELF_HP_PCT,
                    Comparator = ConditionComparator.BETWEEN,
                    RangeLow = 0.9,
                    RangeHigh = 0.1,
                }),
                EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1))));

        thrown.Token.ShouldBe(nameof(ConditionComparator.BETWEEN));
        thrown.Message.ShouldContain("inverted", Case.Sensitive);
    }

    // ------------------------------------------------------------------ the floor

    /// <summary>
    /// Every one of the twenty-three functions reads something — none falls through to an unhandled
    /// switch arm, and no function was added to <see cref="ConditionFunction"/> without an
    /// implementation. Driven by <c>Enum.GetValues</c>, so the subject set could silently empty; the
    /// count is re-asserted here so that dependency is visible from the rule that relies on it.
    /// </summary>
    [Fact]
    public void Every_one_of_the_twenty_two_functions_reads_a_value()
    {
        var functions = Enum.GetValues<ConditionFunction>();
        functions.Length.ShouldBe(
            22,
            "18 §11: '23 conditions = 20 + the three ATTACKER_IS_*', less DIE_FACE_COUNT, which is " +
            "gone with the die's face kinds");

        var hero = EffectTestBattle.Hero(currentHp: 40, maxHp: 100) with
        {
            Statuses = new Dictionary<string, int>(StringComparer.Ordinal) { ["SUNDER"] = 2 },
        };
        var elite = EffectTestBattle.Enemy("ELITE_A", 1) with { IsElite = true };

        // Everything any function could ask for is present, so the ONLY reason one can fail here is
        // that it has no reading at all.
        var fullyPopulated = new EffectEvaluationContext
        {
            Holder = hero,
            CurrentTarget = elite,
            Attacker = elite,
            Actors = new IEffectActorView[] { hero, elite },
            BattleTimeSeconds = 12.0,
            EnrageAtSeconds = EffectTestBattle.EnrageSeconds,
            FightHorizonSeconds = EffectTestBattle.PveTimeoutSeconds,
            Run = EffectTestBattle.Run(),
        };

        var arguments = new ConditionArguments("SUNDER", "OFFENSE");

        var unhandled = new List<string>();

        foreach (var function in functions)
        {
            try
            {
                var reading = ConditionEvaluator.Read(function, arguments, fullyPopulated);

                // And the reading is usable as a valueScale source. Asserted here rather than in a
                // test of its own: StepsFor rejects exactly NaN, infinity and an out-of-int step
                // count, so a separate test would be one that cannot fail while this loop passes.
                new ValueScale { Fn = function, Per = 0.01, Cap = null }.StepsFor(reading);
            }
            catch (Exception e)
            {
                unhandled.Add($"{function} threw {e.GetType().Name}: {e.Message}");
            }
        }

        unhandled.ShouldBeEmpty();
    }
}
