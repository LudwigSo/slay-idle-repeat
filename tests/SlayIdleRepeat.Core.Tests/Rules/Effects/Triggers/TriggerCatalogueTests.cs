using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Triggers;

/// <summary>The 23 trigger kinds — the catalogue, its floor, and the parameter partition it enforces on triggers no schema ever saw.</summary>
public sealed class TriggerCatalogueTests
{
    /// <summary>The floor under every rule stated over the catalogue.</summary>
    /// <remarks>
    /// Checked in both directions and against the enum, not against a literal alone. A catalogue that
    /// lost a row would leave every other rule in this file passing over the rows that remain; a
    /// catalogue with a row the enum does not declare would describe a trigger nothing can author.
    /// </remarks>
    [Fact]
    public void The_catalogue_holds_all_23_trigger_kinds_and_no_others()
    {
        TriggerCatalogue.All.Count.ShouldBe(
            TriggerCatalogue.TriggerKindCount,
            "18 §11: '23 triggers = 21 + ON_DEATH + ON_REVIVE'");

        TriggerCatalogue.TriggerKindCount.ShouldBe(23);

        TriggerCatalogue.All.ShouldBe(Enum.GetValues<TriggerKind>(), ignoreOrder: true);

        // Every declared kind has a row — FactsOf throws for one that does not.
        foreach (var kind in Enum.GetValues<TriggerKind>())
        {
            Should.NotThrow(() => TriggerCatalogue.FactsOf(kind));
        }
    }

    /// <summary>An undeclared kind is refused rather than answered with an empty row.</summary>
    [Fact]
    public void A_kind_outside_the_23_is_refused()
    {
        var failure = Should.Throw<EffectContextException>(
            () => TriggerCatalogue.FactsOf((TriggerKind)99));

        failure.Message.ShouldContain("99 is not one of", Case.Sensitive);
        failure.Message.ShouldContain(EffectContextException.Marker, Case.Sensitive);
    }

    /// <summary>The six run-layer kinds — declared, validated and unit-tested here, fired by the run controller and nothing else.</summary>
    [Theory]
    [InlineData(TriggerKind.ON_TILE_RESOLVED)]
    [InlineData(TriggerKind.ON_ROLL)]
    [InlineData(TriggerKind.ON_PERK_TAKEN)]
    [InlineData(TriggerKind.ON_STAGE_GATE)]
    [InlineData(TriggerKind.ON_RUN_START)]
    [InlineData(TriggerKind.ON_RUN_END)]
    public void The_six_run_layer_kinds_are_declared_as_run_layer(TriggerKind kind)
    {
        TriggerCatalogue.LayerOf(kind).ShouldBe(TriggerLayer.RUN);
    }

    /// <summary>Exactly six are run-layer, one is passive, and the remaining sixteen are combat triggers.</summary>
    /// <remarks>
    /// The counts are asserted rather than the membership alone, so that moving a kind between layers
    /// is a failure here rather than a silent change to which loop fires it.
    /// </remarks>
    [Fact]
    public void The_layers_partition_the_23_kinds_six_one_and_sixteen()
    {
        var byLayer = TriggerCatalogue.All.GroupBy(TriggerCatalogue.LayerOf)
            .ToDictionary(g => g.Key, g => g.Count());

        byLayer[TriggerLayer.RUN].ShouldBe(6, "the kickoff's A4 six: tile, roll, perk, stage, run start, run end");
        byLayer[TriggerLayer.PASSIVE].ShouldBe(1, "18 §3: ALWAYS alone is 'passive, always active'");
        byLayer[TriggerLayer.COMBAT].ShouldBe(16, "23 - 6 - 1");
    }

