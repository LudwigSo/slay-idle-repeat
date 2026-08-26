using System.Buffers.Binary;
using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Application.Hosting;

/// <summary>One meta command's seed, folded from the one sanctioned source of fresh entropy a host has.</summary>
/// <remarks>
/// Shared by every host that issues <c>GameContext.CommandSeed</c> — the in-process host and the
/// wire gateway — because two folds "done the same way" are the second spelling determinism work
/// forbids. Both halves of the guid are folded in rather than the low eight bytes taken: a
/// generator is only required to make the whole identifier unique, so a fake that varies its
/// trailing bytes and a real one that varies its leading bytes are both conforming, and reading
/// half of it would silently draw the same seed forever under one of them.
/// </remarks>
internal static class CommandSeedSource
{
    /// <summary>Draws one fresh seed.</summary>
    /// <param name="ids">The generator to draw from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ids"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The guid did not fill its sixteen bytes — see the body.</exception>
    internal static ulong Fresh(IIdGeneratorPort ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        Span<byte> bytes = stackalloc byte[16];

        // Checked rather than discarded. A write that did not happen leaves the buffer as the stack
        // left it, and the fold below would then draw the same seed for every command — the exact
        // failure the fold itself exists to rule out, and the one shape of it nothing would report.
        if (!ids.NewGuid().TryWriteBytes(bytes))
        {
            throw new InvalidOperationException(
                "A guid did not fit sixteen bytes, so this command's seed would be folded from a " +
                "buffer nothing wrote.");
        }

        return BinaryPrimitives.ReadUInt64LittleEndian(bytes) ^
               BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);
    }
}
