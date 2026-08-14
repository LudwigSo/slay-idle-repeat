using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Targeting;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Targeting;

/// <summary>
/// 🔒 The uniform rule for a `18` §5 token that cannot be answered: <b>a token whose SUBJECT is absent
/// throws <see cref="EffectContextException"/>; a token whose subject is present but whose SET is
/// empty resolves to the empty set.</b>
/// </summary>
/// <remarks>
/// §5 authors a degradation for two of its eleven targets and nothing for the other nine, and steering
/// S6 forbids inventing one — so the rule is stated once and applied uniformly. The empty-set half is
/// <see cref="TargetResolverTests"/>'; this file covers the throwing half and pins <em>which</em> token
/// failed, since several independent absences produce the same exception type.
/// </remarks>
public sealed class TargetContextRuleTests
{
    /// <summary>`18` §5's <c>ATTACKER</c> has no authored default, unlike `18` §4's three ATTACKER_IS_*.</summary>
    [Fact]
    public void ATTACKER_outside_an_attacker_context_fails_loudly()
    {
        var hero = EffectTestBattle.Hero();
        var grunt = EffectTestBattle.Enemy("GRUNT_A", 1);

        var noAttacker = EffectTestBattle.Context(hero, hero, grunt);
        noAttacker.Attacker.ShouldBeNull();

        var thrown = Should.Throw<EffectContextException>(
            () => TargetResolver.Resolve(EffectTarget.ATTACKER, noAttacker));

        thrown.Token.ShouldBe(nameof(EffectTarget.ATTACKER));
        thrown.Message.ShouldStartWith(EffectContextException.Marker, Case.Sensitive);

        // 🔒 A document reference, not the implementer's prose. WildcardMessageAssertions compiles
        // with RegexOptions.IgnoreCase, so a pattern like "*ATTACKER*attacker*" does not distinguish
        // the token from the sentence around it — and pinning the sentence would fail a correct
        // implementation that worded it differently.
        thrown.Message.ShouldContain("18 §5", Case.Sensitive);
    }

    /// <summary><c>CURRENT_TARGET</c> with no target is a question with no answer.</summary>
    [Fact]
    public void CURRENT_TARGET_with_no_target_fails_loudly()
    {
        var hero = EffectTestBattle.Hero();
        var grunt = EffectTestBattle.Enemy("GRUNT_A", 1);

        var thrown = Should.Throw<EffectContextException>(
            () => TargetResolver.Resolve(
                EffectTarget.CURRENT_TARGET,
                EffectTestBattle.Context(hero, hero, grunt)));

        thrown.Token.ShouldBe(nameof(EffectTarget.CURRENT_TARGET));
    }

