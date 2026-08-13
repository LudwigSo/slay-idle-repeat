using Shouldly;
using SlayIdleRepeat.Core.Rng;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rng;

/// <summary>
/// 🔒 The canonical byte encoding of `14` §8.0 — the half of <c>Hash64</c> that is
/// SlayIdleRepeat's own, and the half a correct XXH64 cannot save.
/// </summary>
/// <remarks>
/// <para>
/// | <c>ulong</c> / <c>long</c> / <c>int</c> / enum | widened to 64 bits (ints sign-extended), 8 bytes little-endian |<br/>
/// | <c>string</c> | a 4-byte little-endian UTF-8 <b>byte</b> count, then the UTF-8 bytes |
/// </para>
/// <para>
/// These tests assert the bytes, not just the hash. A hash-only assertion tells you a row
/// moved; a byte-level assertion tells you why — and the two failure modes this encoding
/// actually has (zero-extending a negative int, counting <c>char</c>s instead of UTF-8 bytes)
/// are invisible until someone feeds it a negative number or a non-ASCII string in
/// production.
/// </para>
/// </remarks>
public sealed class Hash64EncodingTests
{
    /// <summary>
    /// 🔒 The one that bites. A negative <c>int</c> widens by sign extension, so -1 is eight
    /// <c>0xFF</c> bytes — not four <c>0xFF</c> bytes followed by four zeros.
    /// </summary>
    [Fact]
    public void The_encoding_sign_extends_a_negative_int_rather_than_zero_extending_it()
    {
        var bytes = Encode(-1);

        bytes.ShouldBe(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
    }

    /// <summary>Positive integers are plain little-endian, which pins byte order.</summary>
    [Fact]
    public void The_encoding_writes_an_integer_little_endian()
    {
        var bytes = Encode(1);

        bytes.ShouldBe(new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
    }

    /// <summary>
    /// The encoding is by <i>value</i>, not by declared type: three arguments that widen to the
    /// same 64 bits are the same eight bytes and therefore the same hash. This is what lets a
    /// caller pass a battle index as <c>int</c> where the spec writes <c>ulong</c> without
    /// silently forking the stream.
    /// </summary>
    [Fact]
    public void The_encoding_is_by_value_so_int_minus_one_long_minus_one_and_ulong_MaxValue_agree()
    {
        Hash64.Of(-1).ShouldBe(Hash64.Of(-1L));
        Hash64.Of(-1L).ShouldBe(Hash64.Of(ulong.MaxValue));
    }

    /// <summary>An enum widens through its underlying type, signed ones by sign extension.</summary>
    [Fact]
    public void The_encoding_widens_a_negative_int_backed_enum_by_sign_extension()
    {
        var bytes = Encode((ReferenceEnums.Int32Enum)(-1));

        bytes.ShouldBe(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
    }

    /// <summary>An unsigned-backed enum widens by zero extension, and past <c>long.MaxValue</c> intact.</summary>
    [Fact]
    public void The_encoding_widens_an_unsigned_enum_without_sign_extending_it()
    {
        var bytes = Encode((ReferenceEnums.UInt32Enum)uint.MaxValue);

        bytes.ShouldBe(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00 });
    }

    /// <summary>The empty string is a four-byte zero count and nothing else — never zero bytes.</summary>
    [Fact]
    public void The_encoding_writes_the_empty_string_as_a_four_byte_zero_count()
    {
        var bytes = Encode(string.Empty);

        bytes.ShouldBe(new byte[] { 0x00, 0x00, 0x00, 0x00 });
    }

    /// <summary>A one-byte ASCII string: the count, then the byte.</summary>
    [Fact]
    public void The_encoding_writes_a_string_as_a_four_byte_count_then_its_UTF8_bytes()
    {
        var bytes = Encode("a");

        bytes.ShouldBe(new byte[] { 0x01, 0x00, 0x00, 0x00, 0x61 });
    }

    /// <summary>
    /// 🔒 The prefix counts <b>UTF-8 bytes</b>, not <c>char</c>s. "größe" is five chars and
    /// seven bytes; a naive implementation writes 5 and produces a hash that is stable, wrong,
    /// and impossible to reconcile with any other language's implementation.
    /// </summary>
    [Fact]
    public void The_encoding_counts_UTF8_bytes_not_chars_for_a_non_ASCII_string()
    {
        var bytes = Encode("größe");

        bytes.Length.ShouldBe(4 + 7);
        bytes.Take(4).ShouldBe(new byte[] { 0x07, 0x00, 0x00, 0x00 });

        // g r ö(c3 b6) ß(c3 9f) e — the payload, not merely its count, so an encoder that got the
        // number right and the bytes wrong is caught here too. Its astral sibling below does the same.
        bytes.Skip(4).ShouldBe(new byte[] { 0x67, 0x72, 0xC3, 0xB6, 0xC3, 0x9F, 0x65 });
    }

    /// <summary>
    /// The other half of the same trap: an astral-plane codepoint is one codepoint, two
    /// <c>char</c>s and four UTF-8 bytes. Only one of those three numbers is correct here.
    /// </summary>
    [Fact]
    public void The_encoding_counts_UTF8_bytes_not_chars_for_an_astral_plane_string()
    {
        var bytes = Encode("\U0001F3B2");

        bytes.Length.ShouldBe(4 + 4);
        bytes.Take(4).ShouldBe(new byte[] { 0x04, 0x00, 0x00, 0x00 });
        bytes.Skip(4).ShouldBe(new byte[] { 0xF0, 0x9F, 0x8E, 0xB2 });
    }

    /// <summary>
    /// Arguments are concatenated in argument order, so the order is part of the input.
    /// <c>runSeed = Hash64(playerId, chapterId, tierId, …)</c> depends on this being true.
    /// </summary>
    [Fact]
    public void The_encoding_is_argument_order_sensitive()
    {
        Hash64.Of(1, "a").ShouldNotBe(Hash64.Of("a", 1));
    }

    /// <summary>
    /// 🔒 Why the length prefix exists. Without it <c>("ab", "c")</c> and <c>("a", "bc")</c>
    /// both concatenate to <c>abc</c> and collide — two different draws answering to one hash.
    /// </summary>
    [Fact]
    public void The_length_prefix_separates_argument_lists_that_would_otherwise_concatenate_alike()
    {
        Hash64.Of("ab", "c").ShouldNotBe(Hash64.Of("a", "bc"));
    }

    /// <summary>
    /// The draw overload — <c>Hash64(seed, streamName, drawIndex)</c>, the shape every single
    /// draw in the game takes — must be byte-identical to the general overload. It exists to
    /// avoid an array allocation per draw, and a fast path that quietly disagrees with the
    /// slow one is the worst possible bug in this file.
    /// </summary>
    [Fact]
    public void The_draw_overload_agrees_with_the_general_overload()
    {
        var viaDrawOverload = Hash64.Of(0x0123456789ABCDEFUL, "dice", 12UL);
        var viaGeneralOverload = Hash64.Of(new Hash64Argument[] { 0x0123456789ABCDEFUL, "dice", 12UL });

        viaDrawOverload.ShouldBe(viaGeneralOverload);
    }

    /// <summary>
    /// A caller who passes a battle index as <c>int</c> binds to the general overload while one
    /// who passes <c>ulong</c> binds to the draw overload. For a non-negative index the two
    /// must land on the same eight bytes, or the same battle would be seeded two ways depending
    /// on an incidental type at the call site.
    /// </summary>
    [Fact]
    public void A_non_negative_int_index_encodes_identically_to_the_equivalent_ulong()
    {
        var index = 12;

        Hash64.Of(7UL, "dice", index).ShouldBe(Hash64.Of(7UL, "dice", 12UL));
    }

    /// <summary>
    /// Concatenating no arguments is the empty buffer, so <c>Hash64.Of()</c> is XXH64 of nothing.
    /// Pinned rather than rejected: the encoding has no special cases, and one fewer special
    /// case is one fewer thing for a second implementation of this spec to disagree about.
    /// </summary>
    [Fact]
    public void The_encoding_of_no_arguments_is_the_empty_buffer()
    {
        Hash64.CanonicalByteCount(Array.Empty<Hash64Argument>()).ShouldBe(0);
        Hash64.Of().ShouldBe(0xEF46DB3751D8E999UL);
    }

    /// <summary>A null string has no canonical encoding; it is a caller bug, not a draw.</summary>
    [Fact]
    public void The_encoding_rejects_a_null_string_argument()
    {
        Action act = () => _ = Hash64.Of((string)null!);

        Should.Throw<ArgumentNullException>(act);
    }

    /// <summary>A null argument array is the same caller bug one level up.</summary>
    [Fact]
    public void The_encoding_rejects_a_null_argument_array()
    {
        Action act = () => _ = Hash64.Of((Hash64Argument[])null!);

        Should.Throw<ArgumentNullException>(act);
    }

    /// <summary>A null stream name on the draw overload is rejected the same way.</summary>
    [Fact]
    public void The_draw_overload_rejects_a_null_stream_name()
    {
        Action act = () => _ = Hash64.Of(0UL, null!, 0UL);

        Should.Throw<ArgumentNullException>(act);
    }

    /// <summary>
    /// The canonical buffer's length is fixed by the argument list alone: 8 bytes per integral
    /// argument, 4 + UTF-8 length per string. Pinning it catches an encoder that reaches the
    /// right hash by a wrong route.
    /// </summary>
    [Fact]
    public void The_canonical_length_of_any_integral_argument_is_eight_bytes()
    {
        var oneOfEach = new Hash64Argument[] { 0UL, 0L, 0, ReferenceEnums.Int32Enum.Zero };

        oneOfEach.ShouldNotBeEmpty();
        foreach (var argument in oneOfEach)
        {
            Hash64.CanonicalByteCount(new[] { argument }).ShouldBe(8, $"argument {argument}");
        }
    }

    /// <summary>The draw shape: 8 + (4 + 4) + 8 for a four-byte stream name.</summary>
    [Fact]
    public void The_canonical_length_of_the_draw_shape_is_the_sum_of_its_parts()
    {
        var length = Hash64.CanonicalByteCount(new Hash64Argument[] { 0UL, "dice", 0UL });

        length.ShouldBe(8 + 4 + 4 + 8);
    }

    /// <summary>
    /// 🔒 The buffer boundary. Both <c>Of</c> overloads build the canonical buffer on the stack up
    /// to 256 bytes and on the <b>heap</b> beyond it, and until this theory existed the heap branch
    /// was dead in the whole suite: the largest canonical buffer any test built was 65 bytes, and
    /// the 222-byte xxHash sanity vectors hand a pre-built array straight to <c>XxHash64</c>,
    /// bypassing buffer construction entirely.
    /// </summary>
    /// <remarks>
    /// A single ASCII string of length <c>n</c> encodes to <c>4 + n</c> bytes, so 252 is the last
    /// length on the stack and 253 the first on the heap. A wrong slice offset or a stale
    /// <c>total</c> in the heap path would hash long arguments differently while all 75 committed
    /// rows still passed. The expectation is an explicitly built buffer rather than a second call
    /// to <c>Of</c>, so the two cannot agree by both being wrong.
    /// </remarks>
    [Theory]
    [InlineData(251)]
    [InlineData(252)]
    [InlineData(253)]
    [InlineData(65536)]
    public void The_encoding_agrees_with_an_explicit_buffer_on_both_sides_of_the_stack_heap_boundary(int length)
    {
        var text = new string('x', length);
        var arguments = new Hash64Argument[] { text };

        Hash64.CanonicalByteCount(arguments).ShouldBe(4 + length);

        var expected = new byte[4 + length];
        Hash64.WriteCanonical(arguments, expected);

        Hash64.Of(text).ShouldBe(Hash64.XxHash64(expected));
    }

    /// <summary>
    /// The same boundary for a mixed argument list, so the heap path is exercised where the string
    /// is not the only argument and an offset can be stale rather than merely wrong.
    /// </summary>
    [Theory]
    [InlineData(235)]
    [InlineData(236)]
    [InlineData(237)]
    [InlineData(65536)]
    public void A_mixed_argument_list_agrees_with_an_explicit_buffer_across_the_boundary(int length)
    {
        var text = new string('y', length);
        var arguments = new Hash64Argument[] { 0x0123456789ABCDEFUL, text, 12UL };

        Hash64.CanonicalByteCount(arguments).ShouldBe(8 + 4 + length + 8);

        var expected = new byte[8 + 4 + length + 8];
        Hash64.WriteCanonical(arguments, expected);

        Hash64.Of(arguments).ShouldBe(Hash64.XxHash64(expected));
    }

    /// <summary>
    /// 🔒 And the <b>draw</b> overload has its own stack/heap branch, reached at a stream name of
    /// 237 bytes: <c>8 + (4 + n) + 8</c> passes 256 there. It is reachable — the overload does
    /// <b>not</b> validate its stream name against <c>RngStreams</c>, so any caller can pass a name
    /// of any length — and a heap path that disagreed with the general overload would fork the
    /// stream for exactly those callers.
    /// </summary>
    [Theory]
    [InlineData(235)]
    [InlineData(236)]
    [InlineData(237)]
    [InlineData(4096)]
    public void The_draw_overload_agrees_with_the_general_overload_across_the_boundary(int nameLength)
    {
        var streamName = new string('s', nameLength);

        var viaDrawOverload = Hash64.Of(0x0123456789ABCDEFUL, streamName, 12UL);
        var viaGeneralOverload = Hash64.Of(new Hash64Argument[] { 0x0123456789ABCDEFUL, streamName, 12UL });

        viaDrawOverload.ShouldBe(viaGeneralOverload);
    }

    private static byte[] Encode(Hash64Argument argument)
    {
        var arguments = new[] { argument };
        var buffer = new byte[Hash64.CanonicalByteCount(arguments)];
        Hash64.WriteCanonical(arguments, buffer);
        return buffer;
    }
}
