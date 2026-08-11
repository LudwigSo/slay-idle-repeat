using Shouldly;
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

        snapshot.ReadInt32($"{ContentTestData.TuningPath}#/merge/inputCount").ShouldBe(2);
    }

    [Fact]
    public void An_override_leaves_its_siblings_untouched()
    {
        var source = SourceWithOverride("""
        { "widgets.json": { "merge": { "inputCount": 2 } } }
        """);

        var snapshot = ContentLoader.Load(source, With(OverridePath)).Require();

        snapshot.ReadDouble($"{ContentTestData.TuningPath}#/merge/statBonusPerLevel").ShouldBe(0.07d);
        snapshot.ReadText($"{ContentTestData.TuningPath}#/merge/topRarity").ShouldBe("SS");
    }

    [Fact]
    public void An_override_leaves_documents_it_does_not_name_untouched()
    {
        var source = SourceWithOverride("""
        { "widgets.json": { "merge": { "inputCount": 2 } } }
        """);

        var snapshot = ContentLoader.Load(source, With(OverridePath)).Require();

        snapshot.ReadText($"{ContentTestData.EnglishPath}#/strings/loc.widget.anvil.name").ShouldBe("Anvil");
    }

    [Fact]
    public void A_nested_partial_override_replaces_the_leaf_and_not_its_parent_object()
    {
        var source = SourceWithOverride("""
        { "widgets.json": { "merge": { "statBonusPerLevel": 0.09 } } }
        """);

        var snapshot = ContentLoader.Load(source, With(OverridePath)).Require();

        snapshot.ReadDouble($"{ContentTestData.TuningPath}#/merge/statBonusPerLevel").ShouldBe(0.09d);
        snapshot.ReadInt32($"{ContentTestData.TuningPath}#/merge/inputCount").ShouldBe(3);
        snapshot.ReadText($"{ContentTestData.TuningPath}#/merge/topRarity").ShouldBe("SS");
    }

    [Fact]
    public void An_override_can_authorise_a_value_that_the_canonical_file_leaves_unauthorised()
    {
        var source = SourceWithOverride("""
        { "widgets.json": { "merge": { "dustSubstituteCost": 4200 } } }
        """);

        var snapshot = ContentLoader.Load(source, With(OverridePath)).Require();

        snapshot.ReadInt32($"{ContentTestData.TuningPath}#/merge/dustSubstituteCost").ShouldBe(4200);
    }

    [Fact]
    public void The_canonical_document_bytes_are_byte_identical_after_an_override_has_been_layered()
    {
        var source = SourceWithOverride("""
        { "widgets.json": { "merge": { "inputCount": 2 } } }
        """);
        var before = source.ReadDocument(ContentTestData.TuningPath).ToArray();

        ContentLoader.Load(source, With(OverridePath)).Require();

        source.ReadDocument(ContentTestData.TuningPath).ToArray().ShouldBe(before);
    }

    [Fact]
    public void Loading_without_the_override_yields_the_canonical_value()
    {
        var source = SourceWithOverride("""
        { "widgets.json": { "merge": { "inputCount": 2 } } }
        """);

        var snapshot = ContentLoader.Load(source).Require();

        snapshot.ReadInt32($"{ContentTestData.TuningPath}#/merge/inputCount").ShouldBe(3);
    }

    [Fact]
    public void An_override_moves_the_version_stamp_because_the_content_it_produced_is_different()
    {
        var source = SourceWithOverride("""
        { "widgets.json": { "merge": { "inputCount": 2 } } }
        """);

        var canonical = ContentLoader.Load(source).Require();
        var overridden = ContentLoader.Load(source, With(OverridePath)).Require();

        overridden.Version.ShouldNotBe(canonical.Version);
    }

    [Fact]
    public void An_overridden_value_is_still_validated_against_the_schema()
    {
        var source = SourceWithOverride("""
        { "widgets.json": { "merge": { "inputCount": 99 } } }
        """);

        var result = ContentLoader.Load(source, With(OverridePath));

        result.Succeeded.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == ContentIssueCode.OutOfRange);
    }

    [Fact]
    public void An_override_that_nulls_a_value_the_schema_does_not_permit_null_on_is_rejected()
    {
        // 🔒 The de-authorising direction. An override is the one mechanism that rewrites content at
        // load time, so it is the one place a null could reach a key the docs DID authorise — and a
        // rule already reading that key would then see "unauthorised" where a number used to be.
        var source = SourceWithOverride("""
        { "widgets.json": { "merge": { "statBonusPerLevel": null } } }
        """);

        var result = ContentLoader.Load(source, With(OverridePath));

        result.Succeeded.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == ContentIssueCode.SchemaViolation);
    }

    [Fact]
    public void An_override_may_de_authorise_a_leaf_the_schema_permits_null_on_and_it_reads_as_unauthorised()
    {
        var source = SourceWithOverride("""
        { "widgets.json": { "merge": { "dustSubstituteCost": null } } }
        """);

        var snapshot = ContentLoader.Load(source, With(OverridePath)).Require();

        snapshot.Read($"{ContentTestData.TuningPath}#/merge/dustSubstituteCost").IsUnauthorised.ShouldBeTrue();

        Action act = () => _ = snapshot.ReadInt32($"{ContentTestData.TuningPath}#/merge/dustSubstituteCost");
        Should.Throw<Core.Content.UnauthorisedTunableException>(act, "a de-authorised leaf is never 0");
    }

    [Fact]
    public void An_override_whose_root_is_not_an_object_is_rejected()
    {
        var result = ContentLoader.Load(SourceWithOverride("[1, 2]"), With(OverridePath));

        result.Succeeded.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == ContentIssueCode.OverrideTargetMissing);
    }

    [Fact]
    public void An_override_naming_a_file_name_that_is_ambiguous_across_two_documents_is_rejected()
    {
        // The moment a second widgets.json exists anywhere, an override keyed on the bare file name
        // would otherwise silently pick one — or stop applying — with nothing said.
        var source = SourceWithOverride("""
        { "widgets.json": { "merge": { "inputCount": 2 } } }
        """)
            .Set("content/widgets.json", ContentTestData.WidgetTuning)
            .Set("schema/widgets2.schema.json", ContentTestData.WidgetSchema);

        var result = ContentLoader.Load(source, With(OverridePath));

        result.Succeeded.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == ContentIssueCode.OverrideTargetMissing);
    }

    [Fact]
    public void An_override_that_names_a_key_the_canonical_file_does_not_have_is_rejected()
    {
        var source = SourceWithOverride("""
        { "widgets.json": { "merge": { "inputCounts": 2 } } }
        """);

        var result = ContentLoader.Load(source, With(OverridePath));

        result.Succeeded.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == ContentIssueCode.OverrideTargetMissing);
    }

    [Fact]
    public void An_override_that_names_a_file_the_data_set_does_not_have_is_rejected()
    {
        var source = SourceWithOverride("""
        { "gadgets.json": { "merge": { "inputCount": 2 } } }
        """);

        var result = ContentLoader.Load(source, With(OverridePath));

        result.Succeeded.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == ContentIssueCode.OverrideTargetMissing);
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

        snapshot.ReadInt32($"{ContentTestData.TuningPath}#/merge/inputCount").ShouldBe(4);
    }

    [Fact]
    public void An_override_replaces_an_array_wholesale_rather_than_merging_it_by_index()
    {
        // A two-item array replaced by a two-item array in the opposite order. An index-wise merge
        // would leave WID_ANVIL at index 0; a wholesale replacement does not.
        var source = SourceWithOverride("""
        {
          "widgets.json": {
            "widgets": [
              { "id": "WID_BELLOWS", "rarity": "S", "icon": "icon_bellows",
                "displayName": "loc.widget.bellows.name", "requires": null },
              { "id": "WID_ANVIL", "rarity": "C", "icon": "icon_anvil",
                "displayName": "loc.widget.anvil.name", "requires": null }
            ]
          }
        }
        """);

        var snapshot = ContentLoader.Load(source, With(OverridePath)).Require();

        snapshot.ReadText($"{ContentTestData.TuningPath}#/widgets/0/id").ShouldBe("WID_BELLOWS");
        snapshot.ReadText($"{ContentTestData.TuningPath}#/widgets/0/rarity").ShouldBe("S");
    }

    [Fact]
    public void The_port_that_content_is_read_through_exposes_no_way_to_write_a_canonical_file()
    {
        // An exact set, not a hunt for verbs: a substring test for Write/Save/Set cannot fail today
        // and would not fail for Persist, Put, Store or Apply tomorrow.
        typeof(Ports.Shared.IContentSourcePort).GetMembers().Select(m => m.Name).ShouldBe(
            ["get_Revision", "Revision", "ListDocuments", "ReadDocument"],
            ignoreOrder: true,
            customMessage:
            "21 §3.3's 'never as edits to them' is structural here, not a convention the loader is " +
            "trusted to keep — any new member on this port is a decision somebody must argue for");
    }
}
