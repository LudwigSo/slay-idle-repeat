using System.Buffers.Binary;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace SlayIdleRepeat.Core.Model.Snapshots;

/// <summary>
/// 🔒 `14` §16.6 — the project's <b>one</b> canonical state serialiser: <b>FNV-1a, 64-bit</b>
/// over the canonical byte encoding of the public snapshot DTOs.
/// </summary>
/// <remarks>
/// <para>
/// §16.6 opens with the reason this class exists: <i>"A second serialiser producing 'almost the
/// same bytes' is how parity tests rot; there is exactly one."</i> Everything that turns state
/// into bytes in this game comes through here — the per-command <c>stateHash</c> (§2.3, §16.2),
/// client-mirror verification (§2.4), the battle <c>LogHash</c> (§8.2, §9), the parity and
/// reconnect chaos tests (§13), and the cross-platform determinism CI (§8.2).
/// </para>
/// <para><b>Why it lives in <c>Model/Snapshots/</c>.</b> `23` §3 and `30` §11.4 both place this
/// directory under <c>Model/</c> as the persistence boundary — <i>"public persistence DTOs +
/// <c>Rehydrate()</c>"</i>. §16.6's declared input is exactly those DTOs, and
/// <see cref="SnapshotSchema"/> — the version the field-order pin is keyed to — is its neighbour.
/// The serialisation contract stays in one directory, and the existing internal-layering rule
/// (<c>Model</c> may not reference <c>Rules</c>/<c>Handlers</c>) governs it for free, which is
/// exactly the constraint a pure encoder should be under.</para>
/// <para>
/// The canonical encoding, per the §16.6 table:
/// </para>
/// <list type="table">
///   <item>
///     <term>Integers</term>
///     <description>widened to 64 bits (signed sign-extended, unsigned zero-extended), 8 bytes little-endian</description>
///   </item>
///   <item><term>Booleans</term><description>one byte, <c>0x00</c> or <c>0x01</c></description></item>
///   <item><term>Enums</term><description>their numeric value, widened through the underlying type, 8 bytes</description></item>
///   <item><term>Strings</term><description>a 4-byte little-endian UTF-8 <b>byte</b> count, then the UTF-8 bytes</description></item>
///   <item><term>Timestamps</term><description>Unix milliseconds UTC, 8 bytes</description></item>
///   <item><term>Optionals</term><description>a presence byte <c>0x00</c>/<c>0x01</c>, then the value</description></item>
///   <item><term>Doubles</term><description>the IEEE-754 bit pattern of the already-rounded value, 8 bytes little-endian</description></item>
///   <item><term>Lists</term><description>a 4-byte little-endian element count, then the elements in stored order</description></item>
///   <item><term>Dictionaries</term><description>a 4-byte little-endian entry count, then the entries in ascending key order</description></item>
///   <item><term>Records</term><description>their fields in declaration order, depth-first</description></item>
/// </list>
/// <para>
/// <b>Three decisions the §16.6 table leaves open, made here and pinned by the committed
/// reference-vector table:</b>
/// </para>
/// <list type="number">
///   <item>
///     A <b>4-byte little-endian element count</b> precedes a list's elements and a dictionary's
///     entries. §16.6 does not say so, but without it <c>[[1],[2,3]]</c> and <c>[[1,2],[3]]</c>
///     encode to the same bytes — two genuinely different states sharing a <c>stateHash</c>, the
///     one failure a state hash may never have. The 4-byte little-endian count mirrors §16.6's
///     own string rule.
///   </item>
///   <item>
///     <b>Every nullable-capable slot</b> — any reference type, any <see cref="Nullable{T}"/> —
///     carries the presence byte; the <b>root</b> snapshot does not, being a required argument.
///     Reading "optional" off C#'s nullable <i>annotations</i> instead would make the byte stream
///     depend on compiler metadata that trimming and AOT are free to drop, which is a silent
///     cross-platform determinism break — the exact failure §16.6 exists to prevent.
///   </item>
///   <item>
///     A snapshot must be a <b>positional record</b>: exactly one public constructor, every
///     parameter mapping to a public readable property of the same name and type. That is what
///     makes "declaration order" mechanical — <see cref="MethodBase.GetParameters"/> is
///     guaranteed to be declaration order, while property order is not guaranteed at all.
///   </item>
/// </list>
/// <para>
/// 🔒 <b>How <i>"no unordered container is ever hashed as-is"</i> is enforced.</b> Not by
/// convention: <see cref="WriteValue"/> dispatches over a <b>closed allowlist</b> and has no
/// <c>IEnumerable</c> fallback. A <see cref="HashSet{T}"/>, an <see cref="ISet{T}"/>, a bare
/// <see cref="IEnumerable{T}"/> or <see cref="ICollection{T}"/> slot, and a dictionary reached
/// through anything but <see cref="IReadOnlyDictionary{TKey, TValue}"/> all fall through to a
/// terminal <see cref="NotSupportedException"/>. The dictionary branch itself has no unsorted
/// path — it materialises the entries and sorts them with a comparer chosen by key type, so
/// there is no code path in this file that can write a map in insertion order.
/// </para>
/// <para>
/// FNV-1a is written out here rather than taken from a package because
/// <c>SlayIdleRepeat.Core</c> references nothing at all (`23` §2.1). The compensating control is
/// the committed reference-vector table in <c>SlayIdleRepeat.Core.Tests</c>, whose FNV-1a rows
/// are Landon Curt Noll's published <c>test_fnv.c</c> vectors and whose encoding rows come from
/// an independent transcription of the table above.
/// </para>
/// <para>
/// It is not cryptographic and does not need to be: <c>stateHash</c> detects divergence between
/// two honest computations of the same state. Anti-cheat is server authority (§9), not this hash.
/// </para>
/// </remarks>
public static class CanonicalStateWriter
{
    /// <summary>
    /// 🔒 The algorithm name every hash carries, per `14` §16.6: <c>"fnv1a:"</c>.
    /// </summary>
    /// <remarks>
    /// The prefix exists so the algorithm can only ever be rotated deliberately and visibly — a
    /// bare 16-hex string would let a future change slip past every reader and every log.
    /// </remarks>
    public const string AlgorithmPrefix = "fnv1a:";

