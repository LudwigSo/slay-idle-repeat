using Shouldly;
using SlayIdleRepeat.Core.Rng;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rng;

/// <summary>The committed reference-vector table, asserted row by row.</summary>
/// <remarks>
/// A failure here is a determinism break, never a test fix. The rows were generated from
/// <c>System.IO.Hashing.XxHash64</c>, a second independent XXH64.
/// </remarks>
public sealed class Hash64ReferenceVectorTests
{
    [Theory]
    [MemberData(nameof(CanonicalIds))]
    public void Hash64_matches_every_committed_reference_vector(string rowId)
    {
        var row = ReferenceVectors.Row(rowId);

        var hash = Hash64.Of(row.Arguments.ToArray());

        hash.ShouldBe(row.Hash, $"'{row.Id}' pins {row.Why}");
    }

    /// <summary>
    /// Asserted alongside the hash because it localises a break: a length mismatch says the
    /// encoding moved, a hash mismatch at the right length says the hash did.
    /// </summary>
    [Theory]
    [MemberData(nameof(CanonicalIds))]
    public void The_canonical_encoding_matches_every_committed_byte_length(string rowId)
    {
        var row = ReferenceVectors.Row(rowId);

        var length = Hash64.CanonicalByteCount(row.Arguments.ToArray());

        length.ShouldBe(row.EncodedByteLength, $"'{row.Id}' pins {row.Why}");
    }

    public static TheoryData<string> CanonicalIds() => ReferenceVectors.CanonicalIds();
}
