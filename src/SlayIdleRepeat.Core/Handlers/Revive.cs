using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// 🔒 M3-13, `14` §2.3, `02` §6 — the <c>REVIVE</c> handler: once per run, restores 50% Max HP and
/// restarts the fight that killed the hero from its beginning.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>"Restarts from the beginning of that battle", realised against <c>Run</c>'s existing battle
/// API.</b> <c>Handlers.ConfirmBattleResult</c>'s loss branch deliberately does <b>not</b> clear the
/// pending tile — see its remarks — so the same <c>Enemy</c>/<c>Elite</c>/<c>Boss</c> tile the hero
/// just lost to is still <c>Run.HasPendingTile</c>. This handler calls <c>Run.EnterBattle()</c>
/// directly (the same call <c>Handlers.StartBattle</c> makes) rather than requiring a fresh
/// <c>START_BATTLE</c>: `14` §8.1 makes a re-entered battle deterministic by construction — the
/// <c>combat</c> stream position (the next <c>battleIndex</c>) is unchanged by a loss, so re-deriving
/// <c>battleSeed</c> from it draws the identical seed and the fight replays from its own start, "not
/// mid-fight", exactly as `02` §6 asks.
/// </para>
/// <para>
/// ⚠️ <b>The 2-second invulnerability window is recorded, not enforced.</b>
/// <see cref="ReviveTuning.InvulnerabilitySeconds"/> is read and validated by this handler's own
/// tests, but nothing in <c>Core</c> simulates combat time yet — `05`'s combat simulator and its
/// event timeline are M4's. There is no invulnerability-window concept anywhere in <c>Run</c> or
/// <c>Rules/Combat/</c> today to attach a flag to, and inventing one here (a field, a duration) would
/// be state with no consumer — exactly the S6 hole this milestone's steering rules warn against.
/// This is a stated gap: the day the combat simulator exists, it is the seam that reads
/// <see cref="ReviveTuning.InvulnerabilitySeconds"/> and applies it to the replayed fight's opening
/// beats.
/// </para>
/// <para>
/// 🔒 <b>Once per run, hard — tracked via <c>Run.AdUses[AD_REVIVE]</c>, not a new field.</b> `02` §6
/// says <em>"Limit once per run, hard"</em>; <see cref="Model.Run.CountAdUse"/> is the existing
/// per-placement counter, and a second <c>RevivesUsed</c> field would be a second source of truth for
/// the same fact.
/// </para>
/// </remarks>
internal static class Revive
{
    /// <summary>🔒 `02` §6 — applies <c>REVIVE</c>.</summary>
    internal static HandlerResult Handle(ReviveCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (run.Phase != RunPhase.InProgress || run.CurrentHp != 0 || !run.HasPendingTile)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        if (run.AdUseCount(ReviveTuning.PlacementId) > 0)
        {
            return HandlerResult.Reject(RejectionReason.CAP_REACHED);
        }

        var tuning = ReviveTuning.Read(input.Context.Content);

        var healedHp = (int)Math.Round(run.MaxHp * tuning.HealPctMaxHp, MidpointRounding.AwayFromZero);
        healedHp = Math.Clamp(healedHp, 1, run.MaxHp);

        run.SetHitPoints(healedHp, run.MaxHp);
        run.CountAdUse(ReviveTuning.PlacementId, 1);
        run.EnterBattle();

        return HandlerResult.Accept();
    }
}
