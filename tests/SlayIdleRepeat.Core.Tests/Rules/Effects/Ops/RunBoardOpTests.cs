using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Ops;

/// <summary>The thirteen run and board ops, tested at the boundary: declared, well-formed, and queued rather than resolved.</summary>
/// <remarks>
/// These are resolved by the run controller, never the simulator (18 §2.5), so the whole claim is
/// that reaching one from a combat trigger produces exactly one queue entry and touches nothing else.
/// The sweep enumerates the family rather than listing the ops.
/// </remarks>
public sealed class RunBoardOpTests
{
    /// <summary>
    /// Every run/board op, driven through the resolver — the same entry point a combat trigger
    /// uses — and each one queues, resolves nothing, and reports
    /// <see cref="OpDisposition.QUEUED_FOR_RUN"/>.
    /// </summary>
    [Fact]
    public void Every_run_and_board_op_is_queued_and_never_resolved()
    {
        var runOps = EffectOps.All.Where(EffectOps.IsRunAndBoard).ToArray();

        // The floor under the loop below: an empty family would make every assertion vacuous.
        runOps.Length.ShouldBe(13, "18 §2.5 — twelve table rows, and APPLY_CURSE/CLEANSE_CURSE are two ops");

        foreach (var op in runOps)
        {
            var hero = EffectTestBattle.Hero();
            var bench = new OpTestBench();
            var effect = OpFixtures.Effect($"TILE_{op}", op, 1.0, EffectTarget.RUN);

            var outcome = EffectOpResolver.Resolve(effect, bench.Context(EffectTestBattle.Context(hero, hero)));

            outcome.Disposition.ShouldBe(OpDisposition.QUEUED_FOR_RUN, $"{op} is a 18 §2.5 op");
            outcome.Amount.ShouldBe(0.0, "no §2.5 op resolves a runtime argument in M2");

            bench.Queued.ShouldBe([($"TILE_{op}", op, "HERO", 0.0)]);
            bench.Amounts.ShouldBeEmpty($"{op} must reach no combat seam at all");
        }
    }

    /// <summary>
    /// The sanctioned combat-context exception: the Dicelord's Scramble fires <c>MODIFY_DIE_FACE</c>
    /// from a <c>PERIODIC</c> trigger, and the simulator queues it.
    /// </summary>
    /// <remarks>
    /// This is why <see cref="EffectOps.IsRunAndBoard"/> is a property of the op and never of the
    /// trigger: a rule that forbade a run op on a combat trigger would be wrong.
    /// </remarks>
    [Fact]
    public void A_run_op_on_a_COMBAT_trigger_is_queued_rather_than_resolved()
    {
        var dicelord = EffectTestBattle.Enemy("BOSS_DICELORD", 1);
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();

        var scramble = OpFixtures.Effect(
            "BOSS_DICELORD_SCRAMBLE", EffectOp.MODIFY_DIE_FACE, target: EffectTarget.RUN) with
        {
            Trigger = new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 10.0 },
            NewFace = new DieFaceSpec("Void"),
            FaceIndex = DieFaceIndex.PlayerChoice,
        };

        var outcome = EffectOpResolver.Resolve(
            scramble, bench.Context(EffectTestBattle.Context(dicelord, dicelord, hero)));

        outcome.Disposition.ShouldBe(OpDisposition.QUEUED_FOR_RUN);
        bench.Queued.ShouldBe([("BOSS_DICELORD_SCRAMBLE", EffectOp.MODIFY_DIE_FACE, "BOSS_DICELORD", 0.0)]);
        bench.Calls.ShouldBe(["Queue(BOSS_DICELORD_SCRAMBLE, MODIFY_DIE_FACE)"], Case.Sensitive);
    }

    /// <summary>With no queue supplied, a run/board op throws naming M3, and never silently does nothing.</summary>
    /// <remarks>
    /// "Declared but not wired" is only a correct end state if reaching one without a queue is loud —
    /// a no-op default would make an unwired run op indistinguishable from a resolved one with no effect.
    /// </remarks>
    [Fact]
    public void A_run_op_with_no_queue_supplied_names_the_run_controller_rather_than_no_opping()
    {
        var hero = EffectTestBattle.Hero();

        var grant = OpFixtures.Effect("TILE_TREASURE_GOLD", EffectOp.GRANT_CURRENCY, 250.0, EffectTarget.RUN);

        var context = new EffectOpContext
        {
            Evaluation = EffectTestBattle.Context(hero, hero),
            Seams = EffectOpSeams.Strict,
        };

        var thrown = Should.Throw<EffectContextException>(() => EffectOpResolver.Resolve(grant, context));

        thrown.Token.ShouldBe("TILE_TREASURE_GOLD");
        thrown.Message.ShouldContain("never by the combat simulator", Case.Sensitive);
        thrown.Message.ShouldContain("M3", Case.Sensitive);
    }

    /// <summary>
    /// Every run/board op with only the eight-part shape is well-formed — the whole of what can be
    /// asserted while their arguments are unauthored.
    /// </summary>
    /// <remarks>This says the ops are authorable today, not that they are complete.</remarks>
    [Fact]
    public void Every_run_and_board_op_is_authorable_with_the_eight_part_shape_alone()
    {
        var offenders = new List<string>();
        var runOps = EffectOps.All.Where(EffectOps.IsRunAndBoard).ToArray();

        // The floor: EffectOps.FamilyOf is a hand-written switch; edit one arm and this loop runs
        // zero times and reports success over nothing.
        runOps.Length.ShouldBe(13, "18 §2.5");

        foreach (var op in runOps)
        {
            var effect = OpFixtures.Effect($"TILE_{op}", op, 1.0, EffectTarget.RUN) with
            {
                // MODIFY_DIE_FACE is the one run/board op with authored keys, and it needs its replacement.
                NewFace = op == EffectOp.MODIFY_DIE_FACE ? new DieFaceSpec("Star") : null,
            };

            offenders.AddRange(EffectOpValidation.Problems(effect).Select(p => $"{op}: {p}"));
        }

        offenders.ShouldBeEmpty();
    }
}
