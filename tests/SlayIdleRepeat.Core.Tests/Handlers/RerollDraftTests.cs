using System.Collections.Generic;
using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content.Perks;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>REROLL_DRAFT, driven through the production dispatch table.</summary>
public sealed class RerollDraftTests
{
    /// <summary>The shipped reroll cost — <c>DraftEconomyTuning.RerollGoldCost</c>, as fixtured.</summary>
    private const long RerollGoldCost = SlayIdleRepeat.Core.Tests.Content.InRunIncomeDocuments.ShippedRerollGoldCost;

    private static CommandResult Reroll(WorldSlice state) =>
        SlayIdleRepeat.Core.GameRules.Apply(state, new RerollDraftCommand(), DraftWorlds.Context);

    [Fact]
    public void A_run_with_no_draft_pending_is_rejected()
    {
        var result = Reroll(Worlds.InARun());

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    [Fact]
    public void An_unaffordable_reroll_is_rejected_and_spends_nothing()
    {
        var state = DraftWorlds.DraftPendingOn(gold: RerollGoldCost - 1);

        var result = Reroll(state);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.INSUFFICIENT_FUNDS);
    }

    [Fact]
    public void An_affordable_reroll_charges_Gold_and_leaves_the_draft_pending()
    {
        var state = DraftWorlds.DraftPendingOn(gold: RerollGoldCost);

        var result = Reroll(state);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.BalanceOf(CurrencyId.GOLD).ShouldBe(0L);
        result.NewState.Run!.DraftPending.ShouldBeTrue("a reroll redraws the offer, it does not close the draft");
    }

    /// <summary>A reroll advances the draft stream, so a later PICK_PERK sees different options.</summary>
    [Fact]
    public void A_reroll_advances_the_draft_stream()
    {
        var before = DraftWorlds.DraftPendingOn(gold: RerollGoldCost);

        var afterReroll = Reroll(before).NewState;

        afterReroll.Run!.StreamPosition(SlayIdleRepeat.Core.Rng.RngStreams.Draft)
            .ShouldBeGreaterThan(before.Run!.StreamPosition(SlayIdleRepeat.Core.Rng.RngStreams.Draft));
    }

    /// <summary>
    /// 🔒 <c>24</c> §1.2's anti-farming test, over the three run-scoped draft counters: a reroll
    /// leaves every one of them exactly where it stood.
    /// </summary>
    /// <remarks>
    /// The counters count the drafts a run <em>resolved</em>, not the option sets it drew. A reroll
    /// that advanced them would let a player walk a guarantee towards themselves for Gold — the
    /// cheaper-than-the-thing-it-protects shape §1.2 exists to catch — and it is the one claim the
    /// handler's own remarks make that no other test in this suite is stated over. The famine
    /// counter starts non-zero so "unchanged" and "reset" are different numbers, and the run owns an
    /// upgradable perk so the famine's own gate is open rather than trivially closed; the other two
    /// start at zero and would read one if the reroll had counted this draw as a draft.
    /// </remarks>
    [Fact]
    public void A_reroll_leaves_all_three_draft_counters_standing()
    {
        var state = DraftWorlds.DraftPendingOn(
            gold: RerollGoldCost,
            ownedPerkTiers: new Dictionary<string, int> { [PerkDocuments.Epic1] = 1 },
            draftsWithoutOwnedUpgrade: 2);

        var run = Reroll(state).NewState.Run!;

        run.DraftsWithoutOwnedUpgrade.ShouldBe(
            2,
            "a reroll is not a draft the run took, so the famine counter it was building towards " +
            "must neither advance nor reset — either movement is a guarantee bought with Gold.");
        run.DraftsSinceLegendaryOffered.ShouldBe(
            0, "the Legendary pity counter is moved by taking a draft, not by redrawing one.");
        run.DraftsWithoutAboveCommon.ShouldBe(
            0, "the quality floor's counter is moved by taking a draft, not by redrawing one.");
    }
}
