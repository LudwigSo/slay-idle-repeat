using FluentAssertions;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Application.Services.Content.Tunables;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// `14` §6 🔒 — <em>"a build-time check enumerates every 📐 marker in the documentation set against
/// the schema keys and fails on a mismatch. That check is what stops the tuning surface eroding
/// over eighteen months."</em>
/// </summary>
public sealed class TunableMarkerAuditTests
{
    private const string Marker = "\U0001F4D0";

    // ------------------------------------------------------------------ the marker scanner

    [Fact]
    public void Scan_attributes_a_marker_to_the_nearest_preceding_numbered_heading()
    {
        var markdown = $"""
        # 08 — Gear

        ## 4. The Forge

        ### 4.2 Enhancement

        Each level adds 7%. {Marker} TUNABLE.
        """;

        var markers = TunableMarkerScanner.Scan("08_GEAR_AND_MERGING.md", markdown);

        markers.Should().ContainSingle().Which.Section.Should().Be(new DocSection("08", "4.2"));
    }

    [Fact]
    public void Scan_attributes_a_marker_under_an_unnumbered_subheading_to_its_numbered_parent()
    {
        var markdown = $"""
        # 03 — Board

        ## 1. Board topology

        ### Rules

        Nodes per stage. {Marker} TUNABLE.
        """;

        var markers = TunableMarkerScanner.Scan("03_BOARD_AND_TILES.md", markdown);

        markers.Should().ContainSingle().Which.Section.Should().Be(new DocSection("03", "1"));
    }

    [Fact]
    public void Scan_handles_a_letter_suffixed_section_number()
    {
        var markdown = $"""
        # 03 — Board

        ## 7a. In-run income tables

        ### 7a.3 Treasure payout {Marker}
        """;

        var markers = TunableMarkerScanner.Scan("03_BOARD_AND_TILES.md", markdown);

        markers.Should().ContainSingle().Which.Section.Should().Be(new DocSection("03", "7a.3"));
    }

    [Fact]
    public void Scan_records_every_data_file_a_marker_names()
    {
        var markdown = $"""
        # 03 — Board

        ## 7. Shop

        All values {Marker} TUNABLE, in `data/tuning/currencies.json`; the simulator validates them.
        """;

        var markers = TunableMarkerScanner.Scan("03_BOARD_AND_TILES.md", markdown);

        markers.Should().ContainSingle().Which.NamedDataFiles.Should().Equal("tuning/currencies.json");
    }

    [Fact]
    public void Scan_finds_every_marker_on_a_line_that_carries_more_than_one()
    {
        var markdown = $"""
        # 03 — Board

        ## 4. Chapters

        Adds **+5%** {Marker} enemy power, capped at **+50%** {Marker}.
        """;

        var markers = TunableMarkerScanner.Scan("03_BOARD_AND_TILES.md", markdown);

        markers.Should().HaveCount(2);
    }

    [Fact]
    public void Scan_ignores_a_document_whose_file_name_carries_no_document_number()
    {
        var markers = TunableMarkerScanner.Scan("NOTES.md", $"## 1. Things\n\n{Marker} TUNABLE.");

        markers.Should().BeEmpty();
    }

    // ---------------------------------------------------------------- path normalisation

    [Theory]
    [InlineData("data/tuning/currencies.json", "tuning/currencies.json")]
    [InlineData("res://data/combat_caps.json", "combat_caps.json")]
    [InlineData("game-data/tuning/luck.json", "tuning/luck.json")]
    [InlineData("SlayIdleRepeat.Data/tuning/luck.json", "tuning/luck.json")]
    [InlineData("data/enemies.json", "enemies.json")]
    [InlineData("tuning/forge.json", "tuning/forge.json")]
    public void NormaliseDataFilePath_strips_the_prefixes_the_docs_write(string asWritten, string expected)
    {
        TunableMarkerAudit.NormaliseDataFilePath(asWritten).Should().Be(expected);
    }

    // ----------------------------------------------------------------- the citation scanner

