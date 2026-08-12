using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Conditions;
using SlayIdleRepeat.Core.Rules.Effects.Targeting;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Conditions;

/// <summary>
/// 🔒 `05` §3.3's Ghost Duel rulings, as the condition layer sees them. PvP is confirmed for v1, so
/// these are live rules and not speculative ones.
/// </summary>
/// <remarks>
/// `05` §3.3's row: <em>"Conditions such as <c>TARGET_HP_BELOW_30</c> (<c>PK_EXECUTIONER</c>) read
/// the <b>opposing hero</b>. <c>TARGET_IS_ELITE</c> / <c>TARGET_IS_BOSS</c> are always false.
/// <c>ENEMY_COUNT</c> is always 1."</em> Plus `18` §9.3: <em>"gear affixes like <c>+X% Gold Gain</c>
/// still need to be neutralised in duels — they are simply <b>skipped</b> rather than
/// converted."</em>
/// <para>
/// The duel itself is M2-14's. What is pinned here is that the conditions answer correctly once they
/// are told it is one.
/// </para>
/// </remarks>
public sealed class PvpConditionTests
{
    /// <summary>`18` §4 — <c>IS_PVP</c>, <em>"the hook that lets a perk behave differently in a duel"</em>.</summary>
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
    /// 🔒 `05` §3.3 — <em>"<c>TARGET_IS_ELITE</c> / <c>TARGET_IS_BOSS</c> are always false."</em>
    /// </summary>
    /// <remarks>
    /// Stated by `05` §3.3 as a rule rather than as a consequence of the roster, and implemented as
    /// one: the duel below deliberately carries an opposing hero flagged as both elite and boss —
    /// something no legitimate ghost snapshot would — so the assertion cannot be satisfied merely by
    /// a roster that happens to be built correctly. Steering S9: where a number or a rule in a
    /// summary disagrees with the document, the document wins.
    /// </remarks>
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

    /// <summary>🔒 `05` §3.3 — <em>"<c>ENEMY_COUNT</c> is always 1."</em></summary>
    /// <remarks>
    /// Also implemented as the stated rule: the roster below is given a second opposing actor, which
    /// a correct duel never has, so a count taken off the roster would read 2.
    /// </remarks>
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

    /// <summary>
    /// `05` §3.3 — <em>"Target-conditional effects read the opposing hero."</em> `18` §7.2's
    /// <c>PK_EXECUTIONER</c> is the named example.
    /// </summary>
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

    /// <summary>
    /// The enemy tokens of `18` §5 in a duel select the opposing hero — one actor, never the
    /// opposing pets (`05` §3.2: <em>"Pets cannot be targeted or killed"</em>, restated for duels in
    /// §3.3).
    /// </summary>
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
    /// 🔒 `18` §9.3 — <em>"gear affixes like <c>+X% Gold Gain</c> still need to be neutralised in
    /// duels — they are simply <b>skipped</b> rather than converted."</em>
    /// </summary>
    /// <remarks>
    /// The mechanism is content-side: the affix carries <c>{"not":{"fn":"IS_PVP","op":"eq","value":
    /// true}}</c> and the evaluator answers <c>false</c>, so the effect is filtered out at `18` §8
    /// step 2. Nothing converts a gold bonus into a combat one, and nothing here needs to know what
    /// the affix does.
    /// </remarks>
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
