using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>Tests <c>content/perks/perks.json</c> and R35, the rule that validates its embedded effects against <c>schema/effect.schema.json</c>.</summary>
public sealed class PerksDataTests
{
    private const string Document = "content/perks/perks.json";

    private static ContentSnapshot Data() => ContentLoader.Load(RepoData.Source()).Require();

    [Fact]
    public void The_document_is_governed_by_its_own_schema()
    {
        ContentLayout.SchemaFor(Document).ShouldBe("schema/perk.schema.json");

        ContentLoader.SchemasAwaitingContent.ShouldNotContain("schema/perk.schema.json");
        ContentLoader.VocabularySchemas.ShouldNotContain("schema/perk.schema.json");
    }

    /// <summary>The effect schema remains a vocabulary schema — it governs no file of its own, even now that perks embed effects.</summary>
    [Fact]
    public void The_effect_schema_is_still_a_vocabulary_schema_governing_no_file()
    {
        ContentLoader.VocabularySchemas.ShouldContain(ContentInvariants.EffectSchemaPath);

        Data().DocumentPaths.ShouldNotContain(
            "schema/effect.schema.json", "a schema is never a document of the snapshot");
    }

    /// <summary>46 of the 82 standard-catalogue rows, a coverage-driven selection.</summary>
    [Fact]
    public void The_starter_catalogue_carries_46_rows()
    {
        var perks = Data().GetDocument(Document).Root;

        perks.TryGetMember("perks", out var rows).ShouldBeTrue();
        rows!.Items.Count.ShouldBe(46, "M3-07's coverage-driven selection off 06 §3's 82-row catalogue");
    }

    /// <summary>Every row carries exactly 3 tiers, numbered 1, 2, 3 in order.</summary>
    [Fact]
    public void Every_row_carries_exactly_3_tiers_in_order()
    {
        var perks = Data().GetDocument(Document).Root;
        perks.TryGetMember("perks", out var rows).ShouldBeTrue();

        foreach (var row in rows!.Items)
        {
            row.TryGetMember("tiers", out var tiers).ShouldBeTrue();
            tiers!.Items.Count.ShouldBe(3, "06 §1.1 — Tier I, II, III and no fourth");

            for (var i = 0; i < 3; i++)
            {
                tiers.Items[i].TryGetMember("tier", out var tierNumber).ShouldBeTrue();
                tierNumber!.AsInt32().ShouldBe(i + 1);
            }
        }
    }

    /// <summary>Every one of the 6 standard categories is represented.</summary>
    [Fact]
    public void All_6_standard_categories_are_represented()
    {
        var perks = Data().GetDocument(Document).Root;
        perks.TryGetMember("perks", out var rows).ShouldBeTrue();

        var categories = rows!.Items
            .Select(r => { r.TryGetMember("category", out var c); return c!.AsText("category"); })
            .ToHashSet(StringComparer.Ordinal);

        categories.ShouldBe(
            new[] { "OFFENSE", "DEFENSE", "SUSTAIN", "DICE_AND_BOARD", "ECONOMY", "TRIGGER_SYNERGY" },
            ignoreOrder: true);
    }

    /// <summary>Every one of the 4 rarities is represented.</summary>
    [Fact]
    public void All_4_rarities_are_represented()
    {
        var perks = Data().GetDocument(Document).Root;
        perks.TryGetMember("perks", out var rows).ShouldBeTrue();

        var rarities = rows!.Items
            .Select(r => { r.TryGetMember("rarity", out var c); return c!.AsText("rarity"); })
            .ToHashSet(StringComparer.Ordinal);

        rarities.ShouldBe(new[] { "COMMON", "RARE", "EPIC", "LEGENDARY" }, ignoreOrder: true);
    }

    // ─────────────────────────────────────────────────────── R35