    [Fact]
    public void Scan_reads_the_doc_section_a_schema_description_opens_with()
    {
        var schema = ParseSchema("""
        {
          "type": "object",
          "properties": {
            "inputCount": { "description": "08 §4.1 — inputs per merge.", "type": "integer" }
          }
        }
        """);

        var citations = SchemaCitationScanner.Scan("schema/forge.schema.json", schema, governsTuningFile: true);

        citations.Should().ContainSingle().Which.Section.Should().Be(new DocSection("08", "4.1"));
    }

    [Fact]
    public void Scan_expands_a_cited_section_range_into_every_section_it_covers()
    {
        var schema = ParseSchema("""
        { "type": "object", "description": "08 §4.1-4.3 (merge, enhance, salvage)." }
        """);

        var citations = SchemaCitationScanner.Scan("schema/forge.schema.json", schema, governsTuningFile: true);

        citations.Select(c => c.Section).Should().BeEquivalentTo(
        [
            new DocSection("08", "4.1"),
            new DocSection("08", "4.2"),
            new DocSection("08", "4.3"),
        ]);
    }

    [Fact]
    public void Scan_reads_every_document_a_description_cites_not_only_the_first()
    {
        var schema = ParseSchema("""
        { "type": "object", "description": "08 §4.1 and 24 §6.1 — the forge-screen view." }
        """);

        var citations = SchemaCitationScanner.Scan("schema/forge.schema.json", schema, governsTuningFile: true);

        citations.Select(c => c.Section).Should().BeEquivalentTo(
        [
            new DocSection("08", "4.1"),
            new DocSection("24", "6.1"),
        ]);
    }

    // ------------------------------------------------------------------- section overlap

    [Theory]
    [InlineData("08", "4", "08", "4.1", true)]
    [InlineData("08", "4.1", "08", "4", true)]
    [InlineData("08", "4.1", "08", "4.1", true)]
    [InlineData("08", "4.1", "08", "4.2", false)]
    [InlineData("08", "4.1", "24", "4.1", false)]
    [InlineData("08", "4", "08", "40", false)]
    public void Overlaps_matches_a_section_with_its_own_ancestors_and_descendants_only(
        string leftDoc, string leftSection, string rightDoc, string rightSection, bool expected)
    {
        new DocSection(leftDoc, leftSection)
            .Overlaps(new DocSection(rightDoc, rightSection))
            .Should().Be(expected);
    }

    // ------------------------------------------------------------------------ the audit

    private static TunableMarker MarkerAt(string doc, string section, params string[] files) =>
        new(new DocSection(doc, section), 1, "line", files);

    private static SchemaCitation CitationAt(string doc, string section, bool tuning = true) =>
        new("schema/forge.schema.json", "/properties/x", new DocSection(doc, section), tuning,
            GovernsNumericKey: true);

