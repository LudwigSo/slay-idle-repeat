using System.Globalization;
using System.IO.Compression;
using System.Text;
using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The shippable form of a snapshot: gzip over the canonical bytes the version stamp is taken
/// over, and back again.
/// </summary>
/// <remarks>
/// <para>
/// Round-trip fidelity is asserted as <em>the recomputed stamp matches</em>, never as record
/// equality: the canonical form normalises a decimal's scale, so a document that round-trips
/// perfectly can come back unequal to the object it was packed from and still be the same content.
/// The stamp is the contract; equality is not.
/// </para>
/// <para>
/// Every refusal case names which of four things the reader found. A single "the bundle is corrupt"
/// would leave an operator unable to tell a mid-flight truncation from a client one release behind.
/// </para>
/// </remarks>
public sealed class ContentBundleTests
{
    private const string PathA = "tuning/a.json";
    private const string PathB = "tuning/b.json";

    private static KeyValuePair<string, ContentValue> Member(string name, ContentValue value) => new(name, value);

    private static ContentValue Obj(params KeyValuePair<string, ContentValue>[] members) =>
        ContentValue.Object(members);

    private static ContentDocument Doc(string path, ContentValue root) => new(path, root);

    /// <summary>
    /// Packs, reopens against the documents' own stamp, and returns the reopened snapshot.
    /// </summary>
    /// <remarks>
    /// The second assertion is the one that bites: an <c>Open</c> that stamped the snapshot with
    /// the version it was handed instead of recomputing one would satisfy the first and fail here.
    /// </remarks>
    private static ContentSnapshot Reopen(params ContentDocument[] documents)
    {
        var expected = ContentHashing.Compute(documents);
        var reopened = ContentBundle.Open(ContentBundle.Pack(documents), expected);

        reopened.Version.ShouldBe(expected);
        ContentHashing.Compute(reopened.DocumentPaths.Select(reopened.GetDocument)).ShouldBe(expected);

        return reopened;
    }

