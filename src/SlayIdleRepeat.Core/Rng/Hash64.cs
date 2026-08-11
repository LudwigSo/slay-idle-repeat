using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace SlayIdleRepeat.Core.Rng;

/// <summary>
/// 🔒 `14` §8.0 — the project's one pinned hash: <b>xxHash64</b> (XXH64, hash-seed parameter 0)
/// over the canonical byte encoding of its arguments, concatenated in argument order.
/// </summary>
/// <remarks>
/// <para>
/// One implementation, used for <b>both</b> seed derivation (<c>runSeed</c> — `02` §2;
/// <c>battleSeed</c> — `14` §8.1) and every draw the game ever makes. Two implementations would
/// eventually be two hashes.
/// </para>
/// <para>
/// The canonical encoding:
/// </para>
/// <list type="table">
///   <item>
///     <term><c>ulong</c> / <c>long</c> / <c>int</c> / enum</term>
///     <description>widened to 64 bits (ints sign-extended), 8 bytes little-endian</description>
///   </item>
///   <item>
///     <term><c>string</c></term>
///     <description>a 4-byte little-endian UTF-8 <b>byte</b> count, then the UTF-8 bytes</description>
///   </item>
/// </list>
/// <para>
/// 🔒 <b>There is a second encoder in this codebase, and that is deliberate.</b>
/// <c>Model/Snapshots/CanonicalStateWriter</c> implements `14` §16.6 — the canonical <i>state</i>
/// encoding behind every <c>stateHash</c>. The paragraph above says "two implementations would
/// eventually be two hashes"; it means two implementations <b>of this</b>, §8.0, the draw and seed
/// hash. §16.6 is a different specification with a different job, and the two are <b>not</b> to be
/// deduplicated: `23`'s <c>Core_internal_layering_holds</c> puts <c>Rng</c> and <c>Model</c> in
/// sibling layers with no shared home to move a shared encoder into, and merging the two tables
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
/// Everything else differs by design: xxHash64 here against FNV-1a 64 there; a two-row table here
/// against a ten-row one there; a raw <see cref="ulong"/> here against the <c>"fnv1a:"</c>-prefixed
/// wire form there. ⚠️ In particular <b>§8.0 has no boolean row</b>. A future caller hand-encoding
/// a bool into a seed would invent <c>1</c>/<c>0</c> as a widened 8-byte integer, which is
/// <i>not</i> §16.6's one-byte rule — so it would be a third encoding, not either of these two.
/// Add a row to §8.0 and to <see cref="Hash64Argument"/> rather than encoding it at the call site.
/// </para>
/// <para>
/// XXH64 is written out here rather than taken from <c>System.IO.Hashing</c> because
/// <c>SlayIdleRepeat.Core</c> references nothing at all (`23` §2.1) — the whole game must be
/// playable from this assembly alone. The compensating control is the committed reference-vector
/// table in <c>SlayIdleRepeat.Core.Tests</c>, whose rows come from Microsoft's independent
/// implementation and from the reference implementation's own published known-answer tests.
/// </para>
/// <para>
/// It is not cryptographic and does not need to be (`14` §8.1): unpredictability comes from the
/// server keeping <c>runSeed</c>, not from the hash.
/// </para>
/// </remarks>
public static class Hash64
{
    private const ulong Prime1 = 0x9E3779B185EBCA87UL;
    private const ulong Prime2 = 0xC2B2AE3D27D4EB4FUL;
    private const ulong Prime3 = 0x165667B19E3779F9UL;
    private const ulong Prime4 = 0x85EBCA77C2B2AE63UL;
    private const ulong Prime5 = 0x27D4EB2F165667C5UL;

    /// <summary>The largest canonical buffer built on the stack. Beyond it, one array is cheaper than a deep frame.</summary>
    private const int StackBufferBytes = 256;

    /// <summary>Bytes a string's length prefix occupies.</summary>
    private const int LengthPrefixBytes = 4;

    /// <summary>Bytes a widened integral argument occupies.</summary>
    private const int IntegerBytes = 8;

