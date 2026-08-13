using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// `14` §6 🔒 — <em>"JSON is validated at build time against schemas in
/// <c>game-data/schema/</c>. The build fails on unknown IDs, missing icons,
/// out-of-range values, orphaned references or duplicate IDs."</em>
/// </summary>
/// <remarks>
/// Every one of those five classes gets a case here that proves the validator actually rejects it.
/// A validator whose negative cases were never committed is a validator nobody can trust after the
/// first refactor.
/// </remarks>
public sealed class SchemaValidationTests
{
    /// <summary>The one schema object in the fixture that is small enough to restate whole.</summary>
    private const string Limits =
        "\"limits\": { \"type\": \"object\", \"maxProperties\": 2, " +
        "\"additionalProperties\": { \"type\": \"integer\" } }";

    private static IReadOnlyList<ContentIssue> Issues(Adapters.InMemory.InMemoryContentSource source) =>
        ContentLoader.Load(source).Issues;

    // ---------------------------------------------------------------- 14 §6: unknown IDs

    [Fact]
    public void Load_rejects_an_enum_member_the_schema_does_not_know()
    {
        var issues = Issues(ContentTestData.WithTuningEdit("\"topRarity\": \"SS\"", "\"topRarity\": \"SSS\""));

        issues.ShouldContain(i => i.Code == ContentIssueCode.UnknownId);
    }

    [Fact]
    public void Load_rejects_an_id_that_does_not_match_its_pattern()
    {
        var issues = Issues(ContentTestData.WithTuningEdit("\"id\": \"WID_ANVIL\"", "\"id\": \"anvil\""));

        issues.ShouldContain(i => i.Code == ContentIssueCode.UnknownId);
    }

    [Fact]
    public void Load_rejects_a_property_the_schema_does_not_declare()
    {
        var issues = Issues(ContentTestData.WithTuningEdit(
            "\"inputCount\": 3", "\"inputCount\": 3, \"inputCountt\": 3"));

        issues.ShouldContain(i => i.Code == ContentIssueCode.UnknownId);
    }

    [Fact]
    public void Load_rejects_a_locale_key_that_breaks_the_key_convention()
    {
        var issues = Issues(ContentTestData.With(ContentTestData.EnglishPath, """
        {
          "$schema": "../schema/loc.schema.json",
          "_locale": "en",
          "strings": { "Widget.Anvil.Name": "Anvil" }
        }
        """));

        issues.ShouldContain(i => i.Code == ContentIssueCode.UnknownId);
    }

    // ------------------------------------------------------------ 14 §6: out-of-range values

    [Fact]
    public void Load_rejects_an_integer_above_its_maximum()
    {
        var issues = Issues(ContentTestData.WithTuningEdit("\"inputCount\": 3", "\"inputCount\": 6"));

        issues.ShouldContain(i => i.Code == ContentIssueCode.OutOfRange);
    }

    [Fact]
    public void Load_rejects_an_integer_below_its_minimum()
    {
        var issues = Issues(ContentTestData.WithTuningEdit("\"inputCount\": 3", "\"inputCount\": 1"));

        issues.ShouldContain(i => i.Code == ContentIssueCode.OutOfRange);
    }

    [Fact]
    public void Load_rejects_a_value_at_an_exclusive_minimum()
    {
        var issues = Issues(ContentTestData.WithTuningEdit(
            "\"statBonusPerLevel\": 0.07", "\"statBonusPerLevel\": 0"));

        issues.ShouldContain(i => i.Code == ContentIssueCode.OutOfRange);
    }

    [Fact]
    public void Load_rejects_an_empty_collection_below_its_minItems()
    {
        var issues = Issues(ContentTestData.With(ContentTestData.TuningPath, """
        {
          "$schema": "../schema/widgets.schema.json",
          "_doc": "08 §4.1 — the miniature stand-in used by the content-pipeline tests.",
          "_status": "partial",
          "merge": {
            "inputCount": 3,
            "statBonusPerLevel": 0.07,
            "topRarity": "SS",
            "dustSubstituteCost": null,
            "perLevelSuccessRate": null
          },
          "widgets": []
        }
        """));

        issues.ShouldContain(i => i.Code == ContentIssueCode.OutOfRange);
    }

    // ------------------------------------------------------------ 14 §6: duplicate IDs

