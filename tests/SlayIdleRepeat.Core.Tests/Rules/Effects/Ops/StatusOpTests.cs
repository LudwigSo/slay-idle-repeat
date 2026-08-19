using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Ops;

/// <summary>The six status ops, each on its own number and its own direction.</summary>
public sealed class StatusOpTests
{
    /// <summary><c>PET_STORMFANG</c>'s active: potency is the status's own X, not reinterpreted by a value mode.</summary>
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

        // The duration block travels with the status; without it the op could pass null and every
        // RAGE in the game would last a tick.
        bench.Lifetimes.ShouldAllBe(l => l.Duration!.Seconds == 999 && l.Duration.Scope == DurationScope.BATTLE);
        bench.Lifetimes.Count.ShouldBe(2, "ShouldAllBe passes on an empty collection");
    }

    /// <summary><c>valueScale</c> is still refused by the strict reader.</summary>
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

    /// <summary><c>REMOVE_STATUS</c>'s <c>statusId</c> form.</summary>
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
    /// <c>REMOVE_STATUS</c>'s tag-group form goes through a <see cref="StatusTag"/>, a different type
    /// from the effect's own author <see cref="AuthorTag"/>s.
    /// </summary>
    [Fact]
    public void REMOVE_STATUS_by_statusTag_clears_a_STATUS_tag_group_and_not_the_effects_own_tags()
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();

        var cleanse = OpFixtures.Effect("PK_PURIFY", EffectOp.REMOVE_STATUS, target: EffectTarget.SELF) with
        {
            StatusTag = new StatusTag("control"),

            // The effect's own tags include the reserved ward-bypass marker; it must not be
            // reachable as a status tag group.
            Tags = ["drawback", "defence"],
        };

        StatusOps.Remove(cleanse, bench.Context(EffectTestBattle.Context(hero, hero)));

        bench.Calls.ShouldBe(["RemoveByTag:control(HERO, 0, PK_PURIFY)"], Case.Sensitive);
        EffectTagging.IsDrawback(cleanse).ShouldBeTrue("the author tag is untouched by the status form");
    }

    /// <summary>Neither statusId nor statusTag removes nothing and reports success; both is ambiguous with no authored precedence.</summary>
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

        // IMMUNE_STATUS carries no magnitude at all — its only number is how long immunity lasts.
        bench.OnlyLifetime("GrantImmunity:STUN").Duration!.Seconds.ShouldBe(3.0);
    }

    /// <summary>
    /// The two bulk ops point in opposite directions: <c>STATUS_POWER_PCT</c> scales what the actor
    /// applies, <c>STATUS_DURATION_PCT</c> scales what is applied to it.
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

    /// <summary>
    /// The <c>FixedPotency</c> narrowing of the "no value" guard is for exactly that case: a
    /// value-less <c>APPLY_STATUS</c> naming a status with no <c>FixedPotency</c> (BURN, whose X is
    /// authored per effect) is the authoring hole the guard exists to catch, and must still throw.
    /// The happy path is pinned end-to-end in <c>Rules/Combat/Status</c>.
    /// </summary>
    [Fact]
    public void APPLY_STATUS_with_no_value_and_no_FixedPotency_status_still_throws()
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench(); // the bench answers HasFixedPotency false for every status

        var noValueBurn = OpFixtures.Effect("PK_X", EffectOp.APPLY_STATUS, target: EffectTarget.SELF) with
        {
            StatusId = "BURN",
        };

        var thrown = Should.Throw<EffectContextException>(
            () => StatusOps.Apply(noValueBurn, bench.Context(EffectTestBattle.Context(hero, hero))));

        thrown.Message.ShouldContain("PK_X", Case.Sensitive);
        thrown.Message.ShouldContain("no value", Case.Sensitive);
        bench.Calls.ShouldBeEmpty("a status with no FixedPotency must not reach the status engine at all");
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
