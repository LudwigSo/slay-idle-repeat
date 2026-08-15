using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>REVIVE</c> handler: once per run, restores 50% Max HP and restarts the fight that killed
/// the hero from its beginning.
/// </summary>
/// <remarks>
/// <para>
/// A battle loss deliberately does not clear the pending tile, so the same tile the hero just lost to
/// is still pending. This handler calls <c>Run.EnterBattle()</c> directly rather than requiring a
/// fresh <c>START_BATTLE</c>: the combat stream position is unchanged by a loss, so re-deriving the
/// battle seed from it reproduces the identical fight from its own start, not mid-fight.
/// </para>
/// <para>
/// The 2-second invulnerability window is recorded, not enforced: nothing in <c>Core</c> simulates
/// combat time yet, so there is no timeline to attach it to. It is the seam a future combat simulator
/// applies <see cref="ReviveTuning.InvulnerabilitySeconds"/> to.
/// </para>
/// <para>
/// Once-per-run is tracked via <c>Run.AdUses[AD_REVIVE]</c>, the existing per-placement counter,
/// rather than a new field.
/// </para>
/// </remarks>
internal static class Revive
{
    /// <summary>Applies <c>REVIVE</c>.</summary>
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
