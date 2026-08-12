using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Ops;

/// <summary>🔒 `18` §2.3's six status ops, each on its own number and its own direction.</summary>
public sealed class StatusOpTests
{
    /// <summary>
    /// `18` §7.7 — <c>PET_STORMFANG</c>'s active applies <c>STUN</c> to every enemy. The potency is
    /// the status's own X (`05` §5) and is not reinterpreted by a value mode.
    /// </summary>
    [Fact]
    public void APPLY_STATUS_applies_the_authored_potency_to_every_target_the_token_names()
    {
        var hero = EffectTestBattle.Hero();
        var first = EffectTestBattle.Enemy("EN_1", 1);
        var second = EffectTestBattle.Enemy("EN_2", 2);
        var bench = new OpTestBench();

        var rage = OpFixtures.Effect(
            "BOSS_THORNMAW_P3_RAGE", EffectOp.APPLY_STATUS, 0.30, EffectTarget.ALL_ENEMIES) with
        {
            StatusId = "RAGE",
            Duration = new EffectDuration { Seconds = 999, Scope = DurationScope.BATTLE },
        };

        StatusOps.Apply(rage, bench.Context(EffectTestBattle.Context(hero, hero, first, second)));

        bench.Calls.ShouldBe(
            [
                "Apply:RAGE(EN_1, 0.3, BOSS_THORNMAW_P3_RAGE)",
                "Apply:RAGE(EN_2, 0.3, BOSS_THORNMAW_P3_RAGE)",
            ],
            Case.Sensitive);

        // 🔒 The 18 §6 block travels with the status. Without this the op could pass null and every
        //    RAGE in the game would last a tick.
        bench.Lifetimes.ShouldAllBe(l => l.Duration!.Seconds == 999 && l.Duration.Scope == DurationScope.BATTLE);
        bench.Lifetimes.Count.ShouldBe(2, "ShouldAllBe passes on an empty collection");
    }

    /// <summary>`18` §1.1's <c>valueScale</c> is still refused by the strict reader — M2-06's half.</summary>
    [Fact]
    public void A_status_potency_carrying_a_valueScale_names_M2_06_rather_than_guessing()
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();

        var scaled = OpFixtures.Effect("PK_X", EffectOp.APPLY_STATUS, 0.10, EffectTarget.SELF) with
        {
            StatusId = "RAGE",
            ValueScale = new ValueScale { Fn = ConditionFunction.GOLD_HELD, Per = 100 },
        };

        var thrown = Should.Throw<EffectContextException>(
            () => StatusOps.Apply(scaled, bench.Context(EffectTestBattle.Context(hero, hero))));