    /// <summary>
    /// Every parameter <see cref="EffectTrigger"/> declares is admitted by at least one kind, and
    /// every parameter a kind admits is one <see cref="EffectTrigger"/> declares.
    /// </summary>
    /// <remarks>
    /// The two are separate statements of the same partition — the record's property list and this
    /// catalogue — and nothing else compares them. A parameter added to <c>EffectTrigger</c> and
    /// given to no kind is a key that silently means nothing.
    /// </remarks>
    [Fact]
    public void The_parameter_set_is_exactly_the_one_EffectTrigger_declares()
    {
        var admitted = TriggerCatalogue.All
            .Select(k => TriggerCatalogue.FactsOf(k).Admits)
            .Aggregate(TriggerParameter.NONE, (all, next) => all | next);

        var unused = Enum.GetValues<TriggerParameter>()
            .Where(p => p != TriggerParameter.NONE && !admitted.HasFlag(p))
            .ToArray();

        unused.ShouldBeEmpty("a parameter no kind admits is a key nothing can ever author");

        // Floored against EffectTrigger's own property list: Kind plus the twelve parameters.
        typeof(EffectTrigger).GetProperties().Length.ShouldBe(
            13,
            "18 §3 gives its kinds twelve parameters, and EffectTrigger carries Kind as well");
    }

    /// <summary><c>ON_ATTACK</c> takes <c>chance</c>; <c>ON_KILL</c> does not. The extension and its boundary, in one assertion.</summary>
    [Fact]
    public void R11_gives_ON_ATTACK_a_chance_and_ON_KILL_none()
    {
        var attack = TriggerCatalogue.FactsOf(TriggerKind.ON_ATTACK);
        var kill = TriggerCatalogue.FactsOf(TriggerKind.ON_KILL);

        attack.Admits.ShouldBe(TriggerParameter.EVERY_NTH | TriggerParameter.CHANCE);
        kill.Admits.ShouldBe(TriggerParameter.EVERY_NTH);

        Should.NotThrow(() => TriggerCatalogue.Validate(
            new EffectTrigger { Kind = TriggerKind.ON_ATTACK, Chance = 0.25, EveryNth = 5 }));

        Should.Throw<EffectContextException>(() => TriggerCatalogue.Validate(
            new EffectTrigger { Kind = TriggerKind.ON_KILL, Chance = 0.25 }));
    }

    /// <summary>The Ossuary King's "ON_HP_THRESHOLD 1%" is spelled as an <c>ON_LOW_HP</c> — there is no 24th trigger kind for it.</summary>
    [Fact]
    public void R9_spells_ON_HP_THRESHOLD_as_ON_LOW_HP()
    {
        Enum.GetNames<TriggerKind>().ShouldNotContain("ON_HP_THRESHOLD");

        var riseAgain = TriggerTestBattle.RiseAgain().Trigger!;

        riseAgain.Kind.ShouldBe(TriggerKind.ON_LOW_HP);
        riseAgain.Threshold.ShouldBe(0.01);
        riseAgain.Once.ShouldBe(true);

        Should.NotThrow(() => TriggerCatalogue.Validate(riseAgain));
    }

    [Fact]
    public void R2_keeps_once_a_boolean_on_the_two_kinds_that_take_it()
    {
        typeof(EffectTrigger).GetProperty(nameof(EffectTrigger.Once))!
            .PropertyType.ShouldBe(typeof(bool?));

        TriggerCatalogue.All
            .Where(k => TriggerCatalogue.FactsOf(k).Admits.HasFlag(TriggerParameter.ONCE))
            .ShouldBe(new[] { TriggerKind.ON_LOW_HP, TriggerKind.ON_LETHAL }, ignoreOrder: true);
    }

    /// <summary>A parameter on a kind that does not admit it is refused, and the failure names the parameter — not merely "invalid".</summary>
    /// <remarks>Each row carries its own control below, so what fired is the partition and not the kind.</remarks>
    [Theory]
    [InlineData(TriggerKind.ALWAYS, "chance")]
    [InlineData(TriggerKind.ON_HIT, "cooldown")]
    [InlineData(TriggerKind.ON_KILL, "interval")]
    [InlineData(TriggerKind.PERIODIC, "everyNth")]
    [InlineData(TriggerKind.ON_DEATH, "once")]
    [InlineData(TriggerKind.ON_BATTLE_START, "phase")]
    [InlineData(TriggerKind.ON_ROLL, "tileType")]
    public void A_parameter_on_a_kind_that_does_not_take_it_is_refused(TriggerKind kind, string parameter)
    {
        Should.NotThrow(
            () => TriggerCatalogue.Validate(Bare(kind)),
            $"the control: {kind} with only its constitutive parameters is valid");

        var failure = Should.Throw<EffectContextException>(
            () => TriggerCatalogue.Validate(With(Bare(kind), parameter)));

        failure.Token.ShouldBe(kind.ToString());
        failure.Message.ShouldContain(parameter, Case.Sensitive);
        failure.Message.ShouldContain("does not give it", Case.Sensitive);
    }

