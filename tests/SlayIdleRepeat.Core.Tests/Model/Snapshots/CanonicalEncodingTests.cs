using Shouldly;
using SlayIdleRepeat.TestSupport;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model.Snapshots;

/// <summary>
/// 🔒 The scalar rules of the `14` §16.6 table, asserted as <b>bytes</b> rather than as hashes.
/// </summary>
/// <remarks>
/// A hash test tells you something moved; a byte test tells you what. The shapes easiest to get
/// subtly right-looking and wrong — a negative integer zero-extended, a string counted in characters,
/// a timestamp in seconds — are pinned explicitly.
/// </remarks>
public sealed class CanonicalEncodingTests
{
    /// <summary>🔒 An integer is widened to 8 bytes little-endian.</summary>
    [Fact]
    public void CanonicalBytes_widens_an_int_to_eight_little_endian_bytes()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<int>(1));

        Hex(bytes).ShouldBe("0100000000000000");
    }

    /// <summary>
    /// 🔒 A <b>negative</b> int is <b>sign</b>-extended, never zero-extended. The single easiest
    /// rule in §16.6 to implement plausibly and wrongly: a zero-extending encoder produces stable,
    /// self-consistent hashes that disagree with every other implementation.
    /// </summary>
    [Fact]
    public void CanonicalBytes_sign_extends_a_negative_int()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<int>(-1));

        Hex(bytes).ShouldBe("ffffffffffffffff");
    }

    /// <summary>A negative int that is not all-ones, so the row above cannot pass by accident.</summary>
    [Fact]
    public void CanonicalBytes_sign_extends_an_arbitrary_negative_int()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<int>(-2));

        Hex(bytes).ShouldBe("feffffffffffffff");
    }

    /// <summary>`14` §16.6 — an unsigned integer is zero-extended, the mirror of the rule above.</summary>
    [Fact]
    public void CanonicalBytes_zero_extends_an_unsigned_int()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<uint>(uint.MaxValue));

        Hex(bytes).ShouldBe("ffffffff00000000");
    }

    /// <summary>`14` §16.6 — every integral width widens to the same 8 bytes for the same value.</summary>
    [Theory]
    [InlineData((sbyte)-1)]
    [InlineData((short)-1)]
    [InlineData(-1)]
    [InlineData(-1L)]
    public void CanonicalBytes_widens_every_signed_integral_width_identically(object value)
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(OneValueOf(value));

        Hex(bytes).ShouldBe("ffffffffffffffff");
    }

    /// <summary>`14` §16.6 — a <see cref="ulong"/> survives its full range, uninterpreted.</summary>
    [Fact]
    public void CanonicalBytes_writes_the_full_ulong_range()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<ulong>(ulong.MaxValue));

        Hex(bytes).ShouldBe("ffffffffffffffff");
    }

    /// <summary>🔒 A boolean is <b>one</b> byte — not a widened integer.</summary>
    [Theory]
    [InlineData(true, "01")]
    [InlineData(false, "00")]
    public void CanonicalBytes_writes_a_boolean_as_a_single_byte(bool value, string expected)
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<bool>(value));

        Hex(bytes).ShouldBe(expected);
    }

    /// <summary>🔒 An enum is its numeric value, 8 bytes — never its name and never its ordinal.</summary>
    [Fact]
    public void CanonicalBytes_writes_an_enum_as_its_numeric_value()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<Element>(Element.Frost));

        Hex(bytes).ShouldBe("0700000000000000");
    }

    /// <summary>An enum widens exactly as its underlying integral type does — signed, negative.</summary>
    [Fact]
    public void CanonicalBytes_sign_extends_a_negative_enum_member()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<Rarity>(Rarity.Cursed));

        Hex(bytes).ShouldBe("fdffffffffffffff");
    }

    /// <summary>A <see cref="ulong"/>-backed enum keeps values no <see cref="long"/> could hold.</summary>
    [Fact]
    public void CanonicalBytes_writes_a_ulong_backed_enum_across_its_full_range()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<BigFlag>(BigFlag.All));

        Hex(bytes).ShouldBe("ffffffffffffffff");
    }

    /// <summary>
    /// An enum and the plain integer of the same value encode identically. §16.6 pins the value,
    /// not the declared type — so the table's rows for the two must agree, and here they do.
    /// </summary>
    [Fact]
    public void CanonicalBytes_writes_an_enum_and_its_numeric_value_identically()
    {
        var asEnum = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<Element>(Element.Frost));
        var asInt = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<int>(7));

        Hex(asEnum).ShouldBe(Hex(asInt));
    }

    /// <summary>
    /// 🔒 A string is a 4-byte little-endian <b>byte</b> count then the UTF-8 bytes. The count is
    /// bytes, not characters: this input is 9 characters and 15 bytes, so a <c>char</c>-counting
    /// encoder writes <c>09</c> here and is wrong in a way no ASCII test can see.
    /// </summary>
    [Fact]
    public void CanonicalBytes_prefixes_a_string_with_its_UTF8_byte_count_not_its_char_count()
    {
        ReferenceSnapshots.MultiByteText.Length.ShouldBe(9);

        var bytes = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<string>(ReferenceSnapshots.MultiByteText));

        // 01 = present, 0f000000 = 15 bytes little-endian, then the UTF-8 bytes themselves.
        Hex(bytes).ShouldBe("010f000000" + "4772c3bcc39f652c20e4b896e7958c");
    }

    /// <summary>A plain ASCII string, so the prefix is readable without decoding UTF-8.</summary>
    [Fact]
    public void CanonicalBytes_writes_an_ascii_string_as_its_length_then_its_bytes()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<string>("abc"));

        Hex(bytes).ShouldBe("01" + "03000000" + "616263");
    }

    /// <summary>An empty string is a <b>present</b> slot with a zero byte count.</summary>
    [Fact]
    public void CanonicalBytes_writes_an_empty_string_as_a_present_zero_length_slot()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<string>(""));

        Hex(bytes).ShouldBe("01" + "00000000");
    }

    /// <summary>🔒 An empty string and an absent string are different states and different bytes.</summary>
    [Fact]
    public void CanonicalBytes_keeps_an_empty_string_and_an_absent_string_apart()
    {
        var empty = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<string?>(""));
        var absent = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<string?>(null));

        Hex(empty).ShouldNotBe(Hex(absent));
    }

    /// <summary>
    /// A string carrying a byte-order mark keeps it and nothing else: <c>U+FEFF</c> is a character
    /// of the value, so it encodes as its three UTF-8 bytes and counts towards the byte prefix —
    /// while the writer adds no BOM of its own. An encoder built on a default
    /// <c>UTF8Encoding</c> emits a leading <c>efbbbf</c> here and reports 7 bytes, not 4.
    /// </summary>
    [Fact]
    public void CanonicalBytes_keeps_a_byte_order_mark_in_the_value_and_adds_none_of_its_own()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<string>("\uFEFFa"));

        Hex(bytes).ShouldBe("01" + "04000000" + "efbbbf" + "61");
    }

    /// <summary>
    /// 🔒 A timestamp is Unix <b>milliseconds</b> UTC, 8 bytes little-endian. 2024-06-01T12:04:56.789Z
    /// is 1717243496789 ms, whose little-endian pattern is the literal below.
    /// </summary>
    [Fact]
    public void CanonicalBytes_writes_a_timestamp_as_unix_milliseconds()
    {
        var stamp = DateTimeOffset.FromUnixTimeMilliseconds(ReferenceSnapshots.StampMilliseconds);

        var bytes = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<DateTimeOffset>(stamp));

        Hex(bytes).ShouldBe("5549b0d38f010000");
    }

    /// <summary>The epoch itself is eight zero bytes — the row a seconds-based encoder also passes.</summary>
    [Fact]
    public void CanonicalBytes_writes_the_unix_epoch_as_eight_zero_bytes()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(
            new OneValueSnapshot<DateTimeOffset>(DateTimeOffset.FromUnixTimeMilliseconds(0)));

        Hex(bytes).ShouldBe("0000000000000000");
    }

    /// <summary>
    /// 🔒 Milliseconds, not seconds. One second past the epoch is 1000, and an encoder that wrote
    /// seconds would produce <c>01</c> here — a difference no epoch-only test can see.
    /// </summary>
    [Fact]
    public void CanonicalBytes_writes_milliseconds_rather_than_seconds()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(
            new OneValueSnapshot<DateTimeOffset>(DateTimeOffset.FromUnixTimeMilliseconds(1000)));

        Hex(bytes).ShouldBe("e803000000000000");
    }

    /// <summary>A pre-epoch instant is a negative millisecond count, sign-extended like any integer.</summary>
    [Fact]
    public void CanonicalBytes_sign_extends_a_pre_epoch_timestamp()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(
            new OneValueSnapshot<DateTimeOffset>(DateTimeOffset.FromUnixTimeMilliseconds(-1000)));

        Hex(bytes).ShouldBe("18fcffffffffffff");
    }

    /// <summary>
    /// 🔒 The same instant expressed in a different offset is the same bytes — "UTC" in §16.6 means
    /// the instant, not the local wall clock the value happens to be wearing.
    /// </summary>
    [Fact]
    public void CanonicalBytes_normalises_a_timestamp_offset_to_the_same_instant()
    {
        var utc = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var shifted = new DateTimeOffset(2024, 6, 1, 14, 0, 0, TimeSpan.FromHours(2));

        var utcBytes = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<DateTimeOffset>(utc));
        var shiftedBytes = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<DateTimeOffset>(shifted));

        Hex(shiftedBytes).ShouldBe(Hex(utcBytes));
    }

    /// <summary>
    /// A <see cref="DateTimeKind.Utc"/> <see cref="DateTime"/> is the other admissible shape, and
    /// encodes to the same milliseconds. 2024-06-01T12:00:00Z is 1717243200000 ms.
    /// </summary>
    [Fact]
    public void CanonicalBytes_writes_a_utc_DateTime_as_unix_milliseconds()
    {
        var value = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        var bytes = CanonicalStateWriter.CanonicalBytes(new OneUtcDateTimeSnapshot(value));

        Hex(bytes).ShouldBe("00c2abd38f010000");
    }

    /// <summary>
    /// 🔒 A <see cref="DateTime"/> that is not UTC is refused. A <c>Local</c> or
    /// <c>Unspecified</c> value names a different instant on a server in Frankfurt than on a
    /// handset in Auckland, so it has no canonical encoding at all.
    /// </summary>
    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void CanonicalBytes_refuses_a_DateTime_that_is_not_UTC(DateTimeKind kind)
    {
        var value = new DateTime(2024, 6, 1, 12, 0, 0, kind);

        var act = () => CanonicalStateWriter.CanonicalBytes(new UnsupportedSnapshots.WithLocalDateTime(value));

        Should.Throw<NotSupportedException>(act).Message.ShouldMatchWildcard("*Utc*");
    }

    /// <summary>🔒 A present optional is the presence byte <c>0x01</c>, then the value.</summary>
    [Fact]
    public void CanonicalBytes_writes_a_present_optional_as_one_then_the_value()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<int?>(1));

        Hex(bytes).ShouldBe("01" + "0100000000000000");
    }

    /// <summary>🔒 An absent optional is the presence byte <c>0x00</c> and nothing else.</summary>
    [Fact]
    public void CanonicalBytes_writes_an_absent_optional_as_a_single_zero_byte()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<int?>(null));

        Hex(bytes).ShouldBe("00");
    }

    /// <summary>
    /// 🔒 A present <b>zero</b> is not an absence. The bug this catches — writing nothing for a
    /// default value — leaves a state where "no gold recorded" and "zero gold" hash alike.
    /// </summary>
    [Fact]
    public void CanonicalBytes_keeps_a_present_zero_and_an_absent_value_apart()
    {
        var present = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<int?>(0));
        var absent = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<int?>(null));

        Hex(present).ShouldBe("01" + "0000000000000000");
        Hex(absent).ShouldBe("00");
    }

    /// <summary>An absent nested record is one zero byte — the descent stops, it does not zero-fill.</summary>
    [Fact]
    public void CanonicalBytes_writes_an_absent_nested_record_as_a_single_zero_byte()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new OptionalsSnapshot(null, null, null));

        Hex(bytes).ShouldBe("000000");
    }

    /// <summary>
    /// 🔒 A double is the IEEE-754 bit pattern of the stored value, 8 bytes little-endian. The
    /// expectation is the literal pattern, not <c>BitConverter</c> re-run over the same input —
    /// restating the encoding with the primitive the writer itself uses proves nothing.
    /// </summary>
    [Fact]
    public void CanonicalBytes_writes_a_double_as_its_IEEE754_bit_pattern()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<double>(1234.5678));

        Hex(bytes).ShouldBe("adfa5c6d454a9340");
    }

    /// <summary>
    /// 🔒 The writer <b>never rounds</b>, and never re-derives a double from its decimal text.
    /// <c>0.1</c> is already at 4 dp yet has no exact binary representation, so its stored pattern
    /// ends <c>…999a</c>; a writer that round-tripped through a decimal form, or rounded again,
    /// would emit a neighbouring pattern and hide the very drift the determinism CI exists to catch.
    /// </summary>
    [Fact]
    public void CanonicalBytes_writes_the_exact_stored_pattern_of_a_double_binary_cannot_represent()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<double>(0.1));

        Hex(bytes).ShouldBe("9a9999999999b93f");
    }

    /// <summary>
    /// 🔒 §16.6 — <c>-0.0</c> is <b>refused</b>. It is the one value where record equality and
    /// <c>stateHash</c> disagree (<c>-0.0 == 0.0</c> is <c>true</c> in C#), so two snapshots the
    /// language calls identical would carry different hashes. Encoding it is a false divergence in
    /// the parity checks; normalising it is the writer silently editing state on its way out.
    /// </summary>
    /// <remarks>
    /// Reachable from `14` §8.2's own rounding rule, not only a hand-written literal:
    /// <c>Math.Round(-0.00004, 4)</c> yields <c>-0.0</c> and .NET preserves the sign. Built through
    /// <see cref="BitConverter"/> because the compiler folds a <c>-0.0</c> literal in some positions.
    /// </remarks>
    [Fact]
    public void CanonicalBytes_refuses_negative_zero()
    {
        var negativeZero = BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000000UL));
        negativeZero.ShouldBe(0.0);
        double.IsNegative(negativeZero).ShouldBeTrue();

        var act = () => CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<double>(negativeZero));

        var thrown = Should.Throw<NotSupportedException>(act);

        thrown.Message.ShouldMatchWildcard("*-0.0*");
        thrown.Message.ShouldMatchWildcard("*record equality*");
    }

    /// <summary>
    /// The way <c>-0.0</c> actually arrives: `14` §8.2 rounds at every accumulation point, and
    /// <c>Math.Round(-0.00004, 4)</c> is a negative zero. A stat that drifts a hair below zero on
    /// one host and not the other must fail loudly here, not diverge quietly downstream.
    /// </summary>
    [Fact]
    public void CanonicalBytes_refuses_the_negative_zero_that_the_rounding_rule_itself_produces()
    {
        var rounded = Math.Round(-0.00004, 4);
        double.IsNegative(rounded).ShouldBeTrue();

        var act = () => CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<double>(rounded));

        Should.Throw<NotSupportedException>(act).Message.ShouldMatchWildcard("*-0.0*");
    }

    /// <summary>
    /// 🔒 And <c>+0.0</c> is unaffected — eight zero bytes, as before. The refusal above is about
    /// the sign bit alone, not about zero.
    /// </summary>
    [Fact]
    public void CanonicalBytes_writes_positive_zero_as_eight_zero_bytes()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<double>(0.0));

        Hex(bytes).ShouldBe("0000000000000000");
    }

    /// <summary>
    /// 🔒 §16.6 — a double that is not already rounded to 4 dp is a bug at its accumulation point,
    /// and the writer says so in debug builds rather than encoding it. Asserted through a
    /// conditional throwing guard rather than <c>Debug.Assert</c>, which would kill the test host.
    /// </summary>
    [Fact]
    public void CanonicalBytes_refuses_a_double_that_is_not_rounded_to_four_decimal_places()
    {
        var act = () => CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<double>(0.123456789));

        Should.Throw<NotSupportedException>(act).Message.ShouldMatchWildcard("*4*");
    }

    /// <summary>A value exactly representable at 4 dp passes the guard untouched.</summary>
    /// <remarks>
    /// <c>-0.0</c> is deliberately absent — it is refused outright by
    /// <see cref="CanonicalBytes_refuses_negative_zero"/>, not merely folded by the compiler.
    /// </remarks>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.0001)]
    [InlineData(1.0)]
    [InlineData(-12.3456)]
    [InlineData(1e15)]
    public void CanonicalBytes_accepts_a_double_already_rounded_to_four_decimal_places(double value)
    {
        var act = () => CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<double>(value));

        Should.NotThrow(act);
    }

    /// <summary>🔒 §16.6 — NaN is forbidden in state. CI fails on it; so does the writer.</summary>
    [Fact]
    public void CanonicalBytes_refuses_NaN()
    {
        var act = () => CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<double>(double.NaN));

        Should.Throw<NotSupportedException>(act).Message.ShouldMatchWildcard("*NaN*");
    }

    /// <summary>🔒 §16.6 — the infinities are forbidden in state, both of them.</summary>
    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void CanonicalBytes_refuses_an_infinity(double value)
    {
        var act = () => CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<double>(value));

        Should.Throw<NotSupportedException>(act).Message.ShouldMatchWildcard("*infinit*");
    }

    /// <summary>
    /// NaN is refused however it is reached, including as a stored bit pattern that is not the
    /// canonical quiet NaN — the check is on the value's class, not on one particular payload.
    /// </summary>
    [Fact]
    public void CanonicalBytes_refuses_a_non_canonical_NaN_payload()
    {
        var signalling = BitConverter.Int64BitsToDouble(unchecked((long)0x7ff0000000000001UL));

        var act = () => CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<double>(signalling));

        Should.Throw<NotSupportedException>(act).Message.ShouldMatchWildcard("*NaN*");
    }

    /// <summary>
    /// 🔒 Fields are written in <b>declaration order</b>, depth-first: the root's fields in order,
    /// descending into each nested record where it is declared rather than after the root's own.
    /// </summary>
    [Fact]
    public void CanonicalBytes_writes_nested_records_depth_first_in_declaration_order()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(ReferenceSnapshots.NestedDepthThree);

        Hex(bytes).ShouldBe(
            "01" + "05000000" + "6f75746572" +          // Head    : "outer"
            "01" +                                       // Middle  : present
                "01" + "06000000" + "6d6964646c65" +     //   Label : "middle"
                "01" +                                   //   Leaf  : present
                    "0300000000000000" +                 //     Depth: 3
                    "01" + "04000000" + "6c656166" +     //     Label: "leaf"
            "0900000000000000");                         // Tail    : 9
    }

    /// <summary>
    /// 🔒 Moving a field between two records changes the byte stream even when the flattened field
    /// list would look the same. Depth-first means the shape is part of the encoding.
    /// </summary>
    [Fact]
    public void CanonicalBytes_distinguishes_two_records_whose_flattened_fields_agree()
    {
        var nested = CanonicalStateWriter.CanonicalBytes(new OuterSnapshot(
            "a", new MiddleSnapshot("b", new InnerSnapshot(1, "c")), 2));
        var flattened = CanonicalStateWriter.CanonicalBytes(new FlattenedSnapshot("a", "b", 1, "c", 2));

        Hex(nested).ShouldNotBe(Hex(flattened));
    }

    /// <summary>The root snapshot is required: there is no presence byte and no "absent state".</summary>
    [Fact]
    public void CanonicalBytes_refuses_a_null_root()
    {
        var act = () => CanonicalStateWriter.CanonicalBytes(null!);

        Should.Throw<ArgumentNullException>(act);
    }

    /// <summary>
    /// The root carries no presence byte of its own — a required argument has nothing to signal.
    /// Pinned so nobody "fixes" the asymmetry between the root and a nested record.
    /// </summary>
    [Fact]
    public void CanonicalBytes_writes_no_presence_byte_for_the_root()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<int>(1));

        bytes.Length.ShouldBe(8);
    }

    private static object OneValueOf(object value) => value switch
    {
        sbyte v => new OneValueSnapshot<sbyte>(v),
        short v => new OneValueSnapshot<short>(v),
        int v => new OneValueSnapshot<int>(v),
        long v => new OneValueSnapshot<long>(v),
        _ => throw new InvalidOperationException($"No fixture for {value.GetType()}."),
    };

    /// <summary>
    /// 🔒 <c>EnergyBanks</c> (M1-10) has the positional shape `14` §16.6 requires, so
    /// <c>PlayerSnapshot</c> can carry it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the case the type's <b>shape</b> exists for, and it is asserted rather than assumed.
    /// <c>EnergyBanks</c> was first written as an <c>internal</c> record struct with <c>internal</c>
    /// properties, which <see cref="CanonicalStateWriter.CanonicalProperties"/> refuses — a
    /// <c>BindingFlags.Public</c> lookup finds neither property, the type falls off the closed
    /// allowlist, and the first person to put one in a snapshot discovers it has no canonical
    /// encoding. <c>PlayerId</c>'s remarks record the same trap; this pins the answer for the type
    /// M1-04 will actually persist.
    /// </para>
    /// <para>
    /// Two banks, two 8-byte little-endian fields, in <b>constructor</b> order — <c>Energy</c> then
    /// <c>Reserve</c>. Swapping the two parameters moves these bytes, which is exactly the
    /// <c>SchemaVersion</c> change `14` §16.6 wants to be impossible to make silently.
    /// </para>
    /// </remarks>
    [Fact]
    public void CanonicalBytes_encodes_EnergyBanks_as_two_widened_fields()
    {
        CanonicalStateWriter.IsCanonicalRecord(typeof(EnergyBanks)).ShouldBeTrue(
            "EnergyBanks is not a positional record by 14 §16.6's test — exactly one public " +
            "constructor, every parameter matched by a public readable property of the same name " +
            "and type, and no public property beyond them. PlayerSnapshot cannot carry it, and " +
            "30 §11.5's 'Energy never exceeds max + reserve' has nothing to persist.");

        CanonicalStateWriter.CanonicalFieldOrder(typeof(OneValueSnapshot<EnergyBanks>)).ShouldBe(
            new[] { "Value.Energy:System.Int32", "Value.Reserve:System.Int32" });

        Hex(CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<EnergyBanks>(new EnergyBanks(138, 200))))
            .ShouldBe("8a00000000000000" + "c800000000000000");
    }

    /// <summary>
    /// 🔒 The converse, and the reason a byte test beats a "does it throw" test: neither bank is
    /// silently omitted. A field that contributed zero bytes would let two states record equality
    /// calls different share a <c>stateHash</c> — the one failure a state hash may never have.
    /// </summary>
    [Fact]
    public void CanonicalBytes_distinguishes_EnergyBanks_that_differ_in_either_bank()
    {
        var baseline = Hex(Bytes(new EnergyBanks(138, 200)));

        Hex(Bytes(new EnergyBanks(139, 200))).ShouldNotBe(
            baseline, "the main bar contributed no bytes.");
        Hex(Bytes(new EnergyBanks(138, 201))).ShouldNotBe(
            baseline, "the Energy Reserve contributed no bytes.");

        // 🔒 And the two banks are not interchangeable: (138, 200) and (200, 138) hold different
        // amounts of spendable Energy in different places and must not collide.
        Hex(Bytes(new EnergyBanks(200, 138))).ShouldNotBe(baseline);

        static byte[] Bytes(EnergyBanks banks) =>
            CanonicalStateWriter.CanonicalBytes(new OneValueSnapshot<EnergyBanks>(banks));
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
