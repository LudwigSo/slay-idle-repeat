using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Economy;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// 🔒 M3-13, `14` §2.3 — the <c>END_RUN</c> handler: banks the run's rewards and closes it (`02`
/// §5's run-end payout), for the two natural terminal states — Victory (the Boss is dead) and Death
/// (the hero's HP is 0 and there is no revive left to take).
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>END_RUN is refused outside Victory or Death.</b> `02` §5.2's <c>CompletionMultiplier</c> has
/// no row for "the player asked to stop, unhurt, boss alive" — that is exactly what
/// <see cref="AbandonRunCommand"/> is for (0.10, no gear kept). So this handler requires
/// <c>Run.BossDefeated</c> or <c>Run.CurrentHp == 0</c>; anything else is <c>ILLEGAL_STATE</c>, and
/// the client is expected to send <c>ABANDON_RUN</c> instead.
/// </para>
/// <para>
/// 🔒 <b>Which Death row.</b> A death fighting the Boss counts as a Stage 3 death — `03` §1's board
/// gives the Boss node no stage of its own (see <see cref="RunCompletionOutcome.Stage3Death"/>'s
/// remarks) — otherwise <c>Run.PendingTileStage</c> (still readable: a loss in
/// <c>Handlers.ConfirmBattleResult</c> deliberately leaves the pending tile in place) names the
/// stage directly.
/// </para>
/// <para>
/// ⚠️ <b>No ad-double path is reachable yet.</b> `02` §5.2's <c>AdDoubleMultiplier</c> needs a signal
/// that the player watched the run-end rewarded ad, and <see cref="EndRunCommand"/> carries no
/// payload at all — `14` §2.3 authors it with none. <see cref="RunPayoutTuning.AdDoubleMultiplier"/>
/// is read and pinned by its own tests, but this handler always calls it with
/// <c>watchedAd: false</c>; wiring the ad path is a later milestone's, once a command exists to carry
/// the signal.
/// </para>
/// </remarks>
internal static class EndRun
{
    /// <summary>🔒 `02` §5.2 — applies <c>END_RUN</c>.</summary>
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

        var outcome = run.BossDefeated
            ? RunCompletionOutcome.Victory
            : DeathOutcomeFor(run);

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

        run.EndRun();

        return HandlerResult.Accept(events);
    }

    /// <summary>🔒 M3-13 — which Death row of <c>CompletionMultiplier</c> a dead-but-not-victorious run pays.</summary>
    private static RunCompletionOutcome DeathOutcomeFor(Run run)
    {
        if (!run.HasPendingTile)
        {
            // Defensive: ConfirmBattleResult's loss branch always leaves the tile pending, so a dead
            // run reaching EndRun with none is a state this handler cannot name a stage for.
            return RunCompletionOutcome.Stage3Death;
        }

        return run.PendingTileStage switch
        {
            1 => RunCompletionOutcome.Stage1Death,
            2 => RunCompletionOutcome.Stage2Death,
            3 => RunCompletionOutcome.Stage3Death,

            // 03 §1's Boss node belongs to no stage (BoardGraph.BossStage == 0) — a death fighting
            // the Boss is the closest of the three named stages. See RunCompletionOutcome.Stage3Death.
            _ => RunCompletionOutcome.Stage3Death,
        };
    }

    /// <summary>The `21` §8.3 income-attribution reason the run-end Soul Shard payout carries.</summary>
    private const string PayoutReason = "run_end_payout";
}