    [Fact]
    public void Run_passes_when_every_marker_has_a_schema_key_and_every_key_has_a_marker()
    {
        var report = TunableMarkerAudit.Run(
            [MarkerAt("08", "4.1", "tuning/forge.json")],
            [CitationAt("08", "4.1")],
            TunableBaseline.None);

        report.Issues.Should().BeEmpty();
        report.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Run_fails_on_a_marker_that_no_schema_key_claims()
    {
        var report = TunableMarkerAudit.Run(
            [MarkerAt("08", "4.1", "tuning/forge.json"), MarkerAt("09", "2", "tuning/forge.json")],
            [CitationAt("08", "4.1")],
            TunableBaseline.None);

        report.Issues.Should().Contain(i => i.Code == ContentIssueCode.TunableMarkerUnmatched);
        report.UnmatchedMarkers.Should().Contain(new DocSection("09", "2"));
    }

    [Fact]
    public void Run_fails_on_a_tuning_schema_key_whose_section_carries_no_marker()
    {
        var report = TunableMarkerAudit.Run(
            [MarkerAt("08", "4.1", "tuning/forge.json")],
            [CitationAt("08", "4.1"), CitationAt("08", "9")],
            TunableBaseline.None);

        report.Issues.Should().Contain(i => i.Code == ContentIssueCode.TunableKeyUnmarked);
        report.UnmarkedCitations.Should().Contain(new DocSection("08", "9"));
    }

    [Fact]
    public void Run_does_not_hold_a_non_tuning_schema_to_the_reverse_direction()
    {
        var report = TunableMarkerAudit.Run(
            [MarkerAt("08", "4.1", "tuning/forge.json")],
            [CitationAt("08", "4.1"), CitationAt("19", "1", tuning: false)],
            TunableBaseline.None);

        report.Issues.Should().NotContain(i => i.Code == ContentIssueCode.TunableKeyUnmarked);
    }

    [Fact]
    public void Run_accepts_a_mismatch_that_the_baseline_records_with_a_reason_and_a_milestone()
    {
        var baseline = new TunableBaseline(
            "2026-08-11",
            [new TunableBaselineEntry(new DocSection("09", "2"), TunableBaselineKind.SpecDebt, "Talent respec costs are M4 work.", "M4-03")],
            []);

        var report = TunableMarkerAudit.Run(
            [MarkerAt("08", "4.1", "tuning/forge.json"), MarkerAt("09", "2", "tuning/forge.json")],
            [CitationAt("08", "4.1")],
            baseline);

        report.Issues.Should().BeEmpty();
        report.UnmatchedMarkers.Should().Contain(new DocSection("09", "2"));
    }

    [Fact]
    public void Run_fails_on_a_baseline_entry_that_no_longer_describes_a_real_mismatch()
    {
        var baseline = new TunableBaseline(
            "2026-08-11",
            [new TunableBaselineEntry(new DocSection("09", "2"), TunableBaselineKind.SpecDebt, "Closed by M4-03.", "M4-03")],
            []);

        var report = TunableMarkerAudit.Run(
            [MarkerAt("08", "4.1", "tuning/forge.json")],
            [CitationAt("08", "4.1")],
            baseline);

        report.Issues.Should().Contain(i => i.Code == ContentIssueCode.StaleBaselineEntry);
        report.StaleBaselineEntries.Should().ContainSingle();
    }

    /// <summary>The real catalogue, so these cases assert a configuration that actually occurs.</summary>
    private static readonly string[] Catalogue = ["currencies.json", "forge.json", "luck.json"];

    [Fact]
    public void Run_fails_when_an_economy_marker_names_a_data_file_outside_the_tuning_directory()
    {
        var report = TunableMarkerAudit.Run(
            [MarkerAt("10", "4", "loot_tables.json")],
            [CitationAt("10", "4")],
            TunableBaseline.None,
            Catalogue);

        report.Issues.Should().Contain(i => i.Code == ContentIssueCode.TunableOutsideTuningDirectory);
    }

    [Fact]
    public void Run_accepts_a_marker_naming_a_tuning_file_by_its_bare_name()
    {
        // The docs write the same file three ways; a bare `currencies.json` IS in the catalogue and
        // must not be reported, or the rule cries wolf on every marker in doc 10.
        var report = TunableMarkerAudit.Run(
            [MarkerAt("10", "4", "currencies.json")],
            [CitationAt("10", "4")],
            TunableBaseline.None,
            Catalogue);

        report.Issues.Should().NotContain(i => i.Code == ContentIssueCode.TunableOutsideTuningDirectory);
    }

    [Fact]
    public void Run_allows_an_allow_listed_non_economy_file_outside_the_tuning_directory()
    {
        var report = TunableMarkerAudit.Run(
            [MarkerAt("05", "2", "combat_caps.json")],
            [CitationAt("05", "2")],
            TunableBaseline.None,
            Catalogue);

        report.Issues.Should().NotContain(i => i.Code == ContentIssueCode.TunableOutsideTuningDirectory);
    }

    [Fact]
    public void The_non_economy_allow_list_is_exactly_these_five_files_and_grows_only_deliberately()
    {
        TunableMarkerAudit.NonEconomyDataFiles.Should().Equal(
        [
            // 05 §2 — combat caps are balance, not economy. 14 §6's locked scope is
            // "every ECONOMY-AFFECTING tunable lives specifically in tuning/". Authored by M2-07.
            "combat_caps.json",

            // 05 §6-6.2 — enemy archetype statlines and elite assignments are content identity
            // (content/enemies/), not an economic dial the 21 simulator sweeps. Authored by M2.
            "enemies.json",

            // 28 Part D — feat definitions. Their Crown payouts are economy and stay in
            // currencies.json; the definitions are content. Authored by M11.
            "feats.json",

            // 19 Part D — the tutorial script. Sequencing, not economy. Authored by M10.
            "ftue.json",

            // 17 §1.2 — boss phases and mechanics. Combat balance and content identity; the kill
            // rewards are economy and stay in currencies.json. Authored by M3.
            "bosses.json",
        ],
        "14 §6's rule survives only while this list is short enough to read in one glance and " +
        "argue with line by line — an escape hatch that widens quietly defeats the whole rule");
    }

    // ------------------------------------------------------- the baseline's own two kinds

    [Fact]
    public void A_baseline_entry_is_either_owned_spec_debt_or_an_argued_scope_exclusion()
    {
        var baseline = TunableBaseline.FromContent(ParseBaseline("""
        {
          "recordedOn": "2026-08-11",
          "unmatchedMarkers": [
            { "doc": "09", "section": "2", "kind": "specDebt", "reason": "M4 work.", "closedBy": "M4-06" },
            { "doc": "00", "section": "0", "kind": "outOfScope", "reason": "The glossary row." }
          ]
        }
        """));

        baseline.UnmatchedMarkers.Should().SatisfyRespectively(
            debt =>
            {
                debt.Kind.Should().Be(TunableBaselineKind.SpecDebt);
                debt.ClosedBy.Should().Be("M4-06");
            },
            excluded =>
            {
                excluded.Kind.Should().Be(TunableBaselineKind.OutOfScope);
                excluded.ClosedBy.Should().BeEmpty();
            });
    }

    /// <summary>
    /// 🔒 The failure <c>--write-baseline</c> used to ship silently. A literal <c>"TODO"</c> owner
    /// satisfied every assertion in reach, because the only one was <c>ClosedBy.Length &gt; 0</c>.
    /// </summary>
    [Fact]
    public void A_regenerated_baseline_nobody_wrote_the_reasons_into_is_refused_by_name()
    {
        var act = () => TunableBaseline.FromContent(ParseBaseline($$"""
        {
          "recordedOn": "2026-08-11",
          "unmatchedMarkers": [
            { "doc": "09", "section": "2", "kind": "{{TunableBaseline.UnreviewedKind}}",
              "reason": "UNREVIEWED — write this by hand." }
          ]
        }
        """));

        act.Should().Throw<FormatException>().WithMessage("*09 §2*unreviewed*");
    }

    [Fact]
    public void Spec_debt_with_no_owner_is_refused()
    {
        var act = () => TunableBaseline.FromContent(ParseBaseline("""
        {
          "recordedOn": "2026-08-11",
          "unmatchedMarkers": [{ "doc": "09", "section": "2", "kind": "specDebt", "reason": "M4 work." }]
        }
        """));

        act.Should().Throw<FormatException>().WithMessage("*no 'closedBy'*");
    }

    [Fact]
    public void A_scope_exclusion_that_names_a_closing_task_is_refused_because_nothing_closes_it()
    {
        var act = () => TunableBaseline.FromContent(ParseBaseline("""
        {
          "recordedOn": "2026-08-11",
          "unmatchedMarkers": [
            { "doc": "00", "section": "0", "kind": "outOfScope", "reason": "Glossary.", "closedBy": "M4-06" }
          ]
        }
        """));

        act.Should().Throw<FormatException>().WithMessage("*out of scope*M4-06*");
    }

    private static Core.Content.ContentValue ParseBaseline(string json)
    {
        JsonContentReader.TryRead("baseline.json", System.Text.Encoding.UTF8.GetBytes(json),
            out var root, out var issues);
        issues.Should().BeEmpty();
        return root!;
    }

    private static Core.Content.ContentValue ParseSchema(string json)
    {
        JsonContentReader.TryRead("schema/test.schema.json", System.Text.Encoding.UTF8.GetBytes(json),
            out var root, out var issues);
        issues.Should().BeEmpty();
        return root!;
    }
}
