namespace SlayIdleRepeat.Core.Primitives;

/// <summary>Why a candidate hero name was refused. Exactly one reason per refusal.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>The reasons are told apart rather than collapsed, and that is the whole purpose of this
/// type.</b> A name can be refused for being blank, for being too long, for carrying a character no
/// name may carry, for matching the English word list or for matching the German one — and a rule
/// that answered only "refused" would let four of those five break while a test asserting the fifth
/// stayed green. The refusal a caller receives names which check fired.
/// </para>
/// <para>
/// Numbered from 1 with no <c>0</c> member, like every other closed vocabulary here, so an
/// uninitialised value cannot read as <see cref="BLANK"/>.
/// </para>
/// <para>
/// ⚠️ It is <b>not</b> a <c>RejectionReason</c> and must not become one. `14` §16.2's table is the
/// wire vocabulary and carries no name-specific row; a hero name is refused with the domain-tier
/// value the calling command chooses, and this is the diagnosis underneath it.
/// </para>
/// </remarks>
public enum HeroNameRefusal
{
    /// <summary>Null, empty, or nothing but whitespace. A name that renders as nothing on every screen that shows one.</summary>
    BLANK = 1,

    /// <summary>Longer than the authored limit, measured in text elements rather than UTF-16 code units.</summary>
    TOO_LONG = 2,

    /// <summary>Carries a control or format character — invisible in every field, and able to reorder or truncate the text around it.</summary>
    DISALLOWED_CHARACTER = 3,

    /// <summary>Matched the English word list.</summary>
    PROFANE_EN = 4,

    /// <summary>Matched the German word list.</summary>
    PROFANE_DE = 5,
}
