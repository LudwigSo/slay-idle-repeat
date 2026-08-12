using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Conditions;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Conditions;

/// <summary>
/// 🔒 The uniform rule for a `18` §4 function that cannot be answered, and the floor under the
/// twenty-three functions themselves.
/// </summary>
/// <remarks>
/// `18` §4 authors exactly one default — the three <c>ATTACKER_IS_*</c> functions are <c>false</c>
/// outside an attacker context, pinned in <see cref="ConditionFunctionTests"/>. Everywhere else it
/// authors nothing, so steering S6 applies and the subject's absence is a failure rather than a
/// substituted zero.
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
    /// A run-state function against a context with no run throws rather than reading zero. `18` §9.3
    /// rules that clauses with no duel meaning are <em>skipped</em> via <c>IS_PVP</c>, so one
    /// reaching this point means the content did not skip it — and a zero would hide that forever
    /// while quietly changing what the effect does.
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
        thrown.Message.ShouldMatchWildcard("*run*");
    }

    /// <summary>
    /// <c>DIE_FACE_COUNT</c> is the ninth run-state function and needs an argument as well as a run,
    /// so it gets its own row rather than sharing the theory above.
    /// </summary>
    [Fact]
    public void DIE_FACE_COUNT_with_no_run_view_fails_loudly()
    {
        var thrown = Should.Throw<EffectContextException>(
            () => ConditionEvaluator.Read(
                ConditionFunction.DIE_FACE_COUNT,
                new ConditionArguments(null, null, "Star"),
                EffectTestBattle.Duel()));

        thrown.Token.ShouldBe(nameof(ConditionFunction.DIE_FACE_COUNT));
    }

    /// <summary>
    /// A function whose `18` §4 row says <em>"by status id"</em> or <em>"by face kind"</em> has no
    /// answer without one. `18` §1.1 puts the same functions behind <c>valueScale</c>, which today
    /// carries no argument key at all — so this is the failure that makes that gap visible instead of
    /// silently counting every status.
    /// </summary>
    [Theory]
    [InlineData(ConditionFunction.HAS_STATUS)]
    [InlineData(ConditionFunction.STATUS_STACKS)]
    [InlineData(ConditionFunction.DIE_FACE_COUNT)]
    public void A_function_that_needs_an_argument_fails_loudly_without_one(ConditionFunction function)
    {
        var hero = EffectTestBattle.Hero();
        var battle = EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1));

        var thrown = Should.Throw<EffectContextException>(
            () => ConditionEvaluator.Read(function, ConditionArguments.None, battle));

        thrown.Token.ShouldBe(function.ToString());
    }

    /// <summary>
    /// A <c>between</c> term missing a bound is malformed. `18` §4 lists the comparator and writes no
    /// example, so the encoding is <see cref="ConditionTerm"/>'s two-element one — and half of it is
    /// not a range.
    /// </summary>
    [Theory]
    [InlineData(0.1, null)]
    [InlineData(null, 0.9)]
    [InlineData(null, null)]
    public void BETWEEN_without_both_bounds_fails_loudly(double? low, double? high)
    {
        var hero = EffectTestBattle.Hero();

        Should.Throw<EffectContextException>(
            () => ConditionEvaluator.IsSatisfied(
                EffectCondition.Of(new ConditionTerm
                {
                    Fn = ConditionFunction.SELF_HP_PCT,
                    Comparator = ConditionComparator.BETWEEN,
                    RangeLow = low,
                    RangeHigh = high,
                }),
                EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1))));
    }

    /// <summary>A comparator other than <c>between</c> with nothing to compare against is malformed.</summary>
    [Fact]
    public void A_comparison_with_neither_a_value_nor_a_flag_fails_loudly()
    {
        var hero = EffectTestBattle.Hero();

        Should.Throw<EffectContextException>(
            () => ConditionEvaluator.IsSatisfied(
                EffectCondition.Of(new ConditionTerm
                {
                    Fn = ConditionFunction.SELF_HP_PCT,
                    Comparator = ConditionComparator.GTE,
                }),
                EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1))));
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

        Should.Throw<EffectContextException>(
            () => ConditionEvaluator.IsSatisfied(
                EffectCondition.Of(new ConditionTerm
                {
                    Fn = ConditionFunction.IS_PVP,
                    Comparator = comparator,
                    Flag = true,
                }),
                EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1))));
    }

    // ------------------------------------------------------------------ the floor (steering S3)

    /// <summary>
    /// 🔒 Every one of `18` §4's twenty-three functions reads something — none falls through to an
    /// unhandled switch arm, and no function was added to <see cref="ConditionFunction"/> without an
    /// implementation.
    /// </summary>
    /// <remarks>
    /// Steering S3: driven by <c>Enum.GetValues</c>, so the subject set could silently empty. Its
    /// floor is the equality in <c>EffectVocabularyCountTests.There_are_23_condition_functions</c>
    /// against `18` §11's <em>"23 conditions = 20 + the three <c>ATTACKER_IS_*</c>"</em>, re-asserted
    /// here so the dependency is visible from the rule that relies on it.
    /// </remarks>
    [Fact]
    public void Every_one_of_the_twenty_three_functions_reads_a_value()
    {
        var functions = Enum.GetValues<ConditionFunction>();
        functions.Length.ShouldBe(23, "18 §11: '23 conditions = 20 + the three ATTACKER_IS_*'");

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
            Run = new RunStateReading(),
        };

        var arguments = new ConditionArguments("SUNDER", "OFFENSE", "Star");

        var unhandled = new List<string>();

        foreach (var function in functions)
        {
            try
            {
                var reading = ConditionEvaluator.Read(function, arguments, fullyPopulated);

                if (double.IsNaN(reading) || double.IsInfinity(reading))
                {
                    unhandled.Add($"{function} read {reading}, which ValueScale.StepsFor rejects");
                }
            }
            catch (Exception e)
            {
                unhandled.Add($"{function} threw {e.GetType().Name}: {e.Message}");
            }
        }

        unhandled.ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 And every function's reading survives the trip through <c>valueScale</c> — `18` §1.1:
    /// <em>"<c>fn</c>: any condition function from §4"</em>. A reading that <c>StepsFor</c> rejects is
    /// a function that cannot be used for half of what the DSL offers it for.
    /// </summary>
    [Fact]
    public void Every_functions_reading_is_a_usable_valueScale_source()
    {
        var hero = EffectTestBattle.Hero(currentHp: 40, maxHp: 100) with
        {
            Statuses = new Dictionary<string, int>(StringComparer.Ordinal) { ["SUNDER"] = 2 },
        };
        var elite = EffectTestBattle.Enemy("ELITE_A", 1) with { IsElite = true };

        var fullyPopulated = new EffectEvaluationContext
        {
            Holder = hero,
            CurrentTarget = elite,
            Attacker = elite,
            Actors = new IEffectActorView[] { hero, elite },
            BattleTimeSeconds = 12.0,
            EnrageAtSeconds = EffectTestBattle.EnrageSeconds,
            FightHorizonSeconds = EffectTestBattle.PveTimeoutSeconds,
            Run = new RunStateReading { GoldHeld = 1_450, PetCount = 2 },
        };

        var arguments = new ConditionArguments("SUNDER", "OFFENSE", "Star");
        var offenders = new List<string>();

        foreach (var function in Enum.GetValues<ConditionFunction>())
        {
            var scale = new ValueScale { Fn = function, Per = 0.01, Cap = null };
            var reading = ConditionEvaluator.Read(function, arguments, fullyPopulated);

            try
            {
                scale.StepsFor(reading);
            }
            catch (ArgumentOutOfRangeException e)
            {
                offenders.Add($"{function} read {reading}, which ValueScale rejected: {e.Message}");
            }
        }

        offenders.ShouldBeEmpty();
    }
}
