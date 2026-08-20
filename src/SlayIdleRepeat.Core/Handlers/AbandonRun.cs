using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Economy;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>ABANDON_RUN</c> handler: closes the run early, applying a 0.10 multiplier to whatever
/// rewards were banked, regardless of HP or Boss status.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Legal from every state a live run can stand in, and that is the point of the command.</b>
/// A player who wants out gets out — mid-battle, mid-draft, paused at a junction, standing on an
/// unresolved tile, or dead with the revive already spent. The two gates in
/// <c>GameRules.Execute</c> that would otherwise refuse it (a battle is open; a draft is open) are
/// exempted through <c>CommandRegistration.LeavesRun</c>, and this handler accepts
/// <see cref="RunPhase.BattlePending"/> as well as <see cref="RunPhase.InProgress"/>. The one state
/// it still refuses is <see cref="RunPhase.Ended"/> — there is no run left to leave, and
/// <c>GameRules.Execute</c> answers <c>RUN_ALREADY_ENDED</c> before this handler is reached at all.
/// </para>
/// <para>
/// 🔒 <b>Every open piece of run state is closed before the run is.</b> An abandoned run is
/// persisted, read back by the results screen and hashed like any other; a row carrying a half-open
/// battle, an unanswered draft or a paused fork would be a terminal state no legal sequence of
/// commands could have produced. Nothing is paid for the state being dropped — the abandoned fight
/// pays no kill reward and the dropped draft grants no perk, which is exactly what abandoning means.
/// </para>
/// <para>
/// Gear drops are not stripped because no gear system exists yet; that lands with whichever task
/// adds gear.
/// </para>
/// </remarks>
internal static class AbandonRun
{
    /// <summary>Applies <c>ABANDON_RUN</c>.</summary>
    internal static HandlerResult Handle(AbandonRunCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        // Ended is unreachable here — GameRules.Execute answers RUN_ALREADY_ENDED first — and is
        // still refused rather than left to Run.EndRun's throw, so a fixture driving this handler
        // directly gets a rejection instead of a domain defect.
        if (run.Phase == RunPhase.Ended)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var payout = RunRewardMath.FinalPayoutFor(
            run.BankedLegendXp,
            run.BankedSoulShards,
            RunCompletionOutcome.Abandon,
            watchedAd: false,
            input.Context.Content);

        var events = new List<DomainEvent>();

        if (payout.LegendXp != 0)
        {
            input.Player.GrantLegendXp(payout.LegendXp);
        }

        if (payout.SoulShards != 0)
        {
            events.Add(input.Player.MoveCurrency(CurrencyId.SOUL_SHARDS, payout.SoulShards, PayoutReason));
        }

        CloseEverythingStillOpen(run);
        run.EndRun();

        return HandlerResult.Accept(events);
    }

    /// <summary>
    /// Closes the four pieces of run state a command can leave open, so the persisted row of an
    /// abandoned run describes a state the game could legally have been in.
    /// </summary>
    /// <remarks>
    /// The battle first: <see cref="Model.Run.ExitBattle"/> is the only one of the four that refuses
    /// to run out of turn, and leaving the phase at <see cref="RunPhase.BattlePending"/> while
    /// <see cref="Model.Run.EndRun"/> moved it to <see cref="RunPhase.Ended"/> would lose the fact
    /// that a fight was open at all. The other three are idempotent or guarded, and are asked only
    /// when they have something to close.
    /// </remarks>
    private static void CloseEverythingStillOpen(Model.Run run)
    {
        if (run.Phase == RunPhase.BattlePending)
        {
            run.ExitBattle();
        }

        if (run.PendingFork is not null)
        {
            run.ClearPendingFork();
        }

        run.ClearDraftPending();
        run.ClearPendingTile();
    }

    /// <summary>Income-attribution reason for the abandon-run payout.</summary>
    private const string PayoutReason = "run_abandon_payout";
}
