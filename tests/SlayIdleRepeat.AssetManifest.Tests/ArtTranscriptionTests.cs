using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetManifest.Tests;

/// <summary>
/// The art register against `15` §E1's summary table, section by section.
/// </summary>
/// <remarks>
/// 🔒 The expected numbers below are written out here, read from `15` by hand, rather than taken
/// from the manifest's own <c>claimedCount</c> fields. A test that sourced both sides of its
/// comparison from the artefact under test would pass for any self-consistent file, including one
/// that transcribed the wrong document.
/// </remarks>
public sealed class ArtTranscriptionTests
{
    /// <summary>
    /// `15` §E1 claim vs what §E2–§E21 actually enumerate. 🔒 E20 is the one disagreement: its
    /// heading says 50 and its list holds 49. It is recorded, never reconciled — O30 owns the
    /// reconciliation at M11-01.
    /// </summary>
    [Theory]
    [InlineData("E2", 64, 64)]
    [InlineData("E3", 128, 128)]
    [InlineData("E4", 32, 32)]
    [InlineData("E5", 32, 32)]
    [InlineData("E6", 48, 48)]
    [InlineData("E7", 24, 24)]
    [InlineData("E8", 14, 14)]
    [InlineData("E9", 112, 112)]
    [InlineData("E10", 28, 28)]
    [InlineData("E11", 120, 120)]
    [InlineData("E12", 98, 98)]
    [InlineData("E13", 60, 60)]
    [InlineData("E14", 12, 12)]
    [InlineData("E15", 9, 9)]
    [InlineData("E16", 11, 11)]
    [InlineData("E17", 86, 86)]
    [InlineData("E18", 0, 0)]
    [InlineData("E19", 32, 32)]
    [InlineData("E20", 50, 49)]
    [InlineData("E21", 15, 15)]
    public void Each_section_carries_the_15_E1_claim_and_the_rows_it_actually_enumerates(
        string sectionId, int e1Claim, int enumerated)
    {
        var section = ManifestFiles.Shipped.Art.Sections
            .SingleOrDefault(s => s.Id == sectionId)
            .ShouldNotBeNull($"15 §E1 lists {sectionId}");

        section.ClaimedCount.ShouldBe(e1Claim, $"15 §E1's row for {sectionId}");
        section.TranscribedCount.ShouldBe(enumerated, $"15 §{sectionId}'s own enumeration");
        ManifestFiles.Shipped.ArtInSection(sectionId).Count().ShouldBe(enumerated);
        section.CountsAgree.ShouldBe(e1Claim == enumerated);
    }

    [Fact]
    public void The_summary_table_covers_exactly_the_twenty_sections_E2_to_E21()
    {
        ManifestFiles.Shipped.Art.Sections
            .Select(s => s.Id)
            .ShouldBe(Enumerable.Range(2, 20).Select(n => $"E{n}"), ignoreOrder: true);
    }

    /// <summary>
    /// 🔒 The mismatch this task exists to surface, pinned by identity. If a second section ever
    /// disagrees with §E1, this fails and names it rather than letting it hide behind a total.
    /// </summary>
    [Fact]
    public void Exactly_one_section_disagrees_with_15_E1_and_it_is_E20()
    {
        var disagreeing = ManifestFiles.Shipped.Art.Sections
            .Where(s => !s.CountsAgree)
            .Select(s => $"{s.Id} (claims {s.ClaimedCount}, transcribes {s.TranscribedCount})")
            .ToArray();

        disagreeing.ShouldBe(["E20 (claims 50, transcribes 49)"]);
    }

    /// <summary>
    /// `15` §E1's TOTAL is 975; the sections enumerate 974. The gap is entirely §E20's missing
    /// fiftieth icon, and both numbers stay in the data so the arithmetic is checkable.
    /// </summary>
    [Fact]
    public void The_total_disagrees_with_15_E1_by_exactly_the_E20_shortfall()
    {
        var totals = ManifestFiles.Shipped.Art.Totals;

        totals.ClaimedBySummaryTable.ShouldBe(975, "15 §E1's TOTAL row");
        totals.Transcribed.ShouldBe(974);

        var shortfall = ManifestFiles.Shipped.Art.Sections
            .Sum(s => s.ClaimedCount - s.TranscribedCount);
        shortfall.ShouldBe(1);
        (totals.ClaimedBySummaryTable - totals.Transcribed).ShouldBe(shortfall);
    }

