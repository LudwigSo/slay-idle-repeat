namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// Which face a <see cref="EffectOp.MODIFY_DIE_FACE"/> replaces: a numbered face, or
/// <c>"PLAYER_CHOICE"</c>.
/// </summary>
/// <remarks>
/// <para>
/// Assumption, recorded rather than hidden: only the <c>"PLAYER_CHOICE"</c> form is authored
/// verbatim, so the numbered form is taken to be 1-based, 1..6, matching the die's six faces.
/// </para>
/// <para>
/// <c>default</c> is a distinct, invalid state, the same rule every enum in the DSL follows by
/// having no <c>0</c> member. Storing "player choice" as a sentinel rather than as the absence of a
/// face number is what makes that possible: a zero-initialised struct would otherwise read as
/// <see cref="PlayerChoice"/> and a forgotten assignment would silently become a real instruction
/// to the run controller.
/// </para>
/// </remarks>
public readonly record struct DieFaceIndex
{
    /// <summary>The token for the player-chosen face.</summary>
    public const string PlayerChoiceToken = "PLAYER_CHOICE";

    /// <summary>The lowest numbered face.</summary>
    public const int MinFace = 1;

    /// <summary>The highest numbered face.</summary>
    public const int MaxFace = 6;

    /// <summary>The sentinel <see cref="_face"/> holds for <see cref="PlayerChoice"/>.</summary>
    private const int PlayerChoiceSentinel = -1;

    /// <summary>0 for an unset (<c>default</c>) value, -1 for player choice, otherwise 1..6.</summary>
    private readonly int _face;

    private DieFaceIndex(int face)
    {
        _face = face;
    }

    /// <summary>The 1..6 face number; <c>null</c> for <see cref="PlayerChoice"/> and for <c>default</c>.</summary>
    public int? Face => _face >= MinFace ? _face : null;

    /// <summary>True when the player picks the face at resolution time.</summary>
    public bool IsPlayerChoice => _face == PlayerChoiceSentinel;

    /// <summary>True for a <c>default</c> value, which names no face at all.</summary>
    public bool IsUnset => _face == 0;

    /// <summary>The player-chosen face.</summary>
    public static DieFaceIndex PlayerChoice { get; } = new(PlayerChoiceSentinel);

    /// <summary>A numbered face, 1..6.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Outside 1..6 — the die has six faces.</exception>
    public static DieFaceIndex At(int face) =>
        face is >= MinFace and <= MaxFace
            ? new DieFaceIndex(face)
            : throw new ArgumentOutOfRangeException(
                nameof(face), face,
                $"04 §1 gives the die {MaxFace} faces, numbered {MinFace}..{MaxFace}.");

    /// <summary>The DSL token this index is written as in JSON.</summary>
    /// <exception cref="InvalidOperationException">
    /// The value is <c>default</c>, which names no face. Throws rather than rendering something
    /// plausible: a forgotten assignment that printed <c>PLAYER_CHOICE</c> would be a real
    /// instruction to the run controller, arrived at by accident.
    /// </exception>
    public override string ToString() => _face switch
    {
        PlayerChoiceSentinel => PlayerChoiceToken,
        >= MinFace => _face.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException(
            "a default DieFaceIndex names no face. Build one with DieFaceIndex.At or " +
            "DieFaceIndex.PlayerChoice."),
    };
}
