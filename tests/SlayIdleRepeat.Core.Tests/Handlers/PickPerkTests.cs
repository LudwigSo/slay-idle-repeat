using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>🔒 M3-06, `14` §2.3 — <c>PICK_PERK</c>, driven through the production dispatch table.</summary>
public sealed class PickPerkTests
{
    private static CommandResult Pick(WorldSlice state, int optionIndex) =>
        SlayIdleRepeat.Core.GameRules.Apply(state, new PickPerkCommand(optionIndex), DraftWorlds.Context);

    [Fact]
    public void A_run_with_no_draft_pending_is_rejected()
    {
        var state = Worlds.InARun();

        var result = Pick(state, 0);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void An_option_index_outside_0_2_is_rejected(int index)
    {
        var state = DraftWorlds.DraftPendingOn();

        var result = Pick(state, index);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    [Fact]
    public void Picking_a_valid_option_closes_the_draft()
    {
        var state = DraftWorlds.DraftPendingOn();

        var result = Pick(state, 0);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.DraftPending.ShouldBeFalse();
    }

    [Fact]
    public void Picking_a_valid_option_grants_or_upgrades_exactly_one_perk()
    {
        var state = DraftWorlds.DraftPendingOn();

        var result = Pick(state, 0);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.DraftedPerks.Tiers.Count.ShouldBe(1,
            "06 §1.1 — taking one option grants or upgrades exactly one perk");
    }

    /// <summary>
    /// 🔒 M3-06 — determinism: picking against the same committed draft-stream position offers the
    /// same three options every time, so the same index always resolves to the same perk.
    /// </summary>
    [Fact]
    public void The_same_option_index_resolves_the_same_perk_every_time()
    {
        var first = Pick(DraftWorlds.DraftPendingOn(), 1);
        var second = Pick(DraftWorlds.DraftPendingOn(), 1);

        first.Accepted.ShouldBeTrue();
        second.Accepted.ShouldBeTrue();
        first.NewState.Run!.DraftedPerks.Tiers.Keys.ShouldBe(second.NewState.Run!.DraftedPerks.Tiers.Keys);
    }

    /// <summary>
    /// 🔒 M3-06 — GameRules.Execute's DraftPending gate: nothing else is legal while a draft is
    /// open. Uses ROLL_DICE rather than START_BATTLE deliberately: a run standing on no pending
    /// tile would refuse START_BATTLE for its own reason regardless of the gate (StartBattle.Handle's
    /// "no pending tile" check), which would make this pass even with the gate deleted — the exact
    /// "several independent rules can emit the same reason" trap steering S1 warns against. ROLL_DICE
    /// has nothing else in this fixture to refuse it, so ILLEGAL_STATE here can only be the gate.
    /// </summary>
    [Fact]
    public void DraftPending_refuses_every_other_run_command()
    {
        var state = DraftWorlds.DraftPendingOn();

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new RollDiceCommand(), DraftWorlds.Context);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>The negative control for the case above: PICK_PERK itself stays legal while pending.</summary>
    [Fact]
    public void DraftPending_does_not_refuse_PICK_PERK_itself()
    {
        var result = Pick(DraftWorlds.DraftPendingOn(), 0);

        result.Accepted.ShouldBeTrue();
    }
}
