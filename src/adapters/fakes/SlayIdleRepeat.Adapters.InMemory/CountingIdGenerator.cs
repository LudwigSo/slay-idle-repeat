using System.Buffers.Binary;
using System.Globalization;
using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>
/// The counting fake for <see cref="IIdGeneratorPort"/>: a reproducible sequence <em>within</em>
/// one generator, and a sequence no other generator shares.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>What a scenario may rely on.</b> The two sequences are separate and count from one, so a
/// draw of one kind never moves the other's counter: the <c>n</c>-th <see cref="NewGuid"/> is
/// always this generator's eight prefix bytes followed by <c>n</c> big-endian, and the
/// <c>n</c>-th <see cref="NewCommandId"/> is always <c>cid-{Tag}-{n}</c>. A test can therefore
/// assert on the third command id without knowing how many guids ran past it.
/// </para>
/// <para>
/// 🔒 <b>What it may not rely on: <see cref="Tag"/> itself.</b> The prefix is eight fresh random
/// bytes taken at construction, and that is the whole reason this fake is not the defect its name
/// invites. A generator restarting at one on every construction would hand the next run of a
/// process the very idempotency keys the last run used, and a retried command would then be
/// matched against an unrelated recorded outcome and its result replayed. The sequence is
/// reproducible relative to one generator, never across two.
/// </para>
/// </remarks>
public sealed class CountingIdGenerator : IIdGeneratorPort
{
    private const int PrefixLength = 8;
    private const int GuidLength = 16;

    private readonly byte[] _prefix = new byte[PrefixLength];
    private long _guidsDrawn;
    private long _commandIdsDrawn;

    /// <summary>Creates a generator with a prefix of its own.</summary>
    public CountingIdGenerator()
    {
        Guid.NewGuid().ToByteArray().AsSpan(0, PrefixLength).CopyTo(_prefix);
        Tag = ToBase64Url(_prefix);
    }

    /// <summary>
    /// This generator's per-instance prefix, base64url-encoded — the part of every identifier it
    /// mints that distinguishes it from every other generator.
    /// </summary>
    public string Tag { get; }

    /// <inheritdoc/>
    public Guid NewGuid()
    {
        Span<byte> bytes = stackalloc byte[GuidLength];
        _prefix.AsSpan().CopyTo(bytes);

        // Counted from one and written big-endian, so the trailing bytes are never all zero and no
        // draw can come out as the empty guid.
        BinaryPrimitives.WriteInt64BigEndian(bytes[PrefixLength..], ++_guidsDrawn);

        return new Guid(bytes);
    }

    /// <inheritdoc/>
    public string NewCommandId() =>
        $"cid-{Tag}-{(++_commandIdsDrawn).ToString(CultureInfo.InvariantCulture)}";

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