    /// <summary>The three constitutive parameters are refused when absent rather than coerced to a plausible value.</summary>
    [Theory]
    [InlineData(TriggerKind.PERIODIC, "interval")]
    [InlineData(TriggerKind.ON_LOW_HP, "threshold")]
    [InlineData(TriggerKind.ON_PHASE_ENTER, "phase")]
    public void A_constitutive_parameter_is_refused_when_absent(TriggerKind kind, string parameter)
    {
        var failure = Should.Throw<EffectContextException>(
            () => TriggerCatalogue.Validate(new EffectTrigger { Kind = kind }));

        failure.Token.ShouldBe(kind.ToString());
        failure.Message.ShouldContain($"carries no {parameter}", Case.Sensitive);
        failure.Message.ShouldContain("S6", Case.Sensitive);
    }

    /// <summary>The narrowing parameters are the complement: absent means "not narrowed", and a kind with none of them written is a valid trigger.</summary>
    /// <remarks>
    /// Derived from the catalogue rather than hand-listed: a kind that gained a constitutive
    /// parameter leaves this theory automatically, and a new kind joins it.
    /// </remarks>
    [Theory]
    [MemberData(nameof(KindsWithNoConstitutiveParameter))]
    public void A_kind_with_only_narrowing_parameters_is_valid_bare(TriggerKind kind)
    {
        TriggerCatalogue.FactsOf(kind).Requires.ShouldBe(TriggerParameter.NONE);

        Should.NotThrow(() => TriggerCatalogue.Validate(new EffectTrigger { Kind = kind }));
    }

    /// <summary>Every fully narrowing kind — valid with nothing but its kind.</summary>
    public static TheoryData<TriggerKind> KindsWithNoConstitutiveParameter
    {
        get
        {
            var data = new TheoryData<TriggerKind>();

            foreach (var kind in TriggerCatalogue.All.Where(
                         k => TriggerCatalogue.FactsOf(k).Requires == TriggerParameter.NONE))
            {
                data.Add(kind);
            }

            return data;
        }
    }

    /// <summary>The floor under both halves of the narrowing/constitutive split: exactly three kinds require a parameter, twenty do not, and together they are the 23.</summary>
    [Fact]
    public void The_two_partitions_of_Requires_cover_all_23()
    {
        var constitutive = TriggerCatalogue.All
            .Where(k => TriggerCatalogue.FactsOf(k).Requires != TriggerParameter.NONE)
            .ToArray();

        constitutive.ShouldBe(
            new[] { TriggerKind.ON_LOW_HP, TriggerKind.PERIODIC, TriggerKind.ON_PHASE_ENTER },
            ignoreOrder: true,
            "18 §3.1: PERIODIC without an interval has no period, ON_LOW_HP without a threshold " +
            "names no crossing, and ON_PHASE_ENTER without a phase cannot say which entry it means");

        KindsWithNoConstitutiveParameter.Count.ShouldBe(
            TriggerCatalogue.TriggerKindCount - constitutive.Length);
    }

