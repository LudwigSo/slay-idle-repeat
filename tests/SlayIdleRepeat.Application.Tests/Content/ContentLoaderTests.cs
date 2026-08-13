using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// `14` §6 — the load path end to end: parse → layer overrides → validate → stamp.
/// </summary>
public sealed class ContentLoaderTests
{
    [Fact]
    public void Load_produces_a_snapshot_for_a_valid_data_set()
    {
        var result = ContentLoader.Load(ContentTestData.Valid());

        result.Issues.ShouldBeEmpty();
        result.Succeeded.ShouldBeTrue();
        result.Snapshot.ShouldNotBeNull();
    }

    [Fact]
    public void Load_does_not_put_the_schema_files_into_the_snapshot()
    {
        var snapshot = ContentLoader.Load(ContentTestData.Valid()).Require();

        snapshot.DocumentPaths.ShouldNotContain(p => p.StartsWith("schema/", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_puts_every_data_document_into_the_snapshot()
    {
        var snapshot = ContentLoader.Load(ContentTestData.Valid()).Require();

        snapshot.DocumentPaths.ShouldBe(new[]
        {
            ContentTestData.GermanPath, ContentTestData.EnglishPath, ContentTestData.TuningPath,
        });
    }

    [Fact]
    public void Load_produces_a_snapshot_whose_unauthorised_leaf_throws_rather_than_reading_as_zero()
    {
        var snapshot = ContentLoader.Load(ContentTestData.Valid()).Require();

        Action act = () => _ = snapshot.ReadInt32($"{ContentTestData.TuningPath}#/merge/dustSubstituteCost");

        Should.Throw<UnauthorisedTunableException>(act);
    }

    [Fact]
    public void Load_is_deterministic_the_same_bytes_produce_the_same_stamp()
    {
        var first = ContentLoader.Load(ContentTestData.Valid()).Require();
        var second = ContentLoader.Load(ContentTestData.Valid()).Require();

        second.Version.ShouldBe(first.Version);
    }

    [Fact]
    public void Load_is_deterministic_regardless_of_the_order_documents_were_written_to_the_source()
    {
        var forwards = ContentLoader.Load(ContentTestData.Valid()).Require();

        var backwards = ContentLoader.Load(
            new Adapters.InMemory.InMemoryContentSource()
                .Set(ContentTestData.GermanPath, ContentTestData.German)
                .Set(ContentTestData.EnglishPath, ContentTestData.English)
                .Set(ContentTestData.LocSchemaPath, ContentTestData.LocSchema)
                .Set(ContentTestData.TuningPath, ContentTestData.WidgetTuning)
                .Set(ContentTestData.SchemaPath, ContentTestData.WidgetSchema)).Require();

        backwards.Version.ShouldBe(forwards.Version);
    }

    [Fact]
    public void Load_produces_a_different_stamp_when_one_byte_of_one_value_changes()
    {
        var before = ContentLoader.Load(ContentTestData.Valid()).Require();

        var after = ContentLoader.Load(ContentTestData.WithTuningEdit(
            "\"inputCount\": 3", "\"inputCount\": 4")).Require();

        after.Version.ShouldNotBe(before.Version);
    }

    [Fact]
    public void Load_produces_a_different_stamp_when_a_key_is_renamed()
    {
        var before = ContentLoader.Load(ContentTestData.Valid()).Require();

        // One key, renamed everywhere it is written: both locales and the data that names it.
        // Nothing about the run changes except the key, and the stamp must still move.
        var after = ContentLoader.Load(
            ContentTestData.Valid()
                .Set(ContentTestData.EnglishPath, Rename(ContentTestData.English))
                .Set(ContentTestData.GermanPath, Rename(ContentTestData.German))
                .Set(ContentTestData.TuningPath, Rename(ContentTestData.WidgetTuning)))
            .Require();

        after.Version.ShouldNotBe(before.Version);

        static string Rename(string json) =>
            json.Replace("loc.widget.anvil.name", "loc.widget.anvil.title", StringComparison.Ordinal);
    }

    [Fact]
    public void Load_reports_a_data_document_that_no_schema_governs()
    {
        var source = ContentTestData.Valid().Set("tuning/ungoverned.json", """{ "a": 1 }""");

        var result = ContentLoader.Load(source);

        result.Issues.ShouldContain(i => i.Code == ContentIssueCode.MissingSchema);
    }

    /// <summary>
    /// 🔒 Content pairs by <b>directory</b>. Under the stem rule this file would look for
    /// <c>schema/CH_01_EMBERFALL.schema.json</c> — one <c>MissingSchema</c> per chapter, and a
    /// <c>chapter.schema.json</c> still governing nothing.
    /// </summary>
    [Theory]
    [InlineData("content/chapters/CH_01_EMBERFALL.json", "schema/chapter.schema.json")]
    [InlineData("content/liveops_events/EVT_EMBERFALL.json", "schema/event.schema.json")]
    public void A_content_file_is_paired_with_its_content_type_schema_not_with_its_own_stem(
        string documentPath, string schemaPath)
    {
        ContentLayout.SchemaFor(documentPath).ShouldBe(schemaPath);
    }

    /// <summary>
    /// A file under a content directory nobody declared: the stem rule takes over and it fails
    /// loudly, which is what forces the table row to be written beside the new schema.
    /// </summary>
    [Fact]
    public void Load_fails_loudly_for_a_content_directory_that_no_schema_is_declared_for()
    {
        var source = ContentTestData.Valid()
            .Set("content/runes/RUNE_EMBER.json", """{ "id": "RUNE_EMBER" }""");

        ContentLoader.Load(source).Issues.ShouldContain(i =>
            i.Code == ContentIssueCode.MissingSchema && i.Location == "content/runes/RUNE_EMBER.json");
    }

    // ------------------------------------- 14 §6 duplicate ids, over a NUMERIC identity field

    /// <summary>
    /// 🔒 <c>ContentInvariants.IdentityMemberNames</c> includes <c>chapter</c>, <c>day</c> and
    /// <c>slot</c>, which are numbers. Keying the duplicate check on <c>ToString()</c> compared
    /// numbers by <em>representation</em>: <c>ContentValue.ToString()</c> is scale-preserving while
    /// <c>ContentValue.Equals</c> is deliberately scale-independent, so <c>3</c> beside <c>3.0</c>
    /// declared the same slot twice and walked through the gate.
    /// </summary>
    [Fact]
    public void Load_sees_a_duplicate_numeric_id_written_at_a_different_scale()
    {
        var issues = ContentLoader.Load(Slots(
            """{ "slot": 3 }""", """{ "slot": 3.0 }""")).Issues;

        issues.ShouldContain(i =>
            i.Code == ContentIssueCode.DuplicateId && i.Location == "tuning/gizmos.json#/gizmos");
    }

    /// <summary>
    /// 🔒 And the other direction. <c>ToString()</c> for an object emits its member <em>names</em>
    /// only, so two structurally different objects sitting in an identity slot reported as
    /// duplicates of each other — a false build failure on data that is correct.
    /// </summary>
    [Fact]
    public void Load_does_not_call_two_different_objects_in_an_identity_slot_duplicates()
    {
        var issues = ContentLoader.Load(Slots(
            """{ "slot": { "a": 1 } }""", """{ "slot": { "a": 2 } }""")).Issues;

        issues.ShouldNotContain(i => i.Code == ContentIssueCode.DuplicateId);
    }

    /// <summary>A two-document source whose entries carry the numeric identity member `slot`.</summary>
    private static Adapters.InMemory.InMemoryContentSource Slots(string first, string second) =>
        new Adapters.InMemory.InMemoryContentSource()
            .Set("schema/gizmos.schema.json", """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "additionalProperties": false,
              "required": ["$schema", "gizmos"],
              "properties": {
                "$schema": { "type": "string" },
                "gizmos": {
                  "type": "array",
                  "minItems": 1,
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["slot"],
                    "properties": {
                      "slot": {
                        "oneOf": [
                          { "type": "number" },
                          { "type": "object", "additionalProperties": { "type": "integer" } }
                        ]
                      }
                    }
                  }
                }
              }
            }
            """)
            .Set("tuning/gizmos.json", $$"""
            {
              "$schema": "../schema/gizmos.schema.json",
              "gizmos": [{{first}}, {{second}}]
            }
            """);

    [Fact]
    public void Load_reports_a_schema_that_governs_no_data_document()
    {
        var source = ContentTestData.Valid().Set("schema/ghosts.schema.json", """
        { "$schema": "https://json-schema.org/draft/2020-12/schema", "type": "object" }
        """);

        var result = ContentLoader.Load(source);

        result.Issues.ShouldContain(i => i.Code == ContentIssueCode.OrphanSchema);
    }

    [Fact]
    public void Load_fails_when_the_source_holds_nothing_because_a_validator_that_validated_nothing_did_not_pass()
    {
        var result = ContentLoader.Load(new Adapters.InMemory.InMemoryContentSource());

        var issue = result.Issues.ShouldHaveSingleItem();

        issue.Code.ShouldBe(ContentIssueCode.MissingSchema);
        issue.Location.ShouldBe("(content source)");
    }

    [Fact]
    public void Load_never_puts_an_experiment_override_into_the_snapshot()
    {
        var source = ContentTestData.Valid().Set("tuning/experiments/cheaper_merges.json", """
        { "widgets.json": { "merge": { "inputCount": 2 } } }
        """);

        var snapshot = ContentLoader.Load(source).Require();

        snapshot.DocumentPaths.ShouldNotContain("tuning/experiments/cheaper_merges.json");
        snapshot.ReadInt32($"{ContentTestData.TuningPath}#/merge/inputCount").ShouldBe(3);
    }

    [Fact]
    public void Require_throws_a_ContentLoadException_naming_every_issue()
    {
        var result = ContentLoader.Load(ContentTestData.WithTuningEdit(
            "\"inputCount\": 3", "\"inputCount\": 99"));

        Action act = () => _ = result.Require();

        Should.Throw<ContentLoadException>(act)
            .Issues.ShouldContain(i => i.Code == ContentIssueCode.OutOfRange);
    }
}
