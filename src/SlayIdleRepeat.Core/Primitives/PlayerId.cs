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
    /// <inheritdoc cref="PlayerId"/>
    public string Value { get; } = IdText.Require(Value, nameof(PlayerId));

    /// <summary>The identifier text, so a log line reads the id rather than the record's shape.</summary>
    public override string ToString() => Value;
}
