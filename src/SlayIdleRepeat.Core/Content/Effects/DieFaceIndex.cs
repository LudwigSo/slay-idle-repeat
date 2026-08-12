namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// Which face a <see cref="EffectOp.MODIFY_DIE_FACE"/> replaces: a numbered face, or `18` §7.9's
/// <c>"PLAYER_CHOICE"</c>.
/// </summary>
/// <remarks>
/// ⚠️ <b>Assumption, recorded rather than hidden.</b> `18` writes only the <c>"PLAYER_CHOICE"</c>
/// form. `04` §1 fixes the die at six faces and prints the starting die as <c>[1] … [6]</c>, so the
/// numbered form is taken to be <b>1-based, 1..6</b>. The count and the bound come from `04`; only
/// the base is an inference, and it is the one the document displays.
/// </remarks>
public readonly record struct DieFaceIndex
{
    /// <summary>The `18` §7.9 token for the player-chosen face.</summary>
    public const string PlayerChoiceToken = "PLAYER_CHOICE";

    /// <summary>The lowest numbered face (`04` §1 — the die has six).</summary>
    public const int MinFace = 1;

    /// <summary>The highest numbered face (`04` §1 — the die has six).</summary>
    public const int MaxFace = 6;

    private DieFaceIndex(int? face)
    {
        Face = face;
    }

    /// <summary>The 1..6 face number, or <c>null</c> for <see cref="PlayerChoice"/>.</summary>
    public int? Face { get; }

    /// <summary>True when the player picks the face at resolution time.</summary>
    public bool IsPlayerChoice => Face is null;

    /// <summary>`18` §7.9's <c>"PLAYER_CHOICE"</c>.</summary>
    public static DieFaceIndex PlayerChoice { get; } = new(null);

    /// <summary>A numbered face, 1..6.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Outside 1..6 — `04` §1 gives the die six faces.</exception>
    public static DieFaceIndex At(int face) =>
        face is >= MinFace and <= MaxFace
            ? new DieFaceIndex(face)
            : throw new ArgumentOutOfRangeException(
                nameof(face), face,
                $"04 §1 gives the die {MaxFace} faces, numbered {MinFace}..{MaxFace}.");

    /// <summary>The DSL token this index is written as in JSON.</summary>
    public override string ToString() =>
        IsPlayerChoice
            ? PlayerChoiceToken
            : Face!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
