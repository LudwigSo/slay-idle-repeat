using System.Text;
using FluentAssertions;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Application.Services.Content.Tunables;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The validator against the real <c>SlayIdleRepeat.Data</c>: 16 tuning files, 19 schemas, two
/// locales and 98 deliberate <c>null</c>s.
/// </summary>
/// <remarks>
/// A validator proven only against a 40-line fixture has not been proven. This is also the suite
/// that keeps the committed 📐 baseline honest — it fails both when a new mismatch appears and
/// when a baselined one is quietly fixed and the entry left behind.
/// </remarks>
public sealed class RealDataSetTests
{
    private const string BaselineRelativePath = "build/content/tunable-marker-baseline.json";

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

    [Fact]
    public void An_exemption_that_outlived_its_milestone_fails_the_build()
    {
        var source = RepoData.Source().Set("content/chapter.json", """
        { "$schema": "../schema/chapter.schema.json" }
        """);

        ContentLoader.Load(source).Issues.Should().Contain(i =>
            i.Code == ContentIssueCode.OrphanSchema && i.Location == "schema/chapter.schema.json");
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

    [Fact]
    public void The_tuning_schemas_still_carry_citations_so_the_reverse_direction_is_not_vacuous()
    {
        Citations().Where(c => c.GovernsTuningFile).Should().NotBeEmpty();
    }

    [Fact]
    public void The_baseline_records_the_date_it_was_taken()
    {
        File.Exists(BaselinePath).Should().BeTrue($"{BaselineRelativePath} is a committed deliverable of M0-09");
        Baseline().RecordedOn.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$");
    }

    [Fact]
    public void Every_baseline_entry_carries_a_reason_and_the_milestone_that_closes_it()
    {
        var baseline = Baseline();

        var entries = baseline.UnmatchedMarkers.Concat(baseline.UnmarkedSchemaCitations).ToArray();

        entries.Should().OnlyContain(e => e.Reason.Length > 0 && e.ClosedBy.Length > 0,
            "a baseline without reasons and owners is a place mismatches go to be forgotten");
    }

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
