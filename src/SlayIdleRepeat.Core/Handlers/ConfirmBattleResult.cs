using System.Globalization;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Gear;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Economy;
using SlayIdleRepeat.Core.Rules.Gear;
using SlayIdleRepeat.Core.Rules.Luck;

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

            GrantKillDrops(input, kind, events);
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

    /// <summary>
    /// The gear a kill drops: how many items, each one's band, and where they land.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The count is decided first and the rest of the content is read only once it is above zero.
    /// An ordinary kill drops at a chance under a tenth, so on the overwhelming majority of kills
    /// the catalogue, the pity registry and the gear tables are never touched at all.
    /// </para>
    /// <para>
    /// 🔒 The item is <b>placed</b>, never refused: at capacity the stock holds it instead, and the
    /// grant is reported either way. No random source in this game may be able to starve a player,
    /// and a drop that vanished because the bag was full is that wound from the other side.
    /// </para>
    /// </remarks>
    private static void GrantKillDrops(HandlerInput input, TileKind kind, List<DomainEvent> events)
    {
        var content = input.Context.Content;
        var trigger = TriggerFor(kind);
        var draws = input.Rng.Stream(RngStreams.Drops);
        var items = DropCount(trigger, GearAcquisitionTuning.Read(content), draws);

        if (items == 0)
        {
            return;
        }

        var run = input.Run;
        var player = input.Player;
        var catalogue = GearCatalogue.Read(content);
        var luck = LuckTuning.Read(content);
        var dropRun = DropRunTuning.Read(content);
        var tables = DropsTuning.Read(content);
        var stock = InventoryTuning.Read(content);

        for (var item = 0; item < items; item++)
        {
            // Captured before the roll: the position the roll STARTS at is what names the item, and
            // reading it afterwards would name every item by the draw index of the next one.
            var ordinal = draws.Position;

            var rolled = GearGeneration.RollRunDrop(
                DropInstanceIds.ForKillDrop(run.Id, ordinal),
                catalogue,
                luck,
                dropRun,
                tables,
                run.ChapterId,
                trigger,
                player.PityCounters,
                draws);

            foreach (var moved in rolled.Changes)
            {
                player.SetPityCounter(moved.Key, moved.Value);
            }

            player.Inventory.Place(rolled.Item, stock);

            events.Add(new GearGranted(
                DomainEvent.UnstampedSequence, rolled.Item, SourceClass.DROP_RUN, rolled.FromPity));

            if (rolled.Item.Rarity >= dropRun.SessionFloor.GrantRarity)
            {
                run.CountItemAtOrAboveFloorBand();
            }
        }
    }

    /// <summary>How many items this kill owes, spending a draw only where the count is drawn.</summary>
    private static int DropCount(
        RunDropTrigger trigger, GearAcquisitionTuning rates, DeterministicRng draws) => trigger switch
    {
        RunDropTrigger.NORMAL_ENEMY => draws.NextDouble() < rates.NormalEnemyChance ? 1 : 0,
        RunDropTrigger.ELITE => rates.EliteKillItems,
        RunDropTrigger.BOSS => draws.Range(rates.BossKillItemsMin, rates.BossKillItemsMax + 1),
        _ => throw new ArgumentOutOfRangeException(
            nameof(trigger), trigger, "That is not a source of an in-run gear drop."),
    };

    /// <summary>Which drop trigger a kill of this tile kind is.</summary>
    private static RunDropTrigger TriggerFor(TileKind kind) => kind switch
    {
        TileKind.Enemy => RunDropTrigger.NORMAL_ENEMY,
        TileKind.Elite => RunDropTrigger.ELITE,
        TileKind.Boss => RunDropTrigger.BOSS,
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind),
            kind,
            "That tile kind is not a kill, so nothing about it can drop gear. Every in-run drop " +
            "trigger names a kill — a treasure tile has no path to a grant at all."),
    };

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
