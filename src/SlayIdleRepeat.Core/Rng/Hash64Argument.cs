using System.Globalization;

namespace SlayIdleRepeat.Core.Rng;

/// <summary>
/// One argument of a <see cref="Hash64"/> call, in the canonical encoding of `14` §8.0.
/// </summary>
/// <remarks>
/// <para>
/// The encoding has exactly two shapes — a 64-bit integer or a length-prefixed UTF-8 string —
/// so this carries exactly two. Everything integral (<see cref="ulong"/>, <see cref="long"/>,
/// <see cref="int"/>, any enum) is widened to 64 bits at the boundary, which is why an
/// <see cref="int"/> of -1 and a <see cref="ulong"/> of <see cref="ulong.MaxValue"/> are the
/// same argument: the encoding is by value, not by declared type.
/// </para>
/// <para>
/// The conversions are implicit so a call site reads like the specification —
/// <c>Hash64.Of(runSeed, "combat", battleIndex)</c> — rather than like a serialisation
/// exercise. That matters more here than it usually would: this expression appears in the
/// design documents in exactly that form, and a reader has to be able to check the code
/// against it at a glance.
/// </para>
/// </remarks>
public readonly struct Hash64Argument : IEquatable<Hash64Argument>
{
    private readonly long _integer;
    private readonly string? _text;

    private Hash64Argument(long integer)
    {
        _integer = integer;
        _text = null;
    }

    private Hash64Argument(string text)
    {
        _integer = 0;
        _text = text;
    }

    /// <summary>True when this argument encodes as a length-prefixed string rather than as 8 bytes.</summary>
    internal bool IsText => _text is not null;

    /// <summary>The string payload. Readable only when <see cref="IsText"/>.</summary>
    /// <remarks>
    /// It throws rather than falling back to the empty string. An integer argument read as text
    /// would encode as a four-byte zero count — a shorter buffer and a different hash that is
    /// perfectly stable and silently wrong, which is the one failure mode this file exists to
    /// prevent. Loud beats plausible.
    /// </remarks>
    /// <exception cref="InvalidOperationException">This argument encodes as an integer.</exception>
    internal string Text => _text ?? throw new InvalidOperationException(
        "This argument encodes as an integer, not as text. Check IsText before reading Text.");

    /// <summary>The value widened to 64 bits. Only meaningful when <see cref="IsText"/> is false.</summary>
    internal long Integer => _integer;

    /// <summary>A <see cref="ulong"/> — reinterpreted, never clamped, so the full 64-bit range survives.</summary>
    public static implicit operator Hash64Argument(ulong value) => new(unchecked((long)value));

    /// <summary>A <see cref="long"/>, as itself.</summary>
    public static implicit operator Hash64Argument(long value) => new(value);

    /// <summary>An <see cref="int"/>, 🔒 <b>sign-extended</b> to 64 bits — never zero-extended.</summary>
    public static implicit operator Hash64Argument(int value) => new(value);

    /// <summary>A string, encoded later as a 4-byte little-endian UTF-8 byte count then its UTF-8 bytes.</summary>
    /// <exception cref="ArgumentNullException">The value is null — null has no canonical encoding.</exception>
    public static implicit operator Hash64Argument(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Hash64Argument(value);
    }

    /// <summary>
    /// Any enum, widened through its underlying integral type — signed ones by sign extension,
    /// unsigned ones by zero extension, matching the plain integer rules above.
    /// </summary>
    public static implicit operator Hash64Argument(Enum value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Hash64Argument(Widen(value));
    }

    /// <summary>
    /// Two arguments are equal when they are the same shape — both integral or both text — and
    /// the same value, strings compared ordinally.
    /// </summary>
    /// <remarks>
    /// Deliberately <b>not</b> "equal when they encode to the same bytes": the canonical encoding
    /// carries no type tag, so the four-byte string <c>"abcd"</c> and the integer
    /// <c>0x6463626100000004</c> produce the same eight bytes while being different arguments.
    /// Equality here is about the argument, and the encoding's ambiguity is `14` §8.0's to own.
    /// </remarks>
    public bool Equals(Hash64Argument other) =>
        _text is null
            ? other._text is null && _integer == other._integer
            : other._text is not null && string.Equals(_text, other._text, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Hash64Argument other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        _text is null ? _integer.GetHashCode() : StringComparer.Ordinal.GetHashCode(_text);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Hash64Argument left, Hash64Argument right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Hash64Argument left, Hash64Argument right) => !left.Equals(right);

    /// <summary>A readable form for assertion messages — not part of the encoding.</summary>
    public override string ToString() =>
        _text is null ? _integer.ToString(CultureInfo.InvariantCulture) : $"\"{_text}\"";

    /// <summary>
    /// Widens an enum through its underlying type. An enum boxes to its own type but unboxes to
    /// its underlying one, which is what makes the casts below legal and exact.
    /// </summary>
    /// <remarks>
    /// The unboxing costs one boxed value per enum argument. That is affordable because enums
    /// only ever reach <see cref="Hash64"/> through seed derivation — a handful of calls per run.
    /// The hot path, a draw, goes through <c>Hash64.Of(ulong, string, ulong)</c> and builds no
    /// <see cref="Hash64Argument"/> at all.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The enum's underlying type is not one the canonical encoding covers. Unreachable from C#,
    /// which admits only the eight integral bases below, but reachable from IL.
    /// </exception>
    private static long Widen(Enum value) => Type.GetTypeCode(value.GetType()) switch
    {
        TypeCode.SByte => (sbyte)(object)value,
        TypeCode.Byte => (byte)(object)value,
        TypeCode.Int16 => (short)(object)value,
        TypeCode.UInt16 => (ushort)(object)value,
        TypeCode.Int32 => (int)(object)value,
        TypeCode.UInt32 => (uint)(object)value,
        TypeCode.Int64 => (long)(object)value,
        TypeCode.UInt64 => unchecked((long)(ulong)(object)value),
        _ => throw new ArgumentException(
            $"{value.GetType()} has an underlying type the canonical encoding of 14 §8.0 does not cover.",
            nameof(value)),
    };
}
