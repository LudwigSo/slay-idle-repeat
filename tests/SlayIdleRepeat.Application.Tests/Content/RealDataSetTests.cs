using System.Text;
using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The validator against the real <c>game-data</c>: 16 tuning files, 21 schemas, two
/// locales and 96 deliberate <c>null</c>s.
/// </summary>
/// <remarks>
/// A validator proven only against a 40-line fixture has not been proven.
/// </remarks>
public sealed partial class RealDataSetTests
{
    [Fact]
    public void The_shipped_data_set_validates_with_no_issues()
    {
        var result = ContentLoader.Load(RepoData.Source());

        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void The_shipped_data_set_produces_a_snapshot()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.DocumentPaths.ShouldNotBeEmpty();
    }

    [Fact]
    public void Every_tuning_file_of_the_21_section_3_1_catalogue_is_in_the_snapshot()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.DocumentPaths
            .Where(p => p.StartsWith("tuning/", StringComparison.Ordinal))
            .Count().ShouldBe(16, "21 §3.1 catalogues exactly sixteen tuning files");
    }

    [Fact]
    public void The_only_schema_governing_nothing_is_the_one_whose_content_has_an_owner_and_a_milestone()
    {
        // schema/chapter.schema.json left this list on the commit that landed
        // content/chapters/CH_01_GREENWOOD_VALE.json and CH_02_ASHEN_MIRE.json (M3-14); chapters
        // 3-8 remain unauthored and are M11-02's rows, not a second exemption here.
        ContentLoader.SchemasAwaitingContent.ShouldBe(
        [
            // 26 §2 — one live-ops event package. content/liveops_events/*.json is authored by M13-06.
            "schema/event.schema.json",
        ]);
    }

    /// <summary>
    /// 🔒 The second, separate exemption: a schema that describes a SHAPE rather than a file, and
    /// therefore governs nothing permanently rather than temporarily.
    /// </summary>
    /// <remarks>
    /// Pinned as its own list because the two claims are different and only one of them expires.
    /// Adding an entry here is a decision that a schema will never govern a file — which is a much
    /// stronger statement than "its content has not been authored yet", and one that deserves to be
    /// made somewhere a reviewer will see it.
    /// </remarks>
    [Fact]
    public void The_only_schema_that_describes_a_shape_rather_than_a_file_is_the_effect_vocabulary()
    {
        ContentLoader.VocabularySchemas.ShouldBe(["schema/effect.schema.json"]);

        ContentLoader.VocabularySchemas.ShouldNotContain(
            s => ContentLoader.SchemasAwaitingContent.Contains(s, StringComparer.Ordinal),
            "the two lists make opposite claims; ContentLoader.Pair reports a schema in both");
    }

    /// <summary>
    /// 🔒 A vocabulary schema that starts governing a data file fails the build, the same way an
    /// awaiting-content one does when its content lands. Different message, because it means
    /// something different: the file is misnamed, or the schema has quietly become a content type.
    /// </summary>
    [Fact]
    public void A_vocabulary_schema_that_starts_governing_a_file_fails_the_build()
    {
        var source = RepoData.Source().Set("effect.json", """
        { "id": "PK_X", "op": "EXTRA_ATTACK", "value": 1 }
        """);

        ContentLoader.Load(source).Issues.ShouldContain(i =>
            i.Code == ContentIssueCode.OrphanSchema && i.Location == "schema/effect.schema.json");
    }

    /// <summary>
    /// 🔒 The self-expiry, stated over the path <c>event.schema.json</c>'s own <c>title</c> claims
    /// (<c>content/liveops_events/*.json</c>) and <c>game-data/README.md</c> declares. Exercised
    /// against <c>event.schema.json</c> rather than <c>chapter.schema.json</c> now that the latter's
    /// exemption has itself expired for real — M3-14 landed content/chapters/CH_01_GREENWOOD_VALE.json
    /// and CH_02_ASHEN_MIRE.json, so <c>ContentLoader.SchemasAwaitingContent</c> no longer names it —
    /// but the mechanism this case pins is general, not specific to either schema.
    /// </summary>
    [Fact]
    public void An_exemption_that_outlived_its_milestone_fails_the_build()
    {
        var source = RepoData.Source().Set("content/liveops_events/EVT_FIRST.json", """
        { "$schema": "../../schema/event.schema.json" }
        """);

        ContentLoader.Load(source).Issues.ShouldContain(i =>
            i.Code == ContentIssueCode.OrphanSchema && i.Location == "schema/event.schema.json");
    }

    /// <summary>
    /// 🔒 Many files, one type schema. Under the stem rule each of these would demand
    /// <c>schema/CH_0n_….schema.json</c> — a <c>MissingSchema</c> per chapter, and the real content
    /// set (M3-14's <c>CH_01_GREENWOOD_VALE.json</c> / <c>CH_02_ASHEN_MIRE.json</c>) proves the
    /// directory-pairing rule holds for more than one file per type alongside a malformed sibling.
    /// </summary>
    [Fact]
    public void Every_file_in_a_content_directory_pairs_with_the_one_schema_for_that_content_type()
    {
        var source = RepoData.Source()
            .Set("content/chapters/CH_03_EMBERFALL.json", """{ "id": 3 }""")
            .Set("content/chapters/CH_04_DUSKMIRE.json", """{ "id": 4 }""");

        var issues = ContentLoader.Load(source).Issues;

        issues.ShouldNotContain(i => i.Code == ContentIssueCode.MissingSchema);
        issues.ShouldContain(i =>
            i.Code == ContentIssueCode.SchemaViolation &&
            i.Location.StartsWith("content/chapters/CH_04_DUSKMIRE.json#", StringComparison.Ordinal),
            "both files were validated against schema/chapter.schema.json, whose required keys they lack");
    }

    [Fact]
    public void The_shipped_data_set_still_carries_its_deliberate_unauthorised_holes()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        // 🔴 The subject was perLevelSuccessRate until M4-04 discharged it: the interpolation
        // between the two published band endpoints turned out to be the document's own ramp
        // notation, so the ladder is a function of numbers already authored rather than a hole.
        // The remaining forge hole is a stronger example anyway — it is an authored n/a rather than
        // an undecided number, and nothing merges out of the top rung for it to price.
        snapshot.IsAuthorised("tuning/forge.json#/merge/dustSubstituteCost/SS").ShouldBeFalse();
    }

    [Fact]
    public void Reading_a_shipped_unauthorised_tunable_throws_instead_of_producing_a_number()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        Action act = () => _ = snapshot.ReadInt64("tuning/forge.json#/merge/dustSubstituteCost/SS");

        Should.Throw<Core.Content.UnauthorisedTunableException>(act);
    }

    [Fact]
    public void A_single_out_of_range_edit_to_a_shipped_tuning_file_is_rejected()
    {
        var source = RepoData.SourceWithEdit(
            "tuning/forge.json", "\"statBonusPerLevel\": 0.07", "\"statBonusPerLevel\": 7");

        ContentLoader.Load(source).Issues.ShouldContain(i => i.Code == ContentIssueCode.OutOfRange);
    }

    [Fact]
    public void A_single_unknown_id_edit_to_a_shipped_tuning_file_is_rejected()
    {
        var source = RepoData.SourceWithEdit(
            "tuning/forge.json", "\"currency\": \"MERGE_DUST\"", "\"currency\": \"MERGE_DUSTT\"");

        ContentLoader.Load(source).Issues.ShouldContain(i => i.Code == ContentIssueCode.UnknownId);
    }

    // -------------------------------------------------------------- 16 D20 · the ship gate

    /// <summary>
    /// 🔒 The gate <c>game-data/README.md</c> and <c>schema/loc.schema.json</c> both
    /// declare 🔒 and neither implemented: <em>"A build that ships to players must fail while any
    /// sentinel remains."</em> All 82 DE values are sentinels, so this is what stops `16` D20's
    /// "nothing machine-translated reaches a player" from being a sentence nobody enforces.
    /// </summary>
    [Fact]
    public void A_shipping_build_fails_while_any_German_string_is_still_a_sentinel()
    {
        var issues = ContentLoader.Load(RepoData.Source(), ContentLoadOptions.Shipping).Issues;

        issues.ShouldContain(i =>
            i.Code == ContentIssueCode.LocalisationMismatch &&
            i.Location == "loc/de.json#/strings/loc.currency.gold.name");
    }

    /// <summary>
    /// And the other state, which is the one M0-M16 run in. The gate has to be off by default or
    /// every milestone before the translation pass fails on purpose and gets switched off for real.
    /// </summary>
    [Fact]
    public void A_development_build_does_not_fail_on_the_sentinels_it_is_supposed_to_still_have()
    {
        ContentLoader.Load(RepoData.Source(), ContentLoadOptions.Canonical).Issues.ShouldBeEmpty();
    }

    // ------------------------------------------------- the declared rules are still alive

    /// <summary>
    /// 🔒 <c>DeclaredRules.Find</c> returns <c>null</c> for two different facts: "the document is
    /// absent", which is correct and is why a rule waiting on M2's content is vacuous rather than
    /// switched off, and "the pointer is a typo in a document that is present", which disables the
    /// rule in silence. <c>The_shipped_data_set_validates_with_no_issues</c> passes <em>hardest</em>
    /// when every rule is dead, so nothing above catches the second.
    /// </summary>
    [Fact]
    public void Every_pointer_the_declared_rules_look_up_resolves_against_the_shipped_data()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        var references = ContentInvariants.DeclaredRuleReferences;

        references.Count.ShouldBeGreaterThan(120,
            "the declared rules resolve well over a hundred pointers today; a collapse means the " +
            "rules stopped running, not that the design docs stopped stating them");

        foreach (var reference in references)
        {
            snapshot.TryRead(reference, out _).ShouldBeTrue(
                $"the rule that names '{reference}' resolves it today. A pointer that stops " +
                "resolving does not fail — it makes its rule vacuous, and the data set then " +
                "validates more cleanly than before.");
        }
    }
}
