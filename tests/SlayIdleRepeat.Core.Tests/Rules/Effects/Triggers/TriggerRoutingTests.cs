using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Triggers;

/// <summary>The combat-context exception: a combat trigger carrying a run/board op emits, it never resolves.</summary>
/// <remarks>
/// The sanctioned case is the Dicelord's Scramble — a <c>PERIODIC</c> firing <c>MODIFY_DIE_FACE</c>.
/// The simulator appends a <c>RunEffectQueued</c> event; in a duel the queue is discarded.
/// </remarks>
public sealed class TriggerRoutingTests
{
    /// <summary>Scramble fires from a <c>PERIODIC</c>, is queued, and nothing about it is resolved in the battle.</summary>
    [Fact]
    public void A_combat_trigger_carrying_a_run_op_emits_rather_than_resolves()
    {
        var registry = TriggerTestBattle.Registry();
        var sink = new RecordingRunEffectSink();
        var dicelord = TriggerTestBattle.Boss();
        var id = TriggerTestBattle.Instance("BOSS#0/BOSS_DICELORD_P2_SCRAMBLE");

        var phaseTwoEntry = TriggerTestBattle.At(18.0);
        registry.Register(id, TriggerTestBattle.Scramble(), phaseTwoEntry);

        var firing = TriggerTestBattle.At(32.0);
        var due = registry.PeriodicDue(new[] { id }, firing);

        due.Count.ShouldBe(1, "PERIODIC 14s anchored at phase 2 entry fires 14 s later (R8)");

        var routing = TriggerRouting.Route(
            due[0].Effect,
            new TriggerOccurrence { Kind = TriggerKind.PERIODIC, Tick = firing },
            TriggerLayer.COMBAT,
            dicelord,
            sink,
            argument: 4.0);

        routing.ShouldBe(EffectRouting.QUEUE_FOR_RUN);

        sink.Queued.ShouldHaveSingleItem();
        sink.Queued[0].Tick.ShouldBe(firing);
        sink.Queued[0].EffectId.ShouldBe("BOSS_DICELORD_P2_SCRAMBLE");
        sink.Queued[0].SourceId.ShouldBe("BOSS");
        sink.Queued[0].Argument.ShouldBe(4.0);
    }

    /// <summary>In a duel the queue is discarded — nothing is emitted and nothing is resolved. A duel has no run to apply anything to.</summary>
    [Fact]
    public void In_a_duel_the_queued_run_op_is_discarded()
    {
        var sink = new RecordingRunEffectSink();

        var routing = TriggerRouting.Route(
            TriggerTestBattle.Scramble(),
            new TriggerOccurrence { Kind = TriggerKind.PERIODIC, Tick = 40, IsPvp = true },
            TriggerLayer.COMBAT,
            TriggerTestBattle.Boss(),
            sink);

        routing.ShouldBe(EffectRouting.DISCARDED_IN_A_DUEL);
        sink.Queued.ShouldBeEmpty();
    }

    /// <summary>A run trigger carrying the same op is resolved, not queued — <c>TILE_DICE_FORGE</c> is already on the run layer.</summary>
    /// <remarks>
    /// This is the arm that is easy to collapse: a rule stated as "a run op is always queued" would
    /// post <c>TILE_DICE_FORGE</c> a letter to the room it is standing in, and M3 would apply it
    /// twice or not at all.
    /// </remarks>
    [Fact]
    public void A_run_trigger_carrying_a_run_op_resolves()
    {
        var sink = new RecordingRunEffectSink();

        var diceForge = new EffectDefinition
        {
            Id = "TILE_DICE_FORGE_REPLACE",
            Op = EffectOp.MODIFY_DIE_FACE,
            Trigger = new EffectTrigger { Kind = TriggerKind.ON_TILE_RESOLVED, TileType = "TILE_DICE_FORGE" },
            Target = EffectTarget.RUN,
            FaceIndex = DieFaceIndex.PlayerChoice,
            NewFace = new DieFaceSpec("Pip", 4),
            Duration = new EffectDuration { Scope = DurationScope.RUN },
        };

        TriggerRouting.Route(
            diceForge,
            new TriggerOccurrence { Kind = TriggerKind.ON_TILE_RESOLVED, Tick = 0 },
            TriggerLayer.RUN,
            TriggerTestBattle.Hero(),
            sink).ShouldBe(EffectRouting.RESOLVE);

        sink.Queued.ShouldBeEmpty();
    }

    /// <summary>
    /// An <c>ALWAYS</c> perk carrying a run/board op is routed by the caller's layer, not by its
    /// trigger's — because the passive kind has no layer of its own.
    /// </summary>
    /// <remarks>
    /// <c>MODIFY_SHOP</c> and <c>MODIFY_DROP_TABLE</c> are exactly the shape a perk authors as
    /// <c>{"kind":"ALWAYS"}</c>. Read off the trigger, <c>ALWAYS</c> is not
    /// <see cref="TriggerLayer.RUN"/>, so evaluating one off the board with no sink to hand must
    /// resolve it, not throw.
    /// </remarks>
    [Theory]
    [InlineData(EffectOp.MODIFY_SHOP)]
    [InlineData(EffectOp.MODIFY_DROP_TABLE)]
    public void An_ALWAYS_run_op_is_routed_by_the_callers_layer(EffectOp op)
    {
        var perk = TriggerTestBattle.Effect(
            "PK_HAGGLER_T1",
            new EffectTrigger { Kind = TriggerKind.ALWAYS },
            op);

        TriggerRouting.RouteOf(perk, TriggerLayer.RUN, isPvp: false).ShouldBe(
            EffectRouting.RESOLVE,
            "the run controller resolves its own run ops; there is no battle to queue against");

        TriggerRouting.RouteOf(perk, TriggerLayer.COMBAT, isPvp: false).ShouldBe(
            EffectRouting.QUEUE_FOR_RUN,
            "reached from inside a battle it is the combat-context exception like any other");
    }