    /// <summary>
    /// 🔒 O8, ruled at the M8 kickoff on 2026-08-12: the 32 §E19 VFX sheets are cut because VFX are
    /// procedural in-engine. The rows are kept and flagged so the post-cut total is derivable from
    /// the data rather than asserted in prose.
    /// </summary>
    [Fact]
    public void Every_E19_row_is_cut_by_the_O8_ruling_and_nothing_else_is()
    {
        var cut = ManifestFiles.Shipped.CutArt.ToArray();

        cut.Length.ShouldBe(32);
        cut.Select(a => a.Section).Distinct().ShouldBe(["E19"]);
        cut.ShouldAllBe(a => a.Cut!.StartsWith("O8 —", StringComparison.Ordinal));
        cut.ShouldAllBe(a => a.Cut!.Contains("2026-08-12", StringComparison.Ordinal));
    }

    /// <summary>
    /// The ruling's arithmetic was 975 − 32 = 943. The data yields 942, because the transcription
    /// holds 974 rather than §E1's claimed 975. Both are recorded; neither was adjusted to match.
    /// </summary>
    [Fact]
    public void The_active_total_is_the_transcribed_total_less_the_O8_cut()
    {
        var totals = ManifestFiles.Shipped.Art.Totals;

        totals.Active.ShouldBe(942);
        totals.Active.ShouldBe(totals.Transcribed - totals.Cut);
        ManifestFiles.Shipped.ActiveArt.Count().ShouldBe(942);

        (totals.ClaimedBySummaryTable - totals.Cut).ShouldBe(943,
            "the kickoff ruling's 943 is 975−32, which is why it differs from the data's 942");
    }

    /// <summary>
    /// `15` §E18 is 0 assets (D14), so it has no row to flag. The cut is recorded on the section,
    /// consistently with E19 — both sections carry a `cut` string.
    /// </summary>
    [Fact]
    public void The_two_cut_sections_are_E18_and_E19_and_both_record_the_ruling()
    {
        var cutSections = ManifestFiles.Shipped.Art.Sections
            .Where(s => s.Cut is not null)
            .ToArray();

        cutSections.Select(s => s.Id).ShouldBe(["E18", "E19"], ignoreOrder: true);
        cutSections.ShouldAllBe(s => s.Cut!.Length > 0);

        var e18 = cutSections.Single(s => s.Id == "E18");
        e18.TranscribedCount.ShouldBe(0);
        e18.Cut.ShouldNotBeNull().ShouldContain("D14", Case.Sensitive);

        var e19 = cutSections.Single(s => s.Id == "E19");
        e19.CutCount.ShouldBe(32);
        e19.Cut.ShouldNotBeNull().ShouldContain("O8", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 The derived flag exists so a reconciliation can find every id no human authored. These
    /// are the only two sections where `15` gives a count without naming the individual assets.
    /// </summary>
    [Fact]
    public void Derived_rows_come_only_from_E9_decor_and_the_four_unnamed_E17_subgroups()
    {
        var derived = ManifestFiles.Shipped.DerivedArt.ToArray();

        derived.Length.ShouldBe(84);
        derived.Count(a => a.Section == "E9").ShouldBe(64, "8 decor props × 8 biomes (15 §E9)");
        derived.Count(a => a.Section == "E17").ShouldBe(20,
            "6 category card frames + 10 dividers/ribbons/banners + 4 toast chrome (15 §E17)");
        derived.Select(a => a.Section).Distinct().ShouldBe(["E9", "E17"], ignoreOrder: true);

        ManifestFiles.Shipped.Art.Totals.Derived.ShouldBe(derived.Length);
    }

    /// <summary>Every derived id was constructed from §D1 — none of them is written in a doc.</summary>
    [Fact]
    public void No_derived_row_claims_a_doc_authored_id()
    {
        var derived = ManifestFiles.Shipped.DerivedArt.ToArray();

        derived.ShouldNotBeEmpty();
        derived.ShouldAllBe(a => a.IdSource == "convention");
    }

    /// <summary>
    /// S3 floor: every collection this suite reasons over must be non-trivially populated, or the
    /// rules above pass forever over an empty set.
    /// </summary>
    [Fact]
    public void The_register_is_populated()
    {
        var art = ManifestFiles.Shipped.Art;

        art.Assets.Count.ShouldBeGreaterThan(900);
        art.Sections.Count.ShouldBe(20);
        art.Biomes.Count.ShouldBe(8);
        art.Rarities.Count.ShouldBe(5);
        art.Atlases.Count.ShouldBe(9, "15 §D2 declares nine atlases");
        art.Discrepancies.Count.ShouldBeGreaterThan(5);
        art.Status.ShouldBe("transcribed");
    }
}