    [Fact]
    public void Load_rejects_the_same_collection_entry_twice()
    {
        var issues = Issues(ContentTestData.WithTuningEdit(
            "{ \"id\": \"WID_BELLOWS\", \"rarity\": \"B\", \"icon\": \"icon_bellows\", " +
            "\"displayName\": \"loc.widget.bellows.name\", \"requires\": \"WID_ANVIL\" }",
            "{ \"id\": \"WID_ANVIL\", \"rarity\": \"C\", \"icon\": \"icon_anvil\", " +
            "\"displayName\": \"loc.widget.anvil.name\", \"requires\": null }"));

        issues.ShouldContain(i => i.Code == ContentIssueCode.DuplicateId);
    }

    [Fact]
    public void Load_rejects_the_same_id_declared_twice_with_different_bodies()
    {
        var issues = Issues(ContentTestData.WithTuningEdit(
            "\"id\": \"WID_BELLOWS\"", "\"id\": \"WID_ANVIL\""));

        issues.ShouldContain(i => i.Code == ContentIssueCode.DuplicateId);
    }

    [Fact]
    public void Load_rejects_a_duplicate_property_name_in_a_data_file()
    {
        var issues = Issues(ContentTestData.WithTuningEdit(
            "\"inputCount\": 3", "\"inputCount\": 3, \"inputCount\": 4"));

        issues.ShouldContain(i => i.Code == ContentIssueCode.DuplicateKey);
    }

    // ------------------------------------------------------- 14 §6: orphaned references

    [Fact]
    public void Load_rejects_a_reference_to_an_id_that_is_declared_nowhere()
    {
        var issues = Issues(ContentTestData.WithTuningEdit(
            "\"requires\": \"WID_ANVIL\"", "\"requires\": \"WID_TONGS\""));

        issues.ShouldContain(i => i.Code == ContentIssueCode.OrphanedReference);
    }

    [Fact]
    public void Load_rejects_a_locale_key_present_in_one_locale_and_absent_from_the_other()
    {
        var issues = Issues(ContentTestData.With(ContentTestData.GermanPath, """
        {
          "$schema": "../schema/loc.schema.json",
          "_locale": "de",
          "strings": { "loc.widget.anvil.name": "##TODO_DE## Anvil" }
        }
        """));

        issues.ShouldContain(i => i.Code == ContentIssueCode.LocalisationMismatch);
    }

    [Fact]
    public void Load_rejects_a_locale_string_that_nothing_in_the_content_set_names()
    {
        // Added to BOTH locales, so this is an orphaned reference and not a parity mismatch.
        var source = ContentTestData.Valid()
            .Set(ContentTestData.EnglishPath, ContentTestData.English.Replace(
                "\"loc.widget.bellows.name\": \"Bellows\"",
                "\"loc.widget.bellows.name\": \"Bellows\", \"loc.widget.tongs.name\": \"Tongs\"",
                StringComparison.Ordinal))
            .Set(ContentTestData.GermanPath, ContentTestData.German.Replace(
                "\"loc.widget.bellows.name\": \"##TODO_DE## Bellows\"",
                "\"loc.widget.bellows.name\": \"##TODO_DE## Bellows\", " +
                "\"loc.widget.tongs.name\": \"##TODO_DE## Tongs\"",
                StringComparison.Ordinal));

        Issues(source).ShouldContain(i => i.Code == ContentIssueCode.OrphanedReference);
    }

    // ----------------------------------------------------------- 14 §6: missing icons

    [Fact]
    public void Load_rejects_a_definition_whose_siblings_declare_an_icon_and_it_does_not()
    {
        var issues = Issues(ContentTestData.WithTuningEdit(
            "\"icon\": \"icon_bellows\", ", string.Empty));

        issues.ShouldContain(i => i.Code == ContentIssueCode.MissingIcon);
    }

    [Fact]
    public void Load_rejects_an_empty_icon_name()
    {
        var issues = Issues(ContentTestData.WithTuningEdit("\"icon\": \"icon_bellows\"", "\"icon\": \"\""));

        issues.ShouldContain(i => i.Code == ContentIssueCode.MissingIcon);
    }

    // ------------------------------------------------------------ null is not a default

    [Fact]
    public void Load_rejects_a_null_where_the_schema_does_not_permit_one()
    {
        var issues = Issues(ContentTestData.WithTuningEdit("\"inputCount\": 3", "\"inputCount\": null"));

        issues.ShouldContain(i => i.Code == ContentIssueCode.SchemaViolation);
    }

    [Fact]
    public void Load_accepts_a_null_where_the_schema_permits_one_and_keeps_it_unauthorised()
    {
        var snapshot = ContentLoader.Load(ContentTestData.Valid()).Require();

        snapshot.Read($"{ContentTestData.TuningPath}#/merge/dustSubstituteCost").IsUnauthorised.ShouldBeTrue();
    }

