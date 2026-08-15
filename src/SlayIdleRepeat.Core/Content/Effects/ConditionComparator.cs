namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>The seven comparators: <c>eq · neq · lt · lte · gt · gte · between</c>.</summary>
/// <remarks>
/// The JSON spelling is lower-case (<c>"op": "gte"</c>), unlike every other enum in the DSL, which
/// is <c>SCREAMING_SNAKE</c>. The member names here are upper-case to match the repository's enum
/// convention; a parity test asserts the two agree case-insensitively so the mapping cannot
/// silently drift.
/// </remarks>
public enum ConditionComparator
{
    /// <summary><c>eq</c> — equal.</summary>
    EQ = 1,

    /// <summary><c>neq</c> — not equal.</summary>
    NEQ = 2,

    /// <summary><c>lt</c> — strictly less than.</summary>
    LT = 3,

    /// <summary><c>lte</c> — less than or equal.</summary>
    LTE = 4,

    /// <summary><c>gt</c> — strictly greater than.</summary>
    GT = 5,

    /// <summary><c>gte</c> — greater than or equal.</summary>
    GTE = 6,

    /// <summary><c>between</c> — inside an inclusive range.</summary>
    BETWEEN = 7,
}