    /// <summary>The FNV-1a 64 offset basis. 🔒 Pinned by `14` §16.6 via the algorithm name.</summary>
    private const ulong OffsetBasis = 0xCBF29CE484222325UL;

    /// <summary>The FNV-1a 64 prime.</summary>
    private const ulong Prime = 0x100000001B3UL;

    /// <summary>Bytes a widened integral value, a timestamp or a double occupies.</summary>
    private const int ScalarBytes = 8;

    /// <summary>Bytes a string's byte count or a collection's element count occupies.</summary>
    private const int CountPrefixBytes = 4;

    /// <summary>The buffer a hash starts with. Grown by doubling; never pooled, never shared.</summary>
    private const int InitialBufferBytes = 256;

    /// <summary>
    /// How deep the descent may go before it gives up and says so.
    /// </summary>
    /// <remarks>
    /// A snapshot is a tree, so any real one is far shallower than this. The limit exists so a
    /// snapshot that accidentally refers to itself produces a diagnosable failure naming the
    /// type, rather than a stack overflow that takes the process with it and explains nothing.
    /// </remarks>
    private const int MaxDepth = 64;

    /// <summary>The specification this file implements, quoted in every refusal.</summary>
    private const string Specification = "14 §16.6";

    /// <summary>
    /// UTF-8 without a byte-order mark. Named explicitly rather than taken from
    /// <see cref="Encoding.UTF8"/> so nothing about this encoding is inherited from a static
    /// whose configuration could be read two ways.
    /// </summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// 🔒 The <c>stateHash</c> of a <b>run</b> command: <c>PlayerSnapshot</c> then
    /// <c>RunSnapshot</c>, concatenated, per `14` §16.6.
    /// </summary>
    /// <param name="playerSnapshot">The player snapshot, written first.</param>
    /// <param name="runSnapshot">The run snapshot, written second.</param>
    /// <returns><c>"fnv1a:"</c> followed by 16 lowercase hexadecimal characters.</returns>
    /// <remarks>
    /// The concatenation lives here, in a named mode, precisely so no caller ever performs it —
    /// two callers concatenating "the same way" is how the one serialiser becomes two. The
    /// parameters are <see cref="object"/> only until <c>M1-04</c>/<c>M1-05</c> declare
    /// <c>PlayerSnapshot</c> and <c>RunSnapshot</c>; tightening them then changes no byte.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Either snapshot is null.</exception>
    /// <exception cref="NotSupportedException">Some part of the state has no canonical encoding.</exception>
    public static string HashRunCommandState(object playerSnapshot, object runSnapshot)
    {
        ArgumentNullException.ThrowIfNull(playerSnapshot);
        ArgumentNullException.ThrowIfNull(runSnapshot);

        var buffer = new CanonicalBuffer();
        WriteRoot(buffer, playerSnapshot);
        WriteRoot(buffer, runSnapshot);

        return ToWireForm(Fnv1a64(buffer.Written));
    }

