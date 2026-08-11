using System.Text;
using FluentAssertions;
using SlayIdleRepeat.Core.Rng;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rng;

/// <summary>
/// 🔒 The external check on `14` §8.0: <c>Hash64</c> is <b>XXH64</b>, not merely a
/// self-consistent 64-bit hash.
/// </summary>
/// <remarks>
/// Every expectation here comes from outside this repository — <c>XSUM_XXH64_testdata</c> in
/// <c>cli/xsum_sanity_check.c</c> of <c>Cyan4973/xxHash</c>, the reference implementation's own
/// known-answer tests. A hash that passes the reference-vector table but fails these is
/// deterministic and wrong: it would diverge the day anyone cross-checked it against another
/// language, and by then a season of runs would be pinned to it.
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

        hash.Should().Be(0xEF46DB3751D8E999UL);
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

        hash.Should().Be(expected);
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

        Convert.ToHexString(buffer).Should().Be("0052929BB732A3242D00AF950EECB893");
    }

    /// <summary>
    /// The three short ASCII vectors that ship with almost every port of xxHash. These are the
    /// raw UTF-8 bytes with no length prefix — the canonical <i>argument</i> encoding of §8.0
    /// adds one, so these are deliberately not canonical-encoding rows.
    /// </summary>
    [Theory]
    [MemberData(nameof(AsciiRows))]
    public void XxHash64_reproduces_the_published_short_ASCII_vectors(string input, ulong expected)
    {
        var hash = Hash64.XxHash64(new UTF8Encoding(false).GetBytes(input));

        hash.Should().Be(expected);
    }

    /// <summary>
    /// XXH64 changes shape at 32 bytes: below it the accumulators are never used, at and above
    /// it four of them run over whole stripes. A hash that gets one branch right and the other
    /// wrong passes half a suite, so both sides of the boundary and the boundary itself are
    /// pinned against the independent table.
    /// </summary>
    [Theory]
    [InlineData("string-len-28", 32)]
    [InlineData("string-len-60", 64)]
    [InlineData("string-len-61", 65)]
    public void XxHash64_hashes_inputs_at_and_above_the_32_byte_stripe_boundary(string rowId, int expectedLength)
    {
        var row = ReferenceVectors.Row(rowId);

        row.EncodedByteLength.Should().Be(expectedLength);
        Hash64.Of(row.Arguments.ToArray()).Should().Be(row.Hash);
    }

    public static TheoryData<int, ulong, ulong> PublishedRows() => ReferenceVectors.PublishedRows();

    public static TheoryData<string, ulong> AsciiRows() => ReferenceVectors.AsciiRows();
}
