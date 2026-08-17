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
    /// 🔴 <b>This is a pin on TODAY'S behaviour, deliberately, and not a claim that today's
    /// behaviour is right.</b> <c>REROLL_DRAFT</c> had this case and <c>SKIP_DRAFT</c> had no
    /// equivalent, so both readings were silent here: a handler that advanced these counters and one
    /// that left them standing were indistinguishable to the suite, and either could have been
    /// introduced by accident.
    /// </para>
    /// <para>
    /// ⚠️ <b>The design set does not obviously agree with what is pinned.</b> `06` §4 states F1 and
    /// F3 over drafts <em>offered</em>, which would have a skip advance them; `24` §1.2's anti-farming
    /// argument — the one the reroll case cites — is about counters a player can walk towards a
    /// guarantee cheaply, and a skip is paid for with a draft rather than with Gold, so the two rules
    /// pull in opposite directions. <b>A follow-up task may deliberately flip this pin</b>; what it
    /// may not do is change the behaviour without noticing, which is the only thing this case exists
    /// to prevent.
    /// </para>
    /// <para>
    /// The famine counter starts non-zero so "unchanged" and "reset" are different numbers, and the
    /// run owns an upgradable perk so the famine's own gate is open rather than trivially closed; the
    /// other two start at zero and would read one if the skip had counted this draft.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_skip_leaves_all_three_draft_counters_standing()
    {
        var state = DraftWorlds.DraftPendingOn(
            ownedPerkTiers: new Dictionary<string, int> { [PerkDocuments.Epic1] = 1 },
            draftsWithoutOwnedUpgrade: 2);

        var result = Skip(state);

        result.Accepted.ShouldBeTrue(
            "the premise: a refused skip leaves every counter standing for the wrong reason.");

        var run = result.NewState.Run!;

        run.DraftsWithoutOwnedUpgrade.ShouldBe(
            2,
            "as SKIP_DRAFT stands today, a skipped draft is not a draft the run took, so the famine " +
            "counter neither advances nor resets. See this case's remarks: 06 §4 counts drafts " +
            "OFFERED and may well win that argument later.");
        run.DraftsSinceLegendaryOffered.ShouldBe(
            0, "…and likewise the Legendary pity counter.");
        run.DraftsWithoutAboveCommon.ShouldBe(
            0, "…and likewise the quality floor's.");
    }
}
