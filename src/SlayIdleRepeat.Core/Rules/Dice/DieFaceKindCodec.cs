using SlayIdleRepeat.Core.Content.Dice;

namespace SlayIdleRepeat.Core.Rules.Dice;

/// <summary>
/// 🔒 The one place `18`'s wire-facing <c>DieFaceSpec.Kind</c> string is parsed into the real
/// <see cref="DieFaceKind"/> enum, and the one place a <see cref="DieFaceKind"/> is rendered back to
/// that string.
/// </summary>
/// <remarks>
/// <c>Content.Effects.DieFaceSpec.Kind</c> stays a string because it is a DSL wire shape
/// (<c>game-data/schema/effect.schema.json</c> validates it as one of six literals, and JSON has no
/// closed-enum literal). This type is the seam a <c>MODIFY_DIE_FACE</c> resolver crosses that shape
/// at, exactly once, the same way <see cref="Content.Effects.DieFaceIndex"/> turns
/// <c>faceIndex</c>'s wire token into a real value without <c>DieFaceSpec</c> itself needing to stop
/// being JSON-shaped.
/// </remarks>
internal static class DieFaceKindCodec
{
    /// <summary>Parses `18`'s wire token into the real <see cref="DieFaceKind"/>.</summary>
    /// <param name="kind">
    /// One of `04` §1's six names, exactly as <c>game-data/schema/effect.schema.json</c> spells them:
    /// <c>Pip</c>, <c>Star</c>, <c>Surge</c>, <c>Fortune</c>, <c>Void</c>, <c>Chain</c>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="kind"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="kind"/> is not one of the six names.</exception>
    public static DieFaceKind Parse(string kind)
    {
        ArgumentNullException.ThrowIfNull(kind);

        return kind switch
        {
            nameof(DieFaceKind.Pip) => DieFaceKind.Pip,
            nameof(DieFaceKind.Star) => DieFaceKind.Star,
            nameof(DieFaceKind.Surge) => DieFaceKind.Surge,
            nameof(DieFaceKind.Fortune) => DieFaceKind.Fortune,
            nameof(DieFaceKind.Void) => DieFaceKind.Void,
            nameof(DieFaceKind.Chain) => DieFaceKind.Chain,
            _ => throw new ArgumentException(
                "'" + kind + "' is not one of 04 §1's six DieFaceKind names (Pip, Star, Surge, " +
                "Fortune, Void, Chain) — the same closed set effect.schema.json enumerates for " +
                "DieFaceSpec.Kind. A value reaching here means content that validated against a " +
                "different vocabulary than this parser knows, which is a content-pipeline defect, " +
                "not a player request.",
                nameof(kind)),
        };
    }

    /// <summary>The wire token a <see cref="DieFaceKind"/> renders back to.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined member.</exception>
    public static string ToWireToken(DieFaceKind kind) =>
        Enum.IsDefined(kind)
            ? kind.ToString()
            : throw new ArgumentOutOfRangeException(
                nameof(kind), kind, "04 §1 fixes DieFaceKind at six named members.");
}
