using SkiaSharp;
using Shouldly;
using SlayIdleRepeat.AssetManifest;
using SlayIdleRepeat.AssetPipeline.Qa;
using SlayIdleRepeat.AssetPipeline.Qa.Checks;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// C13 — `15` Part F item 5:
/// <em>"Palette conforms to the biome's locked six colours + neutrals"</em>.
/// </summary>
/// <remarks>
/// 🔒 The six hues come off the shipped row, so the passing fixture is painted from the register's
/// own colours rather than from six the case chose. "+ neutrals" is the hole: `15` §A5 writes the
/// phrase and enumerates nothing, so <see cref="ThresholdKeys.PaletteNeutrals"/> ships null and this
/// item cannot conclude without somebody stating a list.
/// </remarks>
public sealed class PaletteConformanceCheckTests
{
    /// <summary>Every hole item 5 reaches into.</summary>
    private static readonly string[] ThresholdsItem5Needs =
    [
        ThresholdKeys.PaletteNeutrals,
        ThresholdKeys.PaletteMatchTolerance,
    ];

    /// <summary>Every hole item 5 reaches into, one theory case each.</summary>
    public static TheoryData<string> EveryThresholdItem5Needs()
    {
        var data = new TheoryData<string>();
        foreach (var key in ThresholdsItem5Needs)
        {
            data.Add(key);
        }

        return data;
    }

    /// <summary>
    /// 🔒 The fixture's off-palette pixel is only a violation if it really is off the permitted set.
    /// This measures that against the row's own six hues before any case relies on it, so a palette
    /// edit that happened to make the nudged colour legal fails here rather than silently turning
    /// the violation case green.
    /// </summary>
    [Fact]
    public void The_violating_fixtures_pixel_is_further_from_every_permitted_hue_than_the_stated_tolerance()
    {
        var palette = RequirePalette();
        var fixture = SyntheticAsset.BiomePalette(palette);
        var permitted = palette.Hues
            .Select(SKColor.Parse)
            .Append(Doc15Authorised.OutlineColour)
            .ToArray();
        permitted.Length.ShouldBe(7);

        var nearest = permitted.Min(hue => Pixels.RgbDistance(fixture.OffPaletteColour, hue));

        nearest.ShouldBeGreaterThan(StatedMatchTolerance());
    }

    [Fact]
    public void Evaluate_passes_an_image_painted_only_from_the_rows_six_hues_and_the_A3_outline()
    {
        var image = SyntheticAsset.OnPalette(RequirePalette());

        var outcome = new PaletteConformanceCheck().Evaluate(Subject(image));

        outcome.ItemNumber.ShouldBe(5);
        outcome.Verdict.ShouldBe(QaVerdict.Pass);
        outcome.Measurements
            .Single(m => m.Key == PaletteConformanceCheck.OffPaletteCountMeasurement)
            .Value.ShouldBe(0d);
    }

    [Fact]
    public void Evaluate_fails_naming_the_off_palette_count_when_one_pixel_is_off_the_locked_six()
    {
        var fixture = SyntheticAsset.BiomePalette(RequirePalette());

        var outcome = new PaletteConformanceCheck().Evaluate(Subject(fixture.Image));

        outcome.ItemNumber.ShouldBe(5);
        outcome.Verdict.ShouldBe(QaVerdict.Fail);
        outcome.Reason.ShouldContain(
            PaletteConformanceCheck.OffPaletteCountMeasurement, Case.Sensitive);
        outcome.Measurements
            .Single(m => m.Key == PaletteConformanceCheck.OffPaletteCountMeasurement)
            .Value.ShouldBe(1d);
    }

    /// <summary>
    /// 🔒 `15` §B4 step 3 is "(biome assets only)" and 646 of the 974 rows carry no palette. Grading
    /// the UI kit against a biome's six hues would reject all of it, so a non-biome row passes with
    /// the reason saying why rather than being quietly skipped.
    /// </summary>
    [Fact]
    public void Evaluate_passes_a_non_biome_row_with_a_stated_reason_rather_than_grading_it()
    {
        var spec = TestSpecs.WithTargetSize(
            ManifestRows.NonBiomeUiIcon, SyntheticAsset.PaletteCanvas, SyntheticAsset.PaletteCanvas);
        spec.IsBiomeScoped.ShouldBeFalse(
            "the case is about a row `15` §A5 locks no palette for; a row that gained one proves " +
            "nothing here");
        var subject = QaSubjects.For(
            SyntheticAsset.BiomePalette(RequirePalette()).Image,
            ManifestRows.NonBiomeUiIcon,
            spec);

        var outcome = new PaletteConformanceCheck().Evaluate(subject);

        outcome.Verdict.ShouldBe(QaVerdict.Pass);
        outcome.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// 🔒 The S6 assertion for item 5. `15` §A5's "+ neutrals" is a phrase, not a list, and a check
    /// that passed without one would be enforcing a palette of its own invention on 328 rows.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryThresholdItem5Needs))]
    public void Evaluate_reports_Uncalibrated_naming_the_key_when_one_hole_is_left_open(string key)
    {
        var image = SyntheticAsset.OnPalette(RequirePalette());

        var outcome = new PaletteConformanceCheck()
            .Evaluate(Subject(image, QaThresholds.Except(key)));

        outcome.ItemNumber.ShouldBe(5);
        outcome.Verdict.ShouldBe(QaVerdict.Uncalibrated);
        outcome.Reason.ShouldContain(key, Case.Sensitive);

        // 🔒 Steering rule S2. A reason naming both of item 5's keys would satisfy the assertion
        // above for both cases; the other key is stated here, so naming it names a hole that is
        // not open.
        foreach (var stated in ThresholdsItem5Needs.Where(other => !string.Equals(other, key, StringComparison.Ordinal)))
        {
            outcome.Reason.ShouldNotContain(stated, Case.Sensitive);
        }
    }

    private static double StatedMatchTolerance() =>
        QaThresholds.Stated[ThresholdKeys.PaletteMatchTolerance] is NumericThreshold number
            ? number.Value
            : throw new InvalidOperationException(
                "The Part F cases state paletteMatchTolerance as a number; this case reads it back " +
                "rather than retyping it, so the two can never disagree.");

    private static Palette RequirePalette() =>
        ManifestRows.Require(ManifestRows.BiomeScopedCharacter).PaletteColours
        ?? throw new InvalidOperationException(
            $"'{ManifestRows.BiomeScopedCharacter}' carries no `15` §A5 palette, so it cannot back " +
            "an item 5 case. Pick a biome-scoped row that does.");

    private static QaSubject Subject(SKBitmap image, ThresholdSet? thresholds = null) =>
        QaSubjects.For(
            image,
            ManifestRows.BiomeScopedCharacter,
            TestSpecs.WithTargetSize(
                ManifestRows.BiomeScopedCharacter,
                SyntheticAsset.PaletteCanvas,
                SyntheticAsset.PaletteCanvas),
            thresholds);
}
