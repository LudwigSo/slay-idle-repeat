using SlayIdleRepeat.Core.Content.Dice;

namespace SlayIdleRepeat.Core.Rules.Dice;

/// <summary>
/// The one place the wire-facing <c>DieFaceSpec.Kind</c> string is parsed into the real
/// <see cref="DieFaceKind"/> enum, and the one place a <see cref="DieFaceKind"/> is rendered back to
/// that string.
/// </summary>
/// <remarks>
/// <c>Content.Effects.DieFaceSpec.Kind</c> stays a string because it's a DSL wire shape, and this is
/// the seam a resolver crosses it at exactly once — the same pattern <see cref="Content.Effects.DieFaceIndex"/>
/// uses for <c>faceIndex</c>.
/// </remarks>
internal static class DieFaceKindCodec
{
    /// <summary>Parses the wire token into the real <see cref="DieFaceKind"/>.</summary>
    /// <param name="kind">
    /// One of the six names, exactly as the content schema spells them: <c>Pip</c>, <c>Star</c>,
    /// <c>Surge</c>, <c>Fortune</c>, <c>Void</c>, <c>Chain</c>.
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
