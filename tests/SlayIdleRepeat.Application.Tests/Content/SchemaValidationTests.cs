using FluentAssertions;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// `14` §6 🔒 — <em>"JSON is validated at build time against schemas in
/// <c>SlayIdleRepeat.Data/schema/</c>. The build fails on unknown IDs, missing icons,
/// out-of-range values, orphaned references or duplicate IDs."</em>
/// </summary>
/// <remarks>
/// Every one of those five classes gets a case here that proves the validator actually rejects it.
/// A validator whose negative cases were never committed is a validator nobody can trust after the
/// first refactor.
/// </remarks>
public sealed class SchemaValidationTests
{
    private static IReadOnlyList<ContentIssue> Issues(Adapters.InMemory.InMemoryContentSource source) =>
        ContentLoader.Load(source).Issues;

    // ---------------------------------------------------------------- 14 §6: unknown IDs

    [Fact]
    public void Load_rejects_an_enum_member_the_schema_does_not_know()
    {
        var issues = Issues(ContentTestData.WithTuningEdit("\"topRarity\": \"SS\"", "\"topRarity\": \"SSS\""));

        issues.Should().Contain(i => i.Code == ContentIssueCode.UnknownId);
    }

    [Fact]
    public void Load_rejects_an_id_that_does_not_match_its_pattern()
    {
        var issues = Issues(ContentTestData.WithTuningEdit("\"id\": \"WID_ANVIL\"", "\"id\": \"anvil\""));

        issues.Should().Contain(i => i.Code == ContentIssueCode.UnknownId);
    }

    [Fact]
    public void Load_rejects_a_property_the_schema_does_not_declare()
    {
        var issues = Issues(ContentTestData.WithTuningEdit(
            "\"inputCount\": 3", "\"inputCount\": 3, \"inputCountt\": 3"));

        issues.Should().Contain(i => i.Code == ContentIssueCode.UnknownId);
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

        issues.Should().Contain(i => i.Code == ContentIssueCode.UnknownId);
    }

    // ------------------------------------------------------------ 14 §6: out-of-range values

    [Fact]
    public void Load_rejects_an_integer_above_its_maximum()
    {
        var issues = Issues(ContentTestData.WithTuningEdit("\"inputCount\": 3", "\"inputCount\": 6"));

        issues.Should().Contain(i => i.Code == ContentIssueCode.OutOfRange);
    }

    [Fact]
    public void Load_rejects_an_integer_below_its_minimum()
    {
        var issues = Issues(ContentTestData.WithTuningEdit("\"inputCount\": 3", "\"inputCount\": 1"));

        issues.Should().Contain(i => i.Code == ContentIssueCode.OutOfRange);
    }

    [Fact]
    public void Load_rejects_a_value_at_an_exclusive_minimum()
    {
        var issues = Issues(ContentTestData.WithTuningEdit(
            "\"statBonusPerLevel\": 0.07", "\"statBonusPerLevel\": 0"));

        issues.Should().Contain(i => i.Code == ContentIssueCode.OutOfRange);
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

        issues.Should().Contain(i => i.Code == ContentIssueCode.OutOfRange);
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

        issues.Should().Contain(i => i.Code == ContentIssueCode.DuplicateId);
    }

    [Fact]
    public void Load_rejects_the_same_id_declared_twice_with_different_bodies()
    {
        var issues = Issues(ContentTestData.WithTuningEdit(
            "\"id\": \"WID_BELLOWS\"", "\"id\": \"WID_ANVIL\""));

        issues.Should().Contain(i => i.Code == ContentIssueCode.DuplicateId);
    }

    [Fact]
    public void Load_rejects_a_duplicate_property_name_in_a_data_file()
    {
        var issues = Issues(ContentTestData.WithTuningEdit(
            "\"inputCount\": 3", "\"inputCount\": 3, \"inputCount\": 4"));

        issues.Should().Contain(i => i.Code == ContentIssueCode.DuplicateKey);
    }

    // ------------------------------------------------------- 14 §6: orphaned references

    [Fact]
    public void Load_rejects_a_reference_to_an_id_that_is_declared_nowhere()
    {
        var issues = Issues(ContentTestData.WithTuningEdit(
            "\"requires\": \"WID_ANVIL\"", "\"requires\": \"WID_TONGS\""));

        issues.Should().Contain(i => i.Code == ContentIssueCode.OrphanedReference);
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

        issues.Should().Contain(i => i.Code == ContentIssueCode.LocalisationMismatch);
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

        Issues(source).Should().Contain(i => i.Code == ContentIssueCode.OrphanedReference);
    }

    // ----------------------------------------------------------- 14 §6: missing icons

    [Fact]
    public void Load_rejects_a_definition_whose_siblings_declare_an_icon_and_it_does_not()
    {
        var issues = Issues(ContentTestData.WithTuningEdit(
            "\"icon\": \"icon_bellows\", ", string.Empty));

        issues.Should().Contain(i => i.Code == ContentIssueCode.MissingIcon);
    }

    [Fact]
    public void Load_rejects_an_empty_icon_name()
    {
        var issues = Issues(ContentTestData.WithTuningEdit("\"icon\": \"icon_bellows\"", "\"icon\": \"\""));

        issues.Should().Contain(i => i.Code == ContentIssueCode.MissingIcon);
    }

    // ------------------------------------------------------------ null is not a default

    [Fact]
    public void Load_rejects_a_null_where_the_schema_does_not_permit_one()
    {
        var issues = Issues(ContentTestData.WithTuningEdit("\"inputCount\": 3", "\"inputCount\": null"));

        issues.Should().Contain(i => i.Code == ContentIssueCode.SchemaViolation);
    }

    [Fact]
    public void Load_accepts_a_null_where_the_schema_permits_one_and_keeps_it_unauthorised()
    {
        var snapshot = ContentLoader.Load(ContentTestData.Valid()).Require();

        snapshot.Read($"{ContentTestData.TuningPath}#/merge/dustSubstituteCost").IsUnauthorised.Should().BeTrue();
    }

    [Fact]
    public void Load_rejects_a_required_key_that_was_deleted()
    {
        var issues = Issues(ContentTestData.WithTuningEdit("\"_status\": \"partial\",", string.Empty));

        issues.Should().Contain(i => i.Code == ContentIssueCode.SchemaViolation);
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

        issues.Should().Contain(i => i.Code == ContentIssueCode.UnsupportedSchemaKeyword);
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
        Issues(ContentTestData.WithTuningEdit(find, replaceWith)).Should().Contain(i => i.Code == expected);
    }

    [Fact]
    public void Load_enforces_uniqueItems_so_a_repeated_collection_entry_cannot_pass()
    {
        Issues(ContentTestData.WithTuningEdit(
            "{ \"id\": \"WID_BELLOWS\", \"rarity\": \"B\", \"icon\": \"icon_bellows\", " +
            "\"displayName\": \"loc.widget.bellows.name\", \"requires\": \"WID_ANVIL\" }",
            "{ \"id\": \"WID_ANVIL\", \"rarity\": \"C\", \"icon\": \"icon_anvil\", " +
            "\"displayName\": \"loc.widget.anvil.name\", \"requires\": null }"))
            .Should().Contain(i => i.Code == ContentIssueCode.DuplicateId);
    }

    [Fact]
    public void SupportedKeywords_is_the_exact_set_this_validator_claims()
    {
        JsonSchemaValidator.SupportedKeywords.Should().BeEquivalentTo(
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
        ]);
    }
}
