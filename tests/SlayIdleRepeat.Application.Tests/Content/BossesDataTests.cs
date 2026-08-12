using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// 🔒 `17` §1.2 and §2-9 — <c>content/bosses/bosses.json</c> as the content pipeline sees it, and
/// <b>R35</b>, the cross-file rule that validates its embedded effects against
/// <c>schema/effect.schema.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// The transcription of the coefficient rows and the engine's eight authoring rules are asserted in
/// <c>SlayIdleRepeat.Core.Tests</c>, against the same file embedded there. What is here is what only
/// the pipeline can say: the file pairs with its own schema, its effects survive the real effect
/// schema, and the rule that applies that schema bites.
/// </para>
/// <para>
/// 🔴 <b>This is the first content file in the repository that embeds effect JSON.</b>
/// <c>enemies.json</c> and <c>statuses.json</c> author named parameter numbers instead, which is
/// right for 8 elite modifiers and 12 statuses and wrong for ~50 boss mechanics — per-mechanic
/// branching would become the path of least resistance, and that is the bespoke boss code `17` §11
/// claims does not exist. So the embedded form is the one that had to be made safe, and R35 is how.
/// </para>
/// </remarks>
public sealed class BossesDataTests
{
    private const string Document = "content/bosses/bosses.json";

    private static ContentSnapshot Data() => ContentLoader.Load(RepoData.Source()).Require();

    /// <summary>Every new content type asserts its own pairing (`14` §6).</summary>
    [Fact]
    public void The_document_is_governed_by_its_own_schema()
    {
        ContentLayout.SchemaFor(Document).ShouldBe("schema/bosses.schema.json");

        ContentLoader.SchemasAwaitingContent.ShouldNotContain("schema/bosses.schema.json");
        ContentLoader.VocabularySchemas.ShouldNotContain("schema/bosses.schema.json");
    }

    /// <summary>
    /// 🔒 `18` §1 / M2-01 — the effect schema stays a <b>vocabulary</b> schema. It governs no file of
    /// its own, and authoring bosses did not make it one.
    /// </summary>
    /// <remarks>
    /// The temptation R19′ closes is to give the effect schema a data file, or to let the boss schema
    /// restate it. Both are refused elsewhere; this pins the first.
    /// </remarks>
    [Fact]
    public void The_effect_schema_is_still_a_vocabulary_schema_governing_no_file()
    {
        ContentLoader.VocabularySchemas.ShouldContain(ContentInvariants.EffectSchemaPath);

        Data().DocumentPaths.ShouldNotContain(
            "schema/effect.schema.json", "a schema is never a document of the snapshot");
    }

    /// <summary>🔒 `17` §1.2 — nine rows, and the shipped file really carries them.</summary>
    [Fact]
    public void The_table_carries_17_section_1_2s_nine_rows()
    {
        var scripts = Data().GetDocument(Document).Root;

        scripts.TryGetMember("scripts", out var rows).ShouldBeTrue();
        rows!.Items.Count.ShouldBe(9, "17 §1.2's table: the eight campaign bosses and BOSS_FTUE");
    }

