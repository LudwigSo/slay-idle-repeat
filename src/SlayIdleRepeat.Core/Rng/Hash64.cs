using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace SlayIdleRepeat.Core.Rng;

/// <summary>The project's one pinned hash: <b>xxHash64</b> (XXH64, hash-seed parameter 0) over the canonical byte encoding of its arguments, concatenated in argument order.</summary>
/// <remarks>
/// One implementation, used for both seed derivation (<c>runSeed</c>, <c>battleSeed</c>) and every
/// draw the game ever makes — two implementations would eventually be two hashes.
/// <para>
/// The canonical encoding: <c>ulong</c>/<c>long</c>/<c>int</c>/enum widen to 64 bits (ints
/// sign-extended), 8 bytes little-endian; a <c>string</c> is a 4-byte little-endian UTF-8 byte
/// count followed by the UTF-8 bytes.
/// </para>
/// <para>
/// <c>Model/Snapshots/CanonicalStateWriter</c> is a second, deliberately separate encoder for a
/// different job (the state hash behind <c>stateHash</c>). They independently agree on integer
/// widening, string length-prefixing and the UTF-8 encoding used, and those must change together —
/// a change to one side's widening or prefix without the other silently forks the two encodings
/// with nothing to notice. Everything else differs by design (xxHash64 vs FNV-1a, no boolean row
/// here vs one there).
/// </para>
/// <para>
/// XXH64 is written out here rather than taken from <c>System.IO.Hashing</c> because
/// <c>SlayIdleRepeat.Core</c> references nothing at all — the whole game must be playable from this
/// assembly alone. It is validated against a committed reference-vector table in
/// <c>SlayIdleRepeat.Core.Tests</c>.
/// </para>
/// <para>It is not cryptographic and does not need to be: unpredictability comes from the server keeping <c>runSeed</c>, not from the hash.</para>
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

    /// <summary>UTF-8 without a byte-order mark, named explicitly rather than taken from <see cref="Encoding.UTF8"/>.</summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Hashes the canonical encoding of the given arguments, concatenated in argument order.</summary>
    public static ulong Of(params Hash64Argument[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return OfArguments(arguments);
    }

    /// <summary>The draw shape — <c>Hash64(seed, streamName, drawIndex)</c> — which is every single random draw in the game, and the <c>battleSeed</c> derivation besides.</summary>
    /// <remarks>
    /// Byte-identical to <see cref="Of(Hash64Argument[])"/> with the same three arguments; exists
    /// only to build the buffer on the stack instead of allocating an argument array per draw. A
    /// test asserts the two agree.
    /// </remarks>
    public static ulong Of(ulong seed, string streamName, ulong drawIndex)
    {
        ArgumentNullException.ThrowIfNull(streamName);

        var nameBytes = Utf8.GetByteCount(streamName);
        var total = CheckedTotal((long)IntegerBytes + LengthPrefixBytes + nameBytes + IntegerBytes);

        // Sized to `total`, not StackBufferBytes: a stackalloc zeroes before use, and zeroing the
        // full 256-byte ceiling for the ~24 bytes a draw actually needs would waste per-draw work.
        Span<byte> buffer = total <= StackBufferBytes ? stackalloc byte[total] : new byte[total];

        BinaryPrimitives.WriteUInt64LittleEndian(buffer, seed);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[IntegerBytes..], nameBytes);
        Utf8.GetBytes(streamName, buffer.Slice(IntegerBytes + LengthPrefixBytes, nameBytes));
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[(IntegerBytes + LengthPrefixBytes + nameBytes)..], drawIndex);

        return XxHash64(buffer);
    }

    /// <summary>The length of the canonical buffer these arguments encode to: 8 bytes per integral argument, 4 + the UTF-8 byte count per string.</summary>
    /// <remarks>
    /// Accumulated in <see cref="long"/>: an <see cref="int"/> total could overflow to a negative
    /// number across enough string arguments, and a negative total would reach
    /// <c>stackalloc byte[total]</c>. Refused explicitly rather than left to overflow silently.
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
                "canonical buffer can occupy. An argument list that large is a caller bug — an " +
                "unbounded string, most likely — not a draw to take.");
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

    /// <summary>XXH64 over raw bytes, exactly as the reference implementation defines it.</summary>
    /// <param name="data">The bytes to hash.</param>
    /// <param name="hashSeed">
    /// The XXH64 hash-seed parameter. Production always uses <c>0</c>; it is a parameter only so
    /// the test suite can assert the reference implementation's seeded known-answer vectors.
    /// </param>
    /// <remarks>
    /// Internal because the public surface of this class is the canonical encoding; a public
    /// raw-bytes door would be an invitation to hash something without it.
    /// </remarks>
    internal static ulong XxHash64(ReadOnlySpan<byte> data, ulong hashSeed = 0)
    {
        // Every step relies on 64-bit wraparound; `unchecked` must hold even if a future build
        // flips the project default.
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
