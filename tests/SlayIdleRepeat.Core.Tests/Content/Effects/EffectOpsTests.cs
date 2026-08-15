using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content.Effects;

/// <summary>The op → family map, and the run/board boundary it draws.</summary>
public sealed class EffectOpsTests
{
    /// <summary>
    /// 🔒 The rule that stands in for the compiler. <see cref="EffectOps.FamilyOf"/> needs a default
    /// arm because C# requires one for an enum, so a 45th op would fall into it and throw at run
    /// time rather than fail to build. This enumerates the enum and is what actually catches it.
    /// </summary>
    [Fact]
    public void Every_op_belongs_to_exactly_one_family()
    {
        var offenders = new List<string>();

        foreach (var op in EffectOps.All)
        {
            try
            {
                _ = EffectOps.FamilyOf(op);
            }
            catch (ArgumentOutOfRangeException)
            {
                offenders.Add($"{op} has no family in EffectOps.FamilyOf");
            }
        }

        offenders.ShouldBeEmpty();

        // S3 — the floor under the loop above. Without it, an emptied enum passes this silently.
        EffectOps.All.Count.ShouldBe(44);
    }

    [Fact]
    public void A_value_that_is_not_a_declared_op_is_rejected_rather_than_given_a_family()
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(() => EffectOps.FamilyOf((EffectOp)999));

        thrown.Message.ShouldContain("18 §10", Case.Sensitive);
    }

    /// <summary>The 13 run and board ops: resolved by the run controller, never by the combat simulator.</summary>
    [Fact]
    public void The_run_and_board_ops_are_exactly_the_thirteen_of_section_2_5()
    {
        EffectOps.All.Where(EffectOps.IsRunAndBoard).ShouldBe(
        [
            EffectOp.GRANT_CURRENCY,
            EffectOp.GRANT_ITEM,
            EffectOp.GRANT_PERK,
            EffectOp.UPGRADE_PERK,
            EffectOp.MODIFY_DIE_FACE,
            EffectOp.GRANT_REROLL,
            EffectOp.MOVE_NODES,
            EffectOp.REVEAL_TILES,
            EffectOp.RESOLVE_TILE_AGAIN,
            EffectOp.MODIFY_SHOP,
            EffectOp.MODIFY_DROP_TABLE,
            EffectOp.APPLY_CURSE,
            EffectOp.CLEANSE_CURSE,
        ]);
    }

    /// <summary>
    /// A combat trigger may carry a run/board op: the sanctioned case is the Dicelord's Scramble
    /// firing <c>MODIFY_DIE_FACE</c> from <c>PERIODIC</c>, and nothing in the vocabulary forbids it.
    /// </summary>
    [Fact]
    public void A_combat_trigger_may_carry_a_run_and_board_op()
    {
        var scramble = new EffectDefinition
        {
            Id = "BOSS_DICELORD_SCRAMBLE",
            Op = EffectOp.MODIFY_DIE_FACE,
            Trigger = new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 15.0 },
            NewFace = new DieFaceSpec("Void"),
        };

        EffectOps.IsRunAndBoard(scramble.Op).ShouldBeTrue();
        scramble.Trigger.Kind.ShouldBe(TriggerKind.PERIODIC);
        scramble.Family.ShouldBe(EffectOpFamily.RUN_AND_BOARD);
    }

    /// <summary>Two combat rulings that arrive as ops rather than code: a targeting weight and a state flag.</summary>
    [Fact]
    public void The_two_17_combat_rulings_are_ordinary_combat_flow_ops()
    {
        EffectOps.FamilyOf(EffectOp.SET_TARGET_PRIORITY).ShouldBe(EffectOpFamily.COMBAT_FLOW);
        EffectOps.FamilyOf(EffectOp.DAMAGE_TAKEN_MULT).ShouldBe(EffectOpFamily.COMBAT_FLOW);
    }
}
