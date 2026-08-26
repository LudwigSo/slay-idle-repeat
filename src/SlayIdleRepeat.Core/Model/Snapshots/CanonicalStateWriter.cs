using System.Buffers.Binary;
using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Model.Snapshots;

/// <summary>
/// The project's one canonical state serialiser: FNV-1a, 64-bit over the canonical byte encoding of
/// the public snapshot DTOs.
/// </summary>
/// <remarks>
/// <para>
/// A second serialiser producing "almost the same bytes" is how parity tests rot; there is exactly
/// one. Everything that turns state into bytes in this game comes through here.
/// </para>
/// <para>
/// The canonical encoding: integers widen to 64 bits (signed sign-extended, unsigned zero-extended,
/// 8 bytes little-endian); booleans are one byte (<c>0x00</c>/<c>0x01</c>); enums use their numeric
/// value widened through the underlying type; strings are a 4-byte little-endian UTF-8 byte count
/// then the bytes; timestamps are Unix milliseconds UTC, 8 bytes; optionals carry a presence byte
/// then the value; doubles are the IEEE-754 bit pattern of the already-rounded value, 8 bytes
/// little-endian; lists and dictionaries carry a 4-byte little-endian element/entry count, lists in
/// stored order and dictionaries in ascending key order; records write their fields in declaration
/// order, depth-first.
/// </para>
/// <para>
/// Three decisions not fixed by that table alone, pinned by the committed reference-vector table:
/// the 4-byte count prefix on lists/dictionaries (without it, differently-shaped nested lists could
/// collide); every nullable-capable slot carries a presence byte determined by the declared type,
/// never by compiler nullable annotations (those can be stripped by trimming/AOT); and a snapshot
/// must be a positional record — exactly one public constructor, every parameter mapped to a public
/// readable property of the same name and type — so "declaration order" is something reflection can
/// recover mechanically via <see cref="MethodBase.GetParameters"/>.
/// </para>
/// <para>
/// No unordered container is ever hashed as-is: <see cref="WriteValue"/> dispatches over a closed
/// allowlist with no <c>IEnumerable</c> fallback, and the dictionary branch always sorts its entries
/// before writing them.
/// </para>
/// <para>
/// There is a second encoder in this codebase (<c>Rng/Hash64</c>) for a different purpose — argument
/// hashing behind RNG seed derivation — and the two are deliberately not merged: they live in sibling
/// layers with no shared home, and unifying their tables would silently move bytes on one side or the
/// other. They agree on integer/string encoding and the UTF-8 settings, which must move together; they
/// differ on hash algorithm, row count and wire form, and only this file has a boolean row.
/// </para>
/// <para>
/// FNV-1a is written out here rather than taken from a package because <c>SlayIdleRepeat.Core</c>
/// references nothing at all. The reference-vector table in <c>SlayIdleRepeat.Core.Tests</c> checks it
/// against Landon Curt Noll's published <c>test_fnv.c</c> vectors.
/// </para>
/// <para>
/// It is not cryptographic and does not need to be: <c>stateHash</c> only detects divergence between
/// two honest computations of the same state. Anti-cheat is server authority, not this hash.
/// </para>
/// </remarks>
public static class CanonicalStateWriter
{
    /// <summary>The algorithm name every hash carries: <c>"fnv1a:"</c>.</summary>
    /// <remarks>
    /// The prefix exists so the algorithm can only ever be rotated deliberately and visibly — a bare
    /// 16-hex string would let a future change slip past every reader and every log.
    /// </remarks>
    public const string AlgorithmPrefix = "fnv1a:";

    /// <summary>The FNV-1a 64 offset basis.</summary>
    private const ulong OffsetBasis = 0xCBF29CE484222325UL;

    /// <summary>The FNV-1a 64 prime.</summary>
    private const ulong Prime = 0x100000001B3UL;

    /// <summary>Bytes a widened integral value, a timestamp or a double occupies.</summary>
    private const int ScalarBytes = 8;

    /// <summary>Bytes a string's byte count or a collection's element count occupies.</summary>
    private const int CountPrefixBytes = 4;

    /// <summary>The buffer a hash starts with. Grown by doubling; never pooled, never shared.</summary>
    private const int InitialBufferBytes = 256;

    /// <summary>How deep the descent may go before it gives up and says so.</summary>
    /// <remarks>
    /// A snapshot is a tree, so any real one is far shallower than this. The limit exists so a
    /// snapshot that accidentally refers to itself produces a diagnosable failure naming the type,
    /// rather than a stack overflow that takes the process with it.
    /// </remarks>
    private const int MaxDepth = 64;

    /// <summary>The specification this file implements, quoted in every refusal.</summary>
    private const string Specification = "14 §16.6";

