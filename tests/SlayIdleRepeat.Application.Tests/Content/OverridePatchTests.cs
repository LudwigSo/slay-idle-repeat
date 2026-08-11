using FluentAssertions;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// `21` §3.3 / `14` §6 — <em>"An override file is a sparse JSON patch applied on top of the
/// canonical data at load time. Sweeps, experiments and what-ifs all run as overrides, so the
/// canonical data is only ever edited when a change is adopted."</em>
/// </summary>
public sealed class OverridePatchTests
{
    private const string OverridePath = "tuning/experiments/cheaper_merges.json";

    private static ContentLoadOptions With(params string[] overrides) =>
        new() { OverrideDocuments = overrides };

    private static Adapters.InMemory.InMemoryContentSource SourceWithOverride(string json) =>
        ContentTestData.Valid().Set(OverridePath, json);

    [Fact]
    public void An_override_changes_only_the_key_it_names()
    {
        var source = SourceWithOverride("""
        { "widgets.json": { "merge": { "inputCount": 2 } } }
        """);

        var snapshot = ContentLoader.Load(source, With(OverridePath)).Require();

        snapshot.ReadInt32($"{ContentTestData.TuningPath}#/merge/inputCount").Should().Be(2);
    }

    [Fact]
    public void An_override_leaves_its_siblings_untouched()
    {
        var source = SourceWithOverride("""
        { "widgets.json": { "merge": { "inputCount": 2 } } }
        """);

        var snapshot = ContentLoader.Load(source, With(OverridePath)).Require();

        snapshot.ReadDouble($"{ContentTestData.TuningPath}#/merge/statBonusPerLevel").Should().Be(0.07d);
        snapshot.ReadText($"{ContentTestData.TuningPath}#/merge/topRarity").Should().Be("SS");
    }

    [Fact]
    public void An_override_leaves_documents_it_does_not_name_untouched()
    {
        var source = SourceWithOverride("""
        { "widgets.json": { "merge": { "inputCount": 2 } } }
        """);

        var snapshot = ContentLoader.Load(source, With(OverridePath)).Require();

        snapshot.ReadText($"{ContentTestData.EnglishPath}#/strings/loc.widget.anvil.name").Should().Be("Anvil");
    }

    [Fact]
    public void A_nested_partial_override_replaces_the_leaf_and_not_its_parent_object()
    {
        var source = SourceWithOverride("""
        { "widgets.json": { "merge": { "statBonusPerLevel": 0.09 } } }
        """);

        var snapshot = ContentLoader.Load(source, With(OverridePath)).Require();

        snapshot.ReadDouble($"{ContentTestData.TuningPath}#/merge/statBonusPerLevel").Should().Be(0.09d);
        snapshot.ReadInt32($"{ContentTestData.TuningPath}#/merge/inputCount").Should().Be(3);
        snapshot.ReadText($"{ContentTestData.TuningPath}#/merge/topRarity").Should().Be("SS");
    }

    [Fact]
    public void An_override_can_authorise_a_value_that_the_canonical_file_leaves_unauthorised()
    {
        var source = SourceWithOverride("""
        { "widgets.json": { "merge": { "dustSubstituteCost": 4200 } } }
        """);

        var snapshot = ContentLoader.Load(source, With(OverridePath)).Require();

        snapshot.ReadInt32($"{ContentTestData.TuningPath}#/merge/dustSubstituteCost").Should().Be(4200);
    }

    [Fact]
    public void The_canonical_document_bytes_are_byte_identical_after_an_override_has_been_layered()
    {
        var source = SourceWithOverride("""
        { "widgets.json": { "merge": { "inputCount": 2 } } }
        """);
        var before = source.ReadDocument(ContentTestData.TuningPath).ToArray();

        ContentLoader.Load(source, With(OverridePath)).Require();

        source.ReadDocument(ContentTestData.TuningPath).ToArray().Should().Equal(before);
    }

