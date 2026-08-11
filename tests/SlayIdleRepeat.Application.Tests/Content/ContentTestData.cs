using SlayIdleRepeat.Adapters.InMemory;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// A miniature but structurally faithful stand-in for <c>SlayIdleRepeat.Data</c>: one tuning file,
/// its schema, and a locale pair, following exactly the conventions
/// <c>SlayIdleRepeat.Data/README.md</c> states (<c>$schema</c>/<c>_doc</c>/<c>_status</c> meta keys,
/// <c>additionalProperties: false</c>, a <c>null</c> that means "unauthorised").
/// </summary>
/// <remarks>
/// Deliberately not the real files: a validator test that can only be understood by opening a
/// 200-line tuning file is a validator test nobody maintains. The real 16-file set is exercised
/// separately, end to end, by <c>RealDataSetTests</c>.
/// </remarks>
internal static class ContentTestData
{
    internal const string SchemaPath = "schema/widgets.schema.json";
    internal const string TuningPath = "tuning/widgets.json";
    internal const string LocSchemaPath = "schema/loc.schema.json";
    internal const string EnglishPath = "loc/en.json";
    internal const string GermanPath = "loc/de.json";

    internal const string WidgetSchema = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "$id": "https://slayidlerepeat.local/schema/widgets.schema.json",
      "title": "widgets.json",
      "description": "08 §4.1 — the miniature stand-in used by the content-pipeline tests.",
      "type": "object",
      "additionalProperties": false,
      "required": ["$schema", "_doc", "_status", "merge", "widgets"],
      "$defs": {
        "rarity": { "enum": ["C", "B", "A", "S", "SS"] },
        "nullableCost": { "type": ["integer", "null"], "minimum": 0 }
      },
      "properties": {
        "$schema": { "type": "string" },
        "_doc": { "type": "string", "minLength": 1 },
        "_status": { "enum": ["transcribed", "partial", "skeleton"] },
        "merge": {
          "description": "08 §4.1 — the merge block.",
          "type": "object",
          "additionalProperties": false,
          "$comment": "Every keyword SupportedKeywords claims is exercised by a property here, so a keyword that stopped being enforced fails a test instead of passing silently.",
          "required": ["inputCount", "statBonusPerLevel", "topRarity", "dustSubstituteCost", "perLevelSuccessRate",
                       "batchSize", "rule", "tag", "ratio", "payout", "stoneCosts", "openedAt", "limits"],
          "properties": {
            "inputCount": { "description": "08 §4.1 — inputs per merge.", "type": "integer", "minimum": 2, "maximum": 5 },
            "statBonusPerLevel": { "type": "number", "exclusiveMinimum": 0, "maximum": 1 },
            "topRarity": { "$ref": "#/$defs/rarity" },
            "dustSubstituteCost": { "$ref": "#/$defs/nullableCost" },
            "perLevelSuccessRate": { "type": ["array", "null"], "items": { "type": "number" } },
            "batchSize": { "type": "integer", "multipleOf": 5 },
            "rule": { "const": "MAX_OF_REAL_INPUTS" },
            "tag": { "type": "string", "minLength": 1, "maxLength": 8 },
            "ratio": { "type": "number", "exclusiveMaximum": 1 },
            "payout": { "oneOf": [{ "type": "integer" }, { "const": "FULL" }] },
            "stoneCosts": { "type": "array", "maxItems": 3, "items": { "type": "integer" } },
            "openedAt": { "type": "string", "format": "date-time" },
            "limits": { "type": "object", "maxProperties": 2, "additionalProperties": { "type": "integer" } }
          }
        },
        "widgets": {
          "description": "19 §1 — the widget catalogue.",
          "type": "array",
          "minItems": 1,
          "uniqueItems": true,
          "items": {
            "type": "object",
            "additionalProperties": false,
            "$comment": "icon is deliberately NOT required here: 14 §6's missing-icon class is a cross-file invariant, and a schema keyword that also caught it would hide whether that invariant works.",
            "required": ["id", "rarity", "displayName", "requires"],
            "properties": {
              "id": { "type": "string", "pattern": "^WID_[A-Z0-9_]+$" },
              "rarity": { "$ref": "#/$defs/rarity" },
              "icon": { "type": "string" },
              "displayName": { "type": "string", "pattern": "^loc(\\.[a-z0-9_]+)+$" },
              "requires": { "type": ["string", "null"], "pattern": "^WID_[A-Z0-9_]+$" }
            }
          }
        }
      }
    }
    """;

    internal const string WidgetTuning = """
    {
      "$schema": "../schema/widgets.schema.json",
      "_doc": "08 §4.1 — the miniature stand-in used by the content-pipeline tests.",
      "_status": "partial",
      "merge": {
        "inputCount": 3,
        "statBonusPerLevel": 0.07,
        "topRarity": "SS",
        "dustSubstituteCost": null,
        "perLevelSuccessRate": null,
        "batchSize": 10,
        "rule": "MAX_OF_REAL_INPUTS",
        "tag": "anvil",
        "ratio": 0.5,
        "payout": 5,
        "stoneCosts": [2, 3],
        "openedAt": "2026-08-11T05:00:00Z",
        "limits": { "perDay": 3 }
      },
      "widgets": [
        { "id": "WID_ANVIL", "rarity": "C", "icon": "icon_anvil", "displayName": "loc.widget.anvil.name", "requires": null },
        { "id": "WID_BELLOWS", "rarity": "B", "icon": "icon_bellows", "displayName": "loc.widget.bellows.name", "requires": "WID_ANVIL" }
      ]
    }
    """;

    internal const string LocSchema = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "$id": "https://slayidlerepeat.local/schema/loc.schema.json",
      "type": "object",
      "additionalProperties": false,
      "required": ["$schema", "_locale", "strings"],
      "properties": {
        "$schema": { "type": "string" },
        "_locale": { "enum": ["en", "de"] },
        "strings": {
          "type": "object",
          "minProperties": 1,
          "additionalProperties": false,
          "propertyNames": { "pattern": "^loc(\\.[a-z0-9_]+)+$" },
          "patternProperties": { "^loc(\\.[a-z0-9_]+)+$": { "type": "string", "minLength": 1 } }
        }
      }
    }
    """;

    internal const string English = """
    {
      "$schema": "../schema/loc.schema.json",
      "_locale": "en",
      "strings": {
        "loc.widget.anvil.name": "Anvil",
        "loc.widget.bellows.name": "Bellows"
      }
    }
    """;

    internal const string German = """
    {
      "$schema": "../schema/loc.schema.json",
      "_locale": "de",
      "strings": {
        "loc.widget.anvil.name": "##TODO_DE## Anvil",
        "loc.widget.bellows.name": "##TODO_DE## Bellows"
      }
    }
    """;

    /// <summary>A source holding the whole miniature data set, valid as it stands.</summary>
    internal static InMemoryContentSource Valid() =>
        new InMemoryContentSource()
            .Set(SchemaPath, WidgetSchema)
            .Set(TuningPath, WidgetTuning)
            .Set(LocSchemaPath, LocSchema)
            .Set(EnglishPath, English)
            .Set(GermanPath, German);

    /// <summary>The valid set with one document replaced — the shape every negative case takes.</summary>
    internal static InMemoryContentSource With(string documentPath, string replacementJson) =>
        Valid().Set(documentPath, replacementJson);

    /// <summary>
    /// The valid tuning file with one JSON fragment swapped for another — the cheapest way to write
    /// a single-edit mutation without restating the whole file.
    /// </summary>
    internal static InMemoryContentSource WithTuningEdit(string find, string replaceWith)
    {
        if (!WidgetTuning.Contains(find, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The mutation anchor '{find}' is not in the canonical test tuning file, so this " +
                "negative case would silently test nothing.");
        }

        return With(TuningPath, WidgetTuning.Replace(find, replaceWith, StringComparison.Ordinal));
    }

    /// <summary>The same single-edit mutation, against the <em>schema</em> rather than the data.</summary>
    internal static InMemoryContentSource WithSchemaEdit(string find, string replaceWith)
    {
        if (!WidgetSchema.Contains(find, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The mutation anchor '{find}' is not in the canonical test schema, so this " +
                "negative case would silently test nothing.");
        }

        return With(SchemaPath, WidgetSchema.Replace(find, replaceWith, StringComparison.Ordinal));
    }
}