    /// <summary>
    /// UTF-8 without a byte-order mark. Named explicitly rather than taken from
    /// <see cref="Encoding.UTF8"/> so nothing about this encoding is inherited from a static whose
    /// configuration could be read two ways.
    /// </summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The encoding decision for a declared type, resolved once and reused for the life of the process.</summary>
    /// <remarks>
    /// <para>
    /// Safe to cache: a <see cref="Type"/>'s metadata is immutable and command-independent, so
    /// resolving the plan once cannot make one command's hash depend on the command before it. The
    /// <b>buffer</b> is the opposite case and is deliberately not cached — see
    /// <see cref="CanonicalBuffer"/>.
    /// </para>
    /// <para>
    /// Not a micro-optimisation: without it, every record, list and dictionary node costs several
    /// reflection calls and array allocations, and the client recomputes a hash like this on every
    /// command, on a mid-range handset.
    /// </para>
    /// <para>
    /// <see cref="ConcurrentDictionary{TKey, TValue}"/> because the server hashes commands on many
    /// threads. A duplicate concurrent build is harmless: the plan is derived from immutable
    /// metadata, so two racing builders produce equivalent plans.
    /// </para>
    /// </remarks>
    private static readonly ConcurrentDictionary<Type, TypePlan> Plans = new();

    /// <summary>The <c>stateHash</c> of a run command: <c>PlayerSnapshot</c> then <c>RunSnapshot</c>, concatenated.</summary>
    /// <param name="playerSnapshot">The player snapshot, written first.</param>
    /// <param name="runSnapshot">The run snapshot, written second.</param>
    /// <returns><c>"fnv1a:"</c> followed by 16 lowercase hexadecimal characters.</returns>
    /// <remarks>The concatenation lives here, in a named mode, precisely so no caller ever performs it — two callers concatenating "the same way" is how one serialiser becomes two.</remarks>
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

    /// <summary>The <c>stateHash</c> of a meta command: <c>PlayerSnapshot</c> alone.</summary>
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

    /// <summary>The <c>LogHash</c> of a battle: FNV-1a 64 over the serialised combat-event list.</summary>
    /// <param name="log">The battle's event list — an <see cref="IReadOnlyList{T}"/> of the canonical event record.</param>
    /// <returns>The raw 64-bit hash.</returns>
    /// <remarks>
    /// <para>
    /// A third named mode beside <see cref="HashMetaCommandState"/> and <see cref="HashRunCommandState"/>,
    /// deliberately here rather than a caller elsewhere assembling bytes and calling <see cref="Fnv1a64"/>
    /// directly, since that caller would be the second serialiser this file exists to forbid. The event
    /// type lives in a layer <c>Model</c> may not reference, so the parameter is <see cref="object"/>.
    /// </para>
    /// <para>
    /// It returns a bare <see cref="ulong"/>, not the <c>"fnv1a:"</c>-prefixed wire form — a real
    /// asymmetry with the other two modes, forced by the wire contract that types the field
    /// <c>ulong</c> and compares the client-reported number against the server-computed one directly.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">The log is null.</exception>
    /// <exception cref="NotSupportedException">Some part of the log has no canonical encoding.</exception>
    public static ulong HashCombatLog(object log)
    {
        ArgumentNullException.ThrowIfNull(log);

        // The root must be a LIST: the other two modes take a record and cannot be handed the wrong
        // shape without the allowlist noticing, but this mode's root kind actually varies — a bare
        // event or a map would hash without a count prefix and return a plausible ulong that pins
        // nothing.
        if (PlanFor(log.GetType()).Kind != PlanKind.List)
        {
            throw new NotSupportedException(
                $"A combat log must be a list of events, not {log.GetType().FullName} ({Specification}, `05` §7). " +
                "LogHash is defined over the serialised EVENT LIST — its 4-byte element count is what stops two " +
                "different fights sharing a hash — so a single event or a map would hash to a plausible-looking " +
                "value that pins nothing.");
        }

        var buffer = new CanonicalBuffer();
        WriteRoot(buffer, log);

        return Fnv1a64(buffer.Written);
    }

