using System.Text;
using FluentAssertions;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The parse step. Strict RFC 8259, duplicate-key detection, and — the important one —
/// <c>null</c> loading as <see cref="ContentValueKind.Unauthorised"/> rather than as anything a
/// rule could accidentally compute with.
/// </summary>
public sealed class JsonContentReaderTests
{
    private static bool Read(string json, out ContentValue? root, out IReadOnlyList<ContentIssue> issues) =>
        JsonContentReader.TryRead("tuning/widgets.json", Encoding.UTF8.GetBytes(json), out root, out issues);

    [Fact]
    public void TryRead_loads_a_json_null_as_an_unauthorised_value()
    {
        Read("""{ "perLevelSuccessRate": null }""", out var root, out _);

        root!.TryGetMember("perLevelSuccessRate", out var member).Should().BeTrue();
        member!.Kind.Should().Be(ContentValueKind.Unauthorised);
    }

    [Fact]
    public void TryRead_never_turns_a_json_null_into_a_zero()
    {
        Read("""{ "perLevelSuccessRate": null }""", out var root, out _);

        root!.TryGetMember("perLevelSuccessRate", out var member);
        member!.Should().NotBe(ContentValue.Number(0m));
    }

    [Fact]
    public void TryRead_keeps_a_decimal_at_the_scale_the_data_file_wrote()
    {
        // decimal.Equals is scale-insensitive (1.075m == 1.0750m), so asserting on the value would
        // pass even if the reader normalised away the very thing this test is named for.
        Read("""{ "legendXpExponent": 1.0750 }""", out var root, out _);

        root!.TryGetMember("legendXpExponent", out var member);
        member!.AsNumber().ToString(System.Globalization.CultureInfo.InvariantCulture).Should().Be("1.0750");
    }

    [Fact]
    public void TryRead_keeps_a_value_a_double_round_trip_would_corrupt()
    {
        Read("""{ "quality": 0.1234567890123456789 }""", out var root, out _);

        root!.TryGetMember("quality", out var member);
        member!.AsNumber().Should().Be(0.1234567890123456789m);
    }

    [Fact]
    public void TryRead_reports_a_duplicate_property_name_rather_than_keeping_the_last_one()
    {
        var ok = Read("""{ "inputCount": 3, "inputCount": 4 }""", out _, out var issues);

        ok.Should().BeFalse();
        issues.Should().ContainSingle().Which.Code.Should().Be(ContentIssueCode.DuplicateKey);
    }

    [Fact]
    public void TryRead_reports_a_duplicate_property_name_nested_inside_an_array_item()
    {
        var ok = Read("""{ "widgets": [ { "id": "WID_A", "id": "WID_B" } ] }""", out _, out var issues);

        ok.Should().BeFalse();
        issues.Should().ContainSingle().Which.Code.Should().Be(ContentIssueCode.DuplicateKey);
    }

    [Theory]
    [InlineData("{ /* a comment */ }")]
    [InlineData("""{ "a": 1, }""")]
    [InlineData("""{ "a": 1 """)]
    [InlineData("")]
    [InlineData("not json at all")]
    public void TryRead_rejects_anything_that_is_not_strict_json(string json)
    {
        var ok = Read(json, out var root, out var issues);

        ok.Should().BeFalse();
        root.Should().BeNull();
        issues.Should().Contain(i => i.Code == ContentIssueCode.MalformedJson);
    }

    [Fact]
    public void TryRead_orders_object_members_ordinally_so_two_orderings_of_the_same_file_are_one_value()
    {
        Read("""{ "b": 1, "a": 2 }""", out var first, out _);
        Read("""{ "a": 2, "b": 1 }""", out var second, out _);

        first.Should().Be(second);
    }

    [Fact]
    public void TryRead_accepts_a_document_whose_root_is_an_array()
    {
        var ok = Read("[1, 2, 3]", out var root, out _);

        ok.Should().BeTrue();
        root!.Items.Should().HaveCount(3);
    }
}
