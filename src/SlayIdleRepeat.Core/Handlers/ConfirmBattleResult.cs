using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;

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
/// 🔒 <b>What "closing the battle" applies, and what it does not.</b> This handler assumes the
/// battle the client confirms was won: `14` §2.3's <c>REVIVE</c> and <c>END_RUN</c>/<c>ABANDON_RUN</c>
/// rows are still <c>Deferred</c> to M3-13, so no rule in this milestone ever reduces a run's HP to 0
/// or ends it — there is nothing yet for a "the hero lost" branch to do that a win's branch does not
/// already do more simply. HP change and gold/XP reward are <b>not</b> applied here: M3-13 owns
/// "Reward banking + run-end payout" as its own task, and paying rewards from this handler would
/// duplicate that task's ruling on where a battle's win pays out (`03` §7a.1's <c>GoldPerKill</c>
/// versus a resolved-tile reward). What this handler does: validates the battle is actually open,
/// clears the pending fight tile, marks M3-06's draft-pending hook (see below), and closes the
/// phase.
/// </para>
/// <para>
/// 🔒 <b>The M3-06 hook, named exactly.</b> <c>Run.MarkDraftPending()</c>, called from this handler
/// immediately before <c>Run.ClearPendingTile()</c>. `02` §1-3's post-battle perk draft is drawn the
/// instant a fight is won — this is that instant, and <c>Run.DraftPending</c> is the flag a future
/// M3-06 <c>PickPerkCommand</c>/<c>RerollDraftCommand</c>/<c>SkipDraftCommand</c> reads and clears.
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

        run.ExitBattle();
        // 🔒 M3-06 — read BEFORE ClearPendingTile wipes them: 06 §4's RarityWeights(stage, isElite,
        // isBoss) needs to know which battle this draft opened for, and PendingTileKindValue/
        // PendingTileStage are the only place that fact lives once the tile clears.
        run.MarkDraftPending(run.PendingTileKindValue, run.PendingTileStage);
        run.ClearPendingTile();

        return HandlerResult.Accept();
    }

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
