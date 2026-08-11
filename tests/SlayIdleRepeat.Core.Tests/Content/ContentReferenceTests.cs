using Shouldly;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// The addressing scheme rules use to name a tunable: <c>tuning/forge.json#/merge/inputCount</c>.
/// The fragment is an RFC 6901 JSON Pointer, so a loc key such as <c>loc.chapter.5.name</c> — which
/// contains no slash — needs no escaping, but a member name that does contain one still resolves.
/// </summary>
public sealed class ContentReferenceTests
{
    [Fact]
    public void Parse_splits_the_document_path_from_the_pointer()
    {
        var reference = ContentReference.Parse("tuning/forge.json#/merge/inputCount");

        reference.DocumentPath.ShouldBe("tuning/forge.json");
        reference.Segments.ShouldBe(new[] { "merge", "inputCount" });
    }

    [Fact]
    public void Parse_accepts_a_document_with_no_fragment_as_the_root()
    {
        var reference = ContentReference.Parse("tuning/forge.json");

        reference.DocumentPath.ShouldBe("tuning/forge.json");
        reference.Segments.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_unescapes_a_member_name_containing_a_slash()
    {
        var reference = ContentReference.Parse("loc/en.json#/strings/loc.a~1b");

        reference.Segments.ShouldBe(new[] { "strings", "loc.a/b" });
    }

    [Fact]
    public void Parse_unescapes_a_member_name_containing_a_tilde()
    {
        var reference = ContentReference.Parse("loc/en.json#/strings/loc.a~0b");

        reference.Segments.ShouldBe(new[] { "strings", "loc.a~b" });
    }

    [Fact]
    public void Canonical_round_trips_an_escaped_segment()
    {
        var reference = ContentReference.Parse("loc/en.json#/strings/loc.a~1b");

        reference.Canonical.ShouldBe("loc/en.json#/strings/loc.a~1b");
    }

    [Theory]
    [InlineData("")]
    [InlineData("#/merge")]
    [InlineData("tuning/forge.json#merge")]
    public void Parse_rejects_a_malformed_reference(string candidate)
    {
        var act = () => ContentReference.Parse(candidate);

        Should.Throw<FormatException>(act);
    }

    [Fact]
    public void TryParse_returns_false_without_throwing()
    {
        var parsed = ContentReference.TryParse("tuning/forge.json#merge", out var reference);

        parsed.ShouldBeFalse();
        reference.ShouldBeNull();
    }
}
