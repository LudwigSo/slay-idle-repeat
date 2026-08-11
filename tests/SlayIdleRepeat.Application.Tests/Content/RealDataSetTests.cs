using System.Text;
using FluentAssertions;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Application.Services.Content.Tunables;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The validator against the real <c>SlayIdleRepeat.Data</c>: 16 tuning files, 19 schemas, two
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

        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void The_shipped_data_set_produces_a_snapshot()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.DocumentPaths.Should().NotBeEmpty();
    }

    [Fact]
    public void Every_tuning_file_of_the_21_section_3_1_catalogue_is_in_the_snapshot()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.DocumentPaths
            .Where(p => p.StartsWith("tuning/", StringComparison.Ordinal))
            .Should().HaveCount(16, "21 §3.1 catalogues exactly sixteen tuning files");
    }

    [Fact]
    public void The_only_schemas_governing_nothing_are_the_two_whose_content_has_an_owner_and_a_milestone()
    {
        ContentLoader.SchemasAwaitingContent.Should().Equal(
        [
            // 14 §6 (the schema example) / 19 — content/chapters/*.json is authored by M2.
            "schema/chapter.schema.json",

            // 26 §2 — one live-ops event package. content/liveops_events/*.json is authored by M11.
            "schema/event.schema.json",
        ]);
    }

    /// <summary>
    /// 🔒 The self-expiry, stated over the path <c>chapter.schema.json</c>'s own <c>title</c> claims
    /// (<c>content/chapters/*.json</c>) and <c>SlayIdleRepeat.Data/README.md</c> declares — not over
    /// a singular <c>content/chapter.json</c> that contradicts both and exists only because it was
    /// the one shape a stem-based pairing could reach.
    /// </summary>
    [Fact]
    public void An_exemption_that_outlived_its_milestone_fails_the_build()
    {
        var source = RepoData.Source().Set("content/chapters/CH_01_EMBERFALL.json", """
        { "$schema": "../../schema/chapter.schema.json" }
        """);

        ContentLoader.Load(source).Issues.Should().Contain(i =>
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

        issues.Should().NotContain(i => i.Code == ContentIssueCode.MissingSchema);
        issues.Should().Contain(i =>
            i.Code == ContentIssueCode.SchemaViolation &&
            i.Location.StartsWith("content/chapters/CH_02_DUSKMIRE.json#", StringComparison.Ordinal),
            "both files were validated against schema/chapter.schema.json, whose required keys they lack");
    }

    [Fact]
    public void The_shipped_data_set_still_carries_its_deliberate_unauthorised_holes()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.IsAuthorised("tuning/forge.json#/enhance/perLevelSuccessRate").Should().BeFalse();
    }

    [Fact]
    public void Reading_a_shipped_unauthorised_tunable_throws_instead_of_producing_a_number()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        var act = () => snapshot.ReadDouble("tuning/forge.json#/enhance/perLevelSuccessRate");

        act.Should().Throw<Core.Content.UnauthorisedTunableException>();
    }

    [Fact]
    public void A_single_out_of_range_edit_to_a_shipped_tuning_file_is_rejected()
    {
        var source = RepoData.SourceWithEdit(
            "tuning/forge.json", "\"statBonusPerLevel\": 0.07", "\"statBonusPerLevel\": 7");

        ContentLoader.Load(source).Issues.Should().Contain(i => i.Code == ContentIssueCode.OutOfRange);
    }

    [Fact]
    public void A_single_unknown_id_edit_to_a_shipped_tuning_file_is_rejected()
    {
        var source = RepoData.SourceWithEdit(
            "tuning/forge.json", "\"currency\": \"MERGE_DUST\"", "\"currency\": \"MERGE_DUSTT\"");

        ContentLoader.Load(source).Issues.Should().Contain(i => i.Code == ContentIssueCode.UnknownId);
    }

    // ------------------------------------------------------------------- the 📐 check

    [Fact]
    public void The_tunable_marker_audit_passes_against_the_committed_baseline()
    {
        var report = RunAudit();

        report.Issues.Should().BeEmpty(
            "the 📐 check fails on anything the dated baseline does not record, and equally on a " +
            "baseline entry that no longer describes a real mismatch");
    }

    [Fact]
    public void The_documentation_set_still_carries_markers_so_the_check_is_not_passing_vacuously()
    {
        Markers().Should().HaveCountGreaterThan(40,
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
        Markers().Select(m => m.Section.DocId).Distinct().Should().HaveCountGreaterThan(20,
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
                   .Should().HaveCountGreaterThan(30,
                       "the audited set is 37 numeric tuning citations today; a collapse toward " +
                       "zero is DeclaresANumber breaking, not the schemas losing their provenance");
    }

    [Fact]
    public void The_baseline_records_the_date_it_was_taken()
    {
        File.Exists(BaselinePath).Should().BeTrue($"{BaselineRelativePath} is a committed deliverable of M0-09");
        Baseline().RecordedOn.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$");
    }

    [Fact]
    public void Every_baseline_entry_carries_a_reason()
    {
        Entries().Should().OnlyContain(e => e.Reason.Length > 0,
            "a baseline without reasons is a place mismatches go to be forgotten");
    }

    /// <summary>
    /// 🔒 The two kinds are different facts. Spec debt has an owner; a scope exclusion has none,
    /// because nothing closes it.
    /// </summary>
    [Fact]
    public void Spec_debt_names_an_owner_and_a_scope_exclusion_does_not()
    {
        Entries().Should().OnlyContain(e => e.Kind == TunableBaselineKind.SpecDebt
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

        known.Should().HaveCountGreaterThan(150,
            $"{TrackerRelativePath} lists every milestone task; finding almost none means the " +
            "id pattern broke, not that the tracker emptied");

        foreach (var entry in Entries().Where(e => e.Kind == TunableBaselineKind.SpecDebt))
        {
            entry.ClosedBy.Should().MatchRegex(@"^M\d+-\d+[a-z]?$",
                $"the 📐 baseline entry for {entry.Section} must name a milestone task id");

            known.Should().Contain(entry.ClosedBy,
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
        Entries().Count(e => e.Kind == TunableBaselineKind.OutOfScope).Should().Be(7);
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
