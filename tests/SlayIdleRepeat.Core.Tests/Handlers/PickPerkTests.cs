using System.Collections.Generic;
using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Tests.Content.Perks;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>PICK_PERK, driven through the production dispatch table.</summary>
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
            "taking one option grants or upgrades exactly one perk");
    }

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
    /// ROLL_DICE rather than START_BATTLE deliberately: START_BATTLE would refuse for its own reason
    /// (no pending tile) even with the gate deleted, so here ILLEGAL_STATE can only be the gate.
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

    [Fact]
    public void DraftPending_does_not_refuse_PICK_PERK_itself()
    {
        var result = Pick(DraftWorlds.DraftPendingOn(), 0);

        result.Accepted.ShouldBeTrue();
    }

    /// <remarks>The counter starts non-zero so "unchanged" and "reset" are different numbers.</remarks>
    [Fact]
    public void A_draft_no_upgrade_could_have_reached_leaves_the_famine_counter_standing()
    {
        var state = DraftWorlds.DraftPendingOn(draftsWithoutOwnedUpgrade: 2);

        var result = Pick(state, 0);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.DraftsWithoutOwnedUpgrade.ShouldBe(
            2,
            "the run owned no perk at all, so 24 §4.7 F3's 'while the player owns at least one " +
            "non-maxed perk' was false — a counter that advanced here would spend the famine's whole " +
            "allowance on the drafts an upgrade was not yet possible in, and fire it early on every " +
            "run the game ever plays.");
    }

    /// <summary>
    /// The control keeping the case above honest against a handler that never touches the counter.
    /// A Boss draft over the fixture draws Epic/Legendary only and exactly one Epic row is authored,
    /// which this run owns at Tier I — so the offered upgrade (and the reset) is deterministic.
    /// </summary>
    [Fact]
    public void A_draft_that_offered_an_owned_upgrade_resets_the_famine_counter()
    {
        var state = DraftWorlds.DraftPendingOn(
            battleKind: TileKind.Boss,
            ownedPerkTiers: new Dictionary<string, int> { [PerkDocuments.Epic1] = 1 },
            draftsWithoutOwnedUpgrade: 2);

        var result = Pick(state, 0);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.DraftsWithoutOwnedUpgrade.ShouldBe(
            0,
            "the draft offered the owned Epic back as an upgrade, so the famine it was counting " +
            "towards is satisfied.");
    }
}
