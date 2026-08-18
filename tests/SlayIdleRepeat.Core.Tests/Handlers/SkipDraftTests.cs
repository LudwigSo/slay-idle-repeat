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

    /// <summary>
    /// 🔒 A skipped draft leaves all three run-scoped draft counters exactly where they stood — the
    /// mirror of <c>RerollDraftTests.A_reroll_leaves_all_three_draft_counters_standing</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Ruled, not incidental.</b> All three counters count drafts the run <em>picked from</em>,
    /// and a skip picks nothing, so a skip moves none of them. That reading was settled by the
    /// product owner on 2026-08-18 against the alternative — counting every draft a player was shown
    /// — and the design set was amended to describe what ships rather than the code changed to
    /// follow the documents. This case pins a decision; changing it means reopening the decision.
    /// </para>
    /// <para>
    /// The two exclusions on these counters hold for <em>different</em> reasons, which is why the
    /// skip and the reroll each need their own case rather than sharing one. A skip is excluded
    /// because there is no pick to count: it pays the player, so no can-this-be-farmed argument
    /// applies to it at all. A reroll is excluded because it is <em>bought</em> — a counter Gold
    /// could move would make the guarantee itself purchasable. A reader who collapses the two into
    /// one rule will conclude that one of them is a bug and correct it.
    /// </para>
    /// <para>
    /// The accepted cost, recorded so it is not later mistaken for an oversight: a player who
    /// habitually skips meets each guarantee later than one who does not.
    /// </para>
    /// <para>
    /// All three counters start at distinct non-zero values, so for each one "unchanged", "reset"
    /// and "advanced" are three different numbers and every assertion below separates all three. An
    /// earlier form of this case started two of them at zero, where a handler that <em>reset</em>
    /// the counters was indistinguishable from one that left them alone. The run also owns an
    /// upgradable perk, so the famine's own gate is open rather than trivially closed.
    /// </para>
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