    /// <summary>
    /// 🔒 `17` §1.2's four baseline secondaries, read through the snapshot rather than the Core-side
    /// reader — so a divergence between the two ways of reading this file is visible.
    /// </summary>
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
    /// 🔒 <b>R35</b>, the floor (steering S3). The rule discovers its subjects structurally, so
    /// nothing in it says how many effects it <em>ought</em> to have seen — and a walk that silently
    /// matched nothing would report success exactly as loudly as one that validated every boss
    /// mechanic in the repository.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The subject is the set this load produced, not the accumulator's contents.</b>
    /// <c>ValidatedEmbeddedEffects</c> is process-wide and is never cleared, so by the time this case
    /// runs it already holds whatever every other class in the assembly loaded — and a floor read
    /// straight off it would hold even if <em>this</em> load validated nothing at all. The set is
    /// therefore captured before and compared after, and the count is asserted against the file's own
    /// effect census rather than against a number written twice.
    /// </remarks>
    [Fact]
    public void R35_validated_every_embedded_effect_in_the_shipped_content_set()
    {
        var snapshot = Data();

        // 🔴 Scoped to THIS document, not read off the accumulator whole. ValidatedEmbeddedEffects is
        // process-wide and never cleared, so it holds every earlier load in the assembly — an
        // equality against its total would turn red the first time any other test loads a content set
        // with an embedded effect at a new pointer, for a reason with nothing to do with R35.
        var validated = ContentInvariants.ValidatedEmbeddedEffects
            .Where(e => e.StartsWith(Document, StringComparison.Ordinal))
            .ToArray();

        ContentInvariants.ValidatedEmbeddedEffects.ShouldAllBe(
            e => e.StartsWith(ContentLayout.ContentDirectory, StringComparison.Ordinal),
            "R35 walks content/ and nothing else — a tuning key innocently called 'op' is not an effect");

        // What the file actually carries — the count nothing else in the repository states, so the
        // rule's reach is compared against the data instead of against a literal.
        var embedded = snapshot.GetDocument(Document).Root is var root &&
                       root.TryGetMember("scripts", out var scripts)
            ? scripts!.Items.Sum(s => s.TryGetMember("effects", out var e) ? e!.Items.Count : 0)
            : 0;

        embedded.ShouldBeGreaterThanOrEqualTo(
            50, "S3 — the eight fights of 17 §2-9 embed roughly fifty mechanics between them");

        validated.Length.ShouldBe(
            embedded, "R35 reaches every embedded effect in the file, not a prefix of them");
    }

    /// <summary>
    /// 🔒 <b>R35</b> bites — an embedded effect whose op-specific keys are wrong is refused, and the
    /// finding names the document, the pointer and the rule.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>Two shapes, because they fail the <c>oneOf</c> for different reasons.</b> The first puts
    /// a key on an op that does not admit it — the partition's whole purpose. The second authors a
    /// <c>value</c> on the one op that carries none, which is the case R21 depends on: `18` §2.4
    /// splits that op out precisely so that <c>{charges, value}</c> cannot be written, because it
    /// reads as "three attacks" to whoever wrote it.
    /// </para>
    /// <para>
    /// Neither is reachable through <c>bosses.schema.json</c> alone: it constrains an embedded effect
    /// to <em>an object with an id and an op</em> and says nothing about which keys go with which op,
    /// because it may neither <c>$ref</c> the effect schema nor restate it. So a green result here
    /// with R35 removed would be the exact silent gap the rule exists to close.
    /// </para>
    /// </remarks>
    [Theory]
    // ⚠️ Single-line anchors. RepoData reads the file with its own line endings, so a `find` that
    // spans a line break matches nothing on a CRLF checkout — which its own guard reports rather
    // than letting the case silently test nothing. It caught exactly that here.
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

    /// <summary>
    /// 🔒 <b>R35</b>'s honest failure mode: an embedded effect with no effect schema to check it
    /// against is a <em>finding</em>, not a skip.
    /// </summary>
    /// <remarks>
    /// 🔴 Every other rule in <c>DeclaredRules</c> is vacuous when its documents are absent, because
    /// an absent document means there is nothing to disagree about. That reading does not transfer
    /// here: the subject is present and the <b>authority</b> is missing, so skipping would report
    /// success over unvalidated content. This is the case that proves the rule did not take the easy
    /// reading.
    /// </remarks>
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

    /// <summary>
    /// 🔒 The negative control for the case above: with the schema present, the same documents
    /// produce no <c>MissingSchema</c> finding at all.
    /// </summary>
    /// <remarks>
    /// Without this, "a MissingSchema was reported" is equally consistent with the rule reporting one
    /// unconditionally — which would be a rule that cannot pass rather than one that cannot fail, and
    /// just as useless.
    /// </remarks>
    [Fact]
    public void R35_reports_nothing_when_the_effect_schema_is_present()
    {
        ContentLoader.Load(RepoData.Source()).Issues.ShouldBeEmpty(
            "the shipped content set validates, embedded effects included");
    }
}
