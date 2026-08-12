using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// C8 — `15` §B4 step 7: <em>"Atlas pack -&gt; into the category atlas (see §D2)"</em>.
/// </summary>
public sealed class AtlasPackStepTests
{
    /// <summary>
    /// 🔒 Determinism is not decoration here: M8-10 packs roughly 942 assets and an atlas whose
    /// layout depends on enumeration order produces a different texture and different UVs on every
    /// run. Asserted over placements, in order, not over a count.
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

        // 🔒 Stable is not the same claim as stable-in-the-stated-way. The locked design is
        // "ordered by asset id, ordinal", and a packer that sorted by area would also produce the
        // same layout twice — but a different one, and the three rows below are deliberately
        // 32/64/96 px so the two orderings disagree: ordinal gives beast_feed, crown, energy while
        // area gives beast_feed, energy, crown.
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

    /// <summary>
    /// 🔒 `15` §D2: <em>"Backgrounds are not atlased (they are full-screen and streamed per
    /// biome)."</em> The row chosen carries no atlas AND no ruling, so the reason the packer states
    /// can only be §D2 — an exclusion that fired for the wrong rule would still leave the asset out
    /// and would still look correct from the outside.
    /// </summary>
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
    /// 🔒 §D2 names one atlas per category; §C caps a single texture at 2048x2048. <c>atlas_hero</c>
    /// alone is 64 rows at 512x512 — 16.8 M px against a 4.2 M px cap — so multi-page is
    /// arithmetically unavoidable and no doc authorises a paging convention. The result carries the
    /// collision so it reaches the report instead of being resolved in silence by whoever wrote the
    /// packer.
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

    /// <summary>
    /// The arithmetic the contradiction above rests on, read from the shipped register rather than
    /// asserted from memory.
    /// </summary>
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
