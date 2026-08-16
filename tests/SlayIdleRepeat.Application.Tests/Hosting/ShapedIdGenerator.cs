using System.Buffers.Binary;
using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Application.Tests.Hosting;

/// <summary>
/// A generator whose guids are laid out byte by byte, so a case can state <em>which half</em> of the
/// identifier moves between two draws.
/// </summary>
/// <remarks>
/// <para>
/// The port promises only that the whole identifier is unique, never that any particular half of it
/// is. Both halves are therefore conforming shapes, and a host that read one of them would draw a
/// constant under a generator that varies the other — silently, since the command is still accepted.
/// The shipped fake varies its trailing bytes; a generator that varies its leading bytes is just as
/// legal, and neither shape can be told apart by anything the port declares.
/// </para>
/// <para>
/// Not a decorator over a shipped fake, which is what <c>RecordingIdGenerator</c> is: the layout is
/// the whole subject here, so the bytes have to be this class's own rather than another generator's.
/// </para>
/// </remarks>
internal sealed class ShapedIdGenerator : IIdGeneratorPort
{
    private readonly IReadOnlyList<Guid> _guids;
    private int _drawn;

    private ShapedIdGenerator(IReadOnlyList<Guid> guids) => _guids = guids;

    /// <summary>Guids that share their leading eight bytes and differ only after them.</summary>
    /// <param name="leading">The eight leading bytes every draw repeats — the shipped fake's prefix.</param>
    /// <param name="trailing">The trailing halves, one per draw, in draw order.</param>
    internal static ShapedIdGenerator VaryingTheTrailingBytes(ulong leading, params ulong[] trailing) =>
        new(trailing.Select(half => Compose(leading, half)).ToArray());

    /// <summary>Guids that share their trailing eight bytes and differ only before them.</summary>
    /// <param name="trailing">The eight trailing bytes every draw repeats.</param>
    /// <param name="leading">The leading halves, one per draw, in draw order.</param>
    internal static ShapedIdGenerator VaryingTheLeadingBytes(ulong trailing, params ulong[] leading) =>
        new(leading.Select(half => Compose(half, trailing)).ToArray());

    /// <inheritdoc/>
    public Guid NewGuid() =>
        _drawn < _guids.Count
            ? _guids[_drawn++]
            : throw new InvalidOperationException(
                "This generator was laid out with " + _guids.Count + " guids and a " +
                (_drawn + 1) + "th was drawn. A case that draws more than it laid out is asserting " +
                "about bytes nobody chose.");

    /// <inheritdoc/>
    public string NewCommandId() =>
        throw new NotSupportedException(
            "Nothing in these cases mints a command id, so this generator authors none. A caller " +
            "reaching here is drawing from a fixture that has no answer rather than from one whose " +
            "answer is wrong.");

    private static Guid Compose(ulong leading, ulong trailing)
    {
        Span<byte> bytes = stackalloc byte[16];

        BinaryPrimitives.WriteUInt64LittleEndian(bytes, leading);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[8..], trailing);

        return new Guid(bytes);
    }
}
