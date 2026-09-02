using System.Collections.Generic;
using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content.Perks;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>SKIP_DRAFT, driven through the production dispatch table.</summary>
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
        result.NewState.Run.DraftPending.ShouldBeFalse();
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

    /// <summary>
    /// 🔒 Ruled: the counters count drafts the run <em>picked from</em>, and a skip picks nothing.
    /// Mirror of <c>RerollDraftTests.A_reroll_leaves_all_three_draft_counters_standing</c>, excluded
    /// for a different reason — a skip is rewarded rather than bought, so no farming argument
    /// applies. Accepted cost: a habitual skipper meets each guarantee later.
    /// </summary>
    /// <remarks>
    /// All three counters start at distinct non-zero values so "unchanged", "reset" and "advanced"
    /// are three different numbers; the run owns an upgradable perk so the famine's gate is open.
    /// </remarks>
    [Fact]
    public void A_skip_leaves_all_three_draft_counters_standing()
    {
        var state = DraftWorlds.DraftPendingOn(
            ownedPerkTiers: new Dictionary<string, int> { [PerkDocuments.Epic1] = 1 },
            draftsWithoutOwnedUpgrade: 2,
            draftsSinceLegendaryOffered: 3,
            draftsWithoutAboveCommon: 1);

        var result = Skip(state);

        result.Accepted.ShouldBeTrue(
            "the premise: a refused skip leaves every counter standing for the wrong reason.");

        var run = result.NewState.Run!;

        run.DraftsWithoutOwnedUpgrade.ShouldBe(
            2,
            "a skipped draft is not a draft the run picked from, so the famine counter it was " +
            "building towards must neither advance nor reset.");
        run.DraftsSinceLegendaryOffered.ShouldBe(
            3, "…and likewise the Legendary pity counter, whose unit is also a draft picked from.");
        run.DraftsWithoutAboveCommon.ShouldBe(
            1, "…and likewise the quality floor's.");
    }
}
