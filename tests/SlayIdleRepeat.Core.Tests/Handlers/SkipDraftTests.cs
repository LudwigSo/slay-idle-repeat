using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>🔒 M3-06, `14` §2.3 — <c>SKIP_DRAFT</c>, driven through the production dispatch table.</summary>
public sealed class SkipDraftTests
{
    private const long SkipGoldReward = SlayIdleRepeat.Core.Tests.Content.InRunIncomeDocuments.ShippedSkipGoldReward;

    private static CommandResult Skip(WorldSlice state) =>
        SlayIdleRepeat.Core.GameRules.Apply(state, new SkipDraftCommand(), DraftWorlds.Context);

    [Fact]
    public void A_run_with_no_draft_pending_is_rejected()
    {
        var result = Skip(Worlds.InARun());

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    [Fact]
    public void Skipping_pays_the_skip_reward_and_closes_the_draft()
    {
        var state = DraftWorlds.DraftPendingOn(gold: 0);

        var result = Skip(state);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.BalanceOf(CurrencyId.GOLD).ShouldBe(SkipGoldReward);
        result.NewState.Run!.DraftPending.ShouldBeFalse();
    }

    [Fact]
    public void Skipping_grants_no_perk()
    {
        var state = DraftWorlds.DraftPendingOn();

        var result = Skip(state);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.DraftedPerks.Tiers.ShouldBeEmpty();
    }

    [Fact]
    public void Skipping_draws_nothing_from_the_draft_stream()
    {
        var before = DraftWorlds.DraftPendingOn();

        var after = Skip(before).NewState;

        after.Run!.StreamPosition(SlayIdleRepeat.Core.Rng.RngStreams.Draft)
            .ShouldBe(before.Run!.StreamPosition(SlayIdleRepeat.Core.Rng.RngStreams.Draft));
    }
}