    /// <summary>The canonical bytes of one snapshot, exactly as the hash sees them.</summary>
    /// <remarks>
    /// <c>internal</c> because the public surface of this class is the two hashing modes; a public
    /// bytes-out door would be an invitation to hash them some other way. Reached by the domain test
    /// suite through <c>InternalsVisibleTo</c>.
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
    /// <b>a</b>, not FNV-1: the byte is XORed into the hash <i>before</i> the multiply. The two
    /// orderings produce completely different values, and only the published known-answer vectors
    /// can tell you which one you implemented. <c>internal</c>, and not a general "hash these bytes"
    /// door — a new thing to hash becomes a third named mode beside the two public methods, never a
    /// caller assembling its own bytes and calling this directly.
    /// </remarks>
    internal static ulong Fnv1a64(ReadOnlySpan<byte> data)
    {
        // FNV relies on 64-bit wraparound. `unchecked` is the default, but this hash must not become
        // a runtime exception if a future build ever flips that switch.
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

    /// <summary>Whether a type has the positional-record shape this encoding requires.</summary>
    /// <remarks>
    /// Exposed so the <c>SchemaVersion</c> field-order pin's idea of "a snapshot record" is the same
    /// closed set <see cref="CanonicalBytes"/> will encode, rather than a second, drifting definition.
    /// </remarks>
    internal static bool IsCanonicalRecord(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return PlanFor(type).Kind == PlanKind.Record;
    }

    /// <summary>The ordered field list pinned per <c>SchemaVersion</c>: every scalar slot the encoder will write, in write order, as <c>path:declaredType</c>.</summary>
    /// <param name="rootType">A snapshot record type.</param>
    /// <remarks>
    /// Produced by the writer's own traversal, so the pinned list cannot drift from the bytes it
    /// guards. The path notation names the structure it descends through: <c>Outer.Inner</c> for a
    /// nested record, <c>Items[]</c> for a list's element slot, <c>Map{key}</c>/<c>Map{value}</c> for
    /// a dictionary's — so wrapping a field in a collection, nesting it, or making it optional each
    /// read as the shape change they are.
    /// <para>
    /// Public, unlike <see cref="CanonicalBytes"/>, because the wire projection's field-order pin
    /// lives beside the projection in a layer above this one and must be produced by this same
    /// traversal — a pin built from a second traversal is a pin that can drift from the bytes. It
    /// describes the encoding without performing it, so it opens no second bytes-out door.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="rootType"/> is null.</exception>
    /// <exception cref="NotSupportedException">Some part of the type has no canonical encoding.</exception>
    public static IReadOnlyList<string> CanonicalFieldOrder(Type rootType)
    {
        ArgumentNullException.ThrowIfNull(rootType);

        var paths = new List<string>();
        DescribeRecord(rootType, string.Empty, paths, 0);

        return paths;
    }

    /// <summary><c>"fnv1a:"</c> + 16 lowercase hex characters, zero-padded, never truncated.</summary>
    private static string ToWireForm(ulong hash) =>
        AlgorithmPrefix + hash.ToString("x16", CultureInfo.InvariantCulture);

    /// <summary>The root snapshot: required, so it carries no presence byte.</summary>
    private static void WriteRoot(CanonicalBuffer buffer, object root) =>
        WriteValue(buffer, root, PlanFor(root.GetType()), 0);

    /// <summary>One slot of the encoding: the presence byte where the declared type admits absence, then the value.</summary>
    private static void WriteSlot(CanonicalBuffer buffer, object? value, Type declaredType, int depth)
    {
        var slot = PlanFor(declaredType);

        if (slot.NullableUnderlying is null && slot.IsValueType)
        {
            // A non-nullable value type cannot be absent, so it carries no presence byte.
            WriteValue(buffer, value!, slot, depth);
            return;
        }

        if (value is null)
        {
            buffer.WriteByte(0x00);
            return;
        }

        buffer.WriteByte(0x01);
        WriteValue(buffer, value, slot.NullableUnderlying is { } underlying ? PlanFor(underlying) : slot, depth);
    }

    /// <summary>The closed allowlist. Every shape is a <see cref="PlanKind"/>; everything else is <see cref="PlanKind.Unsupported"/> and falls through to the refusal at the bottom.</summary>
    private static void WriteValue(CanonicalBuffer buffer, object value, TypePlan plan, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new NotSupportedException(
                $"A snapshot nested deeper than {MaxDepth} levels has no canonical encoding " +
                $"({Specification}). A snapshot is a tree; this nesting depth means one refers to " +
                "itself, directly or through a collection.");
        }

        switch (plan.Kind)
        {
            case PlanKind.Scalar:
                WriteScalar(buffer, value, plan);
                return;

            case PlanKind.Dictionary:
                WriteDictionary(buffer, value, plan, depth);
                return;

            case PlanKind.List:
                WriteList(buffer, value, plan, depth);
                return;

            case PlanKind.Record:
                WriteRecord(buffer, value, plan, depth);
                return;

            default:
                throw Unsupported(plan.Type);
        }
    }