        thrown.Message.ShouldContain("M2-06", Case.Sensitive);
    }

    /// <summary>🔒 R12 — <c>REMOVE_STATUS</c>'s <c>statusId</c> form.</summary>
    [Fact]
    public void REMOVE_STATUS_by_statusId_clears_that_one_status()
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();

        var cleanse = OpFixtures.Effect("PK_CLEANSE", EffectOp.REMOVE_STATUS, target: EffectTarget.SELF) with
        {
            StatusId = "FREEZE",
        };

        StatusOps.Remove(cleanse, bench.Context(EffectTestBattle.Context(hero, hero)));

        bench.Calls.ShouldBe(["Remove:FREEZE(HERO, 0, PK_CLEANSE)"], Case.Sensitive);
    }

    /// <summary>
    /// 🔒 R12 — <c>REMOVE_STATUS</c>'s <b>tag-group</b> form goes through a
    /// <see cref="StatusTag"/>, which is a different type from the effect's own author
    /// <see cref="AuthorTag"/>s.
    /// </summary>
    [Fact]
    public void REMOVE_STATUS_by_statusTag_clears_a_STATUS_tag_group_and_not_the_effects_own_tags()
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();

        var cleanse = OpFixtures.Effect("PK_PURIFY", EffectOp.REMOVE_STATUS, target: EffectTarget.SELF) with
        {
            StatusTag = new StatusTag("control"),

            // 🔒 The effect's own tags include the RESERVED ward-bypass marker. It must not be
            //    reachable as a status tag group — that is the whole point of the two types.
            Tags = ["drawback", "defence"],
        };

        StatusOps.Remove(cleanse, bench.Context(EffectTestBattle.Context(hero, hero)));

        bench.Calls.ShouldBe(["RemoveByTag:control(HERO, 0, PK_PURIFY)"], Case.Sensitive);
        EffectTagging.IsDrawback(cleanse).ShouldBeTrue("the author tag is untouched by the status form");
    }

    /// <summary>
    /// `18` §2.3 offers <em>"a status <b>or</b> a tag group"</em>. Neither is an effect that removes
    /// nothing and reports success; both is two removals with no authored precedence.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void REMOVE_STATUS_takes_exactly_one_of_statusId_and_statusTag(bool statusId, bool statusTag)
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();

        var effect = OpFixtures.Effect("PK_X", EffectOp.REMOVE_STATUS, target: EffectTarget.SELF) with
        {
            StatusId = statusId ? "BURN" : null,
            StatusTag = statusTag ? new StatusTag("control") : null,
        };

        var thrown = Should.Throw<EffectContextException>(
            () => StatusOps.Remove(effect, bench.Context(EffectTestBattle.Context(hero, hero))));

        thrown.Message.ShouldContain(statusId ? "names both" : "names neither", Case.Sensitive);
        bench.Calls.ShouldBeEmpty();
    }

    /// <summary>`18` §2.3 — <c>EXTEND_STATUS</c> adds <c>value</c> seconds to a live status.</summary>
    [Fact]
    public void EXTEND_STATUS_adds_its_value_in_seconds()
    {
        var hero = EffectTestBattle.Hero();
        var enemy = EffectTestBattle.Enemy("EN_1", 1);
        var bench = new OpTestBench();

        var extend = OpFixtures.Effect(
            "PK_LINGER", EffectOp.EXTEND_STATUS, 2.5, EffectTarget.ALL_ENEMIES) with
        {
            StatusId = "BURN",
        };

        StatusOps.Extend(extend, bench.Context(EffectTestBattle.Context(hero, hero, enemy)));

        bench.OnlyAmount("Extend:BURN").ShouldBe(2.5);
    }

    /// <summary>An extension of zero or less is a shortening wearing the name of an extension.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    public void EXTEND_STATUS_refuses_a_non_positive_extension(double seconds)
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();

        var extend = OpFixtures.Effect("PK_X", EffectOp.EXTEND_STATUS, seconds, EffectTarget.SELF) with
        {
            StatusId = "BURN",
        };

        Should.Throw<EffectContextException>(
                  () => StatusOps.Extend(extend, bench.Context(EffectTestBattle.Context(hero, hero))))
              .Message.ShouldContain("adds duration", Case.Sensitive);

        bench.Calls.ShouldBeEmpty();
    }

    /// <summary>`18` §2.3 — <c>IMMUNE_STATUS</c> grants immunity to the named status.</summary>
    [Fact]
    public void IMMUNE_STATUS_grants_immunity_to_the_named_status()
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();

        var immunity = OpFixtures.Effect("PK_STEADFAST", EffectOp.IMMUNE_STATUS, target: EffectTarget.SELF) with
        {
            StatusId = "STUN",
            Duration = new EffectDuration { Seconds = 3.0, Scope = DurationScope.BATTLE },
        };

        StatusOps.GrantImmunity(immunity, bench.Context(EffectTestBattle.Context(hero, hero)));

        bench.Calls.ShouldBe(["GrantImmunity:STUN(HERO, 0, PK_STEADFAST)"], Case.Sensitive);

        // 🔒 IMMUNE_STATUS carries no magnitude at all — its ONLY number is how long the immunity
        //    lasts, so a recorder that dropped the duration would leave this op with no numeric
        //    assertion whatever.
        bench.OnlyLifetime("GrantImmunity:STUN").Duration!.Seconds.ShouldBe(3.0);
    }

    /// <summary>
    /// 🔒 The two bulk ops point in <b>opposite</b> directions, and §2.3's two rows are the only
    /// place that is said: <c>STATUS_POWER_PCT</c> scales what the actor <em>applies</em>,
    /// <c>STATUS_DURATION_PCT</c> what is <em>applied to</em> it.
    /// </summary>
    [Fact]
    public void STATUS_POWER_PCT_is_outgoing_and_STATUS_DURATION_PCT_is_incoming()
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();
        var context = bench.Context(EffectTestBattle.Context(hero, hero));

        StatusOps.ScaleOutgoingPower(
            OpFixtures.Effect("TAL_KINDLING", EffectOp.STATUS_POWER_PCT, 0.20, EffectTarget.SELF), context);

        StatusOps.ScaleIncomingDuration(
            OpFixtures.Effect("PK_TENACITY", EffectOp.STATUS_DURATION_PCT, -0.35, EffectTarget.SELF), context);

        bench.Calls.ShouldBe(
            ["ScaleOutgoingPower(HERO, 0.2, TAL_KINDLING)", "ScaleIncomingDuration(HERO, -0.35, PK_TENACITY)"], Case.Sensitive);
    }

    /// <summary>The three status-naming ops refuse an effect with no <c>statusId</c>.</summary>
    [Theory]
    [InlineData(EffectOp.APPLY_STATUS)]
    [InlineData(EffectOp.EXTEND_STATUS)]
    [InlineData(EffectOp.IMMUNE_STATUS)]
    public void A_status_op_with_no_statusId_names_the_op_that_needed_one(EffectOp op)
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();
        var context = bench.Context(EffectTestBattle.Context(hero, hero));
        var effect = OpFixtures.Effect("PK_X", op, 1.0, EffectTarget.SELF);

        var thrown = Should.Throw<EffectContextException>(() => _ = op switch
        {
            EffectOp.APPLY_STATUS => StatusOps.Apply(effect, context),
            EffectOp.EXTEND_STATUS => StatusOps.Extend(effect, context),
            _ => StatusOps.GrantImmunity(effect, context),
        });

        thrown.Message.ShouldContain($"{op} names no statusId", Case.Sensitive);
    }
}
