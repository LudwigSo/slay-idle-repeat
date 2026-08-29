using System.Buffers.Binary;
using System.Globalization;
using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Application.Tests.Parity;

/// <summary>
/// An <see cref="IIdGeneratorPort"/> whose sequence is reproducible <em>across</em> instances built
/// from the same seed.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>This is the one property <c>CountingIdGenerator</c> deliberately refuses</b>, and its
/// remarks say why: a generator that restarted at one on every construction would hand the next run
/// of a process the idempotency keys the last run used. That refusal is right for every other
/// fixture and fatal for this one. The parity comparison runs the same commands through two hosts
/// that each draw their own fresh entropy for a meta command's <c>CommandSeed</c>; with two
/// <c>CountingIdGenerator</c>s the two sides would draw different seeds, every meta command that
/// consumes randomness would legitimately produce different state, and the comparison would be red
/// for a reason that has nothing to do with parity.
/// </para>
/// <para>
/// So this generator is not a second <c>CountingIdGenerator</c> with the safety filed off — it
/// implements a different contract, and it exists only inside the parity fixture, where the two
/// sides are meant to be handed identical entropy on purpose.
/// </para>
/// <para>
/// <see cref="GuidsDrawn"/> is the lockstep check itself: after a sequence, the two sides must have
/// drawn the same number of guids, or one of them consumed entropy the other did not and the
/// matching hashes above it were luck.
/// </para>
/// </remarks>
internal sealed class LockstepIdGenerator : IIdGeneratorPort
{
    private const int PrefixLength = 8;
    private const int GuidLength = 16;

    private readonly byte[] _prefix = new byte[PrefixLength];
    private long _guidsDrawn;
    private long _commandIdsDrawn;

    /// <summary>Builds a generator whose whole sequence is decided by <paramref name="seed"/>.</summary>
    internal LockstepIdGenerator(ulong seed)
    {
        BinaryPrimitives.WriteUInt64BigEndian(_prefix, seed);
        Tag = Convert.ToHexString(_prefix).ToLowerInvariant();
    }

    /// <summary>This generator's prefix, as hexadecimal — the same for every generator on the same seed.</summary>
    internal string Tag { get; }

    /// <summary>How many guids this generator has minted.</summary>
    internal long GuidsDrawn => Interlocked.Read(ref _guidsDrawn);

    /// <summary>Draws forward without using the result, so two sides can be aligned deliberately.</summary>
    /// <param name="draws">How many guids to burn.</param>
    internal void Advance(int draws)
    {
        for (var draw = 0; draw < draws; draw++)
        {
            NewGuid();
        }
    }

    /// <inheritdoc/>
    public Guid NewGuid()
    {
        Span<byte> bytes = stackalloc byte[GuidLength];
        _prefix.AsSpan().CopyTo(bytes);

        // Counted from one and written big-endian, so no draw can come out as the empty guid.
        BinaryPrimitives.WriteInt64BigEndian(
            bytes[PrefixLength..], Interlocked.Increment(ref _guidsDrawn));

        return new Guid(bytes);
    }

    /// <inheritdoc/>
    public string NewCommandId() =>
        $"cid-{Tag}-{Interlocked.Increment(ref _commandIdsDrawn).ToString(CultureInfo.InvariantCulture)}";
}
