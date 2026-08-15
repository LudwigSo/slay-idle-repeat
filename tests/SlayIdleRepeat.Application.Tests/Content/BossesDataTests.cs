using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// Tests <c>content/bosses/bosses.json</c> and <b>R35</b>, the rule that validates its embedded
/// effects against <c>schema/effect.schema.json</c>.
/// </summary>
/// <remarks>
/// Bosses embed effect JSON directly, unlike enemies/statuses which author named parameters —
/// right for a handful of elite modifiers and statuses, wrong for ~50 boss mechanics. R35 is what
/// makes the embedded form safe.
/// </remarks>
public sealed class BossesDataTests
{
    private const string Document = "content/bosses/bosses.json";

    private static ContentSnapshot Data() => ContentLoader.Load(RepoData.Source()).Require();

    [Fact]
    public void The_document_is_governed_by_its_own_schema()
    {
        ContentLayout.SchemaFor(Document).ShouldBe("schema/bosses.schema.json");

        ContentLoader.SchemasAwaitingContent.ShouldNotContain("schema/bosses.schema.json");
        ContentLoader.VocabularySchemas.ShouldNotContain("schema/bosses.schema.json");
    }

    /// <summary>The effect schema remains a vocabulary schema — it governs no file of its own, even now that bosses embed effects.</summary>
    [Fact]
    public void The_effect_schema_is_still_a_vocabulary_schema_governing_no_file()
    {
        ContentLoader.VocabularySchemas.ShouldContain(ContentInvariants.EffectSchemaPath);

        Data().DocumentPaths.ShouldNotContain(
            "schema/effect.schema.json", "a schema is never a document of the snapshot");
    }

    [Fact]
    public void The_table_carries_17_section_1_2s_nine_rows()
    {
        var scripts = Data().GetDocument(Document).Root;

        scripts.TryGetMember("scripts", out var rows).ShouldBeTrue();
        rows!.Items.Count.ShouldBe(9, "17 §1.2's table: the eight campaign bosses and BOSS_FTUE");
    }

    /// <summary>Reads the baseline secondaries through the snapshot rather than the Core-side reader, so a divergence between the two would surface here.</summary>
    [Fact]
    public void The_secondary_stats_are_17_section_1_2s_baseline()
    {
        var data = Data();

        data.ReadDouble(Document + "#/secondaryStats/crit").ShouldBe(0.05);
        data.ReadDouble(Document + "#/secondaryStats/critDamage").ShouldBe(0.50);
        data.ReadDouble(Document + "#/secondaryStats/dodge").ShouldBe(0.0);
        data.ReadDouble(Document + "#/secondaryStats/lifesteal").ShouldBe(0.0);
    }

    // ─────────────────────────────────────────────────────── R35

    /// <summary>
    /// R35 discovers its subjects structurally with no stated expected count, so a walk that
    /// silently matched nothing would report success just as loudly as one that validated everything.
    /// </summary>
    /// <remarks>
    /// ValidatedEmbeddedEffects is process-wide and never cleared, so it also holds every other
    /// test's loads; results are scoped to this document and checked against the file's own effect
    /// count rather than a hardcoded number.
    /// </remarks>
    [Fact]
    public void R35_validated_every_embedded_effect_in_the_shipped_content_set()
    {
        var snapshot = Data();

        var validated = ContentInvariants.ValidatedEmbeddedEffects
            .Where(e => e.StartsWith(Document, StringComparison.Ordinal))
            .ToArray();

        ContentInvariants.ValidatedEmbeddedEffects.ShouldAllBe(
            e => e.StartsWith(ContentLayout.ContentDirectory, StringComparison.Ordinal),
            "R35 walks content/ and nothing else — a tuning key innocently called 'op' is not an effect");

        var embedded = snapshot.GetDocument(Document).Root is var root &&
                       root.TryGetMember("scripts", out var scripts)
            ? scripts!.Items.Sum(s => s.TryGetMember("effects", out var e) ? e!.Items.Count : 0)
            : 0;

        embedded.ShouldBeGreaterThanOrEqualTo(
            50, "S3 — the eight fights of 17 §2-9 embed roughly fifty mechanics between them");

        validated.Length.ShouldBe(
            embedded, "R35 reaches every embedded effect in the file, not a prefix of them");
    }

    /// <summary>R35 refuses an embedded effect whose op-specific keys are wrong.</summary>
    /// <remarks>
    /// Two shapes: a key on an op that doesn't admit it, and a value on the one op that carries
    /// none. <c>bosses.schema.json</c> alone can't catch either — it only checks that an effect has
    /// an id and an op.
    /// </remarks>
    [Theory]
    // Anchors must stay single-line: find/replace can't span a line break on a CRLF checkout.
    [InlineData(
        "\"id\": \"BOSS_THORNMAW_P2_ROOT\",",
        "\"id\": \"BOSS_THORNMAW_P2_ROOT\", \"archetype\": \"SWARM\",",
        "an op-specific key on an op that does not admit it")]
    [InlineData(
        "\"op\": \"FORCE_CRIT_NEXT\",",
        "\"op\": \"FORCE_CRIT_NEXT\", \"value\": 3.0,",
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

    /// <summary>An embedded effect with no effect schema to check it against is a finding, not a silent skip.</summary>
    [Fact]
    public void R35_refuses_embedded_effects_when_the_effect_schema_is_absent()
    {
        var issues = ContentInvariants.Check(
            new Dictionary<string, ContentValue>(StringComparer.Ordinal)
            {
                [Document] = Data().GetDocument(Document).Root,
            },
            new Dictionary<string, IReadOnlyList<PatternBinding>>(StringComparer.Ordinal),
            new Dictionary<string, ContentValue>(StringComparer.Ordinal),
            ContentLoadOptions.Canonical);

        var missing = issues.Where(i => i.Code == ContentIssueCode.MissingSchema).ToArray();

        missing.ShouldHaveSingleItem().Location.ShouldBe(ContentInvariants.EffectSchemaPath);
        missing[0].Message.ShouldContain(
            "unchecked", Case.Insensitive, "the finding says what shipping without it would mean");
    }

    /// <summary>Negative control: with the schema present, no MissingSchema finding is produced.</summary>
    [Fact]
    public void R35_reports_nothing_when_the_effect_schema_is_present()
    {
        ContentLoader.Load(RepoData.Source()).Issues.ShouldBeEmpty(
            "the shipped content set validates, embedded effects included");
    }
}
