using System.Globalization;
using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Dice;

/// <summary>
/// Resolves a legal <c>TILE_DICE_FORGE</c> upgrade choice into the resulting <see cref="DieFace"/>.
/// Pure: given the face being upgraded and the option chosen, it returns the replacement or refuses
/// the choice.
/// </summary>
/// <remarks>
/// Not wired to a command handler yet — board and tile resolution live elsewhere and aren't built
/// yet either. This is the pure function that handler will call once it exists, kept separate from
/// the run-board effect ops, which handle only the combat-context exception (a mid-fight die
/// scramble), not a tile's own resolution.
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
    /// thrown exception, since an illegal choice here is a player's request the caller refuses with a
    /// <c>RejectionReason</c>, not a domain defect.
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