    /// <summary>R35's floor, scoped to this document rather than read off the process-wide accumulator — see <c>BossesDataTests</c>' identical case for why.</summary>
    [Fact]
    public void R35_validated_every_embedded_effect_in_the_shipped_perk_catalogue()
    {
        var snapshot = Data();

        var validated = ContentInvariants.ValidatedEmbeddedEffects
            .Where(e => e.StartsWith(Document, StringComparison.Ordinal))
            .ToArray();

        var root = snapshot.GetDocument(Document).Root;
        root.TryGetMember("perks", out var rows).ShouldBeTrue();

        var embedded = rows!.Items.Sum(r =>
        {
            r.TryGetMember("tiers", out var tiers);
            return tiers!.Items.Sum(t =>
            {
                t.TryGetMember("effects", out var effects);
                return effects!.Items.Count;
            });
        });

        embedded.ShouldBe(196, "the shipped catalogue's own effect census — 46 rows x 3 tiers plus bonus clauses");
        validated.Length.ShouldBe(embedded, "R35 reaches every embedded effect in the file, not a prefix of them");
    }

    /// <summary>R35 refuses an embedded effect whose op-specific keys are wrong, naming the document, pointer, and rule.</summary>
    /// <remarks>
    /// The second case is the collision R35 had to be fixed for: a condition comparator is ALSO
    /// spelled <c>op</c>, and before the fix a well-formed condition was found and validated as if
    /// it were the top-level effect. This pins the genuine negative instead, so a regression in
    /// either direction is caught.
    /// </remarks>
    [Theory]
    [InlineData(
        "\"id\": \"PK_SHARP_EDGE_T1\", \"op\": \"STAT_ADD_PCT\", \"stat\": \"ATK\",",
        "\"id\": \"PK_SHARP_EDGE_T1\", \"op\": \"STAT_ADD_PCT\", \"stat\": \"ATK\", \"charges\": 2,",
        "an op-specific key (charges) on an op that does not admit it")]
    [InlineData(
        "\"id\": \"PK_FLURRY_T3_BONUS\", \"op\": \"FORCE_CRIT_NEXT\", \"charges\": 1,",
        "\"id\": \"PK_FLURRY_T3_BONUS\", \"op\": \"FORCE_CRIT_NEXT\", \"charges\": 1, \"value\": 3.0,",
        "a value on the one combat-flow op that carries none")]
    public void R35_refuses_a_malformed_embedded_effect(string find, string replaceWith, string why)
    {
        var result = ContentLoader.Load(RepoData.SourceWithEdit(Document, find, replaceWith));

        result.Snapshot.ShouldBeNull(why);

        var issue = result.Issues.ShouldHaveSingleItem();

        issue.Code.ShouldBe(ContentIssueCode.SchemaViolation, "S2 — which rule fired");
        issue.Location.ShouldStartWith(Document, Case.Sensitive, "which document");
        issue.Location.ShouldContain("/effects/", Case.Sensitive, "which embedded effect");
        issue.Message.ShouldContain("oneOf", Case.Sensitive, "18 §1's closed op-to-key partition");
    }

    /// <summary>
    /// Negative control: a condition object is refused at the top-level effect oneOf if it's ever
    /// reached there directly — the tripwire for R35's id+op signature regressing back to op-alone.
    /// </summary>
    [Fact]
    public void An_intentionally_malformed_condition_comparator_is_refused()
    {
        var result = ContentLoader.Load(RepoData.SourceWithEdit(
            Document,
            "\"id\": \"PK_EXECUTIONER_T1\", \"op\": \"STAT_ADD_PCT\", \"stat\": \"DMG_PCT\", \"trigger\": { \"kind\": \"ALWAYS\" }, \"condition\": { \"fn\": \"TARGET_HP_PCT\", \"op\": \"lt\", \"value\": 0.30 }",
            "\"id\": \"PK_EXECUTIONER_T1\", \"op\": \"STAT_ADD_PCT\", \"stat\": \"DMG_PCT\", \"trigger\": { \"kind\": \"ALWAYS\" }, \"condition\": { \"fn\": \"TARGET_HP_PCT\", \"op\": \"BOGUS_COMPARATOR\", \"value\": 0.30 }"));

        result.Snapshot.ShouldBeNull("BOGUS_COMPARATOR is not one of the 7 comparators 18 §4 fixes");
        result.Issues.ShouldNotBeEmpty();
    }
}