    /// <summary>
    /// 🔒 The <c>stateHash</c> of a <b>meta</b> command: <c>PlayerSnapshot</c> alone, per
    /// `14` §16.6.
    /// </summary>
    /// <param name="playerSnapshot">The player snapshot.</param>
    /// <returns><c>"fnv1a:"</c> followed by 16 lowercase hexadecimal characters.</returns>
    /// <exception cref="ArgumentNullException">The snapshot is null.</exception>
    /// <exception cref="NotSupportedException">Some part of the state has no canonical encoding.</exception>
    public static string HashMetaCommandState(object playerSnapshot)
    {
        ArgumentNullException.ThrowIfNull(playerSnapshot);

        var buffer = new CanonicalBuffer();
        WriteRoot(buffer, playerSnapshot);

        return ToWireForm(Fnv1a64(buffer.Written));
    }

    /// <summary>The canonical bytes of one snapshot, exactly as the hash sees them.</summary>
    /// <remarks>
    /// <c>internal</c> because the public surface of this class is the two hashing modes; a public
    /// bytes-out door would be an invitation to hash them some other way, which is the one thing
    /// §16.6 forbids. The domain test suite reaches it through the <c>InternalsVisibleTo</c> that
    /// `30` §11.3 sanctions, and asserts the encoding rule by rule.
    /// </remarks>
    internal static byte[] CanonicalBytes(object root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var buffer = new CanonicalBuffer();
        WriteRoot(buffer, root);

        return buffer.ToArray();
    }

    /// <summary>FNV-1a 64 over raw bytes, exactly as the algorithm defines it.</summary>
    /// <remarks>
    /// 🔒 <b>a</b>, not FNV-1: the byte is XORed into the hash <i>before</i> the multiply. The two
    /// orderings produce completely different values, and only the published known-answer vectors
    /// can tell you which one you implemented.
    /// </remarks>
    internal static ulong Fnv1a64(ReadOnlySpan<byte> data)
    {
        // FNV relies on 64-bit wraparound. `unchecked` is the default, but this hash must not
        // become a runtime exception if a future build ever flips that switch.
        unchecked
        {
            var hash = OffsetBasis;
            foreach (var b in data)
            {
                hash ^= b;
                hash *= Prime;
            }

            return hash;
        }
    }

    /// <summary>
    /// Whether a type has the positional-record shape this encoding requires — and therefore a
    /// declaration order reflection can be trusted to reproduce.
    /// </summary>
    /// <remarks>
    /// The subject-set predicate behind the <c>SchemaVersion</c> field-order pin, exposed so the
    /// pin's idea of "a snapshot record" is the same closed set <see cref="CanonicalBytes"/> will
    /// encode rather than a second, drifting definition.
    /// </remarks>
    internal static bool IsCanonicalRecord(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return PrimaryConstructor(type) is not null;
    }