    /// <summary>Values outside a parameter's range are refused — a trigger built in code never passed through the schema.</summary>
    [Theory]
    [InlineData(TriggerKind.ON_ATTACK, "everyNth", -1.0)]
    [InlineData(TriggerKind.ON_ATTACK, "everyNth", 0.0)]
    [InlineData(TriggerKind.ON_HIT, "chance", 1.5)]
    [InlineData(TriggerKind.ON_HIT, "chance", -0.1)]
    [InlineData(TriggerKind.ON_HIT, "chance", double.NaN)]
    [InlineData(TriggerKind.ON_DODGE, "cooldown", -1.0)]
    [InlineData(TriggerKind.ON_PHASE_ENTER, "phase", 4.0)]
    [InlineData(TriggerKind.ON_PHASE_ENTER, "phase", 0.0)]
    [InlineData(TriggerKind.PERIODIC, "interval", 0.0)]
    [InlineData(TriggerKind.PERIODIC, "interval", -8.0)]
    [InlineData(TriggerKind.PERIODIC, "startDelay", -1.0)]
    [InlineData(TriggerKind.ON_LOW_HP, "threshold", 1.4)]
    public void A_value_outside_18_3s_range_is_refused(TriggerKind kind, string parameter, double value)
    {
        var trigger = parameter switch
        {
            "everyNth" => new EffectTrigger { Kind = kind, EveryNth = (int)value },
            "chance" => new EffectTrigger { Kind = kind, Chance = value },
            "cooldown" => new EffectTrigger { Kind = kind, Cooldown = value },
            "phase" => new EffectTrigger { Kind = kind, Phase = (int)value },
            "interval" => new EffectTrigger { Kind = kind, Interval = value },
            "startDelay" => new EffectTrigger { Kind = kind, Interval = 1.0, StartDelay = value },
            "threshold" => new EffectTrigger { Kind = kind, Threshold = value },
            _ => throw new InvalidOperationException($"Unhandled parameter '{parameter}'."),
        };

        var failure = Should.Throw<EffectContextException>(() => TriggerCatalogue.Validate(trigger));

        failure.Token.ShouldBe(kind.ToString());

        // The fragment only the RANGE guard emits. Asserting the parameter name alone would also
        // pass for the surplus-parameter guard, which raises the same token and names the same
        // parameter.
        failure.Message.ShouldContain($"its {parameter} is", Case.Sensitive);
    }

    /// <summary><c>{"once": false}</c> is a written parameter, not an absent one — the partition tests presence, never truthiness.</summary>
    /// <remarks>
    /// Without this the check could be written as <c>Once == true</c> and
    /// <c>{"kind":"ALWAYS","once":false}</c> would validate: a key on a kind that does not take it,
    /// admitted because its value happened to be the falsy one. The second half is the control —
    /// a falsy value that <i>is</i> admitted stays admitted, so what fired above is the partition and
    /// not a blanket ban on zeroes.
    /// </remarks>
    [Fact]
    public void A_falsy_parameter_still_counts_as_written()
    {
        Should.Throw<EffectContextException>(() => TriggerCatalogue.Validate(
                  new EffectTrigger { Kind = TriggerKind.ALWAYS, Once = false }))
              .Message.ShouldContain("once", Case.Sensitive);

        Should.Throw<EffectContextException>(() => TriggerCatalogue.Validate(
                  new EffectTrigger { Kind = TriggerKind.ON_ATTACK, OnlyIfWon = false }))
              .Message.ShouldContain("onlyIfWon", Case.Sensitive);

        Should.NotThrow(() => TriggerCatalogue.Validate(
            new EffectTrigger { Kind = TriggerKind.ON_HIT, Chance = 0.0 }));
    }

    /// <summary>A trigger carrying only what its kind needs.</summary>
    private static EffectTrigger Bare(TriggerKind kind) => kind switch
    {
        TriggerKind.PERIODIC => new EffectTrigger { Kind = kind, Interval = 8.0 },
        TriggerKind.ON_LOW_HP => new EffectTrigger { Kind = kind, Threshold = 0.3 },
        TriggerKind.ON_PHASE_ENTER => new EffectTrigger { Kind = kind, Phase = 2 },
        _ => new EffectTrigger { Kind = kind },
    };

    /// <summary>The same trigger with one extra parameter written.</summary>
    private static EffectTrigger With(EffectTrigger trigger, string parameter) => parameter switch
    {
        "chance" => trigger with { Chance = 0.5 },
        "cooldown" => trigger with { Cooldown = 3.0 },
        "interval" => trigger with { Interval = 1.0 },
        "everyNth" => trigger with { EveryNth = 3 },
        "once" => trigger with { Once = true },
        "phase" => trigger with { Phase = 2 },
        "tileType" => trigger with { TileType = "TILE_DICE_FORGE" },
        _ => throw new InvalidOperationException($"Unhandled parameter '{parameter}'."),
    };
}
