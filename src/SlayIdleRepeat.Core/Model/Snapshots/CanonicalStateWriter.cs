using System.Buffers.Binary;
using System.Collections;
using System.Collections.Concurrent;
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
/// 🔒 <b>There is a second encoder in this codebase, and that is deliberate.</b> <c>Rng/Hash64</c>
/// implements `14` §8.0 — the canonical <i>argument</i> encoding behind every seed derivation and
/// every draw. Both files open by claiming singularity, in nearly the same words, and both are
/// right: §16.6's "there is exactly one" means one <b>state</b> serialiser, §8.0's "two
/// implementations would eventually be two hashes" means one <b>draw</b> hash. They are <b>not</b>
/// to be deduplicated: `23`'s <c>Core_internal_layering_holds</c> puts <c>Rng</c> and <c>Model</c>
/// in sibling layers with no shared home to move a shared encoder into, and merging the two tables
/// would move bytes on one side or the other — a determinism break in a refactoring commit.
/// </para>
/// <para>
/// What they <b>do</b> share, verified field by field: signed integer widening (sign-extended to
/// 8 bytes little-endian), unsigned integer widening (zero-extended, same), enums through their
/// underlying integral type, string length-prefixing (a 4-byte little-endian UTF-8 <b>byte</b>
/// count, then the bytes), and the <c>new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)</c>
/// object itself. ⚠️ Those five rows must move <b>together or not at all</b>: a change to one
/// side's integer widening or string prefix without the other silently forks the two encodings,
/// and no test on either side would notice.
/// </para>
/// <para>
/// Everything else differs by design: FNV-1a 64 here against xxHash64 there; ten rows here against
/// two there; the <c>"fnv1a:"</c>-prefixed wire form here against a raw <c>ulong</c> there. ⚠️ In
/// particular §8.0 has <b>no boolean row</b> — the one-byte <c>0x00</c>/<c>0x01</c> rule above is
/// this file's alone. A caller hand-encoding a bool into a seed would invent a widened 8-byte
/// <c>1</c>/<c>0</c>, which is a third encoding rather than either of these two.
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
    /// The encoding decision for a declared type, resolved once and reused for the life of the
    /// process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>What this may and may not cache, and why they are not the same question.</b> A
    /// <see cref="Type"/>'s metadata is immutable and command-independent: <c>RunSnapshot</c> is a
    /// record of the same shape whichever command is being hashed. Resolving that once
    /// therefore cannot make one command's <c>stateHash</c> depend on the command before it — the
    /// plan is a function of the type alone, and the bytes are a function of the plan and the
    /// value. The <b>buffer</b> is the opposite case and is deliberately <i>not</i> cached: see
    /// <see cref="CanonicalBuffer"/>.
    /// </para>
    /// <para>
    /// It is not a micro-optimisation. Without it, every record, list and dictionary node costs a
    /// <see cref="Type.GetInterfaces"/> array per container probe (twice — dictionary, then list),
    /// a <see cref="Type.GetConstructors(BindingFlags)"/>, a
    /// <see cref="MethodBase.GetParameters"/> and a string-keyed <c>GetProperty</c> per field. A
    /// <c>RunSnapshot</c> with a 60-tile board plus inventories is thousands of reflection calls
    /// and array allocations per hash — and `14` §2.4 has the <b>client</b> recompute that on
    /// every command, on a mid-range handset.
    /// </para>
    /// <para>
    /// <see cref="ConcurrentDictionary{TKey, TValue}"/> because the server hashes commands on many
    /// threads. A duplicate concurrent <see cref="BuildPlan"/> is harmless: the plan is derived
    /// from immutable metadata, so two racing builders produce equivalent plans.
    /// </para>
    /// </remarks>
    private static readonly ConcurrentDictionary<Type, TypePlan> Plans = new();

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
    /// <para>
    /// 🔒 <b>a</b>, not FNV-1: the byte is XORed into the hash <i>before</i> the multiply. The two
    /// orderings produce completely different values, and only the published known-answer vectors
    /// can tell you which one you implemented.
    /// </para>
    /// <para>
    /// 🔒 <c>internal</c>, and it is <b>not</b> a general "hash these bytes" door — it exists so
    /// the domain suite can drive Landon Curt Noll's published vectors against the primitive
    /// itself. A new thing to hash (M2's battle <c>LogHash</c>, §8.2/§9) becomes a <b>third named
    /// mode</b> beside <see cref="HashMetaCommandState"/> and <see cref="HashRunCommandState"/>,
    /// never a caller assembling its own bytes and calling this — that caller would be the second
    /// serialiser §16.6 forbids, one <c>internal</c> away from the class built to prevent it.
    /// </para>
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
        return PlanFor(type).Kind == PlanKind.Record;
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
        WriteValue(buffer, root, PlanFor(root.GetType()), 0);

    /// <summary>
    /// One slot of the encoding: the presence byte where the declared type admits absence, then
    /// the value.
    /// </summary>
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

    /// <summary>
    /// 🔒 The closed allowlist. Every shape §16.6 pins is a <see cref="PlanKind"/>; everything else
    /// is <see cref="PlanKind.Unsupported"/> and falls through to the refusal at the bottom, which
    /// is what makes an unordered container unhashable rather than merely discouraged.
    /// </summary>
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

    /// <summary>One scalar, by the §16.6 table row the plan resolved for its declared type.</summary>
    /// <remarks>
    /// An enum has no branch here: <see cref="BuildPlan"/> already collapsed it to
    /// <see cref="ScalarKind.Signed"/> or <see cref="ScalarKind.Unsigned"/> over its underlying
    /// type code, which is why an enum can never sort as one number and hash as another — the same
    /// two widening helpers impose the ascending order on an enum-keyed map.
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
        // 🔒 The pinned field list belongs to the DECLARED type. A subclass in a base-typed slot
        // carries fields the SchemaVersion pin never saw, so it has no canonical encoding here.
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
    /// <remarks>
    /// The count precedes the elements, so the elements are gathered before any of them is
    /// written. Sized up front wherever the container knows its own size, which is every list
    /// shape a snapshot actually uses.
    /// </remarks>
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

    /// <summary>
    /// 🔒 A dictionary: a 4-byte little-endian entry count, then the entries in <b>ascending key
    /// order</b> — ordinal for strings, numeric for numeric ids.
    /// </summary>
    /// <remarks>
    /// There is deliberately no branch here that writes entries in the order the container
    /// happened to yield them, and none that consults the container's own comparer. The order is
    /// imposed, every time, or the encoding refuses the key type outright.
    /// </remarks>
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

    /// <summary>
    /// The ascending key order for a key type: ordinal for strings, numeric for every integral
    /// and enum id — or <c>null</c> when the key type has no defined order at all.
    /// </summary>
    /// <remarks>
    /// Resolved once per dictionary type by <see cref="BuildPlan"/> and stored on the plan, so the
    /// closure is built once rather than per map, per hash, per command.
    /// </remarks>
    private static Comparison<(object Key, object? Value)>? KeyOrderFor(Type keyType)
    {
        if (keyType == typeof(string))
        {
            // 🔒 Ordinal, never culture-aware: a culture comparer makes the byte stream depend on
            // the machine's locale, which splits client from server on the first non-invariant device.
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

    /// <summary>
    /// A double: the IEEE-754 bit pattern of the <b>stored</b> value, 8 bytes little-endian.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The writer never rounds and never normalises. `14` §8.2 rounds at every accumulation point;
    /// this method's only business with a double is to check that already happened — a writer that
    /// quietly rounded would hide the drift the determinism CI exists to catch.
    /// </para>
    /// <para>
    /// 🔒 Three values are <b>refused</b> rather than encoded: NaN, the infinities, and
    /// <c>-0.0</c>. The first two have no place in persisted state at all. <c>-0.0</c> is refused
    /// for a different reason — it is the one value where record equality and <c>stateHash</c>
    /// disagree, so normalising it here would silently edit state on its way out while accepting
    /// it would hand two states C# calls identical two different hashes. Neither is a choice this
    /// writer may make on the author's behalf; the accumulation point must.
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

        // 🔒 The one value where record equality and stateHash would disagree. It passes the
        // rounding guard below untouched — Math.Round(-0.0, 4) is -0.0 — so it has to be named here.
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
    /// <remarks>
    /// 🔒 The <b>one</b> signed widening rule in the file. It serves both the scalar write and the
    /// ascending order of an enum-keyed map — a boxed enum unboxes straight to its underlying
    /// type — so an enum can never sort as one number and hash as another. Routing one of the two
    /// through <c>Convert</c> instead would be a second widening rule in the file whose entire
    /// point is that there is one.
    /// </remarks>
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
    /// The shape it demands: exactly one public constructor, at least one parameter, every
    /// parameter matched by a public readable property of the same name and type, and — 🔒 the
    /// converse — <b>no public instance property beyond those parameters</b>. The order is the
    /// constructor's, which <see cref="MethodBase.GetParameters"/> guarantees; property order is
    /// not guaranteed at all.
    /// </para>
    /// <para>
    /// 🔒 <b>Why the converse matters.</b> The field list <i>is</i> the parameter list, so a
    /// public property declared outside the primary constructor — <c>public int RevivesUsed
    /// { get; init; }</c> beside a positional record, exactly the shape an optional snapshot
    /// member reaches for — would contribute <b>zero bytes</b>. Two states that record equality
    /// correctly calls different would then share a <c>stateHash</c>, and
    /// <see cref="CanonicalFieldOrder"/> asks the same question, so the <c>SchemaVersion</c>
    /// field-order pin would never see the field either. The writer refuses the shape rather than
    /// silently omitting it.
    /// </para>
    /// <para>
    /// 🔒 <b>And the same for a public <i>field</i>, which is not the same check.</b> M0-07 wrote
    /// the converse over <see cref="Type.GetProperties(BindingFlags)"/> only, so
    /// <c>public int RevivesUsed;</c> beside a positional record — one keyword-pair away from the
    /// property shape above, and the shape a hand-written DTO reaches for first — passed straight
    /// through: it is in no parameter list, it is not a property, and the count above therefore
    /// matched. It would have hashed as <b>zero bytes</b> with no refusal anywhere, which is the
    /// one failure a <c>stateHash</c> may never have. Latent since M0-07 and harmless only while no
    /// snapshot record existed; closed here, by the commit that authors the first one, and driven
    /// red-then-green against <c>UnsupportedSnapshots.WithPublicField</c>.
    /// </para>
    /// <para>
    /// A compliant positional <c>record</c> or <c>record struct</c> compiles every component to a
    /// <b>private</b> backing field, so requiring zero public instance fields costs a real snapshot
    /// nothing. What it also catches, for free and correctly: an <c>enum</c> (its <c>value__</c> is
    /// public) and a <see cref="ValueTuple"/> (its <c>Item1…</c> are public) reaching the record
    /// branch at all — neither has a pinnable declaration order this encoding recognises, and both
    /// are already refused for other reasons before they get here.
    /// </para>
    /// <para>
    /// Recognising the shape and resolving the properties are one pass because the writer needs
    /// both for every record it descends into. The answer is then memoised on the type's
    /// <see cref="TypePlan"/> — see <see cref="Plans"/> for why that is safe and
    /// <see cref="CanonicalBuffer"/> for the thing that is not.
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

        // 🔒 The property set must be EXACT, not merely a superset of the parameters. A public
        // property outside the constructor is state the pinned field list never saw, and the loop
        // above only proves every parameter has a property — never the converse.
        // EqualityContract is `protected`, so BindingFlags.Public excludes it: this reads the same
        // for a `record` and a `record struct`.
        var declared = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        if (declared.Length != parameters.Length)
        {
            return null;
        }

        // 🔒 And the same for FIELDS, which is the half this check was missing from M0-07 until the
        // first snapshot record existed. See the remarks: a positional record's parameter list maps
        // to PROPERTIES, so a public *field* beside them is in no parameter list, has no property
        // to be counted by the check above, and would be written as ZERO BYTES.
        //
        // A positional `record` and `record struct` both compile their components to PRIVATE
        // backing fields, so a compliant snapshot has no public instance field at all and this
        // costs it nothing. Any field with public visibility here — declared by the author, or an
        // enum's `value__`, or a ValueTuple's `Item1` — means the type's state is not entirely
        // described by its primary constructor.
        var publicFields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        if (publicFields.Length != 0)
        {
            return null;
        }

        return properties;
    }

    /// <summary>The cached encoding decision for a declared type, built on first sight.</summary>
    private static TypePlan PlanFor(Type type) => Plans.GetOrAdd(type, BuildPlan);

    /// <summary>
    /// 🔒 The closed allowlist, resolved once per type: the four questions
    /// <see cref="WriteValue"/> and <see cref="DescribeSlot"/> both ask, in the same order, with
    /// the same refusal at the bottom.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One resolver, deliberately. A second list of "the scalars" — one for the bytes and one for
    /// the pinned field list — would let the <c>SchemaVersion</c> field list bless a field the
    /// bytes go on to refuse.
    /// </para>
    /// <para>
    /// The plan holds child <b>types</b>, never child plans: a record that refers to itself
    /// (<c>Next</c> of the same type) would otherwise recurse forever here instead of terminating
    /// at <see cref="MaxDepth"/> where the failure is diagnosable.
    /// </para>
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

            // C# admits only the eight integral types; an IL-authored enum over anything else is
            // named as the enum rather than reported as its underlying type.
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

    /// <summary>The §16.6 scalar rule a type code falls under, or <see cref="ScalarKind.None"/>.</summary>
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

        // 🔒 The same plan the writer dispatches on — including the refusal at the bottom. A
        // traversal that described a slot the writer will not write would pin a field list for a
        // snapshot that cannot be hashed at all.
        switch (plan.Kind)
        {
            case PlanKind.Scalar:
                paths.Add($"{path}:{DescribeType(declaredType)}");
                return;

            case PlanKind.Dictionary:
                // The writer's own key-order decision, for its refusal: a key type with no
                // ascending order must not be pinnable here and unhashable there.
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

    /// <summary>The refusal at the bottom of the allowlist, said the same way every time.</summary>
    private static NotSupportedException Unsupported(Type type) => new(
        $"{type.FullName} has no canonical encoding ({Specification}). The encoding covers " +
        "integers, booleans, enums, strings, doubles, timestamps, optionals, lists " +
        "(IReadOnlyList<T>), maps (IReadOnlyDictionary<TKey, TValue>) and positional records — " +
        "and deliberately nothing else, because 'no unordered container is ever hashed as-is' " +
        "and a type with no pinned byte layout would be a second serialisation contract. A record " +
        "carrying a public property OR A PUBLIC FIELD that is not a primary-constructor parameter " +
        "is refused for the same reason: the field list is the constructor's parameter list, so " +
        "such a member would be hashed as ZERO BYTES — two states differing only in it would share " +
        "a stateHash, and the SchemaVersion field-order pin would never see it. Move it into the " +
        "primary constructor.");

    /// <summary>Which branch of the closed allowlist a declared type falls into.</summary>
    private enum PlanKind
    {
        /// <summary>No branch — the terminal refusal.</summary>
        Unsupported = 0,

        /// <summary>A leaf with a pinned byte layout in the §16.6 table.</summary>
        Scalar,

        /// <summary>An <see cref="IReadOnlyDictionary{TKey, TValue}"/>.</summary>
        Dictionary,

        /// <summary>An <see cref="IReadOnlyList{T}"/>.</summary>
        List,

        /// <summary>A positional record.</summary>
        Record,
    }

    /// <summary>Which §16.6 scalar rule a leaf is written by.</summary>
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

    /// <summary>
    /// Everything the encoder needs to know about a declared type, resolved once.
    /// </summary>
    /// <remarks>
    /// Immutable, and derived only from immutable <see cref="Type"/> metadata — which is what
    /// makes caching it in <see cref="Plans"/> safe. It holds child <b>types</b> rather than child
    /// plans so a self-referencing record cannot make construction recurse.
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

    /// <summary>
    /// The growable byte sink one hash writes into.
    /// </summary>
    /// <remarks>
    /// 🔒 Constructed per call and never shared, pooled or cached — a writer that carried a buffer
    /// across calls would let a command's <c>stateHash</c> depend on the command before it. This is
    /// a different question from <see cref="Plans"/>, which caches only immutable, value-independent
    /// <see cref="Type"/> metadata: the buffer holds one command's <b>state</b>, so reusing it is
    /// exactly the cross-command dependency this class must not have.
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
            // In long throughout: past 1 GiB, `_length + extra` overflows to a negative int and
            // the doubling below walks int.MaxValue -> negative -> 0, which never reaches the
            // target and spins forever. A hash that hangs is worse than one that refuses.
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
