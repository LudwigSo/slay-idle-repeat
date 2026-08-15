using System.Text;
using Shouldly;
using SlayIdleRepeat.Core.Rng;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rng;

/// <summary>The external check: <c>Hash64</c> must be <b>XXH64</b>, not merely a self-consistent 64-bit hash.</summary>
/// <remarks>
/// Every expectation here comes from outside this repository — <c>XSUM_XXH64_testdata</c> in
/// <c>cli/xsum_sanity_check.c</c> of <c>Cyan4973/xxHash</c>, the reference implementation's own
/// known-answer tests.
/// </remarks>
public sealed class Hash64KnownAnswerTests
{
    /// <summary>
    /// The single most-quoted xxHash constant. If nothing else in this file survives, this
    /// row identifies the algorithm.
    /// </summary>
    [Fact]
    public void XxHash64_of_the_empty_input_is_the_published_constant()
    {
        var hash = Hash64.XxHash64(ReadOnlySpan<byte>.Empty);

        hash.ShouldBe(0xEF46DB3751D8E999UL);
    }

    /// <summary>
    /// The reference implementation's own sanity table, every row: lengths 0, 1, 4, 14 and 222
    /// over the buffer <c>xsum</c> generates, at hash-seed 0 and at PRIME32. The seeded rows
    /// exercise the accumulator initialisation that the seed-0 rows cannot reach.
    /// </summary>
    [Theory]
    [MemberData(nameof(PublishedRows))]
    public void XxHash64_reproduces_every_published_xxHash_sanity_vector(int length, ulong seed, ulong expected)
    {
        var buffer = ReferenceVectors.SanityBuffer(length);

        var hash = Hash64.XxHash64(buffer, seed);

        hash.ShouldBe(expected);
    }

    /// <summary>
    /// Pins the harness, not the hash: if the sanity buffer were built wrongly, the rows above
    /// would be testing <c>XXH64</c> of the wrong bytes and would simply fail with no clue why.
    /// The prefix is the reference generator's output for <c>PRIME32</c>/<c>PRIME64</c>.
    /// </summary>
    [Fact]
    public void The_sanity_buffer_is_the_one_the_reference_implementation_generates()
    {
        var buffer = ReferenceVectors.SanityBuffer(16);

        Convert.ToHexString(buffer).ShouldBe("0052929BB732A3242D00AF950EECB893");
    }

    /// <summary>
    /// The three short ASCII vectors that ship with almost every port of xxHash: raw UTF-8 bytes
    /// with no length prefix, deliberately not canonical-encoding rows.
    /// </summary>
    [Theory]
    [MemberData(nameof(AsciiRows))]
    public void XxHash64_reproduces_the_published_short_ASCII_vectors(string input, ulong expected)
    {
        var hash = Hash64.XxHash64(new UTF8Encoding(false).GetBytes(input));

        hash.ShouldBe(expected);
    }

    /// <summary>
    /// XXH64 changes shape at 32 bytes: below it the accumulators are never used, at and above
    /// it four of them run over whole stripes, and whatever is left over is drained in 8-, 4- and
    /// 1-byte steps. A hash that gets one branch right and the other wrong passes half a suite,
    /// so both sides of the boundary, the boundary itself, and a stripe with a ragged tail are
    /// each pinned against the independent table.
    /// </summary>
    [Theory]
    [InlineData("draw-minigame-3", 30)]
    [InlineData("string-len-28", 32)]
    [InlineData("mixed-all-types", 39)]
    [InlineData("string-len-60", 64)]
    [InlineData("string-len-61", 65)]
    public void XxHash64_hashes_inputs_on_both_sides_of_the_32_byte_stripe_boundary(string rowId, int expectedLength)
    {
        var row = ReferenceVectors.Row(rowId);

        row.EncodedByteLength.ShouldBe(expectedLength);
        Hash64.Of(row.Arguments.ToArray()).ShouldBe(row.Hash);
    }

    public static TheoryData<int, ulong, ulong> PublishedRows() => ReferenceVectors.PublishedRows();

    public static TheoryData<string, ulong> AsciiRows() => ReferenceVectors.AsciiRows();
}
