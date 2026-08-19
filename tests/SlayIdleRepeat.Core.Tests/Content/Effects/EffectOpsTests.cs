using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content.Effects;

/// <summary>The op → family map, and the run/board boundary it draws.</summary>
public sealed class EffectOpsTests
{
    /// <summary>
    /// <see cref="EffectOps.FamilyOf"/> needs a default arm because C# requires one for an enum, so
    /// a 45th op would fall into it and throw at run time rather than fail to build. This enumerates
    /// the enum and is what actually catches it.
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
        EffectOps.All.ShouldNotBeEmpty("an emptied enum would pass the loop above vacuously");
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
}