    [Fact]
    public void Load_rejects_a_required_key_that_was_deleted()
    {
        var issues = Issues(ContentTestData.WithTuningEdit("\"_status\": \"partial\",", string.Empty));

        issues.ShouldContain(i => i.Code == ContentIssueCode.SchemaViolation);
    }

    // ------------------------------------------------------ the validator's own honesty

    [Fact]
    public void Load_fails_loudly_on_a_schema_keyword_the_validator_does_not_implement()
    {
        var issues = Issues(ContentTestData.With(ContentTestData.SchemaPath,
            ContentTestData.WidgetSchema.Replace(
                "\"minItems\": 1,",
                "\"minItems\": 1, \"contains\": { \"type\": \"object\" },",
                StringComparison.Ordinal)));

        issues.ShouldContain(i => i.Code == ContentIssueCode.UnsupportedSchemaKeyword);
    }

    /// <summary>
    /// 🔒 A <em>known</em> keyword written in the wrong shape. Every one of these degrades to a
    /// silent no-op without the eager shape check, and the worst of them —
    /// <c>"properties": []</c> — leaves the whole object unvalidated while the file reports clean.
    /// </summary>
    [Theory]
    // properties — an array applies no sub-schema at all.
    [InlineData(Limits, "\"limits\": { \"type\": \"object\", \"properties\": [] }")]
    // propertyNames — a boolean schema, which this validator does not implement.
    [InlineData(Limits, "\"limits\": { \"type\": \"object\", \"propertyNames\": true }")]
    // additionalProperties — neither a boolean nor a schema.
    [InlineData(Limits, "\"limits\": { \"type\": \"object\", \"additionalProperties\": \"integer\" }")]
    // required — a bare string iterates nothing.
    [InlineData("\"required\": [\"id\", \"rarity\", \"displayName\", \"requires\"]", "\"required\": \"id\"")]
    // items — draft-07's tuple form, skipped entirely by the walk.
    [InlineData("\"uniqueItems\": true, \"items\": { \"type\": \"integer\" }",
                "\"uniqueItems\": true, \"items\": [{ \"type\": \"integer\" }]")]
    // oneOf — an empty branch list can never match exactly one.
    [InlineData("\"oneOf\": [{ \"type\": \"integer\" }, { \"const\": \"FULL\" }]", "\"oneOf\": []")]
    // enum — a bare string enumerates nothing.
    [InlineData("\"enum\": [\"transcribed\", \"partial\", \"skeleton\"]", "\"enum\": \"partial\"")]
    // type — neither a type name nor an array of them.
    [InlineData("\"type\": \"integer\", \"multipleOf\": 5", "\"type\": 7, \"multipleOf\": 5")]
    // uniqueItems — the string "true" is not a boolean.
    [InlineData("\"uniqueItems\": true", "\"uniqueItems\": \"true\"")]
    // minimum — a quoted bound used to throw out of AsNumber at validation time.
    [InlineData("\"minimum\": 2", "\"minimum\": \"2\"")]
    // maxItems — a fractional count used to throw out of AsInt32.
    [InlineData("\"maxItems\": 3", "\"maxItems\": 3.5")]
    // maxProperties — a negative count.
    [InlineData("\"maxProperties\": 2", "\"maxProperties\": -1")]
    // pattern — a number is not a regular expression.
    [InlineData("\"pattern\": \"^WID_[A-Z0-9_]+$\"", "\"pattern\": 5")]
    public void Load_rejects_a_known_keyword_whose_value_is_the_wrong_shape(string find, string replaceWith)
    {
        var issues = Issues(ContentTestData.WithSchemaEdit(find, replaceWith));

        issues.ShouldContain(i => i.Code == ContentIssueCode.UnsupportedSchemaKeyword);
    }

    /// <summary>
    /// 🔒 The specific catastrophe, located. <c>"properties": []</c> on an object that declares no
    /// <c>additionalProperties</c> means <b>nothing under that object is validated at all</b>, and
    /// the file reports clean. The finding has to name the schema pointer, or the author is told
    /// only that something, somewhere, is unsupported.
    /// </summary>
    [Fact]
    public void A_properties_keyword_that_is_an_array_does_not_leave_its_object_silently_unvalidated()
    {
        var source = ContentTestData.WithSchemaEdit(
            Limits, "\"limits\": { \"type\": \"object\", \"properties\": [] }");

        Issues(source).ShouldContain(i =>
            i.Code == ContentIssueCode.UnsupportedSchemaKeyword &&
            i.Location == "schema/widgets.schema.json#/properties/merge/properties/limits/properties");
    }

