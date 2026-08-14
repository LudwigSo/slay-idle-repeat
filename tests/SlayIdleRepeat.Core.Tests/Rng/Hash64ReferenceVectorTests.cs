using Shouldly;
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

        hash.ShouldBe(row.Hash, $"'{row.Id}' pins {row.Why}");
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

        length.ShouldBe(row.EncodedByteLength, $"'{row.Id}' pins {row.Why}");
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

        rows.ShouldNotBeEmpty();
    }

    /// <summary>The table must still carry the two derivations of `02` §2 and `14` §8.1.</summary>
    /// <remarks>
    /// <c>ContainSingle</c> rather than <c>Row(id)</c> inside a <c>NotThrow</c>: the same teeth, and
    /// a failure names the missing id instead of reporting that some call threw something.
    /// </remarks>
    [Theory]
    [InlineData("derivation-runseed")]
    [InlineData("derivation-battleseed-0")]
    public void The_reference_table_still_covers_both_seed_derivations(string rowId)
    {
        ReferenceVectors.Canonical.Where(row => row.Id == rowId).ShouldHaveSingleItem();
    }

    /// <summary>
    /// 🔒 The <b>published</b> xxHash sanity rows must keep their teeth too. Nothing guarded them
    /// before: the argument-type guard above only covers the canonical rows, so an edit could have
    /// trimmed these nine to one and left a suite that still passed while no longer checking the
    /// 32-byte accumulator loop (length 222), the sub-4-byte tail (length 1), the empty input, or
    /// the PRIME32 hash seed — which is the only thing that exercises accumulator initialisation.
    /// </summary>
    [Theory]
    [InlineData(0, 0x0000000000000000UL)]
    [InlineData(0, 0x000000009E3779B1UL)]
    [InlineData(1, 0x0000000000000000UL)]
    [InlineData(1, 0x000000009E3779B1UL)]
    [InlineData(4, 0x0000000000000000UL)]
    [InlineData(14, 0x0000000000000000UL)]
    [InlineData(14, 0x000000009E3779B1UL)]
    [InlineData(222, 0x0000000000000000UL)]
    [InlineData(222, 0x000000009E3779B1UL)]
    public void The_published_sanity_set_still_covers_every_length_and_seed(int length, ulong seed)
    {
        ReferenceVectors.Published
            .Where(row => row.Length == length && row.Seed == seed).ShouldHaveSingleItem();
    }

    /// <summary>The three short ASCII vectors that circulate with every port of xxHash.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("abc")]
    public void The_published_ascii_set_still_covers_its_three_inputs(string input)
    {
        ReferenceVectors.PublishedAscii
            .Where(row => row.Input == input).ShouldHaveSingleItem();
    }

    /// <summary>
    /// 🔒 And the nine <see cref="DeterministicRng"/> draw rows. Only <c>combat-0</c>,
    /// <c>drops-99</c> and <c>shrine-near-max</c> are referenced by id anywhere, so trimming the
    /// table to those three would have passed everything while deleting the only seed-0 rows, the
    /// only <c>minigame:{index}</c> row, and the only <c>board</c> and <c>draft</c> rows — the
    /// failure a pinned table is least able to announce.
    /// </summary>
    [Theory]
    [InlineData("dice-0")]
    [InlineData("dice-1")]
    [InlineData("dice-12")]
    [InlineData("board-8")]
    [InlineData("draft-0-seeded")]
    [InlineData("drops-99")]
    [InlineData("combat-0")]
    [InlineData("minigame-3-5")]
    [InlineData("shrine-near-max")]
    public void The_draw_table_still_covers_every_committed_stream_and_position(string rowId)
    {
        ReferenceVectors.Draws.Where(row => row.Id == rowId).ShouldHaveSingleItem();
    }

    /// <summary>
    /// A mixed-type call is the shape both derivations take, and the one place an encoder can
    /// get every individual type right and still concatenate them wrongly.
    /// </summary>
    [Fact]
    public void Hash64_matches_the_committed_vector_for_a_call_mixing_every_argument_type()
    {
        var row = ReferenceVectors.Row("mixed-all-types");

        row.ArgumentTypes.ShouldBe(new[] { "ulong", "long", "int", "enum", "string" });
        Hash64.Of(row.Arguments.ToArray()).ShouldBe(row.Hash);
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

        intMinusOne.Hash.ShouldBe(longMinusOne.Hash);
        intMinusOne.Hash.ShouldBe(ulongMax.Hash);
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

        first.Hash.ShouldNotBe(second.Hash);
        Hash64.Of(first.Arguments.ToArray()).ShouldNotBe(Hash64.Of(second.Arguments.ToArray()));
    }

    public static TheoryData<string> CanonicalIds() => ReferenceVectors.CanonicalIds();
}
