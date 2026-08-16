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

    /// <summary>
    /// Determinism: picking against the same committed draft-stream position offers the same three
    /// options every time, so the same index always resolves to the same perk.
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
    /// GameRules.Execute's DraftPending gate: nothing else is legal while a draft is open. Uses
    /// ROLL_DICE rather than START_BATTLE deliberately — START_BATTLE would refuse for its own reason
    /// (no pending tile) even with the gate deleted, while ROLL_DICE has no other reason to refuse
    /// here, so ILLEGAL_STATE can only be the gate.
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

    // ------------------------------------------------------------------ the upgrade-famine counter

    /// <summary>
    /// 🔒 A run that owns nothing leaves the upgrade-famine counter exactly where it stood: no perk
    /// is owned, so no upgrade could have been offered, and a draft that could not have offered one
    /// is not a draft that withheld one.
    /// </summary>
    /// <remarks>
    /// End-to-end rather than only over the rule, because the handler is where the run's own facts
    /// are read: the rule can be conditioned correctly and still be handed a hard-coded "an upgrade
    /// was available", which is exactly what a caller passing a constant would look like. The
    /// counter starts non-zero so "unchanged" and "reset" are different numbers.
    /// </remarks>
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
    /// The other side of it: a run holding an upgradable perk moves the counter, so the case above
    /// is not passing because the handler never touches it.
    /// </summary>
    /// <remarks>
    /// A Boss draft over the hermetic fixture draws Epic/Legendary only and the fixture authors
    /// exactly one Epic row, which this run owns at Tier I — so the draft offers that upgrade and
    /// the famine is satisfied rather than advanced. Either movement discriminates against a handler
    /// that holds the counter unconditionally; the reset is the one this fixture makes deterministic.
    /// </remarks>
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
