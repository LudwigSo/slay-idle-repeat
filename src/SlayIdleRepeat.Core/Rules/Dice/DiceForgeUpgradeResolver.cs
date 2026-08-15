using System.Globalization;
using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Dice;

/// <summary>
/// 🔒 <b>M3-04's gap to fill</b> — resolves a legal <c>TILE_DICE_FORGE</c> upgrade choice into the
/// resulting <see cref="DieFace"/>. Pure: given the face being upgraded and the option chosen, it
/// returns the replacement or refuses the choice.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Not wired to a command handler, and that is a stated boundary.</b> `18` §7.9's
/// <c>MODIFY_DIE_FACE</c> fires on <c>{"kind":"ON_TILE_RESOLVED","tileType":"TILE_DICE_FORGE"}</c>,
/// and <c>ResolveTileCommand</c> — the handler that would fire that trigger — is
/// <c>GameRules.Dispatch</c>'s own <c>Deferred&lt;ResolveTileCommand&gt;(..., "M3-03")</c> row: board
/// and tile resolution are M3-03's, not M3-04's, and `03` §1 (the board) is itself still a
/// <c>GapRegister</c> entry (M3-01). This type is the piece M3-04 <em>does</em> own today: the pure
/// function M3-03's future handler calls once <c>ResolveTileCommand</c> exists, kept out of
/// <c>Rules/Effects/Ops/RunBoardOps.cs</c> deliberately — that file's own remarks are explicit its
/// <c>Queue</c> method is for the <em>combat-context</em> exception only (the Dicelord's Scramble,
/// `16` §A7 ruling 9), not for a tile's own resolution.
/// </para>
/// </remarks>
internal static class DiceForgeUpgradeResolver
{
    /// <summary>
    /// Resolves a chosen Dice Forge upgrade against the face it targets.
    /// </summary>
    /// <param name="sourceFace">The face at the chosen index, before the upgrade.</param>
    /// <param name="option">The option the player picked — one of <see cref="DiceForgeUpgradeTable.Options"/>.</param>
    /// <param name="higherPipValue">
    /// Required when <paramref name="option"/> is <see cref="DiceForgeUpgradeOption.ToHigherPip"/>:
    /// the new pip count. Must exceed <paramref name="sourceFace"/>'s current value and be at most 6.
    /// Ignored otherwise.
    /// </param>
    /// <returns>
    /// The replacement <see cref="DieFace"/>, or a failure naming why the choice is illegal — never a
    /// thrown exception, because an illegal choice here is a player's request the caller refuses with
    /// a <c>RejectionReason</c>, not a domain defect (`30` §2.1's <b>P3</b>).
    /// </returns>
    public static Result<DieFace> Resolve(
        DieFace sourceFace, DiceForgeUpgradeOption option, int? higherPipValue = null)
    {
        if (!DiceForgeUpgradeTable.IsLegalSource(sourceFace))
        {
            return Result<DieFace>.Failure(
                "Dice Forge can only upgrade a Pip face; the chosen face is " +
                (sourceFace.IsUnset ? "unset" : sourceFace.Kind.ToString()) + ". 04 §1 rules Void out " +
                "by name, and every other special kind is already an upgrade's result, not a further " +
                "source (see DiceForgeUpgradeTable's remarks).");
        }

        if (option.IsHigherPip)
        {
            if (higherPipValue is not { } newValue)
            {
                return Result<DieFace>.Failure(
                    "The 'raise to a higher Pip value' option needs higherPipValue; none was supplied.");
            }

            if (newValue <= sourceFace.Value || newValue > Content.Effects.DieFaceIndex.MaxFace)
            {
                return Result<DieFace>.Failure(
                    "higherPipValue must exceed the current pip count (" + Text(sourceFace.Value) +
                    ") and be at most " + Text(Content.Effects.DieFaceIndex.MaxFace) + "; " + Text(newValue) +
                    " is not. 03's own example is 1 -> 4, strictly higher.");
            }

            return Result<DieFace>.Success(DieFace.Pip(newValue));
        }

        // Kind is never null when IsHigherPip is false — DiceForgeUpgradeOption.ToKind is the only
        // other factory and it always sets Kind.
        return Result<DieFace>.Success(DieFace.Special(option.Kind!.Value));
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
