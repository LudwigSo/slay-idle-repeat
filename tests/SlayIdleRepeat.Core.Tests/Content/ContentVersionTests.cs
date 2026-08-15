using Shouldly;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// The version stamp is a content hash, not a hand-bumped number; these tests pin its shape.
/// </summary>
public sealed class ContentVersionTests
{
    private const string ValidStamp = "9f2c4a1b8e7d6c5f4a3b2c1d0e9f8a7b6c5d4e3f2a1b0c9d8e7f6a5b4c3d2e1f";

    [Fact]
    public void FromHex_accepts_sixty_four_lowercase_hex_characters()
    {
        var version = ContentVersion.FromHex(ValidStamp);

        version.Value.ShouldBe(ValidStamp);
    }

    [Fact]
    public void Short_is_the_first_twelve_characters()
    {
        var version = ContentVersion.FromHex(ValidStamp);

        version.Short.ShouldBe("9f2c4a1b8e7d");
    }

    [Theory]
    [InlineData("")]
    [InlineData("9f2c4a1b")]
    [InlineData("9F2C4A1B8E7D6C5F4A3B2C1D0E9F8A7B6C5D4E3F2A1B0C9D8E7F6A5B4C3D2E1F")]
    [InlineData("9f2c4a1b8e7d6c5f4a3b2c1d0e9f8a7b6c5d4e3f2a1b0c9d8e7f6a5b4c3d2e1g")]
    public void FromHex_rejects_anything_that_is_not_a_lowercase_sha256_hex_string(string candidate)
    {
        var act = () => ContentVersion.FromHex(candidate);

        Should.Throw<FormatException>(act);
    }

    [Fact]
    public void TryFromHex_returns_false_without_throwing_for_a_malformed_stamp()
    {
        var parsed = ContentVersion.TryFromHex("not-a-hash", out var version);

        parsed.ShouldBeFalse();
        version.ShouldBeNull();
    }

    [Fact]
    public void Equality_is_by_value_so_two_snapshots_of_the_same_content_compare_equal()
    {
        ContentVersion.FromHex(ValidStamp).ShouldBe(ContentVersion.FromHex(ValidStamp));
    }
}
