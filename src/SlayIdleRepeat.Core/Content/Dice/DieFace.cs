using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Content.Dice;

/// <summary>
/// 🔒 `04` §1 — one face of the die, transcribed verbatim from the design document's own struct:
/// <c>{ DieFaceKind Kind; int Value; int Tier; }</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Value"/> is pips, meaningful only for <see cref="DieFaceKind.Pip"/> — every other kind
/// ignores it (`04` §1's own comment: <c>// pips for Kind == Pip; ignored otherwise</c>).
/// </para>
/// <para>
/// <see cref="Tier"/> is 0..3 and "scales the face's non-movement effect only" — `04` §1 names one
/// concrete case, <see cref="DieFaceKind.Surge"/>'s heal (12/15/18/21% at T0-3). "Purely mechanical;
/// there is no cosmetic tier (D14)": a kind whose effect the design does not tie to Tier (Pip, Star,
/// Fortune, Void, Chain) simply does not read it, and this type does not invent a meaning for those
/// combinations.
/// </para>
/// <para>
/// A validating factory rather than a public constructor, matching every other closed value type in
/// this codebase (<see cref="Effects.DieFaceIndex"/>): a <c>default</c> value is a distinct, invalid
/// state (<see cref="IsUnset"/>) rather than a face that silently reads as <c>Pip 0</c>.
/// </para>
/// </remarks>
public readonly record struct DieFace
{
    /// <summary>`04` §1's floor for <see cref="Tier"/>.</summary>
    public const int MinTier = 0;

    /// <summary>`04` §1's ceiling for <see cref="Tier"/>.</summary>
    public const int MaxTier = 3;

    private readonly bool _isSet;

    private DieFace(DieFaceKind kind, int value, int tier)
    {
        Kind = kind;
        Value = value;
        Tier = tier;
        _isSet = true;
    }

    /// <summary>Which of `04` §1's six faces this is. <c>default</c> for an unset value — see <see cref="IsUnset"/>.</summary>
    public DieFaceKind Kind { get; }

    /// <summary>Pips, meaningful only when <see cref="Kind"/> is <see cref="DieFaceKind.Pip"/>; otherwise ignored.</summary>
    public int Value { get; }

    /// <summary>0..3 — scales the face's non-movement effect only (`04` §1). No cosmetic tier.</summary>
    public int Tier { get; }

    /// <summary>True for a <c>default</c> value, which names no face at all.</summary>
    public bool IsUnset => !_isSet;

    /// <summary>A <see cref="DieFaceKind.Pip"/> face showing <paramref name="pips"/>, at tier 0 (pips carry no tier).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pips"/> is outside 1..6 (`04` §1's starting die).</exception>
    public static DieFace Pip(int pips) =>
        pips is >= DieFaceIndex.MinFace and <= DieFaceIndex.MaxFace
            ? new DieFace(DieFaceKind.Pip, pips, MinTier)
            : throw new ArgumentOutOfRangeException(
                nameof(pips), pips,
                "04 §1's die shows 1..6 pips; " + Text(pips) + " is outside that range.");

    /// <summary>A non-<see cref="DieFaceKind.Pip"/> face at the given tier. <see cref="Value"/> is 0 (ignored for these kinds).</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="kind"/> is <see cref="DieFaceKind.Pip"/> (use <see cref="Pip"/>), not a defined
    /// <see cref="DieFaceKind"/>, or <paramref name="tier"/> is outside 0..3.
    /// </exception>
    public static DieFace Special(DieFaceKind kind, int tier = MinTier)
    {
        if (kind == DieFaceKind.Pip)
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind), kind, "A Pip face carries pips — build it with DieFace.Pip(value), not " +
                "DieFace.Special, so Value is never silently 0 for the one kind that reads it.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind), kind, "04 §1 fixes DieFaceKind at six named members; " + Text((int)kind) +
                " is not one of them.");
        }

        if (tier is < MinTier or > MaxTier)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tier), tier,
                "04 §1's Tier is 0..3; " + Text(tier) + " is outside that range.");
        }

        return new DieFace(kind, 0, tier);
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