    /// <summary>
    /// 🔒 One violation per enforced keyword. The list-equality test below can only see that a
    /// keyword is <em>claimed</em>; this is what sees whether it is <em>enforced</em>. Without it,
    /// deleting the <c>multipleOf</c> or <c>maxLength</c> block from the validator leaves every
    /// test green while a bound in the real schemas silently stops biting.
    /// </summary>
    [Theory]
    [InlineData("\"inputCount\": 3", "\"inputCount\": 9", ContentIssueCode.OutOfRange)]                    // maximum
    [InlineData("\"inputCount\": 3", "\"inputCount\": 1", ContentIssueCode.OutOfRange)]                    // minimum
    [InlineData("\"statBonusPerLevel\": 0.07", "\"statBonusPerLevel\": 0", ContentIssueCode.OutOfRange)]   // exclusiveMinimum
    [InlineData("\"ratio\": 0.5", "\"ratio\": 1", ContentIssueCode.OutOfRange)]                            // exclusiveMaximum
    [InlineData("\"batchSize\": 10", "\"batchSize\": 12", ContentIssueCode.OutOfRange)]                    // multipleOf
    [InlineData("\"tag\": \"anvil\"", "\"tag\": \"averylongtag\"", ContentIssueCode.OutOfRange)]           // maxLength
    [InlineData("\"tag\": \"anvil\"", "\"tag\": \"\"", ContentIssueCode.OutOfRange)]                       // minLength
    [InlineData("\"stoneCosts\": [2, 3]", "\"stoneCosts\": [2, 3, 4, 5]", ContentIssueCode.OutOfRange)]    // maxItems
    [InlineData("\"limits\": { \"perDay\": 3 }", "\"limits\": { \"a\": 1, \"b\": 2, \"c\": 3 }", ContentIssueCode.OutOfRange)] // maxProperties
    [InlineData("\"rule\": \"MAX_OF_REAL_INPUTS\"", "\"rule\": \"MIN_OF_REAL_INPUTS\"", ContentIssueCode.UnknownId)] // const
    [InlineData("\"topRarity\": \"SS\"", "\"topRarity\": \"SSS\"", ContentIssueCode.UnknownId)]             // enum
    [InlineData("\"id\": \"WID_ANVIL\"", "\"id\": \"anvil\"", ContentIssueCode.UnknownId)]                  // pattern
    [InlineData("\"payout\": 5", "\"payout\": 1.5", ContentIssueCode.SchemaViolation)]                     // oneOf
    [InlineData("\"openedAt\": \"2026-08-11T05:00:00Z\"", "\"openedAt\": \"the fifth\"", ContentIssueCode.SchemaViolation)] // format
    [InlineData("\"inputCount\": 3", "\"inputCount\": \"3\"", ContentIssueCode.SchemaViolation)]             // type
    public void Load_enforces_every_keyword_it_claims_to_support(
        string find, string replaceWith, ContentIssueCode expected)
    {
        Issues(ContentTestData.WithTuningEdit(find, replaceWith)).ShouldContain(i => i.Code == expected);
    }

    /// <summary>
    /// 🔒 Pins the <b>schema-layer</b> identity, not just the code. On an id-bearing collection
    /// <c>CheckIdSpaces</c> emits <c>DuplicateId</c> for the same edit, so deleting the
    /// <c>CheckUniqueItems</c> call — leaving the keyword in <c>SupportedKeywords</c>, so no
    /// <c>UnsupportedSchemaKeyword</c> fires either — left this green while <c>uniqueItems</c>
    /// silently stopped biting on every array in all 19 schemas.
    /// </summary>
    [Fact]
    public void Load_enforces_uniqueItems_so_a_repeated_collection_entry_cannot_pass()
    {
        Issues(ContentTestData.WithTuningEdit(
            "{ \"id\": \"WID_BELLOWS\", \"rarity\": \"B\", \"icon\": \"icon_bellows\", " +
            "\"displayName\": \"loc.widget.bellows.name\", \"requires\": \"WID_ANVIL\" }",
            "{ \"id\": \"WID_ANVIL\", \"rarity\": \"C\", \"icon\": \"icon_anvil\", " +
            "\"displayName\": \"loc.widget.anvil.name\", \"requires\": null }"))
            .ShouldContain(i =>
                i.Code == ContentIssueCode.DuplicateId &&
                i.Location == "tuning/widgets.json#/widgets/1" &&
                i.Message.Contains("is identical to item 0", StringComparison.Ordinal));
    }

