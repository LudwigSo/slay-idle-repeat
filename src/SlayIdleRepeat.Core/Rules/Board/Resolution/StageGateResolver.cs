using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Board.Resolution;

/// <summary>
/// The Stage Gate: fires the instant movement lands exactly on a stage's last node. Called from
/// both places that can produce that landing — <c>Handlers.RollDice</c>'s chain and
/// <c>Handlers.ChooseFork</c>'s resumed movement — so the rule lives once rather than twice.
/// </summary>
/// <remarks>
/// <para>
/// Three things move, all through <c>Run.ApplyStageGate</c>: the hero heals
/// <see cref="StageGateTuning.HealPctMaxHp"/> of Max HP (clamped, the same rounding shape
/// <c>Handlers.RollDice</c> already uses for a Surge face's heal); reroll charges spent this stage
/// reset to 0; and the Fair-Dice bag's reset anchor moves to the <c>dice</c> stream's position at
/// the gate, so the next <c>FairDiceBag.Replay</c> call reconstructs weights from THIS stage rather
/// than draw 0 of the whole run.
/// </para>
/// <para>
/// What does not move, and why: rarity-shift and the enemy-power step are driven by the stage
/// index alone (<c>Run.PendingTileStage</c>), already fed to
/// <see cref="Board.EnemyPowerFormula.Compute"/> — nothing about crossing a gate needs a mutation
/// beyond the index itself already changing, so there is no field here for either. The
/// interstitial ad seam is left alone entirely. Autosave is a persistence-layer concern and a
/// no-op at this layer: <c>Core</c> has no persistence adapter to checkpoint.
/// </para>
/// </remarks>
internal static class StageGateResolver
{
    /// <summary>
    /// Applies a Stage Gate to <paramref name="input"/>'s run, healing from
    /// <paramref name="currentHpBeforeGate"/>.
    /// </summary>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <param name="currentHpBeforeGate">
    /// The hero's hit points immediately before the gate — the caller's own running total, since
    /// <c>Handlers.RollDice</c> may have already applied a Surge heal earlier in the same chain and
    /// has not yet written it to <c>Run</c>.
    /// </param>
    /// <returns>The hero's hit points after the gate's heal, clamped to Max HP.</returns>
    internal static int Apply(HandlerInput input, int currentHpBeforeGate)
    {
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;
        var tuning = StageGateTuning.Read(input.Context.Content);

        var healed = currentHpBeforeGate +
            (int)Math.Round(run.MaxHp * tuning.HealPctMaxHp, MidpointRounding.AwayFromZero);
        var healedCurrentHp = Math.Min(healed, run.MaxHp);

        // Read off the OPEN stream, not Run.StreamPosition: this command's own draws (the chain
        // that reached the gate) have not been folded back into Run yet, so the committed position
        // would be stale by exactly this command's own draws.
        var diceStreamPositionAtGate = input.Rng.Stream(RngStreams.Dice).Position;

        run.ApplyStageGate(healedCurrentHp, diceStreamPositionAtGate);

        return healedCurrentHp;
    }
}
