using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>USE_CONSUMABLE: `03` §7.1's two held consumables, and the four ways a use is refused.</summary>
public sealed class UseConsumableTests
{
    private static CommandResult Use(WorldSlice state, string consumableId) =>
        SlayIdleRepeat.Core.GameRules.Apply(
            state, new UseConsumableCommand(consumableId), TileWorlds.Context);

    /// <summary>A run between rolls, holding one of each held consumable.</summary>
    private static WorldSlice OnTheBoard(int currentHp = 40, bool ropeArmed = false) =>
        TileWorlds.OnNoTileHolding(
            currentHp: currentHp,
            consumables: new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [Consumables.HealthDraught] = 1,
                [Consumables.EscapeRope] = 1,
            },
            escapeRopeArmed: ropeArmed);

    [Fact]
    public void A_draught_heals_and_is_spent()
    {
        var result = Use(OnTheBoard(currentHp: 40), Consumables.HealthDraught);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.CurrentHp.ShouldBe(70, "30% of a 100 Max HP bar, on top of 40.");
        result.NewState.Run!.ConsumableCount(Consumables.HealthDraught).ShouldBe(0);
        result.NewState.Run!.ConsumableCount(Consumables.EscapeRope).ShouldBe(
            1, "…and only the one named is spent.");
    }

    /// <summary>
    /// 🔒 `03` §7.1: "disabled at full HP — a draught can never be wasted by a mis-tap". The stack is
    /// still there afterwards, which is the half that makes it a refusal rather than a silent no-op.
    /// </summary>
    [Fact]
    public void A_draught_is_refused_at_full_health_and_not_spent()
    {
        var result = Use(OnTheBoard(currentHp: 100), Consumables.HealthDraught);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        result.NewState.Run!.ConsumableCount(Consumables.HealthDraught).ShouldBe(1);
    }

    [Fact]
    public void A_rope_arms_and_is_spent()
    {
        var result = Use(OnTheBoard(), Consumables.EscapeRope);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.EscapeRopeArmed.ShouldBeTrue();
        result.NewState.Run!.ConsumableCount(Consumables.EscapeRope).ShouldBe(0);
    }

    /// <summary>Only one rope may be armed (`03` §7.1), so a second is refused rather than destroyed.</summary>
    [Fact]
    public void A_second_rope_is_refused_while_one_is_armed()
    {
        var result = Use(OnTheBoard(ropeArmed: true), Consumables.EscapeRope);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        result.NewState.Run!.ConsumableCount(Consumables.EscapeRope).ShouldBe(1);
    }

    /// <summary>A consumable the run does not hold is refused.</summary>
    [Fact]
    public void A_consumable_the_run_does_not_hold_is_refused()
    {
        var empty = TileWorlds.OnNoTileHolding(currentHp: 40);

        Use(empty, Consumables.HealthDraught).Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>
    /// The token consumable is never held, so naming it is the same refusal as naming a Draught
    /// the run has none of.
    /// </summary>
    [Theory]
    [InlineData(Consumables.DraftToken)]
    [InlineData("CON_NOT_A_THING")]
    public void A_consumable_that_is_never_held_is_refused(string consumableId)
    {
        Use(OnTheBoard(), consumableId).Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>
    /// 🔒 Board-only: `03` §7.1 allows use in <c>AWAIT_ROLL</c> alone — never on an unresolved tile,
    /// never at a paused junction.
    /// </summary>
    [Fact]
    public void A_use_is_refused_on_an_unresolved_tile()
    {
        var onATile = TileWorlds.OnTile(
            TileKind.Event,
            currentHp: 40,
            consumables: new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [Consumables.HealthDraught] = 1,
            });

        Use(onATile, Consumables.HealthDraught).Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>A run-less slice still throws, the same guard every other run command hits.</summary>
    [Fact]
    public void A_run_less_slice_throws_rather_than_rejects()
    {
        Should.Throw<InvalidOperationException>(() =>
                SlayIdleRepeat.Core.GameRules.Apply(
                    Worlds.OutsideARun(),
                    new UseConsumableCommand(Consumables.HealthDraught),
                    Worlds.Context))
            .Message.ShouldContain("USE_CONSUMABLE", Case.Sensitive);
    }
}