    /// <summary>
    /// An effect with no trigger is fired by its wrapper, so the wrapper's layer answers for it —
    /// the fourth arm of the rule, which is easy to collapse into a throw.
    /// </summary>
    /// <remarks>Neither of the authored triggerless effects carries a run op today; what is pinned is that the classifier does not fall over when one does.</remarks>
    [Fact]
    public void A_triggerless_run_op_is_routed_by_the_callers_layer_too()
    {
        var triggerless = new EffectDefinition
        {
            Id = "PET_DICEBEAST_ACTIVE",
            Op = EffectOp.MODIFY_DIE_FACE,
            Target = EffectTarget.RUN,
            FaceIndex = DieFaceIndex.At(1),
            NewFace = new DieFaceSpec("Star"),
        };

        TriggerRouting.RouteOf(triggerless, TriggerLayer.RUN, isPvp: false).ShouldBe(EffectRouting.RESOLVE);
        TriggerRouting.RouteOf(triggerless, TriggerLayer.COMBAT, isPvp: false).ShouldBe(EffectRouting.QUEUE_FOR_RUN);
    }

    /// <summary>The one runtime-resolved argument is rounded to 4 dp on the way to the log.</summary>
    [Fact]
    public void The_queued_argument_is_rounded_to_4_dp()
    {
        var sink = new RecordingRunEffectSink();

        TriggerRouting.Route(
            TriggerTestBattle.Scramble(),
            new TriggerOccurrence { Kind = TriggerKind.PERIODIC, Tick = 40 },
            TriggerLayer.COMBAT,
            TriggerTestBattle.Boss(),
            sink,
            argument: 1.0 / 3.0);

        sink.Queued.ShouldHaveSingleItem();
        sink.Queued[0].Argument.ShouldBe(0.3333);
        Math.Round(sink.Queued[0].Argument, 4).ShouldBe(sink.Queued[0].Argument);
    }

    /// <summary>An ordinary combat op is resolved and never touches the sink.</summary>
    [Fact]
    public void A_combat_trigger_carrying_a_combat_op_resolves()
    {
        var sink = new RecordingRunEffectSink();

        TriggerRouting.Route(
            TriggerTestBattle.ThornmawRoot(),
            new TriggerOccurrence { Kind = TriggerKind.PERIODIC, Tick = 160 },
            TriggerLayer.COMBAT,
            TriggerTestBattle.Boss(),
            sink).ShouldBe(EffectRouting.RESOLVE);

        sink.Queued.ShouldBeEmpty();
    }

    /// <summary>
    /// Every one of the thirteen run/board ops is queued when a combat trigger fires it — the rule
    /// is on the op family, not on the one op the Dicelord happens to use.
    /// </summary>
    /// <remarks>
    /// A rule keyed on <c>MODIFY_DIE_FACE</c> alone would go quiet the moment a boss design reached
    /// for <c>GRANT_CURRENCY</c>, and the simulator would resolve a currency grant mid-fight.
    /// </remarks>
    [Fact]
    public void Every_run_and_board_op_is_queued_when_a_combat_trigger_fires_it()
    {
        var runOps = EffectOps.All.Where(EffectOps.IsRunAndBoard).ToArray();

        runOps.Length.ShouldBe(13, "18 §2.5 tabulates thirteen run and board ops");

        foreach (var op in runOps)
        {
            var effect = TriggerTestBattle.Effect(
                $"BOSS_TEST_{op}",
                new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 10.0 },
                op);

            TriggerRouting.RouteOf(effect, TriggerLayer.COMBAT, isPvp: false).ShouldBe(
                EffectRouting.QUEUE_FOR_RUN,
                $"{op} is a run/board op and the simulator never resolves one");
        }
    }

    /// <summary>A queued op with nowhere to go fails loudly rather than vanishing.</summary>
    /// <remarks>
    /// Dropping it silently would look exactly like the duel case, and Scramble — which persists into
    /// the remainder of the run if the player survives — would simply stop happening.
    /// </remarks>
    [Fact]
    public void A_queued_run_op_with_no_sink_is_refused()
    {
        var failure = Should.Throw<EffectContextException>(() => TriggerRouting.Route(
            TriggerTestBattle.Scramble(),
            new TriggerOccurrence { Kind = TriggerKind.PERIODIC, Tick = 40 },
            TriggerLayer.COMBAT,
            TriggerTestBattle.Boss(),
            sink: null));

        failure.Token.ShouldBe("BOSS_DICELORD_P2_SCRAMBLE");
        failure.Message.ShouldContain("no run-effect sink", Case.Sensitive);
    }

    /// <summary>The sink is declared in <c>Rules.Effects</c> and names nothing from <c>Rules.Combat</c>.</summary>
    /// <remarks>
    /// Asserted on the signature rather than left to the layering rule alone, because the layering
    /// rule is stated over IL references and would go green if the seam were deleted.
    /// </remarks>
    [Fact]
    public void The_sink_hands_over_the_actor_and_the_effect_and_names_no_combat_type()
    {
        var queue = typeof(IRunEffectSink).GetMethod(nameof(IRunEffectSink.QueueRunEffect))!;

        queue.GetParameters().Select(p => p.ParameterType).ShouldBe(
            new[] { typeof(int), typeof(IEffectActorView), typeof(EffectDefinition), typeof(double) });

        typeof(IRunEffectSink).GetMethods().Length.ShouldBe(1, "one member, and it stays one");
    }
}
