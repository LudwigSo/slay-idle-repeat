using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content.Effects;

/// <summary>The eight-part effect shape, and the id ruling.</summary>
public sealed class EffectDefinitionTests
{
    [Fact]
    public void The_canonical_effect_of_section_1_round_trips()
    {
        var effect = new EffectDefinition
        {
            Id = "PK_SHARP_EDGE_T1_ATK",
            Op = EffectOp.STAT_ADD_PCT,
            Stat = StatSelector.Of(StatId.ATK),
            Value = 0.12,
            ValueScale = null,
            Trigger = new EffectTrigger { Kind = TriggerKind.ALWAYS },
            Condition = null,
            Target = EffectTarget.SELF,
            Duration = null,
            Stacking = new EffectStacking { Mode = StackingMode.ADDITIVE, MaxStacks = 1 },
            Tags = ["offense"],
        };

        effect.Op.ShouldBe(EffectOp.STAT_ADD_PCT);
        effect.Family.ShouldBe(EffectOpFamily.STAT);
        effect.Tags.ShouldBe(["offense"]);
        effect.Stacking!.MaxStacks.ShouldBe(1);
    }

    /// <summary>
    /// The ruling on a field the vocabulary never writes: the id is authored and required.
    /// A record with <c>required</c> members cannot be constructed without it, so "derived from
    /// array position" is not a shortcut anyone can take by accident.
    /// </summary>
    [Fact]
    public void Id_and_op_are_the_only_required_parts_and_the_id_is_one_of_them()
    {
        var required = typeof(EffectDefinition)
            .GetProperties()
            .Where(p => p.GetCustomAttributes(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute), false).Length > 0)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        required.ShouldBe(
            ["Id", "Op"],
            "18 §1 names an eight-part shape, but 18 §9.1 and §7.7 author effects with no trigger " +
            "and §7.4-§7.8 author effects with no target, and 18 states no default for either. " +
            "Manufacturing one here would be the invisible hole game-data/README.md warns about.");
    }

    /// <summary>Parts left out of an effect are <c>null</c>, not defaulted — a hole that's null is greppable.</summary>
    [Fact]
    public void An_effect_with_no_trigger_or_target_carries_nulls_rather_than_manufactured_defaults()
    {
        var effect = new EffectDefinition
        {
            Id = "CP_GLASS_HEART_MULT",
            Op = EffectOp.STAT_MULT,
            Stat = StatSelector.AllCombat,
            Value = 2.0,
        };

        effect.Trigger.ShouldBeNull();
        effect.Target.ShouldBeNull();
        effect.Condition.ShouldBeNull();
        effect.Duration.ShouldBeNull();
        effect.Stacking.ShouldBeNull();
        effect.ValueScale.ShouldBeNull();
        effect.Value.ShouldBe(2.0);
        effect.Tags.ShouldBeEmpty();
    }

    /// <summary>Built-in enrage, expressed with no new concept — if it needed one, the vocabulary would be defective.</summary>
    [Fact]
    public void SYS_ENRAGE_is_expressible_with_no_concept_the_DSL_lacks()
    {
        var enrage = new EffectDefinition
        {
            Id = "SYS_ENRAGE",
            Op = EffectOp.STAT_MULT,
            Stat = StatSelector.Of(StatId.ATK),
            Value = 1.08,
            Trigger = new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 1.0, StartDelay = 70.0 },
            Target = EffectTarget.SELF,
            Duration = new EffectDuration { Scope = DurationScope.BATTLE },
            Stacking = new EffectStacking { Mode = StackingMode.MULTIPLICATIVE, MaxStacks = null },
        };

        enrage.Trigger!.Interval.ShouldBe(1.0);
        enrage.Trigger.StartDelay.ShouldBe(70.0);
        enrage.Stacking!.Mode.ShouldBe(StackingMode.MULTIPLICATIVE);
        enrage.Stacking.MaxStacks.ShouldBeNull("uncapped — 05 §3.1");
        enrage.Duration!.Scope.ShouldBe(DurationScope.BATTLE);
    }

    /// <summary>A duration carrying both a timer and an early terminator: <c>until</c> fields fire whichever comes first.</summary>
    [Fact]
    public void A_duration_may_carry_both_a_timer_and_a_terminator()
    {
        var ossify = new EffectDefinition
        {
            Id = "BOSS_OSSUARY_KING_OSSIFY_DR",
            Op = EffectOp.STAT_ADD_PCT,
            Stat = StatSelector.Of(StatId.DR_PCT),
            Value = 0.30,
            Trigger = new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 14.0 },
            Target = EffectTarget.SELF,
            Duration = new EffectDuration
            {
                Seconds = 6.0,
                Scope = DurationScope.BATTLE,
                Until = DurationTerminator.WARD_BROKEN,
            },
        };

        ossify.Duration!.Seconds.ShouldBe(6.0);
        ossify.Duration.Until.ShouldBe(DurationTerminator.WARD_BROKEN);
    }

    /// <summary>A duration scope with no timer at all.</summary>
    [Fact]
    public void A_duration_may_carry_a_scope_with_no_timer()
    {
        var bogAir = new EffectDefinition
        {
            Id = "BOSS_GULGROT_BOG_AIR",
            Op = EffectOp.STAT_ADD_PCT,
            Stat = StatSelector.Of(StatId.HEAL_PCT),
            Value = -0.35,
            Trigger = new EffectTrigger { Kind = TriggerKind.ON_PHASE_ENTER, Phase = 2 },
            Target = EffectTarget.ALL_ENEMIES,
            Duration = new EffectDuration { Scope = DurationScope.PHASE },
        };

        bogAir.Duration!.Seconds.ShouldBeNull();
        bogAir.Value.ShouldBe(-0.35, "a value may be negative — this one is a debuff");
    }

    /// <summary>Records compare by value, which is what lets a parity or determinism test compare two builds.</summary>
    [Fact]
    public void Two_effects_with_the_same_parts_are_equal()
    {
        var a = new EffectDefinition { Id = "PK_X", Op = EffectOp.EXTRA_ATTACK, Value = 1 };
        var b = new EffectDefinition { Id = "PK_X", Op = EffectOp.EXTRA_ATTACK, Value = 1 };

        a.ShouldBe(b);
        (a with { Id = "PK_Y" }).ShouldNotBe(b);
    }
}
