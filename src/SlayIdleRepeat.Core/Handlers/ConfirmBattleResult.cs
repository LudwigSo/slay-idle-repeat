using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Economy;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// 🔒 M3-05, `14` §2.3 — the <c>CONFIRM_BATTLE_RESULT</c> handler: closes the battle
/// <c>START_BATTLE</c> opened.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>What <c>LogHash</c> validation this handler does, and what it deliberately does not.</b>
/// `05` §7 / `14` §16.6 define <c>LogHash</c> as a hash over the serialised combat-event list, and
/// `14` §2.4 has the server recompute it and compare — but recomputing it needs the same hero
/// <c>ActorStats</c> the client simulated with, which nothing in <c>Core</c> can build yet (see
/// <c>Handlers.StartBattle</c>'s remarks: `18` §8's stat pipeline is M4's). No hash-check helper
/// exists elsewhere in the repository either (searched for one against every other <c>LogHash</c>
/// reference in <c>Core</c> and <c>Core.Tests</c>). So this handler does the minimal legitimate
/// check available without inventing a validation the design has not specified yet: <c>LogHash</c>
/// must be present and must parse as the <see cref="ulong"/>
/// <c>CanonicalStateWriter.HashCombatLog</c> actually produces, in the invariant format `14` §8.2
/// requires everywhere else in this codebase. A malformed or absent value is refused as
/// <see cref="RejectionReason.ILLEGAL_STATE"/>; a well-formed one is trusted, exactly as `14` §2.4's
/// "documented exception to server authority" already trusts <c>MINIGAME_SUBMIT</c>'s
/// client-asserted tiers for two of its four minigames. Recomputing and comparing the hash is
/// scoped OUT of this task and recorded here rather than guessed at — the day a hero
/// <c>ActorStats</c> snapshot exists (M4), this is the seam that grows the real comparison.
/// </para>
/// <para>
/// 🔒 <b>M3-13 fills in the win/loss branch and the reward payout this handler's own remarks used to
/// defer.</b> <c>Won</c> was added to <see cref="ConfirmBattleResultCommand"/> for exactly this: `02`
/// §6's death/revive flow needs a way for a battle loss to reduce HP toward zero, and nothing else in
/// the command family could carry that signal (see the field's own remarks). On a <b>win</b>, this
/// handler pays `03` §7a.1's Gold-per-kill <b>immediately</b> into <c>Run.Gold</c>, banks `02`
/// §5.1a's Legend XP (and, on a Boss kill, `10` §2's Soul Shards) onto <c>Run</c> via
/// <c>Run.BankRewards</c> — Legend XP and Soul Shards are <b>not</b> paid to <c>Player</c> here; they
/// wait for the run-end payout in <c>Handlers.EndRun</c>/<c>Handlers.AbandonRun</c> — marks a Boss
/// kill with <c>Run.MarkBossDefeated()</c>, grants the one-time first-clear Soul Shard bonus the
/// first time this player clears this (Chapter, Tier), marks the M3-06 draft-pending hook, and clears
/// the pending tile. On a <b>loss</b>, it sets the hero's HP to zero
/// (<c>Run.SetHitPoints(0, MaxHp)</c> — already a legal call: that seam's own floor is zero, "the
/// state a downed hero is in") and leaves the pending tile <b>in place</b>, so
/// <c>Handlers.Revive</c> can re-open the same fight and <c>Handlers.EndRun</c> can still read which
/// tile/stage the hero died on. Either way, <c>Run.ExitBattle()</c> always runs: `02` §1.1's
/// <c>DEATH_PROMPT</c> is client-only UI, not a server phase (see <c>RunPhase</c>'s remarks), so a
/// dead hero standing at <see cref="RunPhase.InProgress"/> with HP 0 is the correct server state
/// either way.
/// </para>
/// <para>
/// 🔒 <b>The M3-06 hook, named exactly.</b> <c>Run.MarkDraftPending()</c>, called from this handler
/// immediately before <c>Run.ClearPendingTile()</c>, on a win only. `02` §1-3's post-battle perk draft
/// is drawn the instant a fight is won — this is that instant, and <c>Run.DraftPending</c> is the
/// flag a future M3-06 <c>PickPerkCommand</c>/<c>RerollDraftCommand</c>/<c>SkipDraftCommand</c> reads
/// and clears.
/// </para>
/// </remarks>
internal static class ConfirmBattleResult
{
    /// <summary>🔒 `14` §2.3 — applies <c>CONFIRM_BATTLE_RESULT</c>.</summary>
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
            // 🔒 A defect rather than a rejection: RunPhase.BattlePending only ever exists because
            // Run.EnterBattle checked HasPendingTile before opening it, and nothing between then and
            // now clears a pending tile without also leaving BattlePending. Reaching here is a
            // miswired caller, not a player asking for something illegal.
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
    /// 🔒 M3-13 — the whole win branch: immediate Gold, banked Legend XP/Soul Shards, the Boss-kill
    /// and first-clear marks, and M3-06's draft-pending hook.
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

        run.MarkDraftPending();
        run.ClearPendingTile();
    }

    /// <summary>The `21` §8.3 income-attribution reason every kill's Gold payment carries.</summary>
    private const string RewardReason = "battle_kill";

    /// <summary>
    /// Whether <paramref name="logHash"/> could legitimately be a `05` §7 <c>LogHash</c>: non-blank,
    /// and parseable as the <see cref="ulong"/> <c>CanonicalStateWriter.HashCombatLog</c> produces,
    /// in `14` §8.2's culture-invariant, base-10 form.
    /// </summary>
    private static bool IsWellFormedLogHash(string? logHash) =>
        !string.IsNullOrWhiteSpace(logHash) &&
        ulong.TryParse(
            logHash,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out _);
}
