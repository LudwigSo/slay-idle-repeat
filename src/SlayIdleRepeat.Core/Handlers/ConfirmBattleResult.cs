using System.Globalization;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Economy;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>CONFIRM_BATTLE_RESULT</c> handler: closes the battle <c>START_BATTLE</c> opened.
/// </summary>
/// <remarks>
/// <para>
/// <c>LogHash</c> is only checked for a well-formed shape here, not recomputed and compared —
/// recomputing needs hero <c>ActorStats</c> that nothing in <c>Core</c> can build yet. A malformed
/// or absent hash is refused; a well-formed one is trusted, the same way client-asserted tiers are
/// trusted for two of the four minigames.
/// </para>
/// <para>
/// On a win, this handler pays Gold-per-kill immediately into <c>Run.Gold</c>, banks Legend XP (and,
/// on a Boss kill, Soul Shards) onto <c>Run</c> rather than <c>Player</c> — those wait for the
/// run-end payout in <c>Handlers.EndRun</c>/<c>Handlers.AbandonRun</c> — marks a Boss kill, grants
/// the one-time first-clear bonus, and marks the post-battle draft as pending. On a loss, it sets the
/// hero's HP to zero and leaves the pending tile in place so <c>Handlers.Revive</c> can reopen the
/// fight and <c>Handlers.EndRun</c> can still read which tile the hero died on. Either way
/// <c>Run.ExitBattle()</c> runs: the death prompt is client-only UI, not a server phase.
/// </para>
/// </remarks>
internal static class ConfirmBattleResult
{
    /// <summary>Applies <c>CONFIRM_BATTLE_RESULT</c>.</summary>
    internal static HandlerResult Handle(ConfirmBattleResultCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (run.Phase != RunPhase.BattlePending)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        if (!IsWellFormedLogHash(command.LogHash))
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        if (!run.HasPendingTile)
        {
            // A defect, not a rejection: Run.EnterBattle never opens a battle without a pending
            // tile, so this is unreachable through the production dispatch table.
            throw new InvalidOperationException(
                "This run is BattlePending but carries no pending tile. Run.EnterBattle refuses to " +
                "open a battle without one, so this is unreachable through the production dispatch " +
                "table.");
        }

        var kind = (TileKind)run.PendingTileKindValue;
        var events = new List<DomainEvent>();

        if (command.Won)
        {
            ApplyWin(input, kind, events);
        }
        else
        {
            run.SetHitPoints(0, run.MaxHp);
        }

        run.ExitBattle();

        return HandlerResult.Accept(events);
    }

    /// <summary>
    /// The win branch: immediate Gold, banked Legend XP/Soul Shards, Boss-kill and first-clear
    /// marks, and the draft-pending hook.
    /// </summary>
    private static void ApplyWin(HandlerInput input, TileKind kind, List<DomainEvent> events)
    {
        var run = input.Run;

        if (kind is TileKind.Enemy or TileKind.Elite or TileKind.Boss)
        {
            var reward = RunRewardMath.ForKill(kind, run.ChapterId, run.Tier, input.Context.Content);

            if (reward.Gold != 0)
            {
                events.Add(run.MoveCurrency(CurrencyId.GOLD, reward.Gold, RewardReason));
            }

            run.BankRewards(reward.LegendXp, reward.SoulShards);
        }

        if (kind == TileKind.Boss)
        {
            run.MarkBossDefeated();

            var player = input.Player;
            if (!player.HasClearedChapterTier(run.ChapterId, run.Tier))
            {
                player.MarkChapterTierCleared(run.ChapterId, run.Tier);
                run.BankRewards(legendXp: 0, RunRewardMath.FirstClearBonus(input.Context.Content));
            }
        }

        // Captured before ClearPendingTile wipes PendingTileKindValue/Stage, which the draft's
        // rarity weights need.
        run.MarkDraftPending(run.PendingTileKindValue, run.PendingTileStage);
        run.ClearPendingTile();
    }

    /// <summary>Income-attribution reason for a kill's Gold payment.</summary>
    private const string RewardReason = "battle_kill";

    /// <summary>
    /// Whether <paramref name="logHash"/> could legitimately be a <c>LogHash</c>: non-blank and
    /// parseable as the invariant-culture <see cref="ulong"/> the server produces.
    /// </summary>
    private static bool IsWellFormedLogHash(string? logHash) =>
        !string.IsNullOrWhiteSpace(logHash) &&
        ulong.TryParse(logHash, NumberStyles.None, CultureInfo.InvariantCulture, out _);
}