    /// <summary>
    /// UTF-8 without a byte-order mark. Named explicitly rather than taken from
    /// <see cref="Encoding.UTF8"/> so nothing about this encoding is inherited from a static
    /// whose configuration could be read two ways.
    /// </summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Hashes the canonical encoding of the given arguments, concatenated in argument order.
    /// </summary>
    /// <remarks>
    /// No arguments is the empty buffer, and hashes as such. The encoding has no special cases.
    /// </remarks>
    public static ulong Of(params Hash64Argument[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return OfArguments(arguments);
    }

    /// <summary>
    /// The draw shape — <c>Hash64(seed, streamName, drawIndex)</c> — which is every single random
    /// draw in the game, and the <c>battleSeed</c> derivation besides.
    /// </summary>
    /// <remarks>
    /// Byte-identical to <see cref="Of(Hash64Argument[])"/> with the same three arguments; it
    /// exists only to build the buffer on the stack instead of allocating an argument array per
    /// draw. A test asserts the two agree, because a fast path that quietly disagrees with the
    /// slow one is the worst defect this file could carry.
    /// </remarks>
    public static ulong Of(ulong seed, string streamName, ulong drawIndex)
    {
        ArgumentNullException.ThrowIfNull(streamName);

        var nameBytes = Utf8.GetByteCount(streamName);
        var total = CheckedTotal((long)IntegerBytes + LengthPrefixBytes + nameBytes + IntegerBytes);

        // Sized to `total`, not to StackBufferBytes: a stackalloc is zeroed before it is handed
        // over, and this method runs once per draw. Zeroing the whole 256-byte ceiling to fill
        // the ~24 bytes a draw actually uses would be per-draw work for nothing.
        Span<byte> buffer = total <= StackBufferBytes ? stackalloc byte[total] : new byte[total];

        BinaryPrimitives.WriteUInt64LittleEndian(buffer, seed);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[IntegerBytes..], nameBytes);
        Utf8.GetBytes(streamName, buffer.Slice(IntegerBytes + LengthPrefixBytes, nameBytes));
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[(IntegerBytes + LengthPrefixBytes + nameBytes)..], drawIndex);

