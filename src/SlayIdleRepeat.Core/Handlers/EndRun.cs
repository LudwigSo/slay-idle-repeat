using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Gear;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Economy;
using SlayIdleRepeat.Core.Rules.Gear;
using SlayIdleRepeat.Core.Rules.Luck;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>END_RUN</c> handler: banks the run's rewards and closes it, for the two natural terminal
/// states — Victory (the Boss is dead) and Death (the hero's HP is 0 and there is no revive left).
/// </summary>
/// <remarks>
/// <para>
/// Refused outside Victory or Death — "stopped early, unhurt, boss alive" is
/// <see cref="AbandonRunCommand"/>'s case instead.
/// </para>
/// <para>
/// A death fighting the Boss is scored as a Stage 3 death, since the Boss node has no stage of its
/// own; otherwise the stage comes from <c>Run.PendingTileStage</c>, still readable because a loss in
/// <c>Handlers.ConfirmBattleResult</c> leaves the pending tile in place.
/// </para>
/// <para>
/// No ad-double path is reachable yet: <see cref="EndRunCommand"/> carries no payload to signal a
/// watched ad, so this always pays with <c>watchedAd: false</c>.
/// </para>
/// </remarks>
internal static class EndRun
{
    /// <summary>Applies <c>END_RUN</c>.</summary>
    internal static HandlerResult Handle(EndRunCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (run.Phase != RunPhase.InProgress)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        if (!run.BossDefeated && run.CurrentHp != 0)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var outcome = RunRewardMath.OutcomeFor(
            run.BossDefeated, run.HasPendingTile ? run.PendingTileStage : null);

        if (outcome == RunCompletionOutcome.Victory)
        {
            run.BankRewards(
                RunRewardMath.VictoryBonus(run.ChapterId, run.Tier, input.Context.Content), soulShards: 0);
        }

        var payout = RunRewardMath.FinalPayoutFor(
            run.BankedLegendXp, run.BankedSoulShards, outcome, watchedAd: false, input.Context.Content);

        var events = new List<DomainEvent>();

        if (payout.LegendXp != 0)
        {
            input.Player.GrantLegendXp(payout.LegendXp);
        }

        if (payout.SoulShards != 0)
        {
            events.Add(input.Player.MoveCurrency(CurrencyId.SOUL_SHARDS, payout.SoulShards, PayoutReason));
        }

        GrantSessionFloor(input, outcome, events);

        run.EndRun();

        return HandlerResult.Accept(events);
    }

    /// <summary>
    /// The session floor: what a run that was genuinely played out and produced nothing worth
    /// keeping is owed, and the three reasons it is owed nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the protections document is read before the count is asked for; the catalogue, the gear
    /// tables and the stock's numbers come after it. So a run that is owed nothing — which is most of
    /// them — pays for one document rather than four, and mints nothing.
    /// </para>
    /// <para>
    /// 🔒 The day's allowance is spent in <b>grants</b> and the payout is made in <b>items</b>. They
    /// are equal only while the document authors a grant of one item, which it is free to stop
    /// doing, so the two halves of the answer are kept apart all the way to the counter.
    /// </para>
    /// <para>
    /// Every floor item is reported as a pity grant, because the floor firing IS a protection
    /// firing — a grant that called itself an ordinary drop would make the protection unauditable
    /// from the event log.
    /// </para>
    /// </remarks>
    private static void GrantSessionFloor(
        HandlerInput input, RunCompletionOutcome outcome, List<DomainEvent> events)
    {
        var content = input.Context.Content;
        var dropRun = DropRunTuning.Read(content);
        var player = input.Player;
        var run = input.Run;

        var qualified = outcome is RunCompletionOutcome.Victory or RunCompletionOutcome.Stage3Death;
        // Clamped, not cast: the counter is a long the rehydration seam only floors at zero, and an
        // unchecked narrowing of a row above int range wraps NEGATIVE, which the façade throws on.
        var spentToday = (int)Math.Min(player.DailyCount(DailyFloorGrantCounter), int.MaxValue);

        var owed = LuckService.SessionFloorGrant(
            dropRun, qualified, run.ItemsAtOrAboveFloorBand, spentToday);

        if (owed.Items == 0)
        {
            return;
        }

        var identities = new GearInstanceId[owed.Items];

        for (var i = 0; i < identities.Length; i++)
        {
            identities[i] = DropInstanceIds.ForSessionFloor(run.Id, i);
        }

        var granted = GearGeneration.RollSessionFloor(
            identities,
            GearCatalogue.Read(content),
            dropRun,
            DropsTuning.Read(content),

            // The chapter the player has reached, not the one this run happened to be played in:
            // the floor is a session's payout rather than a run's drop.
            player.HighestChapterCleared,
            qualified,
            run.ItemsAtOrAboveFloorBand,
            spentToday,
            input.Rng.Stream(RngStreams.Drops));

        var stock = InventoryTuning.Read(content);

        foreach (var item in granted)
        {
            player.Inventory.Place(item, stock);

            events.Add(new GearGranted(
                DomainEvent.UnstampedSequence, item, SourceClass.DROP_RUN, FromPity: true));
        }

        player.CountDaily(DailyFloorGrantCounter, owed.Grants);
    }

    /// <summary>The daily counter the floor's per-day allowance is spent out of.</summary>
    private const string DailyFloorGrantCounter = "session_floor_grant";

    /// <summary>Income-attribution reason for the run-end Soul Shard payout.</summary>
    private const string PayoutReason = "run_end_payout";
}
