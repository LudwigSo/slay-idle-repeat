using Shouldly;
using SlayIdleRepeat.AssetManifest;
using SlayIdleRepeat.AssetPipeline.Qa;
using SlayIdleRepeat.AssetPipeline.Qa.Checks;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// C13 — `15` Part F item 10: <em>"File named per §D1 and packed into the correct atlas"</em>. The
/// second fully mechanical item, and like item 7 it never reports
/// <see cref="QaVerdict.Uncalibrated"/>.
/// </summary>
public sealed class NamingAndAtlasCheckTests
{
    /// <summary>The `15` §D2 atlas the currency icons belong to, per the shipped register.</summary>
    private const string UiAtlas = "atlas_ui";

    [Fact]
    public void Evaluate_passes_a_row_named_per_D1_and_present_in_its_D2_atlas()
    {
        var subject = Subject(
            ManifestRows.NonBiomeUiIcon + AssetNaming.PngExtension,
            QaSubjects.PackHolding(UiAtlas, ManifestRows.NonBiomeUiIcon));

        var outcome = new NamingAndAtlasCheck().Evaluate(subject);

        outcome.ItemNumber.ShouldBe(10);
        outcome.Verdict.ShouldBe(QaVerdict.Pass);
        outcome.Measurements
            .Single(m => m.Key == NamingAndAtlasCheck.PlacementCountMeasurement)
            .Value.ShouldBe(1d);
    }

    /// <summary>
    /// 🔒 CamelCase is what an image tool names a file by default, so it is the failure most likely
    /// to reach a batch. The reason names the §D1 rule broken, not merely that one was.
    /// </summary>
    [Fact]
    public void Evaluate_fails_naming_the_D1_rule_broken_when_the_file_is_CamelCase()
    {
        var subject = Subject(
            "IconCurCrown.png", QaSubjects.PackHolding(UiAtlas, ManifestRows.NonBiomeUiIcon));

        var outcome = new NamingAndAtlasCheck().Evaluate(subject);

        outcome.ItemNumber.ShouldBe(10);
        outcome.Verdict.ShouldBe(QaVerdict.Fail);
        outcome.Reason.ShouldContain(
            nameof(AssetNameRejection.NotSnakeCase), Case.Sensitive);
    }

    /// <summary>
    /// 🔒 The name can be perfect and the packing still wrong. A row `15` §D2 assigns to
    /// <c>atlas_ui</c> that no pack placed has not been "packed into the correct atlas", and the
    /// measurement says so with a number rather than a shrug.
    /// </summary>
    [Fact]
    public void Evaluate_fails_naming_the_placement_count_when_the_atlas_does_not_hold_the_asset()
    {
        var subject = Subject(
            ManifestRows.NonBiomeUiIcon + AssetNaming.PngExtension,
            QaSubjects.PackHolding(UiAtlas, ManifestRows.NonBiomeUiIconSecond));

        var outcome = new NamingAndAtlasCheck().Evaluate(subject);

        outcome.ItemNumber.ShouldBe(10);
        outcome.Verdict.ShouldBe(QaVerdict.Fail);
        outcome.Reason.ShouldContain(
            NamingAndAtlasCheck.PlacementCountMeasurement, Case.Sensitive);
        outcome.Measurements
            .Single(m => m.Key == NamingAndAtlasCheck.PlacementCountMeasurement)
            .Value.ShouldBe(0d);
    }

    /// <summary>
    /// 🔒 A pack for the wrong atlas holding the right asset is still the wrong atlas. Without this
    /// case an implementation that only searched the placements would pass a hero sprite packed into
    /// <c>atlas_ui</c>.
    /// </summary>
    [Fact]
    public void Evaluate_fails_when_the_pack_is_for_an_atlas_15_D2_does_not_assign_the_row()
    {
        var subject = Subject(
            ManifestRows.NonBiomeUiIcon + AssetNaming.PngExtension,
            QaSubjects.PackHolding("atlas_hero", ManifestRows.NonBiomeUiIcon));

        var outcome = new NamingAndAtlasCheck().Evaluate(subject);

        outcome.Verdict.ShouldBe(QaVerdict.Fail);
        outcome.Reason.ShouldContain(UiAtlas, Case.Sensitive);
    }

    /// <summary>
    /// 🔒 A row `15` §D2 assigns an atlas to that reaches item 10 with no pack at all has not been
    /// packed. Reading null as "nothing to check" is how 942 unpacked assets would report clean.
    /// </summary>
    [Fact]
    public void Evaluate_fails_when_an_atlased_row_arrives_with_no_pack_at_all()
    {
        var subject = Subject(ManifestRows.NonBiomeUiIcon + AssetNaming.PngExtension, atlasPack: null);

        var outcome = new NamingAndAtlasCheck().Evaluate(subject);

        outcome.Verdict.ShouldBe(QaVerdict.Fail);
        outcome.Measurements
            .Single(m => m.Key == NamingAndAtlasCheck.PlacementCountMeasurement)
            .Value.ShouldBe(0d);
    }

    /// <summary>
    /// 🔒 `15` §D2: <em>"Backgrounds are not atlased (they are full-screen and streamed per
    /// biome)"</em>. A row the doc assigns no atlas passes on its name alone, and the reason says
    /// which sentence of the doc let it.
    /// </summary>
    [Fact]
    public void Evaluate_passes_an_unatlased_row_on_its_name_alone_per_D2()
    {
        var row = ManifestRows.Require(ManifestRows.RowWithoutDeliverySize);
        row.Atlas.ShouldBeNull(
            "the case is about a row `15` §D2 assigns no atlas; if this one gained one it proves " +
            "nothing and must be replaced, not reinterpreted");

        var spec = new AssetSpec(
            row.Id,
            row.Section,
            new PixelSize(SyntheticAsset.Canvas, SyntheticAsset.Canvas),
            Doc15Pivots.Center,
            row.Atlas,
            row.Biome,
            row.PaletteColours);
        var subject = new QaSubject(
            SyntheticAsset.ChibiCutOut().Image,
            spec,
            row,
            row.Id + AssetNaming.PngExtension,
            null,
            QaThresholds.All(),
            SilhouetteRegistry.Empty);

        var outcome = new NamingAndAtlasCheck().Evaluate(subject);

        outcome.Verdict.ShouldBe(QaVerdict.Pass);
        outcome.Reason.ShouldContain("D2", Case.Sensitive);
    }

    private static QaSubject Subject(string fileName, AtlasPackResult? atlasPack) => QaSubjects.For(
        SyntheticAsset.ChibiCutOut().Image,
        ManifestRows.NonBiomeUiIcon,
        TestSpecs.FromRow(ManifestRows.NonBiomeUiIcon),
        fileName: fileName,
        atlasPack: atlasPack);
}