    /// <summary>
    /// 🔒 The companion case, on an array with <b>no <c>id</c> members</b>. Several shipped arrays
    /// are like this — <c>neverPays</c>, <c>shrineForbiddenNodes</c>, <c>dungeons</c> — so
    /// <c>uniqueItems</c> is the only thing standing between them and a silently repeated entry.
    /// There is no invariant backstop to make this pass for the wrong reason.
    /// </summary>
    [Fact]
    public void Load_enforces_uniqueItems_on_an_array_that_has_no_invariant_backstop()
    {
        Issues(ContentTestData.WithTuningEdit("\"stoneCosts\": [2, 3]", "\"stoneCosts\": [2, 2]"))
            .ShouldContain(i =>
                i.Code == ContentIssueCode.DuplicateId &&
                i.Location == "tuning/widgets.json#/merge/stoneCosts/1" &&
                i.Message.Contains("is identical to item 0", StringComparison.Ordinal));
    }

    /// <summary>
    /// 🔒 <c>minProperties</c> was claimed by <c>SupportedKeywords</c> and enforced by nothing that
    /// any test could see. Delete the block from the validator and a locale file shipping
    /// <c>"strings": {}</c> validates clean — every UI string then renders as its own key, and the
    /// locale-parity rules are vacuous because both sides are empty.
    /// </summary>
    [Fact]
    public void Load_rejects_a_locale_file_whose_string_table_is_empty()
    {
        var issues = Issues(ContentTestData.With(ContentTestData.EnglishPath, """
        {
          "$schema": "../schema/loc.schema.json",
          "_locale": "en",
          "strings": {}
        }
        """));

        issues.ShouldContain(i =>
            i.Code == ContentIssueCode.OutOfRange && i.Location == "loc/en.json#/strings");
    }

    /// <summary>
    /// The claimed set. It can only ever see what the validator <em>claims</em>; every claim that
    /// is an assertion needs a case that proves it is <em>enforced</em>.
    /// </summary>
    /// <remarks>
    /// The audit of claimed-versus-enforced, as it stands:
    /// <list type="bullet">
    /// <item>Annotations, asserted by nothing on purpose: <c>$schema</c>, <c>$id</c>,
    /// <c>$comment</c>, <c>title</c>, <c>description</c>, <c>default</c>, <c>examples</c>,
    /// <c>deprecated</c>. Their <em>shape</em> is unconstrained; a value may legitimately be
    /// anything.</item>
    /// <item>Assertions covered by <c>Load_enforces_every_keyword_it_claims_to_support</c>:
    /// <c>minimum</c>, <c>maximum</c>, <c>exclusiveMinimum</c>, <c>exclusiveMaximum</c>,
    /// <c>multipleOf</c>, <c>minLength</c>, <c>maxLength</c>, <c>maxItems</c>,
    /// <c>maxProperties</c>, <c>const</c>, <c>enum</c>, <c>pattern</c>, <c>oneOf</c>,
    /// <c>format</c>, <c>type</c>.</item>
    /// <item>Assertions covered by a named case of their own: <c>required</c>, <c>minItems</c>,
    /// <c>additionalProperties</c>, <c>propertyNames</c>, <c>uniqueItems</c> (twice — once with an
    /// invariant backstop and once without), <c>minProperties</c>, <c>properties</c>,
    /// <c>patternProperties</c>, <c>items</c>, <c>$ref</c>, <c>$defs</c>.</item>
    /// <item>Every keyword's <em>value shape</em> is covered by
    /// <c>Load_rejects_a_known_keyword_whose_value_is_the_wrong_shape</c>.</item>
    /// </list>
    /// <c>minProperties</c> was the one claim nothing enforced. Adding a keyword here means adding
    /// a case to one of those lists in the same commit.
    /// </remarks>
    [Fact]
    public void SupportedKeywords_is_the_exact_set_this_validator_claims()
    {
        JsonSchemaValidator.SupportedKeywords.ShouldBe(
        [
            "$schema", "$id", "$comment", "$defs", "$ref",
            "title", "description", "default", "examples", "deprecated",
            "type", "enum", "const", "required",
            "properties", "patternProperties", "additionalProperties", "propertyNames",
            "minProperties", "maxProperties",
            "items", "minItems", "maxItems", "uniqueItems",
            "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf",
            "minLength", "maxLength", "pattern", "format",
            "oneOf",
        ],
        ignoreOrder: true);
    }
}