    /// <summary>One scalar, by the table row the plan resolved for its declared type.</summary>
    /// <remarks>
    /// An enum has no branch here: <see cref="BuildPlan"/> already collapsed it to
    /// <see cref="ScalarKind.Signed"/> or <see cref="ScalarKind.Unsigned"/> over its underlying type
    /// code, so it can never sort as one number and hash as another.
    /// </remarks>
    private static void WriteScalar(CanonicalBuffer buffer, object value, TypePlan plan)
    {
        switch (plan.Scalar)
        {
            case ScalarKind.Boolean:
                buffer.WriteByte((bool)value ? (byte)0x01 : (byte)0x00);
                return;

            case ScalarKind.Signed:
                WriteSigned(buffer, WidenSigned(value, plan.NumericCode));
                return;

            case ScalarKind.Unsigned:
                WriteUnsigned(buffer, WidenUnsigned(value, plan.NumericCode));
                return;

            case ScalarKind.Double:
                WriteDouble(buffer, (double)value);
                return;

            case ScalarKind.String:
                WriteString(buffer, (string)value);
                return;

            case ScalarKind.Timestamp:
                WriteSigned(buffer, ((DateTimeOffset)value).ToUnixTimeMilliseconds());
                return;

            case ScalarKind.UtcDateTime:
                WriteUtcDateTime(buffer, (DateTime)value);
                return;

            default:
                // Unreachable: PlanKind.Scalar is the gate on every call site.
                throw Unsupported(plan.Type);
        }
    }

    /// <summary>A record: its fields in declaration order, depth-first.</summary>
    private static void WriteRecord(CanonicalBuffer buffer, object value, TypePlan plan, int depth)
    {
        // The pinned field list belongs to the DECLARED type. A subclass in a base-typed slot carries
        // fields the SchemaVersion pin never saw, so it has no canonical encoding here.
        var runtimeType = value.GetType();
        if (runtimeType != plan.Type)
        {
            throw new NotSupportedException(
                $"A {plan.Type.FullName} slot holds a {runtimeType.FullName}, whose extra state is " +
                $"outside the field list pinned for SchemaVersion {SnapshotSchema.SchemaVersion} " +
                $"({Specification}). Snapshot records are not polymorphic.");
        }

        foreach (var property in plan.Properties!)
        {
            WriteSlot(buffer, property.GetValue(value), property.PropertyType, depth + 1);
        }
    }

    /// <summary>A list: a 4-byte little-endian element count, then the elements in stored order.</summary>
    private static void WriteList(CanonicalBuffer buffer, object value, TypePlan plan, int depth)
    {
        var elements = new List<object?>(value is ICollection sized ? sized.Count : 0);
        foreach (var element in (IEnumerable)value)
        {
            elements.Add(element);
        }

        WriteCount(buffer, elements.Count);
        foreach (var element in elements)
        {
            WriteSlot(buffer, element, plan.ElementType!, depth + 1);
        }
    }

    /// <summary>A dictionary: a 4-byte little-endian entry count, then the entries in ascending key order — ordinal for strings, numeric for numeric ids.</summary>
    /// <remarks>There is deliberately no branch that writes entries in the container's own order, and none that consults its own comparer. The order is imposed, every time, or the key type is refused outright.</remarks>
    private static void WriteDictionary(CanonicalBuffer buffer, object value, TypePlan plan, int depth)
    {
        if (plan.KeyOrder is null)
        {
            throw NoAscendingKeyOrder(plan.KeyType!);
        }

        var entries = new List<(object Key, object? Value)>(value is ICollection sized ? sized.Count : 0);
        foreach (var entry in (IEnumerable)value)
        {
            entries.Add((plan.EntryKey!.GetValue(entry)!, plan.EntryValue!.GetValue(entry)));
        }

        entries.Sort(plan.KeyOrder);

        WriteCount(buffer, entries.Count);
        foreach (var (key, entryValue) in entries)
        {
            WriteSlot(buffer, key, plan.KeyType!, depth + 1);
            WriteSlot(buffer, entryValue, plan.ElementType!, depth + 1);
        }
    }

    /// <summary>The ascending key order for a key type, or <c>null</c> when the key type has no defined order at all.</summary>
    /// <remarks>Resolved once per dictionary type by <see cref="BuildPlan"/> and stored on the plan, so the closure is built once rather than per map, per hash, per command.</remarks>
    private static Comparison<(object Key, object? Value)>? KeyOrderFor(Type keyType)
    {
        if (keyType == typeof(string))
        {
            // Ordinal, never culture-aware: a culture comparer makes the byte stream depend on the
            // machine's locale, which splits client from server on the first non-invariant device.
            return (left, right) => string.CompareOrdinal((string)left.Key, (string)right.Key);
        }

        var numericCode = Type.GetTypeCode(keyType.IsEnum ? Enum.GetUnderlyingType(keyType) : keyType);

        switch (numericCode)
        {
            case TypeCode.SByte:
            case TypeCode.Int16:
            case TypeCode.Int32:
            case TypeCode.Int64:
                return (left, right) => WidenSigned(left.Key, numericCode).CompareTo(WidenSigned(right.Key, numericCode));

            case TypeCode.Byte:
            case TypeCode.UInt16:
            case TypeCode.UInt32:
            case TypeCode.UInt64:
                // Compared as unsigned: a key above long.MaxValue is a large id, not a negative one.
                return (left, right) => WidenUnsigned(left.Key, numericCode).CompareTo(WidenUnsigned(right.Key, numericCode));

            default:
                return null;
        }
    }

