using System.Globalization;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Gear;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Economy;
using SlayIdleRepeat.Core.Rules.Gear;
using SlayIdleRepeat.Core.Rules.Luck;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>CONFIRM_BATTLE_RESULT</c> handler: closes the battle <c>START_BATTLE</c> opened.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The fight is recomputed here, and the server's answer wins.</b> <c>14</c> §9: <em>"the client
/// reports <c>LogHash</c>; the server has already computed the same fight from the same seed. Mismatch
/// → server result wins, counter incremented, no player-facing error."</em> All three clauses are
/// implemented, and the third is as load-bearing as the first: a mismatch raises no
/// <c>RejectionReason</c>, emits no event and reaches no screen. The command is ACCEPTED and the
/// server's own result applied.
/// </para>
/// <para>
/// 🔒 <b>Through <c>RunBattle.Simulate</c>, never a composition of this handler's own.</b> That is the
/// same entry point the client reaches through the row door, pinned together by
/// <c>Both_doors_compose_the_same_fight</c>. A second composition here would diverge from the client's
/// the first time either changed, and the divergence would read as tampering — so honest players would
/// be the ones it caught.
/// </para>
/// <para>
/// 🔒 <b>Why recomputing is safe at all:</b> the battle's seed is
/// <c>SeedFrom(RunSeed, StreamPosition(combat))</c>, and <c>START_BATTLE</c> advances that stream and
/// then touches nothing else — so the position standing here is the one the fight was drawn at, and
/// the replay is the same fight. ⚠️ Which is also why the recompute runs BEFORE
/// <c>Run.ExitBattle()</c>: <c>RunBattle</c> refuses to compose for a run that is no longer standing
/// in a battle.
/// </para>
/// <para>
/// 🔒 <b>And why <c>GameRules</c> refuses a stock change while this is pending.</b> The hero is
/// recomposed from the PERSISTED stock, so an equip or an enhancement between opening a battle and
/// confirming it would legitimately produce a different fight — indistinguishable from a forged log.
/// <c>RejectionReason.BATTLE_IN_PROGRESS</c> is what keeps <em>server result wins</em> from landing on
/// a player who did nothing wrong. The two halves are one mechanism and neither is correct alone.
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

        if (!RunBattle.HasOpenBattle(run))
        {
            // A defect, not a rejection, on the pending-tile guard's own argument. START_BATTLE
            // advances the combat stream (RunRngScope.BeginBattle) BEFORE Run.EnterBattle sets the
            // phase, so a run in this phase whose combat stream stands at zero never came through the
            // production dispatch table. Asked through RunBattle rather than by reading the counter
            // here: what counts as "standing in a battle" is four facts that type already reads to
            // decide whether it can compose at all, and a caller answering it privately would answer
            // it more narrowly.
            throw new InvalidOperationException(
                "This run is BattlePending but is not standing in a battle RunBattle can compose: " +
                "its 'combat' stream has never been drawn from. START_BATTLE draws it before setting " +
                "the phase, so this state is unreachable through the production dispatch table. A " +
                "fixture reaching it has hand-built the phase instead of submitting START_BATTLE — " +
                "which also means it never committed a battle seed, so there is no fight for 14 §9 " +
                "to recompute and no result for the server to win with.");
        }

        // 🔒 Before ExitBattle, and before anything is paid out: this is the only reading of the fight
        // that decides what happened. command.Won and command.LogHash are the CLIENT's report, and
        // from here on neither is consulted for anything but the comparison.
        var truth = RunBattle.Simulate(input.Player, run, input.Context.Content);

        if (Disagrees(command, truth))
        {
            // 14 §9's second and third clauses. The tally is for the review queue the ladder feeds;
            // nothing here rejects, reports or emits, because a mismatch is equally consistent with a
            // forged log and with a legitimate replay this server could not reproduce — and telling an
            // honest player they cheated is the more expensive mistake.
            input.Player.CountBattleHashMismatch();
        }

        if (truth.HeroWon)
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

                // 🔒 The contract PityCounterAdvanced states in its own remarks, which this handler was
                // breaking while MinigameSubmit honoured it. The DROP_RUN class HAS an authored counter
                // key (24 §3, `drop.run`), so there is a real name to put in the event.
                //
                // ⚠️ Do NOT read Run's own argument that moving a counter emits nothing as covering
                // this: that argument is about the three RUN-SCOPED DRAFT counters, which 24 §3 keys
                // "per run" and for which no id exists to name. Both are correct because they are about
                // different counters, and a reader who misses the distinction will fix one by breaking
                // the other.
                events.Add(new PityCounterAdvanced(
                    DomainEvent.UnstampedSequence, moved.Key, moved.Value));
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
    /// <summary>Whether the client's report differs from the fight the server just ran.</summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>BOTH fields, not only the hash.</b> A client that forged <c>Won</c> while relaying an
    /// honest hash would otherwise pass unnoticed — the payout arm already reads the server's answer,
    /// so the fight would resolve correctly, but the attempt would never be counted and <c>14</c> §9's
    /// ladder acts on repeated attempts. Comparing the hash alone would make the tally blind to the
    /// cheapest possible forgery.
    /// </para>
    /// <para>
    /// The hash is re-parsed rather than carried from the shape check: <see cref="ulong.TryParse"/>
    /// with <see cref="NumberStyles.None"/> has already refused everything that is not a bare unsigned
    /// integer, so this parse cannot fail, and threading an <c>out</c> value through the two guards
    /// between them would put the parse further from its own refusal.
    /// </para>
    /// </remarks>
    private static bool Disagrees(ConfirmBattleResultCommand command, SimulationResult truth) =>
        !ulong.TryParse(
            command.LogHash, NumberStyles.None, CultureInfo.InvariantCulture, out var reported) ||
        reported != truth.LogHash ||
        command.Won != truth.HeroWon;

    private static bool IsWellFormedLogHash(string? logHash) =>
        !string.IsNullOrWhiteSpace(logHash) &&
        ulong.TryParse(logHash, NumberStyles.None, CultureInfo.InvariantCulture, out _);
}
