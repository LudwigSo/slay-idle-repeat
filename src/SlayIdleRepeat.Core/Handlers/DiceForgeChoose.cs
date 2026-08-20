using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Dice;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>DICE_FORGE_CHOOSE</c> handler: permanently upgrades one face of the run's die at a Dice
/// Forge tile, and clears the tile.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The menu is a SUBSET of <see cref="DiceForgeUpgradeTable.Options"/>, and the two that are
/// withheld are withheld to stop the run wedging.</b> The table is a pure statement of what a Dice
/// Forge could install; this handler is where "could" meets what the rest of the game can actually
/// resolve, and the difference is not cosmetic:
/// </para>
/// <list type="bullet">
/// <item>
/// <b><see cref="DieFaceKind.Star"/> is withheld</b> because <c>ROLL_DICE</c> carries no payload a
/// player's chosen 1-6 could arrive on. A Star face installed today makes every later
/// <c>ROLL_DICE</c> answer <c>ILLEGAL_STATE</c> — a run that can never move again, bought with the
/// player's own tap. It comes back the day the roll command can carry a movement choice.
/// </item>
/// <item>
/// <b><see cref="DieFaceKind.Chain"/> is withheld</b> because a Chain hop is specified to resolve
/// its landing tile in full and then roll again (`03` §1.1), and while <c>ROLL_DICE</c> now
/// persists the link count, nothing fires the follow-up roll: the player would simply be handed a
/// face worth two nodes where the die's average is 3.5. Strictly worse, sold as an upgrade.
/// </item>
/// </list>
/// <para>
/// What is offered — a higher Pip value, <see cref="DieFaceKind.Surge"/> and
/// <see cref="DieFaceKind.Fortune"/> — is exactly what the rest of the game resolves today.
/// ⚠️ Fortune's own double-reward half is NOT applied (no landed-tile reward multiplier exists), so
/// it is offered as what it currently is: a face that moves four.
/// </para>
/// <para>
/// Free, and that is the design: landing on the tile is the whole cost (`03` §2), like every other
/// single-target board tile that is spent by the act of arriving.
/// </para>
/// </remarks>
internal static class DiceForgeChoose
{
    /// <summary>
    /// The options a Dice Forge tile offers today, in menu order — the index
    /// <c>DiceForgeChooseCommand.OptionIndex</c> names.
    /// </summary>
    /// <remarks>
    /// 🔒 <c>Rules.Dice.DiceForgeMenu</c>'s, not this handler's, and read rather than restated: the
    /// forge SCREEN draws the same menu, and a handler validating against a private list would
    /// accept an index the screen never offered or refuse one it did.
    /// </remarks>
    internal static IReadOnlyList<DiceForgeUpgradeOption> Menu => DiceForgeMenu.Offered;

    /// <summary>Applies <c>DICE_FORGE_CHOOSE</c>.</summary>
    /// <param name="command">The face to upgrade, the option, and the new pip count where one is needed.</param>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <returns>
    /// <see cref="RejectionReason.ILLEGAL_STATE"/> when no Dice Forge is pending, the face index or
    /// option index names nothing, the chosen face is not a legal source, or the chosen upgrade is
    /// illegal for it; otherwise accepted, with the face installed and the tile cleared.
    /// </returns>
    internal static HandlerResult Handle(DiceForgeChooseCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (!run.HasPendingTile || (TileKind)run.PendingTileKindValue != TileKind.DiceForge)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        if (command.FaceIndex is < Content.Effects.DieFaceIndex.MinFace
                              or > Content.Effects.DieFaceIndex.MaxFace)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        if (command.OptionIndex < 0 || command.OptionIndex >= Menu.Count)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        // The face as the run currently rolls it, not the starting die's: a second Dice Forge must
        // upgrade what the first one left, and offering "raise to a higher pip" against a value the
        // player no longer has would let them lower a face.
        var source = RunDie.Of(run)[command.FaceIndex - 1];

        var resolved = DiceForgeUpgradeResolver.Resolve(
            source, Menu[command.OptionIndex], command.HigherPipValue);

        if (resolved.IsFailure)
        {
            // A rejection, not a throw: every failure this resolver reports is the player asking for
            // something they cannot have — a special face as a source, or a "higher" pip that is not
            // higher — rather than a defect in the game.
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        run.UpgradeDieFace(command.FaceIndex, DieFaceCodec.Encode(resolved.Value));
        run.ClearPendingTile();

        return HandlerResult.Accept();
    }

}