    /// <summary>
    /// 🔒 The ordered field list `14` §16.6 pins per <c>SchemaVersion</c>: every scalar slot the
    /// encoder will write, in write order, as <c>path:declaredType</c>.
    /// </summary>
    /// <param name="rootType">A snapshot record type.</param>
    /// <remarks>
    /// <para>
    /// Produced by the writer's <b>own</b> traversal, so the pinned list cannot drift from the
    /// bytes it guards. The path notation names the structure it descends through:
    /// <c>Outer.Inner</c> for a nested record, <c>Items[]</c> for a list's element slot,
    /// <c>Map{key}</c> and <c>Map{value}</c> for a dictionary's — so wrapping a field in a
    /// collection, nesting it, or making it optional each read as the shape change they are.
    /// </para>
    /// <para>
    /// Structural slots contribute no entry of their own: a presence byte and an element count
    /// are consequences of the declared type, and the declared type is already in the entry.
    /// </para>
    /// </remarks>
    /// <exception cref="NotSupportedException">Some part of the type has no canonical encoding.</exception>
    internal static IReadOnlyList<string> CanonicalFieldOrder(Type rootType)
    {
        ArgumentNullException.ThrowIfNull(rootType);

        var paths = new List<string>();
        DescribeRecord(rootType, string.Empty, paths, 0);

        return paths;
    }

    /// <summary>🔒 <c>"fnv1a:"</c> + 16 lowercase hex characters, zero-padded, never truncated.</summary>
    private static string ToWireForm(ulong hash) =>
        AlgorithmPrefix + hash.ToString("x16", CultureInfo.InvariantCulture);

    /// <summary>
    /// The root snapshot: required, so it carries no presence byte. Its declared type is its
    /// runtime type — there is no slot above it to have declared anything else.
    /// </summary>
    private static void WriteRoot(CanonicalBuffer buffer, object root) =>
        WriteValue(buffer, root, root.GetType(), 0);

    /// <summary>
    /// One slot of the encoding: the presence byte where the declared type admits absence, then
    /// the value.
    /// </summary>
    private static void WriteSlot(CanonicalBuffer buffer, object? value, Type declaredType, int depth)
    {
        var underlying = Nullable.GetUnderlyingType(declaredType);

        if (underlying is null && declaredType.IsValueType)
        {
            // A non-nullable value type cannot be absent, so it carries no presence byte.
            WriteValue(buffer, value!, declaredType, depth);
            return;
        }

        if (value is null)
        {
            buffer.WriteByte(0x00);
            return;
        }

        buffer.WriteByte(0x01);
        WriteValue(buffer, value, underlying ?? declaredType, depth);
    }

    /// <summary>
    /// 🔒 The closed allowlist. Every shape §16.6 pins has a branch; everything else falls through
    /// to the refusal at the bottom, which is what makes an unordered container unhashable rather
    /// than merely discouraged.
    /// </summary>
    private static void WriteValue(CanonicalBuffer buffer, object value, Type type, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new NotSupportedException(
                $"A snapshot nested deeper than {MaxDepth} levels has no canonical encoding " +
                $"({Specification}). A snapshot is a tree; this nesting depth means one refers to " +
                "itself, directly or through a collection.");
        }

        if (type.IsEnum)
        {
            WriteWidenedEnum(buffer, value, type);
            return;
        }

        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Boolean:
                buffer.WriteByte((bool)value ? (byte)0x01 : (byte)0x00);
                return;

            case TypeCode.SByte:
                WriteSigned(buffer, (sbyte)value);
                return;

            case TypeCode.Int16:
                WriteSigned(buffer, (short)value);
                return;

            case TypeCode.Int32:
                WriteSigned(buffer, (int)value);
                return;

            case TypeCode.Int64:
                WriteSigned(buffer, (long)value);
                return;

            case TypeCode.Byte:
                WriteUnsigned(buffer, (byte)value);
                return;

            case TypeCode.UInt16:
                WriteUnsigned(buffer, (ushort)value);
                return;

            case TypeCode.UInt32:
                WriteUnsigned(buffer, (uint)value);
                return;

            case TypeCode.UInt64:
                WriteUnsigned(buffer, (ulong)value);
                return;

            case TypeCode.Double:
                WriteDouble(buffer, (double)value);
                return;

            case TypeCode.String:
                WriteString(buffer, (string)value);
                return;

            case TypeCode.DateTime:
                WriteUtcDateTime(buffer, (DateTime)value);
                return;

