using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Conditions;
using SlayIdleRepeat.Core.Rules.Effects.Targeting;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Conditions;

/// <summary>
/// The Ghost Duel rulings as the condition layer sees them: target conditions read the opposing hero,
/// <c>TARGET_IS_ELITE</c>/<c>TARGET_IS_BOSS</c> are always false, <c>ENEMY_COUNT</c> is always 1, and
/// no-duel-meaning clauses are skipped rather than converted.
/// </summary>
/// <remarks>
/// The duel itself is <c>PvpDuelTests</c>'. What is pinned here is that the conditions answer correctly
/// once they are told it is one.
/// </remarks>
public sealed class PvpConditionTests
{
    /// <summary><c>IS_PVP</c> is the hook that lets a perk behave differently in a duel.</summary>
    [Fact]
    public void IS_PVP_is_true_in_a_duel_and_false_in_a_run()
    {
        ConditionEvaluator.Read(ConditionFunction.IS_PVP, ConditionArguments.None, EffectTestBattle.Duel())
            .ShouldBe(1);

        var hero = EffectTestBattle.Hero();
        var pve = EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1));

        ConditionEvaluator.Read(ConditionFunction.IS_PVP, ConditionArguments.None, pve).ShouldBe(0);
    }

    /// <summary>
    /// <c>TARGET_IS_ELITE</c> / <c>TARGET_IS_BOSS</c> are always false in a duel — stated as a rule
    /// rather than as a consequence of the roster, and implemented as one: the duel below deliberately
    /// carries an opposing hero flagged as both elite and boss, something no legitimate ghost snapshot
    /// would, so the assertion cannot be satisfied merely by a roster that happens to be built
    /// correctly.
    /// </summary>
    [Fact]
    public void TARGET_IS_ELITE_and_TARGET_IS_BOSS_are_always_false_in_a_duel()
    {
        var duel = EffectTestBattle.Duel();
        var opposing = (EffectTestActor)duel.CurrentTarget!;

        var mislabelled = duel with
        {
            CurrentTarget = opposing with { IsElite = true, IsBoss = true },
        };

        ConditionEvaluator.Read(ConditionFunction.TARGET_IS_ELITE, ConditionArguments.None, mislabelled)
            .ShouldBe(0);
        ConditionEvaluator.Read(ConditionFunction.TARGET_IS_BOSS, ConditionArguments.None, mislabelled)
            .ShouldBe(0);
    }

    /// <summary>
    /// <c>ENEMY_COUNT</c> is always 1 in a duel — implemented as the stated rule: the roster below is
    /// given a second opposing actor, which a correct duel never has, so a count taken off the roster
    /// would read 2.
    /// </summary>
    [Fact]
    public void ENEMY_COUNT_is_always_one_in_a_duel()
    {
        var duel = EffectTestBattle.Duel();

        ConditionEvaluator.Read(ConditionFunction.ENEMY_COUNT, ConditionArguments.None, duel).ShouldBe(1);

        var withAStowaway = duel with
        {
            Actors = duel.Actors.Append(EffectTestBattle.Enemy("STOWAWAY", 4)).ToArray(),
        };

        ConditionEvaluator.Read(ConditionFunction.ENEMY_COUNT, ConditionArguments.None, withAStowaway)
            .ShouldBe(1, "05 §3.3 states this as a rule of the duel, not as a count of the roster");
    }

    /// <summary>Target-conditional effects read the opposing hero — <c>PK_EXECUTIONER</c> is the named example.</summary>
    [Fact]
    public void A_target_conditional_effect_reads_the_opposing_hero()
    {
        var duel = EffectTestBattle.Duel();
        var opposing = (EffectTestActor)duel.CurrentTarget!;

        var wounded = duel with { CurrentTarget = opposing with { CurrentHp = 20, MaxHp = 100 } };

        ConditionEvaluator.Read(ConditionFunction.TARGET_HP_PCT, ConditionArguments.None, wounded)
            .ShouldBe(0.20);

        var executioner = EffectCondition.Of(new ConditionTerm
        {
            Fn = ConditionFunction.TARGET_HP_PCT,
            Comparator = ConditionComparator.LT,
            Value = 0.30,
        });

        ConditionEvaluator.IsSatisfied(executioner, wounded).ShouldBeTrue();
    }

    /// <summary>The enemy tokens in a duel select the opposing hero — one actor, never the opposing pets.</summary>
    [Theory]
    [InlineData(EffectTarget.ALL_ENEMIES)]
    [InlineData(EffectTarget.LOWEST_HP_ENEMY)]
    [InlineData(EffectTarget.HIGHEST_HP_ENEMY)]
    public void An_enemy_token_in_a_duel_is_the_opposing_hero_alone(EffectTarget token)
    {
        TargetResolver.Resolve(token, EffectTestBattle.Duel())
            .Select(a => a.Id)
            .ShouldBe(["HERO_DEFENDER"]);
    }

    /// <summary>
    /// <c>OTHER_ENEMIES</c> in a duel is empty: a duel leaves exactly one opposing actor, and it is
    /// the attack's primary target, so <c>PK_CLEAVE</c>'s splash has nobody left to reach. This falls
    /// out of the two rules being implemented rather than needing a duel branch of its own.
    /// </summary>
    [Fact]
    public void OTHER_ENEMIES_in_a_duel_is_empty()
    {
        var duel = EffectTestBattle.Duel();
        duel.CurrentTarget.ShouldNotBeNull("a duel is always an attack context against the opposing hero");

        TargetResolver.Resolve(EffectTarget.OTHER_ENEMIES, duel).ShouldBeEmpty();
    }

    /// <summary>
    /// <c>RANDOM_ENEMY</c> in a duel draws from one candidate and still consumes its draw — the
    /// stream must not desynchronise between a duel and a run.
    /// </summary>
    [Fact]
    public void RANDOM_ENEMY_in_a_duel_is_the_opposing_hero_and_still_spends_a_draw()
    {
        var rng = EffectTestBattle.CombatRng(11);

        TargetResolver.Resolve(EffectTarget.RANDOM_ENEMY, EffectTestBattle.Duel() with { Rng = rng })
            .Select(a => a.Id).ShouldBe(["HERO_DEFENDER"]);

        rng.Position.ShouldBe(
            1UL,
            "a one-candidate draw is still a draw — skipping it would make the same effect consume a " +
            "different number of draw indices in a duel than in a run");
    }

    /// <summary>
    /// The three <c>ATTACKER_IS_*</c> functions are not switched off in a duel. The attacker trio is
    /// already <c>false</c> against an opposing hero for the honest reason — a hero is neither elite,
    /// boss nor summon — so no duel rule is needed. Pinned so a later "while we are here" edit cannot
    /// add one silently.
    /// </summary>
    [Fact]
    public void The_ATTACKER_IS_trio_still_reads_the_attacker_in_a_duel()
    {
        var duel = EffectTestBattle.Duel();
        var opposing = (EffectTestActor)duel.CurrentTarget!;

        var struckByTheOpposingHero = duel with { Attacker = opposing };

        ConditionEvaluator.Read(ConditionFunction.ATTACKER_IS_ELITE, ConditionArguments.None, struckByTheOpposingHero)
            .ShouldBe(0);
        ConditionEvaluator.Read(ConditionFunction.ATTACKER_IS_BOSS, ConditionArguments.None, struckByTheOpposingHero)
            .ShouldBe(0);
        ConditionEvaluator.Read(ConditionFunction.ATTACKER_IS_SUMMON, ConditionArguments.None, struckByTheOpposingHero)
            .ShouldBe(0);

        // And they still READ the attacker rather than being hard-wired off: a duel context whose
        // attacker genuinely carries a flag reports it. TARGET_IS_* above does not behave this way.
        var struckByAFlaggedActor = duel with { Attacker = opposing with { IsSummon = true } };

        ConditionEvaluator.Read(ConditionFunction.ATTACKER_IS_SUMMON, ConditionArguments.None, struckByAFlaggedActor)
            .ShouldBe(1);
    }

    /// <summary>
    /// Gear affixes like <c>+X% Gold Gain</c> still need to be neutralised in duels — they are simply
    /// skipped rather than converted. The mechanism is content-side: the affix carries
    /// <c>{"not":{"fn":"IS_PVP","op":"eq","value":true}}</c> and the evaluator answers <c>false</c>,
    /// so the effect is filtered out during condition gating.
    /// </summary>
    [Fact]
    public void The_IS_PVP_skip_neutralises_a_non_combat_affix_in_a_duel()
    {
        var goldGain = EffectCondition.Not(
            EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.IS_PVP,
                Comparator = ConditionComparator.EQ,
                Flag = true,
            }));

        ConditionEvaluator.IsSatisfied(goldGain, EffectTestBattle.Duel())
            .ShouldBeFalse("skipped in a duel");

        var hero = EffectTestBattle.Hero();
        ConditionEvaluator.IsSatisfied(
            goldGain,
            EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1)))
            .ShouldBeTrue("and untouched everywhere else");
    }
}
