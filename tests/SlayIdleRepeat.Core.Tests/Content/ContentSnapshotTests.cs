using FluentAssertions;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// `14` §6 / `30` §3 — <em>"Content is loaded once into an immutable, version-stamped
/// <c>ContentSnapshot</c> … reading content is a rule."</em> These are the reading rules.
/// </summary>
public sealed class ContentSnapshotTests
{
    private const string ForgePath = "tuning/forge.json";

    private static ContentSnapshot Snapshot() =>
        new(
            ContentVersion.FromHex(new string('a', ContentVersion.HexLength)),
            [
                new ContentDocument(ForgePath, ContentValue.Object(
                [
                    new("_status", ContentValue.Text("partial")),
                    new("merge", ContentValue.Object(
                    [
                        new("inputCount", ContentValue.Number(3m)),
                        new("dustSubstituteCost", ContentValue.Object(
                        [
                            new("C", ContentValue.Number(50m)),
                            new("SS", ContentValue.Unauthorised),
                        ])),
                    ])),
                    new("enhance", ContentValue.Object(
                    [
                        new("statBonusPerLevel", ContentValue.Number(0.07m)),
                        new("neverDestroysOrDowngrades", ContentValue.True),
                        new("perLevelSuccessRate", ContentValue.Unauthorised),
                        new("stoneCostPerLevel", ContentValue.Array(
                            [ContentValue.Number(2m), ContentValue.Number(3m), ContentValue.Number(4m)])),
                    ])),
                ])),
            ]);

    [Fact]
    public void Version_is_the_stamp_the_snapshot_was_built_with()
    {
        var snapshot = Snapshot();

        snapshot.Version.Value.Should().Be(new string('a', ContentVersion.HexLength));
    }

    [Fact]
    public void DocumentPaths_are_ordinal_sorted()
    {
        var snapshot = new ContentSnapshot(
            ContentVersion.FromHex(new string('b', ContentVersion.HexLength)),
            [
                new ContentDocument("tuning/luck.json", ContentValue.EmptyObject),
                new ContentDocument("loc/en.json", ContentValue.EmptyObject),
                new ContentDocument("tuning/ads.json", ContentValue.EmptyObject),
            ]);

        snapshot.DocumentPaths.Should().Equal("loc/en.json", "tuning/ads.json", "tuning/luck.json");
    }