    /// <summary>The refusal for a key type with no defined ascending order, said the same way twice.</summary>
    private static NotSupportedException NoAscendingKeyOrder(Type keyType) => new(
        $"{keyType.FullName} has no ascending key order — {Specification} defines one " +
        "for strings (ordinal) and numeric ids (numeric), and no other. A map keyed by " +
        "anything else cannot be hashed in a defined order.");

    /// <summary>A double: the IEEE-754 bit pattern of the stored value, 8 bytes little-endian.</summary>
    /// <remarks>
    /// <para>
    /// The writer never rounds and never normalises — accumulation points round; this method only
    /// checks that already happened, so it does not hide the drift the determinism CI exists to catch.
    /// </para>
    /// <para>
    /// Three values are refused rather than encoded: NaN, the infinities, and <c>-0.0</c>. The first
    /// two have no place in persisted state at all. <c>-0.0</c> is refused because it is the one value
    /// where record equality and <c>stateHash</c> disagree (<c>-0.0 == 0.0</c> in C#, but the bit
    /// patterns differ) — normalising it here would silently edit state on its way out, while
    /// accepting it would hand two states C# calls identical two different hashes.
    /// </para>
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

        // The one value where record equality and stateHash would disagree. It passes the rounding
        // guard below untouched — Round(-0.0) is +0.0, which compares EQUAL to -0.0 — so it has to be
        // named here.
        if (double.IsNegative(value) && value == 0.0)
        {
            throw new NotSupportedException(
                $"-0.0 is forbidden in persisted state ({Specification}). It is the one value " +
                "where record equality and stateHash disagree: -0.0 == 0.0 is true in C#, so two " +
                "snapshots the language calls IDENTICAL would carry different hashes — the writer " +
                "encodes the bit pattern, and that is 0x8000000000000000 against " +
                "0x0000000000000000. It is reachable from `14` §8.2's own rule: Math.Round(-0.00004, 4) " +
                "yields -0.0 and .NET preserves the sign of zero, so a stat accumulating to a tiny " +
                "negative on one host and not the other is a false divergence in the §2.4 mirror " +
                "check and in the §13 chaos tests. Normalise at the accumulation point — `x + 0.0` " +
                "is +0.0 — rather than letting the writer edit state on its way out.");
        }

        // Always on: this project's CI builds and tests in Release, so a debug-only assert would be
        // silent in the one place it is meant to fire. The cost is one Math.Round per double.
        if (DeterminismRounding.Round(value) != value)
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

    /// <summary>A string: a 4-byte little-endian UTF-8 byte count, then the UTF-8 bytes.</summary>
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
    /// <remarks>The one signed widening rule in the file, serving both the scalar write and the ascending order of an enum-keyed map.</remarks>
    private static long WidenSigned(object value, TypeCode numericCode) => numericCode switch
    {
        TypeCode.SByte => (sbyte)value,
        TypeCode.Int16 => (short)value,
        TypeCode.Int32 => (int)value,
        TypeCode.Int64 => (long)value,
        _ => throw Unsupported(value.GetType()),
    };

    /// <summary>A boxed unsigned integral value as a <see cref="ulong"/>, by zero extension.</summary>
    private static ulong WidenUnsigned(object value, TypeCode numericCode) => numericCode switch
    {
        TypeCode.Byte => (byte)value,
        TypeCode.UInt16 => (ushort)value,
        TypeCode.UInt32 => (uint)value,
        TypeCode.UInt64 => (ulong)value,
        _ => throw Unsupported(value.GetType()),
    };

    /// <summary>
    /// A positional record's public readable properties, in the primary constructor's parameter
    /// order — or <c>null</c> when the type has no such constructor and therefore no
    /// reflection-guaranteed declaration order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Requires exactly one public constructor, at least one parameter, every parameter matched by a
    /// public readable property of the same name and type — and, the converse, no public instance
    /// property or field beyond those parameters. Without the converse, a property declared outside
    /// the primary constructor would contribute zero bytes: two states record equality correctly
    /// calls different would then share a <c>stateHash</c>, and the <c>SchemaVersion</c> field-order
    /// pin would never see the field either.
    /// </para>
    /// <para>
    /// The field check is not the same as the property check and was historically missing: a public
    /// <em>field</em> beside a positional record is in no parameter list, is not a property, and would
    /// pass the property-count check while hashing as zero bytes. A positional <c>record</c>'s
    /// components compile to private backing fields, so requiring zero public instance fields costs a
    /// compliant snapshot nothing.
    /// </para>
    /// <para>
    /// Recognising the shape and resolving the properties are one pass because the writer needs both
    /// for every record it descends into; the answer is memoised on the type's <see cref="TypePlan"/>.
    /// </para>
    /// </remarks>
    private static PropertyInfo[]? CanonicalProperties(Type type)
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