            default:
                break;
        }

        if (type == typeof(DateTimeOffset))
        {
            WriteSigned(buffer, ((DateTimeOffset)value).ToUnixTimeMilliseconds());
            return;
        }

        if (TryGetDictionaryTypes(type, out var keyType, out var valueType))
        {
            WriteDictionary(buffer, value, keyType, valueType, depth);
            return;
        }

        if (TryGetListElementType(type, out var elementType))
        {
            WriteList(buffer, value, elementType, depth);
            return;
        }

        if (IsCanonicalRecord(type))
        {
            WriteRecord(buffer, value, type, depth);
            return;
        }

        throw Unsupported(type);
    }

    /// <summary>A record: its fields in declaration order, depth-first.</summary>
    private static void WriteRecord(CanonicalBuffer buffer, object value, Type type, int depth)
    {
        // 🔒 The pinned field list belongs to the DECLARED type. A subclass in a base-typed slot
        // carries fields the SchemaVersion pin never saw, so it has no canonical encoding here.
        var runtimeType = value.GetType();
        if (runtimeType != type)
        {
            throw new NotSupportedException(
                $"A {type.FullName} slot holds a {runtimeType.FullName}, whose extra state is " +
                $"outside the field list pinned for SchemaVersion {SnapshotSchema.SchemaVersion} " +
                $"({Specification}). Snapshot records are not polymorphic.");
        }

        foreach (var parameter in PrimaryConstructor(type)!.GetParameters())
        {
            var property = type.GetProperty(parameter.Name!, BindingFlags.Public | BindingFlags.Instance)!;
            WriteSlot(buffer, property.GetValue(value), parameter.ParameterType, depth + 1);
        }
    }

    /// <summary>A list: a 4-byte little-endian element count, then the elements in stored order.</summary>
    private static void WriteList(CanonicalBuffer buffer, object value, Type elementType, int depth)
    {
        var elements = new List<object?>();
        foreach (var element in (IEnumerable)value)
        {
            elements.Add(element);
        }

        WriteCount(buffer, elements.Count);
        foreach (var element in elements)
        {
            WriteSlot(buffer, element, elementType, depth + 1);
        }
    }

    /// <summary>
    /// 🔒 A dictionary: a 4-byte little-endian entry count, then the entries in <b>ascending key
    /// order</b> — ordinal for strings, numeric for numeric ids.
    /// </summary>
    /// <remarks>
    /// There is deliberately no branch here that writes entries in the order the container
    /// happened to yield them, and none that consults the container's own comparer. The order is
    /// imposed, every time, or the encoding refuses the key type outright.
    /// </remarks>
    private static void WriteDictionary(
        CanonicalBuffer buffer, object value, Type keyType, Type valueType, int depth)
    {
        var entryType = typeof(KeyValuePair<,>).MakeGenericType(keyType, valueType);
        var keyProperty = entryType.GetProperty("Key")!;
        var valueProperty = entryType.GetProperty("Value")!;

        var entries = new List<(object Key, object? Value)>();
        foreach (var entry in (IEnumerable)value)
        {
            entries.Add((keyProperty.GetValue(entry)!, valueProperty.GetValue(entry)));
        }

        entries.Sort(KeyComparison(keyType));

        WriteCount(buffer, entries.Count);
        foreach (var (key, entryValue) in entries)
        {
            WriteSlot(buffer, key, keyType, depth + 1);
            WriteSlot(buffer, entryValue, valueType, depth + 1);
        }
    }

    /// <summary>
    /// The ascending key order for a key type: ordinal for strings, numeric for every integral
    /// and enum id. Any other key type has no defined order and is refused.
    /// </summary>
    private static Comparison<(object Key, object? Value)> KeyComparison(Type keyType)
    {
        if (keyType == typeof(string))
        {
            // 🔒 Ordinal, never culture-aware: a culture comparer makes the byte stream depend on
            // the machine's locale, which splits client from server on the first non-invariant device.
            return (left, right) => string.CompareOrdinal((string)left.Key, (string)right.Key);
        }

        var numericType = keyType.IsEnum ? Enum.GetUnderlyingType(keyType) : keyType;

        switch (Type.GetTypeCode(numericType))
        {
            case TypeCode.SByte:
            case TypeCode.Int16:
            case TypeCode.Int32:
            case TypeCode.Int64:
                return (left, right) => WidenSigned(left.Key, numericType).CompareTo(WidenSigned(right.Key, numericType));

            case TypeCode.Byte:
            case TypeCode.UInt16:
            case TypeCode.UInt32:
            case TypeCode.UInt64:
                // Compared as unsigned: a key above long.MaxValue is a large id, not a negative one.
                return (left, right) => WidenUnsigned(left.Key, numericType).CompareTo(WidenUnsigned(right.Key, numericType));

            default:
                throw new NotSupportedException(
                    $"{keyType.FullName} has no ascending key order — {Specification} defines one " +
                    "for strings (ordinal) and numeric ids (numeric), and no other. A map keyed by " +
                    "anything else cannot be hashed in a defined order.");
        }
    }

    /// <summary>An enum: its numeric value, widened through its underlying integral type.</summary>
    private static void WriteWidenedEnum(CanonicalBuffer buffer, object value, Type enumType)
    {
        var underlying = Enum.GetUnderlyingType(enumType);
        var numeric = Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);

        switch (Type.GetTypeCode(underlying))
        {
            case TypeCode.Byte:
            case TypeCode.UInt16:
            case TypeCode.UInt32:
            case TypeCode.UInt64:
                WriteUnsigned(buffer, WidenUnsigned(numeric, underlying));
                return;

            default:
                WriteSigned(buffer, WidenSigned(numeric, underlying));
                return;
        }
    }

    /// <summary>
    /// A double: the IEEE-754 bit pattern of the <b>stored</b> value, 8 bytes little-endian.
    /// </summary>
    /// <remarks>
    /// The writer never rounds and never normalises. `14` §8.2 rounds at every accumulation point;
    /// this method's only business with a double is to check that already happened — a writer that
    /// quietly rounded would hide the drift the determinism CI exists to catch, and one that
    /// normalised <c>-0.0</c> would silently edit state on its way out.
    /// </remarks>
    private static void WriteDouble(CanonicalBuffer buffer, double value)
    {
        if (double.IsNaN(value))
        {
            throw new NotSupportedException(
                $"NaN is forbidden in persisted state ({Specification}). It is not equal to " +
                "itself, so a state containing one can never be compared with another — fix the " +
                "calculation that produced it.");
        }

        if (double.IsInfinity(value))
        {
            throw new NotSupportedException(
                $"Infinities are forbidden in persisted state ({Specification}). An infinite stat " +
                "is an overflow upstream, not a value to serialise — fix the calculation that " +
                "produced it.");
        }

        // 🔒 §16.6 words this as a debug-build assert. It is always on here on purpose: this
        // project's CI builds and tests in Release (`.github/workflows/ci.yml`), so a
        // [Conditional("DEBUG")] guard would be silent in the one place it is meant to fire. The
        // cost is one Math.Round per double in a per-command hash.
        if (Math.Round(value, 4) != value)
        {
            throw new NotSupportedException(
                $"{value.ToString("R", CultureInfo.InvariantCulture)} is not rounded to 4 decimal " +
                $"places ({Specification}, `14` §8.2). Every double in persisted state is rounded " +
                "at its accumulation point; a value that reaches the writer unrounded means an " +
                "accumulation point is missing its Math.Round(x, 4), and platforms will disagree " +
                "about the low bits.");
        }

        WriteSigned(buffer, BitConverter.DoubleToInt64Bits(value));
    }

    /// <summary>A string: a 4-byte little-endian UTF-8 <b>byte</b> count, then the UTF-8 bytes.</summary>
    private static void WriteString(CanonicalBuffer buffer, string value)
    {
        var byteCount = Utf8.GetByteCount(value);

        WriteCount(buffer, byteCount);
        Utf8.GetBytes(value, buffer.Reserve(byteCount));
    }

    /// <summary>A <see cref="DateTime"/>: Unix milliseconds UTC, and only if it says it is UTC.</summary>
    private static void WriteUtcDateTime(CanonicalBuffer buffer, DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new NotSupportedException(
                $"A DateTime with Kind {value.Kind} has no canonical encoding ({Specification}) — " +
                "it names a different instant on a server in one timezone than on a handset in " +
                "another. Persist a Utc DateTime or a DateTimeOffset.");
        }

        WriteSigned(buffer, new DateTimeOffset(value).ToUnixTimeMilliseconds());
    }

    /// <summary>A signed integral value, sign-extended to 8 bytes little-endian.</summary>
    private static void WriteSigned(CanonicalBuffer buffer, long value) =>
        BinaryPrimitives.WriteInt64LittleEndian(buffer.Reserve(ScalarBytes), value);

    /// <summary>An unsigned integral value, zero-extended to 8 bytes little-endian.</summary>
    private static void WriteUnsigned(CanonicalBuffer buffer, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.Reserve(ScalarBytes), value);

    /// <summary>A byte count or an element count: 4 bytes little-endian.</summary>
    private static void WriteCount(CanonicalBuffer buffer, int count) =>
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Reserve(CountPrefixBytes), count);

    /// <summary>A boxed signed integral value as a <see cref="long"/>, by sign extension.</summary>
    private static long WidenSigned(object value, Type numericType) => Type.GetTypeCode(numericType) switch
    {
        TypeCode.SByte => (sbyte)value,
        TypeCode.Int16 => (short)value,
        TypeCode.Int32 => (int)value,
        TypeCode.Int64 => (long)value,
        _ => throw Unsupported(numericType),
    };

    /// <summary>A boxed unsigned integral value as a <see cref="ulong"/>, by zero extension.</summary>
    private static ulong WidenUnsigned(object value, Type numericType) => Type.GetTypeCode(numericType) switch
    {
        TypeCode.Byte => (byte)value,
        TypeCode.UInt16 => (ushort)value,
        TypeCode.UInt32 => (uint)value,
        TypeCode.UInt64 => (ulong)value,
        _ => throw Unsupported(numericType),
    };

    /// <summary>
    /// The single public constructor whose parameters all map to public readable properties of
    /// the same name and type — the positional-record shape — or <c>null</c> when the type has
    /// no such constructor and therefore no reflection-guaranteed declaration order.
    /// </summary>
    private static ConstructorInfo? PrimaryConstructor(Type type)
    {
        if (type.IsAbstract || type.IsInterface || type.IsArray || type.IsPointer ||
            type.IsEnum || type.IsPrimitive || type.ContainsGenericParameters || type == typeof(string))
        {
            return null;
        }

        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (constructors.Length != 1)
        {
            // Two public constructors means "the" declaration order is a guess. None means the
            // type is not constructed positionally at all.
            return null;
        }

        var parameters = constructors[0].GetParameters();
        if (parameters.Length == 0)
        {
            return null;
        }

        foreach (var parameter in parameters)
        {
            var property = type.GetProperty(parameter.Name!, BindingFlags.Public | BindingFlags.Instance);
            if (property is null || !property.CanRead || property.PropertyType != parameter.ParameterType)
            {
                return null;
            }
        }

        return constructors[0];
    }

    /// <summary>The <see cref="IReadOnlyDictionary{TKey, TValue}"/> a type is, or implements once.</summary>
    private static bool TryGetDictionaryTypes(Type type, out Type keyType, out Type valueType) =>
        TryGetClosedInterface(type, typeof(IReadOnlyDictionary<,>), out keyType, out valueType);

    /// <summary>The <see cref="IReadOnlyList{T}"/> a type is, or implements once.</summary>
    private static bool TryGetListElementType(Type type, out Type elementType) =>
        TryGetClosedInterface(type, typeof(IReadOnlyList<>), out elementType, out _);

    /// <summary>
    /// The single closed form of an open generic interface that a type is or implements.
    /// </summary>
    /// <remarks>
    /// "Single" matters: a type implementing two closings of the same interface offers two
    /// encodings, and the encoding must not pick one. Such a type falls through to the refusal.
    /// </remarks>
    private static bool TryGetClosedInterface(
        Type type, Type openInterface, out Type first, out Type second)
    {
        first = typeof(void);
        second = typeof(void);

        var candidates = new List<Type>();
        if (type.IsInterface && type.IsGenericType && type.GetGenericTypeDefinition() == openInterface)
        {
            candidates.Add(type);
        }

        foreach (var implemented in type.GetInterfaces())
        {
            if (implemented.IsGenericType && implemented.GetGenericTypeDefinition() == openInterface)
            {
                candidates.Add(implemented);
            }
        }

        if (candidates.Count != 1)
        {
            return false;
        }

        var arguments = candidates[0].GetGenericArguments();
        first = arguments[0];
        second = arguments.Length > 1 ? arguments[1] : typeof(void);
        return true;
    }

    /// <summary>The field paths of a record, depth-first, appended to <paramref name="paths"/>.</summary>
    private static void DescribeRecord(Type type, string prefix, List<string> paths, int depth)
    {
        var constructor = PrimaryConstructor(type) ?? throw Unsupported(type);

        foreach (var parameter in constructor.GetParameters())
        {
            DescribeSlot(parameter.ParameterType, prefix + parameter.Name, paths, depth + 1);
        }
    }

    /// <summary>The field paths of one slot: a leaf entry, or a descent into its structure.</summary>
    private static void DescribeSlot(Type declaredType, string path, List<string> paths, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new NotSupportedException(
                $"'{path}' nests deeper than {MaxDepth} levels ({Specification}). A snapshot is a " +
                "tree; this nesting depth means a type refers to itself.");
        }

        var effectiveType = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        if (TryGetDictionaryTypes(effectiveType, out var keyType, out var valueType))
        {
            DescribeSlot(keyType, path + "{key}", paths, depth + 1);
            DescribeSlot(valueType, path + "{value}", paths, depth + 1);
            return;
        }

        if (TryGetListElementType(effectiveType, out var elementType))
        {
            DescribeSlot(elementType, path + "[]", paths, depth + 1);
            return;
        }

        if (IsCanonicalRecord(effectiveType))
        {
            DescribeRecord(effectiveType, path + ".", paths, depth + 1);
            return;
        }

        paths.Add($"{path}:{DescribeType(declaredType)}");
    }

    /// <summary>A type's name for the pinned field list: <c>Namespace.Name&lt;Argument&gt;</c>.</summary>
    private static string DescribeType(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        var definition = type.GetGenericTypeDefinition().FullName ?? type.Name;
        var arity = definition.IndexOf('`', StringComparison.Ordinal);
        var name = arity >= 0 ? definition[..arity] : definition;

        return $"{name}<{string.Join(",", type.GetGenericArguments().Select(DescribeType))}>";
    }

    /// <summary>The refusal at the bottom of the allowlist, said the same way every time.</summary>
    private static NotSupportedException Unsupported(Type type) => new(
        $"{type.FullName} has no canonical encoding ({Specification}). The encoding covers " +
        "integers, booleans, enums, strings, doubles, timestamps, optionals, lists " +
        "(IReadOnlyList<T>), maps (IReadOnlyDictionary<TKey, TValue>) and positional records — " +
        "and deliberately nothing else, because 'no unordered container is ever hashed as-is' " +
        "and a type with no pinned byte layout would be a second serialisation contract.");

    /// <summary>
    /// The growable byte sink one hash writes into.
    /// </summary>
    /// <remarks>
    /// Constructed per call and never shared, pooled or cached — a writer that carried a buffer
    /// across calls would let a command's <c>stateHash</c> depend on the command before it.
    /// </remarks>
    private sealed class CanonicalBuffer
    {
        private byte[] _bytes = new byte[InitialBufferBytes];
        private int _length;

        /// <summary>The bytes written so far.</summary>
        internal ReadOnlySpan<byte> Written => _bytes.AsSpan(0, _length);

        /// <summary>A copy of the bytes written so far.</summary>
        internal byte[] ToArray() => _bytes.AsSpan(0, _length).ToArray();

        /// <summary>Appends one byte.</summary>
        internal void WriteByte(byte value)
        {
            EnsureCapacity(1);
            _bytes[_length++] = value;
        }

        /// <summary>Reserves and returns the next <paramref name="count"/> bytes to be filled in.</summary>
        internal Span<byte> Reserve(int count)
        {
            EnsureCapacity(count);
            var reserved = _bytes.AsSpan(_length, count);
            _length += count;
            return reserved;
        }

        private void EnsureCapacity(int extra)
        {
            if (_length + extra <= _bytes.Length)
            {
                return;
            }

            var capacity = _bytes.Length;
            while (capacity < _length + extra)
            {
                capacity *= 2;
            }

            Array.Resize(ref _bytes, capacity);
        }
    }
}
