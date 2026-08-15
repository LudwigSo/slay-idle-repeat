using System.Globalization;
using SlayIdleRepeat.Core.Content.Dice;

namespace SlayIdleRepeat.Core.Rules.Dice;

/// <summary>
/// Composes the six-face die a run actually plays with, from the starting die plus every upgrade
/// source layered on top: talents (permanent), mounts (permanent while equipped), Dice Forge
/// (run-scoped), perks (run-scoped), curses (temporary). Pure: no <c>Run</c>, no RNG, no clock.
/// </summary>
/// <remarks>
/// Not wired to <c>Run</c> yet — every upgrade source is still a deferred entry there, so nothing
/// calls this with real data today. It's ready for the milestone that has real sources to hand it.
/// Precedence is left-to-right in <see cref="Compose"/>'s <paramref name="layers"/> order, last
/// write wins per face index — the caller supplies the order (permanent sources first, run-scoped
/// next, curses last) rather than this type hard-coding a source-kind ranking.
/// </remarks>
internal static class DieComposer
{
    /// <summary>The starting die: <c>[1] [2] [3] [4] [5] [6]</c>, all <see cref="DieFaceKind.Pip"/>.</summary>
    public static IReadOnlyList<DieFace> StartingDie { get; } = Array.AsReadOnly(new[]
    {
        DieFace.Pip(1), DieFace.Pip(2), DieFace.Pip(3), DieFace.Pip(4), DieFace.Pip(5), DieFace.Pip(6),
    });

    /// <summary>
    /// Composes a six-face die: <paramref name="baseDie"/> with each of <paramref name="layers"/>
    /// applied in order, last write wins per face index.
    /// </summary>
    /// <param name="baseDie">Exactly six faces — normally <see cref="StartingDie"/>.</param>
    /// <param name="layers">
    /// Every override to apply, in precedence order (earliest first). A layer's
    /// <see cref="DieFaceOverride.FaceIndex"/> is 1-based, matching
    /// <see cref="Content.Effects.DieFaceIndex"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="baseDie"/> or <paramref name="layers"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="baseDie"/> does not have exactly six faces, or a layer's
    /// <see cref="DieFaceOverride.FaceIndex"/> is outside 1..6.
    /// </exception>
    public static IReadOnlyList<DieFace> Compose(
        IReadOnlyList<DieFace> baseDie, IReadOnlyList<DieFaceOverride> layers)
    {
        ArgumentNullException.ThrowIfNull(baseDie);
        ArgumentNullException.ThrowIfNull(layers);

        if (baseDie.Count != FairDiceBag.FaceCount)
        {
            throw new ArgumentException(
                "04 §1's die has exactly " + Text(FairDiceBag.FaceCount) + " faces; " + Text(baseDie.Count) +
                " were supplied.",
                nameof(baseDie));
        }

        var composed = baseDie.ToArray();

        foreach (var layer in layers)
        {
            if (layer.FaceIndex is < Content.Effects.DieFaceIndex.MinFace or > Content.Effects.DieFaceIndex.MaxFace)
            {
                throw new ArgumentException(
                    "A DieFaceOverride names face " + Text(layer.FaceIndex) + "; 04 §1 numbers faces 1..6.",
                    nameof(layers));
            }

            composed[layer.FaceIndex - 1] = layer.ReplacementFace;
        }

        return Array.AsReadOnly(composed);
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>One upgrade source's instruction: replace the face at <see cref="FaceIndex"/> with <see cref="ReplacementFace"/>.</summary>
/// <param name="FaceIndex">1-based, 1..6.</param>
/// <param name="ReplacementFace">The face this source installs there.</param>
internal readonly record struct DieFaceOverride(int FaceIndex, DieFace ReplacementFace);