        var properties = new PropertyInfo[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var property = type.GetProperty(parameters[i].Name!, BindingFlags.Public | BindingFlags.Instance);
            if (property is null || !property.CanRead || property.PropertyType != parameters[i].ParameterType)
            {
                return null;
            }

            properties[i] = property;
        }

        // The property set must be EXACT, not merely a superset of the parameters. EqualityContract
        // is `protected`, so BindingFlags.Public excludes it: this reads the same for a `record` and
        // a `record struct`.
        var declared = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        if (declared.Length != parameters.Length)
        {
            return null;
        }

        // And the same for FIELDS: a public field beside a positional record is in no parameter list
        // and has no property to be counted above, so it would otherwise be written as ZERO BYTES.
        // A positional `record`/`record struct` compiles its components to PRIVATE backing fields, so
        // a compliant snapshot has no public instance field at all and this costs it nothing.
        var publicFields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        if (publicFields.Length != 0)
        {
            return null;
        }

        return properties;
    }

    /// <summary>The cached encoding decision for a declared type, built on first sight.</summary>
    private static TypePlan PlanFor(Type type) => Plans.GetOrAdd(type, BuildPlan);

    /// <summary>The closed allowlist, resolved once per type: the same four questions <see cref="WriteValue"/> and <see cref="DescribeSlot"/> both ask, in the same order, with the same refusal at the bottom.</summary>
    /// <remarks>
    /// One resolver, deliberately — a second list of "the scalars" would let the field-order pin bless
    /// a field the bytes go on to refuse. The plan holds child <b>types</b>, never child plans, so a
    /// self-referencing record recurses at <see cref="MaxDepth"/> instead of here.
    /// </remarks>
    private static TypePlan BuildPlan(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            // A Nullable<T> slot is a presence byte then T; the payload decision belongs to T.
            return new TypePlan(type, PlanKind.Unsupported) { IsValueType = true, NullableUnderlying = underlying };
        }

        if (type.IsEnum)
        {
            // Collapsed to its underlying integral rule here, so the write path has no enum branch
            // and the widening cannot diverge from the ascending order of an enum-keyed map.
            var enumCode = Type.GetTypeCode(Enum.GetUnderlyingType(type));
            var enumKind = ScalarKindOf(enumCode);

            return enumKind is ScalarKind.Signed or ScalarKind.Unsigned
                ? Scalar(type, enumKind, enumCode)
                : new TypePlan(type, PlanKind.Unsupported) { IsValueType = true };
        }

        if (type == typeof(DateTimeOffset))
        {
            return Scalar(type, ScalarKind.Timestamp, TypeCode.Object);
        }

        var code = Type.GetTypeCode(type);
        var scalar = ScalarKindOf(code);
        if (scalar != ScalarKind.None)
        {
            return Scalar(type, scalar, code);
        }

        if (TryGetDictionaryTypes(type, out var keyType, out var valueType))
        {
            var entryType = typeof(KeyValuePair<,>).MakeGenericType(keyType, valueType);

            return new TypePlan(type, PlanKind.Dictionary)
            {
                IsValueType = type.IsValueType,
                KeyType = keyType,
                ElementType = valueType,
                EntryKey = entryType.GetProperty("Key")!,
                EntryValue = entryType.GetProperty("Value")!,
                KeyOrder = KeyOrderFor(keyType),
            };
        }

        if (TryGetListElementType(type, out var elementType))
        {
            return new TypePlan(type, PlanKind.List) { IsValueType = type.IsValueType, ElementType = elementType };
        }

        if (CanonicalProperties(type) is { } properties)
        {
            return new TypePlan(type, PlanKind.Record) { IsValueType = type.IsValueType, Properties = properties };
        }

        return new TypePlan(type, PlanKind.Unsupported) { IsValueType = type.IsValueType };
    }

    /// <summary>The scalar rule a type code falls under, or <see cref="ScalarKind.None"/>.</summary>
    private static ScalarKind ScalarKindOf(TypeCode code) => code switch
    {
        TypeCode.Boolean => ScalarKind.Boolean,
        TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 => ScalarKind.Signed,
        TypeCode.Byte or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64 => ScalarKind.Unsigned,
        TypeCode.Double => ScalarKind.Double,
        TypeCode.String => ScalarKind.String,
        TypeCode.DateTime => ScalarKind.UtcDateTime,
        _ => ScalarKind.None,
    };

    private static TypePlan Scalar(Type type, ScalarKind scalar, TypeCode numericCode) =>
        new(type, PlanKind.Scalar) { IsValueType = type.IsValueType, Scalar = scalar, NumericCode = numericCode };

    /// <summary>The <see cref="IReadOnlyDictionary{TKey, TValue}"/> a type is, or implements once.</summary>
    private static bool TryGetDictionaryTypes(Type type, out Type keyType, out Type valueType) =>
        TryGetClosedInterface(type, typeof(IReadOnlyDictionary<,>), out keyType, out valueType);

    /// <summary>The <see cref="IReadOnlyList{T}"/> a type is, or implements once.</summary>
    private static bool TryGetListElementType(Type type, out Type elementType) =>
        TryGetClosedInterface(type, typeof(IReadOnlyList<>), out elementType, out _);

    /// <summary>The single closed form of an open generic interface that a type is or implements.</summary>
    /// <remarks>"Single" matters: a type implementing two closings of the same interface offers two encodings, and the encoding must not pick one — such a type falls through to the refusal.</remarks>
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
        var plan = PlanFor(type);
        if (plan.Kind != PlanKind.Record)
        {
            throw Unsupported(type);
        }

        foreach (var property in plan.Properties!)
        {
            DescribeSlot(property.PropertyType, prefix + property.Name, paths, depth + 1);
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

        var slot = PlanFor(declaredType);
        var plan = slot.NullableUnderlying is { } underlying ? PlanFor(underlying) : slot;

        // The same plan the writer dispatches on, including the refusal at the bottom, so this
        // traversal never describes a slot the writer would refuse to write.
        switch (plan.Kind)
        {
            case PlanKind.Scalar:
                paths.Add($"{path}:{DescribeType(declaredType)}");
                return;

            case PlanKind.Dictionary:
                if (plan.KeyOrder is null)
                {
                    throw NoAscendingKeyOrder(plan.KeyType!);
                }

                DescribeSlot(plan.KeyType!, path + "{key}", paths, depth + 1);
                DescribeSlot(plan.ElementType!, path + "{value}", paths, depth + 1);
                return;

            case PlanKind.List:
                DescribeSlot(plan.ElementType!, path + "[]", paths, depth + 1);
                return;

            case PlanKind.Record:
                DescribeRecord(plan.Type, path + ".", paths, depth + 1);
                return;

            default:
                throw Unsupported(declaredType);
        }
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

    /// <summary>The refusal at the bottom of the allowlist, said the same way every time — plus the offending fields, when knowable.</summary>
    /// <remarks>Naming the offending fields makes the field-shape refusal distinguishable from the property-shape one, so a test can pin which rule fired.</remarks>
    private static NotSupportedException Unsupported(Type type)
    {
        var publicFields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);

        return new NotSupportedException(
            GenericRefusal(type) +
            (publicFields.Length == 0
                ? string.Empty
                : $" SPECIFICALLY: {type.Name} declares the public instance field(s) " +
                  $"[{string.Join(", ", publicFields.Select(f => f.Name))}], which is the shape that hashes as " +
                  "zero bytes. Move them into the primary constructor."));
    }

    /// <summary>The rule list every refusal carries.</summary>
    private static string GenericRefusal(Type type) =>
        $"{type.FullName} has no canonical encoding ({Specification}). The encoding covers " +
        "integers, booleans, enums, strings, doubles, timestamps, optionals, lists " +
        "(IReadOnlyList<T>), maps (IReadOnlyDictionary<TKey, TValue>) and positional records — " +
        "and deliberately nothing else, because 'no unordered container is ever hashed as-is' " +
        "and a type with no pinned byte layout would be a second serialisation contract. A record " +
        "carrying a public property OR A PUBLIC FIELD that is not a primary-constructor parameter " +
        "is refused for the same reason: the field list is the constructor's parameter list, so " +
        "such a member would be hashed as ZERO BYTES — two states differing only in it would share " +
        "a stateHash, and the SchemaVersion field-order pin would never see it. A public instance " +
        "field is the more dangerous of the two shapes, because it is neither a parameter nor a " +
        "property and so slips past both checks — `05` §7's CombatEvent is written that way and " +
        "would hash to nothing. Move it into the primary constructor.";

    /// <summary>Which branch of the closed allowlist a declared type falls into.</summary>
    private enum PlanKind
    {
        /// <summary>No branch — the terminal refusal.</summary>
        Unsupported = 0,

        /// <summary>A leaf with a pinned byte layout.</summary>
        Scalar,

        /// <summary>An <see cref="IReadOnlyDictionary{TKey, TValue}"/>.</summary>
        Dictionary,

        /// <summary>An <see cref="IReadOnlyList{T}"/>.</summary>
        List,

        /// <summary>A positional record.</summary>
        Record,
    }

    /// <summary>Which scalar rule a leaf is written by.</summary>
    private enum ScalarKind
    {
        /// <summary>Not a scalar.</summary>
        None = 0,

        /// <summary>One byte, <c>0x00</c> or <c>0x01</c>.</summary>
        Boolean,

        /// <summary>Sign-extended to 8 bytes. Enums over a signed integral land here too.</summary>
        Signed,

        /// <summary>Zero-extended to 8 bytes. Enums over an unsigned integral land here too.</summary>
        Unsigned,

        /// <summary>The IEEE-754 bit pattern of the already-rounded value, 8 bytes.</summary>
        Double,

        /// <summary>A 4-byte UTF-8 byte count, then the UTF-8 bytes.</summary>
        String,

        /// <summary>A <see cref="DateTimeOffset"/> as Unix milliseconds UTC.</summary>
        Timestamp,

        /// <summary>A <see cref="DateTime"/> as Unix milliseconds, and only if it says it is UTC.</summary>
        UtcDateTime,
    }

    /// <summary>Everything the encoder needs to know about a declared type, resolved once.</summary>
    /// <remarks>
    /// Immutable, and derived only from immutable <see cref="Type"/> metadata, which is what makes
    /// caching it in <see cref="Plans"/> safe. Holds child <b>types</b> rather than child plans so a
    /// self-referencing record cannot make construction recurse.
    /// </remarks>
    private sealed class TypePlan(Type type, PlanKind kind)
    {
        /// <summary>The declared type this plan was built for.</summary>
        internal Type Type { get; } = type;

        /// <summary>Which branch of the allowlist it falls into.</summary>
        internal PlanKind Kind { get; } = kind;

        /// <summary>Whether the type is a value type, so the slot cannot be absent.</summary>
        internal bool IsValueType { get; init; }

        /// <summary>The <c>T</c> of a <see cref="Nullable{T}"/>, or <c>null</c>.</summary>
        internal Type? NullableUnderlying { get; init; }

        /// <summary>The scalar rule, when <see cref="Kind"/> is <see cref="PlanKind.Scalar"/>.</summary>
        internal ScalarKind Scalar { get; init; }

        /// <summary>The integral type code to unbox through, for a signed or unsigned scalar.</summary>
        internal TypeCode NumericCode { get; init; }

        /// <summary>A dictionary's key type.</summary>
        internal Type? KeyType { get; init; }

        /// <summary>A list's element type, or a dictionary's value type.</summary>
        internal Type? ElementType { get; init; }

        /// <summary><c>KeyValuePair&lt;K, V&gt;.Key</c>, resolved once rather than per entry.</summary>
        internal PropertyInfo? EntryKey { get; init; }

        /// <summary><c>KeyValuePair&lt;K, V&gt;.Value</c>, resolved once rather than per entry.</summary>
        internal PropertyInfo? EntryValue { get; init; }

        /// <summary>The ascending key order, or <c>null</c> when the key type has none.</summary>
        internal Comparison<(object Key, object? Value)>? KeyOrder { get; init; }

        /// <summary>A record's fields, in primary-constructor parameter order.</summary>
        internal PropertyInfo[]? Properties { get; init; }
    }

    /// <summary>The growable byte sink one hash writes into.</summary>
    /// <remarks>
    /// Constructed per call and never shared, pooled or cached — a writer that carried a buffer
    /// across calls would let a command's <c>stateHash</c> depend on the command before it. Unlike
    /// <see cref="Plans"/>, which caches only immutable, value-independent <see cref="Type"/>
    /// metadata, this buffer holds one command's state, so reusing it would be exactly the
    /// cross-command dependency this class must not have.
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
            // In long throughout: past 1 GiB, `_length + extra` overflows to a negative int and the
            // doubling below walks int.MaxValue -> negative -> 0, which never reaches the target and
            // spins forever. A hash that hangs is worse than one that refuses.
            var required = (long)_length + extra;
            if (required <= _bytes.Length)
            {
                return;
            }

            if (required > Array.MaxLength)
            {
                throw new NotSupportedException(
                    $"A snapshot needing {required} bytes exceeds the {Array.MaxLength} bytes one " +
                    $"canonical encoding can occupy ({Specification}). A snapshot that large is a " +
                    "state bug — an unbounded collection, most likely — not a hash to compute.");
            }

            var capacity = (long)_bytes.Length;
            while (capacity < required)
            {
                capacity *= 2;
            }

            Array.Resize(ref _bytes, (int)Math.Min(capacity, Array.MaxLength));
        }
    }
}
