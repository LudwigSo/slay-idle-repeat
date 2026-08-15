using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

public sealed class AtlasPackStepTests
{
    /// <summary>
    /// An atlas whose layout depends on enumeration order produces a different texture and
    /// different UVs on every run, so this asserts placements in order, not just a count.
    /// </summary>
    [Fact]
    public void Run_places_the_same_members_identically_whatever_order_they_arrive_in()
    {
        var forwards = new[]
        {
            Entry(ManifestRows.NonBiomeUiIcon, 96, 96),
            Entry(ManifestRows.NonBiomeUiIconSecond, 64, 64),
            Entry(ManifestRows.NonBiomeUiIconThird, 32, 32),
        };
        var backwards = forwards.Reverse().ToArray();
        var step = new AtlasPackStep();

        var first = step.Run(UiInput(forwards));
        var second = step.Run(UiInput(backwards));

        first.Placements.Count.ShouldBe(forwards.Length);
        Describe(first).ShouldBe(Describe(second));

        // Ordering must be by asset id, not by area: the rows are deliberately 32/64/96 px so an
        // area-sorted packer would also be stable but would disagree with this expected order.
        first.Placements.Select(placement => placement.AssetId).ToArray().ShouldBe(
            [
                ManifestRows.NonBiomeUiIconThird,
                ManifestRows.NonBiomeUiIcon,
                ManifestRows.NonBiomeUiIconSecond,
            ]);
    }

    [Fact]
    public void Run_excludes_a_row_a_ruling_has_cut_and_states_the_ruling()
    {
        var cut = ManifestRows.Require(ManifestRows.CutVfxSheet);
        cut.Cut.ShouldNotBeNull("the case proves nothing against a live row");
        var entry = new AtlasPackEntry(cut, SyntheticAsset.SolidBlock(64, 64));

        var result = new AtlasPackStep().Run(
            new AtlasPackInput("atlas_vfx", [entry], StatedThresholds.ForSyntheticFixtures()));

        result.Placements.ShouldBeEmpty();
        result.Exclusions.Count.ShouldBe(1);
        result.Exclusions[0].AssetId.ShouldBe(cut.Id);
        result.Exclusions[0].Reason.ShouldContain(cut.Cut, Case.Sensitive);
    }

    /// <summary>The row carries no atlas and no ruling, so the stated exclusion reason can only be the missing atlas.</summary>
    [Fact]
    public void Run_excludes_a_row_15_D2_assigns_no_atlas_and_says_so()
    {
        var background = ManifestRows.Require(ManifestRows.RowWithoutDeliverySize);
        background.Atlas.ShouldBeNull();
        background.Cut.ShouldBeNull("otherwise the exclusion below could be about the ruling");
        var entry = new AtlasPackEntry(background, SyntheticAsset.SolidBlock(64, 64));

        var result = new AtlasPackStep().Run(
            new AtlasPackInput("atlas_ui", [entry], StatedThresholds.ForSyntheticFixtures()));

        result.Placements.ShouldBeEmpty();
        result.Exclusions.Count.ShouldBe(1);
        result.Exclusions[0].AssetId.ShouldBe(background.Id);
        result.Exclusions[0].Reason.ShouldContain("§D2", Case.Sensitive);
    }

    /// <summary>
    /// <c>atlas_hero</c> alone is 64 rows at 512x512 (16.8 M px) against the 4.2 M px single-texture
    /// cap, so multi-page is arithmetically unavoidable; the collision must reach the report rather
    /// than be resolved silently.
    /// </summary>
    [Fact]
    public void Run_reports_that_15_D2_and_15_C_cannot_both_be_satisfied()
    {
        var result = new AtlasPackStep().Run(
            UiInput([Entry(ManifestRows.NonBiomeUiIcon, 96, 96)]));

        result.Contradictions.ShouldNotBeEmpty();
        var contradiction = result.Contradictions
            .Single(reported => reported.Id == AtlasPackStep.PageCapContradictionId);
        contradiction.FirstReference.ShouldContain("§D2", Case.Sensitive);
        contradiction.SecondReference.ShouldContain("§C", Case.Sensitive);
    }

    [Fact]
    public void The_hero_atlas_really_does_exceed_15_Cs_single_texture_cap()
    {
        var members = PipelineFiles.Shipped.AtlasMembers("atlas_hero").ToArray();
        members.Length.ShouldBe(64);

        var pixels = members.Sum(member =>
            (long)member.RequireDeliverySize().Width * member.RequireDeliverySize().Height);

        pixels.ShouldBeGreaterThan(
            (long)Doc15Authorised.MaxSingleTextureWidth * Doc15Authorised.MaxSingleTextureHeight);
    }

    private static AtlasPackEntry Entry(string id, int width, int height) =>
        new(ManifestRows.Require(id), SyntheticAsset.SolidBlock(width, height));

    private static AtlasPackInput UiInput(IReadOnlyList<AtlasPackEntry> entries) =>
        new("atlas_ui", entries, StatedThresholds.ForSyntheticFixtures());

    private static IReadOnlyList<string> Describe(AtlasPackResult result) =>
    [
        .. result.Placements.Select(placement =>
            $"{placement.AssetId}@{placement.PageIndex}:" +
            $"{placement.X},{placement.Y},{placement.Width}x{placement.Height}"),
    ];
}