    [Fact]
    public void Constructor_throws_when_the_same_document_path_appears_twice()
    {
        var act = () => new ContentSnapshot(
            ContentVersion.FromHex(new string('c', ContentVersion.HexLength)),
            [
                new ContentDocument(ForgePath, ContentValue.EmptyObject),
                new ContentDocument(ForgePath, ContentValue.EmptyObject),
            ]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ReadInt32_returns_the_value_at_a_json_pointer()
    {
        var snapshot = Snapshot();

        snapshot.ReadInt32($"{ForgePath}#/merge/inputCount").Should().Be(3);
    }

    [Fact]
    public void ReadDouble_returns_the_value_at_a_json_pointer()
    {
        var snapshot = Snapshot();

        snapshot.ReadDouble($"{ForgePath}#/enhance/statBonusPerLevel").Should().Be(0.07d);
    }

    [Fact]
    public void ReadBoolean_returns_the_value_at_a_json_pointer()
    {
        var snapshot = Snapshot();

        snapshot.ReadBoolean($"{ForgePath}#/enhance/neverDestroysOrDowngrades").Should().BeTrue();
    }

    [Fact]
    public void ReadText_returns_the_value_at_a_json_pointer()
    {
        var snapshot = Snapshot();

        snapshot.ReadText($"{ForgePath}#/_status").Should().Be("partial");
    }

    [Fact]
    public void Read_indexes_into_an_array_by_position()
    {
        var snapshot = Snapshot();

        snapshot.ReadInt32($"{ForgePath}#/enhance/stoneCostPerLevel/2").Should().Be(4);
    }

    [Fact]
    public void ReadDouble_throws_UnauthorisedTunableException_rather_than_returning_zero()
    {
        var snapshot = Snapshot();

        var act = () => snapshot.ReadDouble($"{ForgePath}#/enhance/perLevelSuccessRate");

        act.Should().Throw<UnauthorisedTunableException>()
           .Which.Reference.Should().Be($"{ForgePath}#/enhance/perLevelSuccessRate");
    }

    [Fact]
    public void ReadInt32_throws_UnauthorisedTunableException_for_an_unauthorised_leaf_inside_a_map()
    {
        var snapshot = Snapshot();

        var act = () => snapshot.ReadInt32($"{ForgePath}#/merge/dustSubstituteCost/SS");

        act.Should().Throw<UnauthorisedTunableException>();
    }

    [Fact]
    public void Read_returns_the_unauthorised_value_so_a_caller_can_inspect_the_hole()
    {
        var snapshot = Snapshot();

        var value = snapshot.Read($"{ForgePath}#/enhance/perLevelSuccessRate");

        value.IsUnauthorised.Should().BeTrue();
    }

    [Fact]
    public void IsAuthorised_is_false_for_an_unauthorised_leaf()
    {
        var snapshot = Snapshot();

        snapshot.IsAuthorised($"{ForgePath}#/enhance/perLevelSuccessRate").Should().BeFalse();
    }

    [Fact]
    public void IsAuthorised_is_false_for_a_reference_that_does_not_exist()
    {
        var snapshot = Snapshot();

        snapshot.IsAuthorised($"{ForgePath}#/enhance/thereIsNoSuchKey").Should().BeFalse();
    }

    [Fact]
    public void IsAuthorised_is_true_for_an_authored_value()
    {
        var snapshot = Snapshot();

        snapshot.IsAuthorised($"{ForgePath}#/enhance/statBonusPerLevel").Should().BeTrue();
    }

    [Fact]
    public void ReadInt32_throws_MissingContentException_for_an_absent_key()
    {
        var snapshot = Snapshot();

        var act = () => snapshot.ReadInt32($"{ForgePath}#/merge/thereIsNoSuchKey");

        act.Should().Throw<MissingContentException>();
    }

    [Fact]
    public void ReadInt32_throws_MissingContentException_for_an_absent_document()
    {
        var snapshot = Snapshot();

        var act = () => snapshot.ReadInt32("tuning/there_is_no_such_file.json#/a");

        act.Should().Throw<MissingContentException>();
    }

    [Fact]
    public void ReadInt32_throws_MissingContentException_for_an_array_index_past_the_end()
    {
        var snapshot = Snapshot();

        var act = () => snapshot.ReadInt32($"{ForgePath}#/enhance/stoneCostPerLevel/3");

        act.Should().Throw<MissingContentException>();
    }

    [Fact]
    public void ReadText_throws_ContentTypeMismatchException_when_the_value_is_a_number()
    {
        var snapshot = Snapshot();

        var act = () => snapshot.ReadText($"{ForgePath}#/merge/inputCount");

        act.Should().Throw<ContentTypeMismatchException>();
    }

    [Fact]
    public void TryRead_returns_false_for_an_absent_reference_and_writes_no_value()
    {
        var snapshot = Snapshot();

        var found = snapshot.TryRead($"{ForgePath}#/merge/thereIsNoSuchKey", out var value);

        found.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void GetDocument_throws_MissingContentException_for_an_unknown_path()
    {
        var snapshot = Snapshot();

        var act = () => snapshot.GetDocument("tuning/there_is_no_such_file.json");

        act.Should().Throw<MissingContentException>();
    }

    [Fact]
    public void ContentSnapshot_exposes_no_public_mutation_surface()
    {
        var members = typeof(ContentSnapshot)
            .GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .OfType<System.Reflection.PropertyInfo>()
            .Where(p => p.CanWrite)
            .Select(p => p.Name);

        members.Should().BeEmpty(
            "14 §6 requires the snapshot to be immutable — a settable property is how hot-reload " +
            "starts mutating instead of swapping");
    }
}
