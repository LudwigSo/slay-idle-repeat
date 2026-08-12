using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Targeting;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Targeting;

/// <summary>
/// 🔒 The uniform rule for a `18` §5 token that cannot be answered, and the floor under the eleven
/// tokens themselves.
/// </summary>
/// <remarks>
/// <para>
/// `18` §5 authors a degradation for exactly two of its eleven targets — <c>OTHER_ENEMIES</c>
/// degrades, <c>OWNER</c> is skipped — and those two are pinned in
/// <see cref="TargetResolverTests"/>. For the other nine it authors nothing, and steering S6 forbids
/// inventing a rule to fill the gap. The rule chosen is therefore stated once and applied uniformly:
/// </para>
/// <para>
/// 🔒 <b>A token whose SUBJECT is absent from the context throws
/// <see cref="EffectContextException"/>; a token whose subject is present but whose SET is empty
/// resolves to the empty set.</b>
/// </para>
/// <para>
/// The empty-set half is covered in <see cref="TargetResolverTests"/>. This file covers the throwing
/// half, and pins <em>which</em> token failed rather than merely that something did (steering S2) —
/// several independent absences produce the same exception type, so the type alone would not
/// distinguish them.
/// </para>
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
    /// ⚠️ <b>This was a failure in the first draft of this suite, and the reasoning that changed it
    /// is worth keeping.</b> The argument for throwing was that a PvE enemy side holds no hero and
    /// therefore <em>cannot</em> hold a pet, so the token is meaningless there. But "a side with no
    /// hero can hold no pets" appears in no document: `18` §5 names <c>ALL_PETS</c> once, in its token
    /// list, with no semantics at all. Inventing that inference to justify a throw is exactly the hole
    /// steering S6 forbids filling — and it would crash a battle over any boss effect authored with
    /// <c>target: "ALL_PETS"</c>. The subject of the token is the holder's side, which is always
    /// present; only the set is empty.
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
    /// 🔒 `18` §5 declares <c>RUN</c> — <em>"the run itself, for board ops"</em> — and `18` §2.5 rules
    /// that run and board ops <em>"are resolved by the run controller, never by the combat
    /// simulator"</em>: the simulator appends <c>RunEffectQueued</c> and the controller applies the
    /// queue when the battle resolves.
    /// </summary>
    /// <remarks>
    /// A declared token with no resolver is the correct end state here — the run controller is M3's
    /// and the <c>Run</c> aggregate is M1-05's, and a placeholder resolver would be a guess at both.
    /// The throw is what makes "nobody wired this yet" impossible to mistake for "this resolved to
    /// nobody".
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
    /// arm, and no token was added to <see cref="EffectTarget"/> without a resolution.
    /// </summary>
    /// <remarks>
    /// Steering S3: this rule is driven by <c>Enum.GetValues</c>, so its subject set could silently
    /// empty. Its floor is the count pin in
    /// <c>EffectVocabularyCountTests.There_are_11_targets</c>, which is an equality against `18` §11's
    /// <em>"11 targets = 9 + <c>OTHER_ENEMIES</c> + <c>OWNER</c>"</em>; deleting members there is a
    /// build failure, so this rule cannot quantify over an emptied enum. The count is re-asserted
    /// below so that the dependency is visible from here rather than only from the other file.
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
            Run = new RunStateReading(),
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
