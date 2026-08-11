using System.Text;
using FluentAssertions;
using SlayIdleRepeat.Core.Model.Snapshots;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model.Snapshots;

/// <summary>
/// 🔒 The <b>external</b> check on `14` §16.6's algorithm: Landon Curt Noll's published
/// FNV-1a 64 test vectors, over raw bytes.
/// </summary>
/// <remarks>
/// <para>
/// A hash that is merely self-consistent passes every test its own author writes and fails the
/// day someone cross-checks it against another implementation. These rows come from outside this
/// repository — <c>github.com/jakedouglas/fnv-java</c>'s <c>test_fnv1a_64()</c> and
/// <c>github.com/littledan/Factor</c>'s <c>fnv1-tests.factor</c>, both transcribing Noll's
/// <c>test_fnv.c</c> — and an implementation that reproduces them <i>is</i> FNV-1a 64.
/// </para>
/// <para>
/// These are <b>not</b> canonical-encoding rows: the input is the raw UTF-8 bytes, with no length
/// prefix and no presence byte. They pin the hash, not the encoding.
/// </para>
/// </remarks>
public sealed class Fnv1a64KnownAnswerTests
{
    /// <summary>Every published vector: the raw UTF-8 bytes hash to exactly the published value.</summary>
    [Theory]
    [MemberData(nameof(PublishedRows))]
    public void Fnv1a64_reproduces_every_published_known_answer_vector(string input, ulong expected)
    {
        var bytes = Encoding.UTF8.GetBytes(input);

        var hash = CanonicalStateWriter.Fnv1a64(bytes);

        hash.Should().Be(expected);
    }

    /// <summary>
    /// The empty input is the offset basis itself, untouched. It is the one vector that pins the
    /// initialisation rather than the mixing, and the one a "clever" empty-input shortcut breaks.
    /// </summary>
    [Fact]
    public void Fnv1a64_returns_the_offset_basis_for_an_empty_input()
    {
        var hash = CanonicalStateWriter.Fnv1a64(ReadOnlySpan<byte>.Empty);

        hash.Should().Be(0xcbf29ce484222325UL);
    }

    /// <summary>
    /// One byte, computed by hand from the FNV-1a definition — basis XOR the byte, times the
    /// prime. Written out so the parameters are pinned by arithmetic, not only by a table.
    /// </summary>
    [Fact]
    public void Fnv1a64_xors_before_it_multiplies()
    {
        var expected = unchecked((0xcbf29ce484222325UL ^ 'a') * 0x100000001b3UL);

        var hash = CanonicalStateWriter.Fnv1a64(new byte[] { (byte)'a' });

        hash.Should().Be(expected).And.Be(0xaf63dc4c8601ec8cUL);
    }

    /// <summary>
    /// 🔒 FNV-1**a**, not FNV-1: the two differ only in the order of the XOR and the multiply,
    /// and produce completely different values. Pinned against FNV-1's published <c>"a"</c>.
    /// </summary>
    [Fact]
    public void Fnv1a64_is_not_the_multiply_first_FNV_1_variant()
    {
        var fnv1 = unchecked((0xcbf29ce484222325UL * 0x100000001b3UL) ^ 'a');

        var hash = CanonicalStateWriter.Fnv1a64(new byte[] { (byte)'a' });

        hash.Should().NotBe(fnv1);
    }

    /// <summary>
    /// The offset basis and prime the committed table names are the ones the specification names.
    /// The table guards the code; this guards the table's own header.
    /// </summary>
    [Fact]
    public void The_reference_table_names_the_specified_FNV_1a_64_parameters()
    {
        CanonicalReferenceVectors.OffsetBasis.Should().Be(0xcbf29ce484222325UL);
        CanonicalReferenceVectors.Prime.Should().Be(0x100000001b3UL);
    }

    /// <summary>
    /// The published set must keep its teeth: the empty input, a single byte, a multi-byte input
    /// and an input carrying a non-printable byte. A future edit that trimmed it to "foobar"
    /// alone would leave a suite that still passes while checking far less.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("foobar")]
    [InlineData("chongo was here!\n")]
    [InlineData("Hello, world!")]
    public void The_published_set_still_covers_its_load_bearing_inputs(string input)
    {
        CanonicalReferenceVectors.Published
            .Should().ContainSingle(row => row.Input == input);
    }

    public static TheoryData<string, ulong> PublishedRows() => CanonicalReferenceVectors.PublishedRows();
}
