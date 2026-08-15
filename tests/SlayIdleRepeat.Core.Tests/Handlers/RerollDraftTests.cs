using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
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
}