    private static byte[] Gzip(byte[] payload)
    {
        using var output = new MemoryStream();
        using (var compressor = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
        {
            compressor.Write(payload);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Asserts a refusal message says what byte it found, in hex or in decimal.
    /// </summary>
    /// <remarks>
    /// Written as a disjunction deliberately: the rule being pinned is that the reader states the
    /// value it could not handle, not which base it writes it in. A message that names no value at
    /// all still fails.
    /// </remarks>
    private static void ShouldNameTheByte(string message, byte value)
    {
        var named = message.Contains($"0x{value:X2}", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains(value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        named.ShouldBeTrue(
            $"a refusal must say what it found, and this one does not name {value}: {message}");
    }

    // =========================================================================== C2 — round trip

    /// <summary>One document exercising every kind the canonical encoder has a tag for.</summary>
    private static ContentDocument EveryKind() => Doc(PathA, Obj(
        Member("anArray", ContentValue.Array(
        [
            ContentValue.Number(1m),
            ContentValue.Array([ContentValue.Text("nested item")]),
            ContentValue.EmptyArray,
        ])),
        Member("aFalse", ContentValue.False),
        Member("aTrue", ContentValue.True),
        Member("anEmptyObject", ContentValue.EmptyObject),
        Member("aNestedObject", Obj(Member("inner", ContentValue.Text("still here")))),
        Member("aNumber", ContentValue.Number(-1.25m)),
        Member("aText", ContentValue.Text("plain")),
        Member("anUnauthorisedHole", ContentValue.Unauthorised)));

    private static IEnumerable<ContentValueKind> KindsIn(ContentValue value)
    {
        yield return value.Kind;

        foreach (var kind in value.Items.SelectMany(KindsIn))
        {
            yield return kind;
        }

        foreach (var name in value.MemberNames)
        {
            value.TryGetMember(name, out var member);
            foreach (var kind in KindsIn(member!))
            {
                yield return kind;
            }
        }
    }

    /// <summary>
    /// The floor under the round-trip fixture: a kind added to the enum without a case here would
    /// otherwise leave the round trip green while nothing had ever encoded it.
    /// </summary>
    [Fact]
    public void The_round_trip_fixture_exercises_every_declared_content_value_kind()
    {
        var covered = KindsIn(EveryKind().Root).ToHashSet();

        covered.Count.ShouldBeGreaterThanOrEqualTo(6);
        covered.ShouldBe(Enum.GetValues<ContentValueKind>().ToHashSet(), ignoreOrder: true);
    }

    [Fact]
    public void A_bundle_round_trips_every_value_kind_the_canonical_encoder_has_a_tag_for()
    {
        var reopened = Reopen(EveryKind());

        var root = reopened.GetDocument(PathA).Root;
        root.Kind.ShouldBe(ContentValueKind.Object);
        reopened.Read($"{PathA}#/anUnauthorisedHole").IsUnauthorised.ShouldBeTrue();
        reopened.ReadText($"{PathA}#/aText").ShouldBe("plain");
        reopened.ReadNumber($"{PathA}#/aNumber").ShouldBe(-1.25m);
        reopened.ReadText($"{PathA}#/aNestedObject/inner").ShouldBe("still here");
        reopened.ReadText($"{PathA}#/anArray/1/0").ShouldBe("nested item");
    }

    /// <summary>
    /// Both booleans, because they are two different tag bytes: a reader that answered every
    /// boolean tag with <c>true</c> would round-trip half the corpus perfectly.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_bundle_round_trips_a_boolean_as_the_boolean_it_was(bool value)
    {
        var reopened = Reopen(Doc(PathA, Obj(Member("flag", ContentValue.Boolean(value)))));

        reopened.ReadBoolean($"{PathA}#/flag").ShouldBe(value);
    }

    /// <summary>
    /// An empty array and an empty object survive as themselves. Both encode as a tag and a zero
    /// count, so a reader that treated a zero count as "absent" would drop the member entirely and
    /// still produce a plausible-looking snapshot.
    /// </summary>
    [Fact]
    public void A_bundle_round_trips_an_empty_array_and_an_empty_object_as_present_and_empty()
    {
        var reopened = Reopen(Doc(PathA, Obj(
            Member("emptyArray", ContentValue.EmptyArray),
            Member("emptyObject", ContentValue.EmptyObject))));

        var array = reopened.Read($"{PathA}#/emptyArray");
        array.Kind.ShouldBe(ContentValueKind.Array);
        array.Items.ShouldBeEmpty();

        var @object = reopened.Read($"{PathA}#/emptyObject");
        @object.Kind.ShouldBe(ContentValueKind.Object);
        @object.MemberNames.ShouldBeEmpty();
    }

    /// <summary>
    /// Text is length-prefixed UTF-8, so neither a non-ASCII character nor an embedded NUL is a
    /// terminator. A reader that read strings up to a NUL would truncate the second of these
    /// silently, and the recomputed stamp is what catches it.
    /// </summary>
    [Theory]
    [InlineData("Grünwald — ünïcödé ✦ 世界")]
    [InlineData("before\0after")]
    [InlineData("")]
    public void A_bundle_round_trips_text_that_no_terminator_could_survive(string text)
    {
        var reopened = Reopen(Doc(PathA, Obj(Member("label", ContentValue.Text(text)))));

        reopened.ReadText($"{PathA}#/label").ShouldBe(text);
    }

    /// <summary>
    /// The decimals that exercise the canonical normaliser: a trailing-zero scale, a negative zero,
    /// and the largest scale a decimal carries. Fidelity is the recomputed stamp, not the object:
    /// 1.500 comes back as 1.5 and that is the same content.
    /// </summary>
    [Theory]
    [InlineData("1.500")]
    [InlineData("-0")]
    [InlineData("0.0000000000000000000000000001")]
    [InlineData("-1.25")]
    [InlineData("79228162514264337593543950335")]
    public void A_bundle_round_trips_a_decimal_through_the_normalised_invariant_form(string literal)
    {
        var value = decimal.Parse(literal, NumberStyles.Float, CultureInfo.InvariantCulture);
        var reopened = Reopen(Doc(PathA, Obj(Member("rate", ContentValue.Number(value)))));

        reopened.ReadNumber($"{PathA}#/rate").ShouldBe(value);
    }

    [Fact]
    public void A_bundle_round_trips_a_multi_document_set_and_hands_the_documents_back_in_ordinal_path_order()
    {
        var b = Doc(PathB, Obj(Member("x", ContentValue.Number(2m))));
        var a = Doc(PathA, Obj(Member("x", ContentValue.Number(1m))));

        var documents = ContentBundle.Unpack(ContentBundle.Pack([b, a]));

        documents.Select(d => d.Path).ShouldBe([PathA, PathB]);
        ContentHashing.Compute(documents).ShouldBe(ContentHashing.Compute([a, b]));
    }

    /// <summary>
    /// The identity of a bundle is what it decodes to, never the name it arrived under: a bundle
    /// packed from one document set opens as that set's stamp and no other.
    /// </summary>
    [Fact]
    public void Open_stamps_the_snapshot_with_the_version_it_recomputed_from_the_bundle()
    {
        var documents = new[] { Doc(PathA, Obj(Member("x", ContentValue.Number(1m)))) };
        var expected = ContentHashing.Compute(documents);

        ContentBundle.Open(ContentBundle.Pack(documents), expected).Version.Value.ShouldBe(expected.Value);
    }

    // ================================================================ C3 — a bad bundle is refused

    /// <summary>
    /// A gzip stream whose payload is a perfectly valid canonical encoding apart from its first
    /// byte. Nothing else about it is wrong, so a refusal that named anything but the format
    /// version would be naming the wrong defect.
    /// </summary>
    [Fact]
    public void A_bundle_leading_with_an_unknown_canonical_format_version_is_refused_by_that_name()
    {
        const byte unknown = 0x2A;

        var payload = ContentHashing.CanonicalBytes([Doc(PathA, Obj(Member("x", ContentValue.Number(1m))))]);
        payload[0].ShouldBe(ContentHashing.CanonicalFormatVersion,
            "the mutation below is only a single edit while the format byte still leads the payload");
        payload[0] = unknown;

        var bundle = Gzip(payload);
        Action act = () => _ = ContentBundle.Unpack(bundle);

        var found = Should.Throw<ContentBundleFormatException>(act);
        found.Message.ShouldMatchWildcard("*format version*");
        ShouldNameTheByte(found.Message, unknown);
        found.InnerException.ShouldBeNull("nothing failed to decompress here; the payload was read");
    }

    /// <summary>
    /// The canonical bytes themselves, handed over without the gzip envelope. They decode cleanly
    /// once wrapped — the round-trip cases above prove it — so the envelope is the only defect.
    /// </summary>
    [Fact]
    public void A_payload_that_was_never_gzipped_is_refused_as_not_gzip_rather_than_as_bad_content()
    {
        var payload = ContentHashing.CanonicalBytes([Doc(PathA, Obj(Member("x", ContentValue.Number(1m))))]);
        payload[0].ShouldNotBe((byte)0x1F, "0x1F 0x8B is the gzip magic; these bytes carry none");

        Action act = () => _ = ContentBundle.Unpack(payload);

        var found = Should.Throw<ContentBundleFormatException>(act);
        found.Message.ShouldMatchWildcard("*gzip*");
        found.InnerException.ShouldNotBeNull("the decompression fault is carried, not swallowed");
    }

    /// <summary>
    /// A valid gzip stream over a canonical payload cut one byte short, inside the last string.
    /// Everything the reader passes before that point is well-formed, so "truncated" is the only
    /// honest thing to say about it.
    /// </summary>
    [Fact]
    public void A_bundle_whose_canonical_payload_ends_early_is_refused_as_truncated()
    {
        var payload = ContentHashing.CanonicalBytes([Doc(PathA, Obj(Member("label", ContentValue.Text("z"))))]);
        payload.Length.ShouldBeGreaterThan(9, "the cut has to land past the format byte and the count");

        var bundle = Gzip(payload[..^1]);
        Action act = () => _ = ContentBundle.Unpack(bundle);

        var found = Should.Throw<ContentBundleFormatException>(act);
        found.Message.ShouldMatchWildcard("*truncated*");
        found.Message.ShouldNotContain("format version", Case.Insensitive);
    }

    /// <summary>
    /// One tag byte replaced, in a payload that is otherwise exactly the bytes the round-trip cases
    /// read successfully. The value tag is the reader's only instruction about what comes next, so
    /// an unknown one has to stop it rather than be guessed at as an object.
    /// </summary>
    [Fact]
    public void A_bundle_carrying_an_unknown_value_tag_is_refused_by_naming_the_tag()
    {
        const byte unknown = 0x63;
        const byte textTag = 0x04;

        var payload = ContentHashing.CanonicalBytes([Doc(PathA, ContentValue.Text("z"))]);
        var tagOffset = 1 + 4 + 4 + Encoding.UTF8.GetByteCount(PathA);
        payload[tagOffset].ShouldBe(textTag,
            "the root value's tag sits after the format byte, the document count and the path; if " +
            "the canonical layout moved, this mutation is no longer the single edit it reads as");
        payload[tagOffset] = unknown;

        var bundle = Gzip(payload);
        Action act = () => _ = ContentBundle.Unpack(bundle);

        var found = Should.Throw<ContentBundleFormatException>(act);
        found.Message.ShouldMatchWildcard("*tag*");
        ShouldNameTheByte(found.Message, unknown);
        found.Message.ShouldNotContain("format version", Case.Insensitive);
    }

    /// <summary>
    /// A readable bundle of the wrong content. The refusal has to name both stamps in full — the
    /// one asked for and the one recomputed — because "the hash did not match" leaves a caller
    /// unable to tell a stale client from a swapped file.
    /// </summary>
    [Fact]
    public void Open_refuses_a_readable_bundle_whose_recomputed_stamp_is_not_the_one_asked_for()
    {
        var packed = new[] { Doc(PathA, Obj(Member("x", ContentValue.Number(1m)))) };
        var other = new[] { Doc(PathA, Obj(Member("x", ContentValue.Number(2m)))) };

        var actual = ContentHashing.Compute(packed);
        var asked = ContentHashing.Compute(other);
        asked.Value.ShouldNotBe(actual.Value, "the two document sets have to differ for this case to bite");

        var bundle = ContentBundle.Pack(packed);
        Action act = () => _ = ContentBundle.Open(bundle, asked);

        var found = Should.Throw<ContentBundleFormatException>(act);
        found.Message.ShouldContain(asked.Value, Case.Sensitive, "the stamp that was asked for");
        found.Message.ShouldContain(actual.Value, Case.Sensitive, "the stamp the bundle actually carries");
    }

    /// <summary>
    /// The same bundle, opened against the stamp it really carries, is accepted — the control that
    /// keeps the case above from being satisfied by an <c>Open</c> that refuses everything.
    /// </summary>
    [Fact]
    public void Open_accepts_the_very_bundle_the_case_above_refuses_when_the_stamp_is_the_right_one()
    {
        var packed = new[] { Doc(PathA, Obj(Member("x", ContentValue.Number(1m)))) };
        var actual = ContentHashing.Compute(packed);

        ContentBundle.Open(ContentBundle.Pack(packed), actual).Version.ShouldBe(actual);
    }
}
