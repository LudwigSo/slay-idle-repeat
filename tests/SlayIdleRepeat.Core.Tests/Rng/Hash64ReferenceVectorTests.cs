using FluentAssertions;
using SlayIdleRepeat.Core.Rng;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rng;

/// <summary>
/// 🔒 The committed reference-vector table of `14` §8.0, asserted row by row.
/// </summary>
/// <remarks>
/// <para>
/// <b>A failure here is a determinism break, never a test fix.</b> The rows live in
/// <c>Rng/Hash64ReferenceVectors.json</c> and were generated from
/// <c>System.IO.Hashing.XxHash64</c> — a second, independent XXH64 — driven by an encoder
/// transcribed from the spec table. If a row moves, every seed derivation, board layout, die
/// roll and drop the game has produced for a given seed moved with it.
/// </para>
/// <para>
/// This is the table the cross-platform determinism job (`14` §8.2, task M5-12) re-asserts on
/// Linux x64, Android ARM64 and iOS ARM64.
/// </para>
/// </remarks>
public sealed class Hash64ReferenceVectorTests
{
    /// <summary>Every committed row: the arguments hash to exactly the committed value.</summary>
    [Theory]
    [MemberData(nameof(CanonicalIds))]
    public void Hash64_matches_every_committed_reference_vector(string rowId)
    {
        var row = ReferenceVectors.Row(rowId);

        var hash = Hash64.Of(row.Arguments.ToArray());

        hash.Should().Be(row.Hash, "'{0}' pins {1}", row.Id, row.Why);
    }

    /// <summary>
    /// Every committed row's canonical buffer is exactly the committed length. Asserted
    /// alongside the hash because it localises a break: a length mismatch says the encoding
    /// moved, a hash mismatch at the right length says the hash did.
    /// </summary>
    [Theory]
    [MemberData(nameof(CanonicalIds))]
    public void The_canonical_encoding_matches_every_committed_byte_length(string rowId)
    {
        var row = ReferenceVectors.Row(rowId);

        var length = Hash64.CanonicalByteCount(row.Arguments.ToArray());

        length.Should().Be(row.EncodedByteLength, "'{0}' pins {1}", row.Id, row.Why);
    }

    /// <summary>
    /// The table guards the code; this guards the table. A future edit that quietly drops the
    /// only negative-int row, or the only non-ASCII string, would leave a suite that still
    /// passes while covering less — the failure mode a pinned table is least able to announce.
    /// </summary>
    [Theory]
    [InlineData("ulong")]
    [InlineData("long")]
    [InlineData("int")]
    [InlineData("enum")]
    [InlineData("string")]
    public void The_reference_table_still_covers_every_argument_type(string argumentType)
    {
        var rows = ReferenceVectors.Canonical
            .Where(row => row.ArgumentTypes.Contains(argumentType, StringComparer.Ordinal));

        rows.Should().NotBeEmpty();
    }

    /// <summary>The table must still carry the two derivations of `02` §2 and `14` §8.1.</summary>
    [Theory]
    [InlineData("derivation-runseed")]
    [InlineData("derivation-battleseed-0")]
    public void The_reference_table_still_covers_both_seed_derivations(string rowId)
    {
        var act = () => ReferenceVectors.Row(rowId);

        act.Should().NotThrow();
    }

    /// <summary>Row ids identify a row in a failure message; duplicates make that a lie.</summary>
    [Fact]
    public void The_reference_table_ids_are_unique()
    {
        ReferenceVectors.Canonical.Select(row => row.Id).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// A mixed-type call is the shape both derivations take, and the one place an encoder can
    /// get every individual type right and still concatenate them wrongly.
    /// </summary>
    [Fact]
    public void Hash64_matches_the_committed_vector_for_a_call_mixing_every_argument_type()
    {
        var row = ReferenceVectors.Row("mixed-all-types");

        row.ArgumentTypes.Should().Equal("ulong", "long", "int", "enum", "string");
        Hash64.Of(row.Arguments.ToArray()).Should().Be(row.Hash);
    }

    /// <summary>
    /// Rows that deliberately share a hash. Three different declared types widen to the same
    /// eight bytes, and the table records that as agreement rather than as a collision to fix.
    /// </summary>
    [Fact]
    public void Rows_that_widen_to_the_same_bytes_share_a_committed_hash()
    {
        var intMinusOne = ReferenceVectors.Row("int-minus-one");
        var longMinusOne = ReferenceVectors.Row("long-minus-one");
        var ulongMax = ReferenceVectors.Row("ulong-max");

        intMinusOne.Hash.Should().Be(longMinusOne.Hash).And.Be(ulongMax.Hash);
    }

    /// <summary>
    /// Rows that must never share a hash: argument order and the length prefix. Pinned as a
    /// pair so the table itself proves the property, not just two unrelated constants.
    /// </summary>
    [Theory]
    [InlineData("order-int-then-string", "order-string-then-int")]
    [InlineData("ambiguity-two-strings-a", "ambiguity-two-strings-b")]
    [InlineData("derivation-runseed", "derivation-runseed-next-run")]
    [InlineData("derivation-battleseed-0", "derivation-battleseed-1")]
    public void Rows_the_encoding_must_keep_apart_have_different_committed_hashes(string firstId, string secondId)
    {
        var first = ReferenceVectors.Row(firstId);
        var second = ReferenceVectors.Row(secondId);

        first.Hash.Should().NotBe(second.Hash);
        Hash64.Of(first.Arguments.ToArray()).Should().NotBe(Hash64.Of(second.Arguments.ToArray()));
    }

    public static TheoryData<string> CanonicalIds() => ReferenceVectors.CanonicalIds();
}
