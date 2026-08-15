namespace SlayIdleRepeat.Core.Primitives;

/// <summary>The identity of the <c>Player</c> aggregate root.</summary>
/// <param name="Value">The identifier text. Never null, empty or whitespace.</param>
/// <remarks>
/// A distinct type rather than a bare <c>string</c>: a <see cref="PlayerId"/> handed to something
/// expecting a <see cref="RunId"/> is a compile error instead of a lookup against the wrong
/// aggregate. No conversion operator is declared, not even an explicit one to <c>string</c>, because
/// a conversion is the hole through which that protection leaks back out.
/// <para>
/// A <c>readonly record struct</c> rather than the sealed-class-with-private-constructor idiom
/// <c>ContentVersion</c> uses: <c>CanonicalStateWriter</c> only recognises a positional record with
/// exactly one public constructor and matching public properties — a private constructor gets no
/// canonical encoding at all. This shape also carries no presence byte, so wrapping a snapshot field
/// in an id costs zero bytes.
/// </para>
/// <para>
/// The capital in <c>Value</c> is load-bearing: the writer matches constructor parameters to
/// properties by case-sensitive name. Lower-casing the parameter would make the compiler emit a
/// second public property, and the writer refuses that superset outright.
/// </para>
/// <para>
/// <c>default(PlayerId)</c> bypasses the constructor and holds a null <see cref="Value"/> — the seam
/// that validates a persisted id is <c>Rehydrate</c>, not this type.
/// </para>
/// <para>
/// Equality is the compiler-generated record equality over one string — ordinal. A culture-aware
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
    /// The <c>??</c> covers <c>default(PlayerId)</c>, where <see cref="Value"/> is null: a bare
    /// <c>=&gt; Value</c> would return null from a method every caller types as non-null. The marker
    /// cannot be mistaken for a real identifier and greps easily.
    /// </remarks>
    public override string ToString() => Value ?? $"default({nameof(PlayerId)})";
}