    /// <summary>
    /// 🔒 No RNG, no draw. Falling back to the first candidate would be a "random" pick that is
    /// perfectly stable and wrong — the determinism failure `14` §8.1 exists to prevent.
    /// </summary>
    [Fact]
    public void RANDOM_ENEMY_with_no_draw_stream_fails_loudly()
    {
        var hero = EffectTestBattle.Hero();
        var grunt = EffectTestBattle.Enemy("GRUNT_A", 1);

        var noRng = EffectTestBattle.Context(hero, hero, grunt);
        noRng.Rng.ShouldBeNull();

        var thrown = Should.Throw<EffectContextException>(
            () => TargetResolver.Resolve(EffectTarget.RANDOM_ENEMY, noRng));

        thrown.Token.ShouldBe(nameof(EffectTarget.RANDOM_ENEMY));
        thrown.Message.ShouldContain("14 §8.1", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 <c>ALL_PETS</c> on a PvE enemy side is the <b>empty set</b>, not a failure — the side is
    /// present, it simply holds no pet.
    /// </summary>
    /// <remarks>
    /// ⚠️ A failure in the first draft. The argument for throwing was that a side with no hero can hold
    /// no pets — an inference that appears in no document, and inventing it would crash a battle over
    /// any boss effect authored with <c>target: "ALL_PETS"</c>. The subject of the token is the holder's
    /// side, which is always present.
    /// </remarks>
    [Fact]
    public void ALL_PETS_on_a_side_that_holds_no_pet_is_the_empty_set()
    {
        var hero = EffectTestBattle.Hero();
        var boss = EffectTestBattle.Enemy("BOSS_THORNMAW", 1) with { IsBoss = true };

        TargetResolver.Resolve(EffectTarget.ALL_PETS, EffectTestBattle.Context(boss, hero, boss))
            .ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 A summon that records no summoner is a malformed actor view, <b>not</b> `18` §5's authored
    /// skip.
    /// </summary>
    /// <remarks>
    /// ⚠️ The two are one condition from being spelled identically, and collapsing them hides a
    /// roster-construction bug behind a documented no-op: <c>OwnerId</c> documents <c>null</c> as
    /// <em>"an actor that was not summoned"</em>, which an actor flagged <c>IsSummon</c> claims not to
    /// be.
    /// </remarks>
    [Fact]
    public void OWNER_on_a_summon_that_records_no_summoner_fails_loudly()
    {
        var hero = EffectTestBattle.Hero();
        var malformed = EffectTestBattle.Enemy("SUMMON_SPORELING", 1) with
        {
            IsSummon = true,
            OwnerId = null,
        };

        var thrown = Should.Throw<EffectContextException>(
            () => TargetResolver.Resolve(
                EffectTarget.OWNER,
                EffectTestBattle.Context(malformed, hero, malformed)));

        thrown.Token.ShouldBe(nameof(EffectTarget.OWNER));
        thrown.Message.ShouldContain("records no summoner", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 `18` §5 declares <c>RUN</c>, and §2.5 rules that run and board ops are resolved by the run
    /// controller, never the simulator — which appends <c>RunEffectQueued</c> instead.
    /// </summary>
    /// <remarks>
    /// A declared token with no resolver is the correct end state: the run controller is M3's, and a
    /// placeholder would be a guess. The throw makes "nobody wired this yet" impossible to mistake for
    /// "this resolved to nobody".
    /// </remarks>
    [Fact]
    public void RUN_has_no_actor_resolver_and_says_so()
    {
        var hero = EffectTestBattle.Hero();
        var grunt = EffectTestBattle.Enemy("GRUNT_A", 1);

        var thrown = Should.Throw<EffectContextException>(
            () => TargetResolver.Resolve(
                EffectTarget.RUN,
                EffectTestBattle.Context(hero, hero, grunt)));

        thrown.Token.ShouldBe(nameof(EffectTarget.RUN));
        thrown.Message.ShouldContain("18 §2.5", Case.Sensitive);
    }

    // ------------------------------------------------------------------ the floor (steering S3)

    /// <summary>
    /// 🔒 Every one of `18` §5's eleven tokens is handled — none falls through to an unhandled switch
    /// arm, and no token was added without a resolution.
    /// </summary>
    /// <remarks>
    /// Driven by <c>Enum.GetValues</c>, so its subject set could silently empty; its floor is the count
    /// pin in <c>EffectVocabularyCountTests</c>, re-asserted below so the dependency is visible from
    /// here rather than only from the other file.
    /// </remarks>
    [Fact]
    public void Every_one_of_the_eleven_targets_resolves_or_states_why_it_cannot()
    {
        var tokens = Enum.GetValues<EffectTarget>();
        tokens.Length.ShouldBe(11, "18 §11: '11 targets = 9 + OTHER_ENEMIES + OWNER'");

        var hero = EffectTestBattle.Hero();
        var pet = EffectTestBattle.Pet("PET_STORMFANG", 1);
        var elite = EffectTestBattle.Enemy("ELITE_HITTER", 2) with { IsElite = true };
        var sporeling = EffectTestBattle.Enemy("SUMMON_SPORELING", 3) with
        {
            IsSummon = true,
            OwnerId = "ELITE_HITTER",
        };

        // Everything a token could ask for is present, so the ONLY reason a token can fail here is
        // that it has no resolution at all.
        var fullyPopulated = new EffectEvaluationContext
        {
            Holder = hero,
            CurrentTarget = elite,
            Attacker = elite,
            Actors = new IEffectActorView[] { hero, pet, elite, sporeling },
            BattleTimeSeconds = 12.0,
            EnrageAtSeconds = EffectTestBattle.EnrageSeconds,
            FightHorizonSeconds = EffectTestBattle.PveTimeoutSeconds,
            Run = EffectTestBattle.Run(),
            Rng = EffectTestBattle.CombatRng(3),
        };

        var unhandled = new List<string>();

        foreach (var token in tokens)
        {
            try
            {
                TargetResolver.Resolve(token, fullyPopulated).ShouldNotBeNull();
            }
            catch (EffectContextException)
            {
                // RUN is the one token with no actor resolver by design, and it announces itself
                // with the suite's own exception rather than a NullReferenceException or a silent
                // empty set. Anything else reaching here is a token that lost its resolution.
                if (token is not EffectTarget.RUN)
                {
                    unhandled.Add($"{token} threw on a fully-populated context");
                }
            }
            catch (Exception e)
            {
                unhandled.Add($"{token} threw {e.GetType().Name}: {e.Message}");
            }
        }

        unhandled.ShouldBeEmpty();
    }
}