    [Fact]
    public void Loading_without_the_override_yields_the_canonical_value()
    {
        var source = SourceWithOverride("""
        { "widgets.json": { "merge": { "inputCount": 2 } } }
        """);

        var snapshot = ContentLoader.Load(source).Require();

        snapshot.ReadInt32($"{ContentTestData.TuningPath}#/merge/inputCount").Should().Be(3);
    }

    [Fact]
    public void An_override_moves_the_version_stamp_because_the_content_it_produced_is_different()
    {
        var source = SourceWithOverride("""
        { "widgets.json": { "merge": { "inputCount": 2 } } }
        """);

        var canonical = ContentLoader.Load(source).Require();
        var overridden = ContentLoader.Load(source, With(OverridePath)).Require();

        overridden.Version.Should().NotBe(canonical.Version);
    }

    [Fact]
    public void An_overridden_value_is_still_validated_against_the_schema()
    {
        var source = SourceWithOverride("""
        { "widgets.json": { "merge": { "inputCount": 99 } } }
        """);

        var result = ContentLoader.Load(source, With(OverridePath));

        result.Succeeded.Should().BeFalse();
        result.Issues.Should().Contain(i => i.Code == ContentIssueCode.OutOfRange);
    }

    [Fact]
    public void An_override_that_names_a_key_the_canonical_file_does_not_have_is_rejected()
    {
        var source = SourceWithOverride("""
        { "widgets.json": { "merge": { "inputCounts": 2 } } }
        """);

        var result = ContentLoader.Load(source, With(OverridePath));

        result.Issues.Should().Contain(i => i.Code == ContentIssueCode.OverrideTargetMissing);
    }

    [Fact]
    public void An_override_that_names_a_file_the_data_set_does_not_have_is_rejected()
    {
        var source = SourceWithOverride("""
        { "gadgets.json": { "merge": { "inputCount": 2 } } }
        """);

        var result = ContentLoader.Load(source, With(OverridePath));

        result.Issues.Should().Contain(i => i.Code == ContentIssueCode.OverrideTargetMissing);
    }

    [Fact]
    public void Overrides_layer_in_the_order_they_are_named_so_the_last_one_wins()
    {
        var source = ContentTestData.Valid()
            .Set("tuning/experiments/first.json", """{ "widgets.json": { "merge": { "inputCount": 2 } } }""")
            .Set("tuning/experiments/second.json", """{ "widgets.json": { "merge": { "inputCount": 4 } } }""");

        var snapshot = ContentLoader
            .Load(source, With("tuning/experiments/first.json", "tuning/experiments/second.json"))
            .Require();

        snapshot.ReadInt32($"{ContentTestData.TuningPath}#/merge/inputCount").Should().Be(4);
    }

    [Fact]
    public void An_override_replaces_an_array_wholesale_rather_than_merging_it_by_index()
    {
        var source = SourceWithOverride("""
        {
          "widgets.json": {
            "widgets": [ { "id": "WID_ANVIL", "rarity": "S", "icon": "icon_anvil", "requires": null } ]
          }
        }
        """);

        var snapshot = ContentLoader.Load(source, With(OverridePath)).Require();

        snapshot.Read($"{ContentTestData.TuningPath}#/widgets").Items.Should().HaveCount(1);
        snapshot.ReadText($"{ContentTestData.TuningPath}#/widgets/0/rarity").Should().Be("S");
    }

    [Fact]
    public void The_port_that_content_is_read_through_exposes_no_way_to_write_a_canonical_file()
    {
        var writeShaped = typeof(Ports.Shared.IContentSourcePort)
            .GetMethods()
            .Where(m => m.Name.Contains("Write", StringComparison.OrdinalIgnoreCase)
                     || m.Name.Contains("Save", StringComparison.OrdinalIgnoreCase)
                     || m.Name.Contains("Set", StringComparison.OrdinalIgnoreCase));

        writeShaped.Should().BeEmpty(
            "21 §3.3's 'never as edits to them' is a structural guarantee here, not a convention " +
            "the loader is trusted to keep");
    }
}