        return XxHash64(buffer);
    }

    /// <summary>
    /// The length of the canonical buffer these arguments encode to: 8 bytes per integral
    /// argument, 4 + the UTF-8 byte count per string.
    /// </summary>
    /// <remarks>
    /// Accumulated in <see cref="long"/>: an <see cref="int"/> total can overflow to a negative
    /// number across enough or long enough string arguments, and a negative total reaches
    /// <c>stackalloc byte[total]</c> in both <see cref="Of(Hash64Argument[])"/> and the draw
    /// overload. Unreachable in practice, and refused explicitly anyway — the equivalent case in
    /// <c>CanonicalStateWriter.CanonicalBuffer.EnsureCapacity</c> is guarded the same way, and an
    /// asymmetry between the two encoders is the kind of thing that becomes a real difference.
    /// </remarks>
    /// <exception cref="NotSupportedException">The arguments encode to more than <see cref="Array.MaxLength"/> bytes.</exception>
    internal static int CanonicalByteCount(ReadOnlySpan<Hash64Argument> arguments)
    {
        long total = 0;
        foreach (var argument in arguments)
        {
            total += argument.IsText
                ? LengthPrefixBytes + Utf8.GetByteCount(argument.Text)
                : IntegerBytes;
        }

        return CheckedTotal(total);
    }

    /// <summary>A canonical buffer length, or the refusal for one that cannot be allocated.</summary>
    private static int CheckedTotal(long total)
    {
        if (total > Array.MaxLength)
        {
            throw new NotSupportedException(
                $"These arguments encode to {total} bytes, beyond the {Array.MaxLength} bytes one " +
                "canonical buffer can occupy (`14` §8.0). An argument list that large is a caller " +
                "bug — an unbounded string, most likely — not a draw to take.");
        }

        return (int)total;
    }

    /// <summary>
    /// Writes the canonical encoding of the arguments into <paramref name="destination"/>, which
    /// must be exactly <see cref="CanonicalByteCount"/> bytes long.
    /// </summary>
    internal static void WriteCanonical(ReadOnlySpan<Hash64Argument> arguments, Span<byte> destination)
    {
        var written = 0;
        foreach (var argument in arguments)
        {
            if (argument.IsText)
            {
                var byteCount = Utf8.GetByteCount(argument.Text);
                BinaryPrimitives.WriteInt32LittleEndian(destination[written..], byteCount);
                written += LengthPrefixBytes;
                Utf8.GetBytes(argument.Text, destination.Slice(written, byteCount));
                written += byteCount;
            }
            else
            {
                BinaryPrimitives.WriteInt64LittleEndian(destination[written..], argument.Integer);
                written += IntegerBytes;
            }
        }
    }

    /// <summary>
    /// XXH64 over raw bytes, exactly as the reference implementation defines it.
    /// </summary>
    /// <param name="data">The bytes to hash.</param>
    /// <param name="hashSeed">
    /// The XXH64 hash-seed parameter. 🔒 Production always uses <c>0</c> — `14` §8.0 pins it, and
    /// nothing in <c>Core</c> passes anything else. It is a parameter so the suite can assert the
    /// reference implementation's PRIME32-seeded known-answer vectors, which are the only rows
    /// that exercise the accumulator initialisation below.
    /// </param>
    /// <remarks>
    /// <c>internal</c> because the public surface of this class is the canonical encoding; a
    /// public raw-bytes door would be an invitation to hash something without it. The domain test
    /// suite reaches it through the <c>InternalsVisibleTo</c> that `30` §11.3 sanctions.
    /// </remarks>
    internal static ulong XxHash64(ReadOnlySpan<byte> data, ulong hashSeed = 0)
    {
        // Every step of XXH64 relies on 64-bit wraparound. `unchecked` is the default, but this
        // hash must not become a runtime exception if a future build ever flips that switch.
        unchecked
        {
            var index = 0;
            ulong hash;

            if (data.Length >= 32)
            {
                var accumulator1 = hashSeed + Prime1 + Prime2;
                var accumulator2 = hashSeed + Prime2;
                var accumulator3 = hashSeed;
                var accumulator4 = hashSeed - Prime1;

                do
                {
                    accumulator1 = Round(accumulator1, BinaryPrimitives.ReadUInt64LittleEndian(data[index..]));
                    accumulator2 = Round(accumulator2, BinaryPrimitives.ReadUInt64LittleEndian(data[(index + 8)..]));
                    accumulator3 = Round(accumulator3, BinaryPrimitives.ReadUInt64LittleEndian(data[(index + 16)..]));
                    accumulator4 = Round(accumulator4, BinaryPrimitives.ReadUInt64LittleEndian(data[(index + 24)..]));
                    index += 32;
                }
                while (index <= data.Length - 32);

                hash = BitOperations.RotateLeft(accumulator1, 1) +
                       BitOperations.RotateLeft(accumulator2, 7) +
                       BitOperations.RotateLeft(accumulator3, 12) +
                       BitOperations.RotateLeft(accumulator4, 18);

                hash = MergeRound(hash, accumulator1);
                hash = MergeRound(hash, accumulator2);
                hash = MergeRound(hash, accumulator3);
                hash = MergeRound(hash, accumulator4);
            }
            else
            {
                hash = hashSeed + Prime5;
            }

            hash += (ulong)data.Length;

            while (data.Length - index >= 8)
            {
                hash ^= Round(0, BinaryPrimitives.ReadUInt64LittleEndian(data[index..]));
                hash = (BitOperations.RotateLeft(hash, 27) * Prime1) + Prime4;
                index += 8;
            }

            if (data.Length - index >= 4)
            {
                hash ^= BinaryPrimitives.ReadUInt32LittleEndian(data[index..]) * Prime1;
                hash = (BitOperations.RotateLeft(hash, 23) * Prime2) + Prime3;
                index += 4;
            }

            while (index < data.Length)
            {
                hash ^= data[index] * Prime5;
                hash = BitOperations.RotateLeft(hash, 11) * Prime1;
                index++;
            }

            return Avalanche(hash);
        }
    }

    private static ulong OfArguments(ReadOnlySpan<Hash64Argument> arguments)
    {
        var total = CanonicalByteCount(arguments);

        Span<byte> buffer = total <= StackBufferBytes ? stackalloc byte[total] : new byte[total];

        WriteCanonical(arguments, buffer);

        return XxHash64(buffer);
    }

    private static ulong Round(ulong accumulator, ulong input)
    {
        unchecked
        {
            accumulator += input * Prime2;
            return BitOperations.RotateLeft(accumulator, 31) * Prime1;
        }
    }

    private static ulong MergeRound(ulong accumulator, ulong value)
    {
        unchecked
        {
            return ((accumulator ^ Round(0, value)) * Prime1) + Prime4;
        }
    }

    private static ulong Avalanche(ulong hash)
    {
        unchecked
        {
            hash ^= hash >> 33;
            hash *= Prime2;
            hash ^= hash >> 29;
            hash *= Prime3;
            hash ^= hash >> 32;
            return hash;
        }
    }
}
