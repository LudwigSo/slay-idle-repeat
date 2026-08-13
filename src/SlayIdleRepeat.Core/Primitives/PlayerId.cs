namespace SlayIdleRepeat.Core.Primitives;

/// <summary>
/// The identity of the <c>Player</c> aggregate root (`30` §4).
/// </summary>
/// <param name="Value">The identifier text. Never null, empty or whitespace.</param>
/// <remarks>
/// <para>
/// A distinct type rather than a bare <c>string</c>, and that is the whole point: a
/// <see cref="PlayerId"/> handed to something expecting a <see cref="RunId"/> is a compile error
/// instead of a lookup against the wrong aggregate. Nothing here declares a conversion operator —
/// not even an explicit one to <c>string</c> — because a conversion is the hole through which that
/// protection leaks back out.
/// </para>
/// <para>
/// 🔒 <b>Why a <c>readonly record struct</c> over the sealed-class-with-private-constructor idiom
/// <c>ContentVersion</c> uses.</b> <c>CanonicalStateWriter</c> dispatches over a closed allowlist
/// (`14` §16.6) and recognises a positional record by <i>exactly one public constructor</i>, every
/// parameter matched by a public readable property of the same name and type, and no public
/// property beyond them. A private constructor fails that test, so a <c>ContentVersion</c>-shaped
/// id would have <b>no canonical encoding</b> — discovered by whoever first put one in a snapshot,
/// not by whoever chose the shape. This shape encodes as the string it wraps, needs no new
/// allowlist entry, and — being a value type — carries no presence byte, so wrapping a snapshot
/// field in an id costs zero bytes.
/// </para>
/// <para>
/// 🔒 <b>The capital in <c>Value</c> is load-bearing</b>, and it is the same test that makes it so:
/// the writer matches constructor parameters to properties by <b>case-sensitive</b> name. Lower-case
/// the parameter alone and the compiler emits a second public property beside the one declared
/// below; the property set is then a superset of the parameter list, which the writer refuses, and
/// this id joins <c>ContentVersion</c> in having no canonical encoding at all.
/// </para>
/// <para>
/// ⚠️ Two consequences of that choice, both deliberate and neither hidden. <c>default(PlayerId)</c>
/// bypasses the constructor and holds a null <see cref="Value"/> — a struct's default runs no code,
/// so the seam that validates a persisted id is <c>Rehydrate</c> (`30` §11.3), not this type. And
/// <c>CanonicalStateWriter.KeyOrderFor</c> defines an ascending order for strings and numeric ids
/// only, so an <c>IReadOnlyDictionary&lt;PlayerId, …&gt;</c> in a snapshot is refused rather than
/// hashed in an undefined order; no M1 snapshot needs one, and the writer is the right place to fix
/// that if one ever does.
/// </para>
/// <para>
/// Equality is the compiler-generated record equality over one string, which is
/// <see cref="EqualityComparer{T}"/>'s — ordinal. That matters more than it looks: a culture-aware
/// comparison would make two distinct player rows the same player on an ICU host and not on a
/// globalization-invariant one.
/// </para>
/// </remarks>
public readonly record struct PlayerId(string Value)
{
    /// <summary>
    /// The identifier text. Never null, empty or whitespace — except on
    /// <c>default(PlayerId)</c>, whose backing field no constructor ever assigned.
    /// </summary>
    public string Value { get; } = IdText.Require(Value, nameof(PlayerId));

    /// <summary>The identifier text, so a log line reads the id rather than the record's shape.</summary>
    /// <remarks>
    /// ⚠️ The <c>??</c> is the <c>default(PlayerId)</c> case above, and it is not defensive
    /// padding: <see cref="Value"/> is null there, so a bare <c>=&gt; Value</c> returns <c>null</c>
    /// from a method the language and every caller type as non-null — <c>id.ToString().Length</c>
    /// is a <see cref="NullReferenceException"/> and <c>$"{id}"</c> is the empty string. Both fail
    /// on the diagnostic path, at the one moment the reader needs the line to say the id was never
    /// set. The marker says exactly that and cannot be mistaken for an identifier: no id contains
    /// parentheses, and it greps.
    /// </remarks>
    public override string ToString() => Value ?? $"default({nameof(PlayerId)})";
}
