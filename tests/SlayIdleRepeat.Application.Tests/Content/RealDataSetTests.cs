using System.Text;
using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Application.Services.Content.Tunables;
using SlayIdleRepeat.ContentValidator;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The validator against the real <c>game-data</c>: 16 tuning files, 19 schemas, two
/// locales and 96 deliberate <c>null</c>s.
/// </summary>
/// <remarks>
/// A validator proven only against a 40-line fixture has not been proven. This is also the suite
/// that keeps the committed 📐 baseline honest — it fails both when a new mismatch appears and
/// when a baselined one is quietly fixed and the entry left behind.
/// </remarks>
public sealed partial class RealDataSetTests
{
    private const string BaselineRelativePath = "build/content/tunable-marker-baseline.json";
    private const string TrackerRelativePath = "IMPLEMENTATION_TRACKER.md";

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
    public void The_only_schemas_governing_nothing_are_the_two_whose_content_has_an_owner_and_a_milestone()
    {
        ContentLoader.SchemasAwaitingContent.ShouldBe(
        [
            // 14 §6 (the schema example) / 19 — content/chapters/*.json is authored by M2.
            "schema/chapter.schema.json",

            // 26 §2 — one live-ops event package. content/liveops_events/*.json is authored by M11.
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
    /// 🔒 The self-expiry, stated over the path <c>chapter.schema.json</c>'s own <c>title</c> claims
    /// (<c>content/chapters/*.json</c>) and <c>game-data/README.md</c> declares — not over
    /// a singular <c>content/chapter.json</c> that contradicts both and exists only because it was
    /// the one shape a stem-based pairing could reach.
    /// </summary>
    [Fact]
    public void An_exemption_that_outlived_its_milestone_fails_the_build()
    {
        var source = RepoData.Source().Set("content/chapters/CH_01_EMBERFALL.json", """
        { "$schema": "../../schema/chapter.schema.json" }
        """);

        ContentLoader.Load(source).Issues.ShouldContain(i =>
            i.Code == ContentIssueCode.OrphanSchema && i.Location == "schema/chapter.schema.json");
    }

    /// <summary>
    /// 🔒 Many files, one type schema. Under the stem rule each of these would demand
    /// <c>schema/CH_0n_….schema.json</c> — a <c>MissingSchema</c> per chapter the day M3-14 lands.
    /// </summary>
    [Fact]
    public void Every_file_in_a_content_directory_pairs_with_the_one_schema_for_that_content_type()
    {
        var source = RepoData.Source()
            .Set("content/chapters/CH_01_EMBERFALL.json", """{ "id": 1 }""")
            .Set("content/chapters/CH_02_DUSKMIRE.json", """{ "id": 2 }""");

        var issues = ContentLoader.Load(source).Issues;

        issues.ShouldNotContain(i => i.Code == ContentIssueCode.MissingSchema);
        issues.ShouldContain(i =>
            i.Code == ContentIssueCode.SchemaViolation &&
            i.Location.StartsWith("content/chapters/CH_02_DUSKMIRE.json#", StringComparison.Ordinal),
            "both files were validated against schema/chapter.schema.json, whose required keys they lack");
    }

    [Fact]
    public void The_shipped_data_set_still_carries_its_deliberate_unauthorised_holes()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.IsAuthorised("tuning/forge.json#/enhance/perLevelSuccessRate").ShouldBeFalse();
    }

    [Fact]
    public void Reading_a_shipped_unauthorised_tunable_throws_instead_of_producing_a_number()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        Action act = () => _ = snapshot.ReadDouble("tuning/forge.json#/enhance/perLevelSuccessRate");

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

    // ------------------------------------------------------------------- the 📐 check

    [Fact]
    public void The_tunable_marker_audit_passes_against_the_committed_baseline()
    {
        var report = RunAudit();

        report.Issues.ShouldBeEmpty(
            "the 📐 check fails on anything the dated baseline does not record, and equally on a " +
            "baseline entry that no longer describes a real mismatch");
    }

    [Fact]
    public void The_documentation_set_still_carries_markers_so_the_check_is_not_passing_vacuously()
    {
        Markers().Count().ShouldBeGreaterThan(40,
            "M0-10 counted 58 markers across 24 docs; a sudden collapse means the scanner broke, " +
            "not that the docs did");
    }

    /// <summary>
    /// 🔒 The document floor, not only the marker floor. <c>ScanMarkers</c> globs top-level
    /// <c>*.md</c> only, so moving a document into <c>game-design/archive/</c> takes its markers
    /// with it — silently, and with the marker count still comfortably over its floor.
    /// </summary>
    [Fact]
    public void The_marker_scan_still_reaches_the_whole_documentation_set()
    {
        Markers().Select(m => m.Section.DocId).Distinct().Count().ShouldBeGreaterThan(20,
            "M0-10 counted 58 markers across 24 docs; markers surviving in a handful of documents " +
            "means the glob stopped reaching the rest");
    }

    /// <summary>
    /// 🔒 The <em>same</em> predicate <see cref="TunableMarkerAudit.Run"/> applies. Guarding on
    /// <c>GovernsTuningFile</c> alone watches hundreds of citations while the audited set — which
    /// also requires <c>GovernsNumericKey</c> — could shrink to nothing behind a regression in
    /// <c>DeclaresANumber</c>.
    /// </summary>
    [Fact]
    public void The_tuning_schemas_still_carry_citations_so_the_reverse_direction_is_not_vacuous()
    {
        Citations().Where(c => c.GovernsTuningFile && c.GovernsNumericKey)
                   .Count().ShouldBeGreaterThan(30,
                       "the audited set is 37 numeric tuning citations today; a collapse toward " +
                       "zero is DeclaresANumber breaking, not the schemas losing their provenance");
    }

    [Fact]
    public void The_baseline_records_the_date_it_was_taken()
    {
        File.Exists(BaselinePath).ShouldBeTrue($"{BaselineRelativePath} is a committed deliverable of M0-09");
        Baseline().RecordedOn.ShouldMatch(@"^\d{4}-\d{2}-\d{2}$");
    }

    [Fact]
    public void Every_baseline_entry_carries_a_reason()
    {
        var entries = Entries();

        entries.ShouldNotBeEmpty();
        entries.ShouldAllBe(e => e.Reason.Length > 0,
            "a baseline without reasons is a place mismatches go to be forgotten");
    }

    /// <summary>
    /// 🔒 The two kinds are different facts. Spec debt has an owner; a scope exclusion has none,
    /// because nothing closes it.
    /// </summary>
    [Fact]
    public void Spec_debt_names_an_owner_and_a_scope_exclusion_does_not()
    {
        var entries = Entries();

        entries.ShouldNotBeEmpty();
        entries.ShouldAllBe(e => e.Kind == TunableBaselineKind.SpecDebt
            ? e.ClosedBy.Length > 0
            : e.ClosedBy.Length == 0);
    }

    /// <summary>
    /// 🔒 The assertion that would have caught <c>closedBy: "M0-11"</c> — a task that has never
    /// existed, naming a milestone that is complete — and equally the literal <c>"TODO"</c> that
    /// <c>--write-baseline</c> used to emit. The task list is <b>read from the tracker</b>, never
    /// restated here: a hard-coded copy would go stale in the same silence.
    /// </summary>
    [Fact]
    public void Every_spec_debt_entry_is_closed_by_a_task_that_exists_in_the_tracker()
    {
        var tracker = File.ReadAllText(Path.Combine(RepoData.RepositoryRoot, TrackerRelativePath));
        var known = TrackerTaskId().Matches(tracker).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);

        known.Count.ShouldBeGreaterThan(150,
            $"{TrackerRelativePath} lists every milestone task; finding almost none means the " +
            "id pattern broke, not that the tracker emptied");

        foreach (var entry in Entries().Where(e => e.Kind == TunableBaselineKind.SpecDebt))
        {
            entry.ClosedBy.ShouldMatch(@"^M\d+-\d+[a-z]?$",
                $"the 📐 baseline entry for {entry.Section} must name a milestone task id");

            known.ShouldContain(entry.ClosedBy,
                $"the 📐 baseline entry for {entry.Section} is closed by a task that has to exist. " +
                "Whoever reaches that task and deletes the entry as instructed turns content " +
                "validation red on a mismatch that is still real.");
        }
    }

    /// <summary>
    /// The count the header prose states. It said "four" while seven entries carried it — a file
    /// that miscounts its own conspicuous exceptions is not being read.
    /// </summary>
    [Fact]
    public void The_baseline_carries_exactly_seven_permanent_scope_exclusions()
    {
        Entries().Count(e => e.Kind == TunableBaselineKind.OutOfScope).ShouldBe(7);
    }

    private static IReadOnlyList<TunableBaselineEntry> Entries()
    {
        var baseline = Baseline();
        return baseline.UnmatchedMarkers.Concat(baseline.UnmarkedSchemaCitations).ToArray();
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\bM\d+-\d+[a-z]?\b")]
    private static partial System.Text.RegularExpressions.Regex TrackerTaskId();

    /// <summary>
    /// 🔒 The SAME composition `tools/ContentValidator` runs. Re-assembling the wiring inside the
    /// test would prove the algorithm and leave the tool's doc glob, its governsTuningFile
    /// derivation and its baseline path unexercised — all of which can break with this green.
    /// </summary>
    private static TunableAuditReport RunAudit() =>
        TunableAuditComposition.Run(RepoData.DataRoot, RepoData.DesignDocsRoot, BaselinePath);

    private static IReadOnlyList<TunableMarker> Markers() =>
        TunableAuditComposition.ScanMarkers(RepoData.DesignDocsRoot);

    private static IReadOnlyList<SchemaCitation> Citations() =>
        TunableAuditComposition.ScanCitations(
            RepoData.DataRoot, TunableAuditComposition.TuningFileNames(RepoData.DataRoot));

    private static TunableBaseline Baseline() => TunableAuditComposition.ReadBaseline(BaselinePath);

    private static string BaselinePath =>
        Path.Combine(RepoData.RepositoryRoot, BaselineRelativePath);
}
