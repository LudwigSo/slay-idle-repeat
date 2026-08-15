using SkiaSharp;
using Shouldly;
using SlayIdleRepeat.AssetPipeline.Qa;
using SlayIdleRepeat.AssetPipeline.Qa.Checks;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// This item is <see cref="QaClassification.Human"/> because deciding it needs OCR, which is
/// forbidden here. A mechanical corner-opacity proxy ships as evidence only: these cases pin that
/// the proxy works, and it still cannot conclude the item.
/// </summary>
public sealed class WatermarkCheckTests
{
    /// <summary>The proxy is real, not decorative: a stamped corner moves the measurement past the stated ceiling.</summary>
    [Fact]
    public void Evaluate_measures_a_higher_corner_opacity_for_a_stamped_corner_than_a_clean_one()
    {
        var (stamped, signature) = SyntheticAsset.ChibiCutOutWithCornerSignature();
        signature.Width.ShouldBe(SyntheticAsset.CornerSignatureSize);

        var clean = Measurement(SyntheticAsset.ChibiCutOut().Image);
        var dirty = Measurement(stamped);

        dirty.ShouldBeGreaterThan(clean);
    }

    /// <summary>The proxy firing must still yield <see cref="QaVerdict.HumanGapOnly"/>, or a batch could report "item 8 passed" with text mid-asset.</summary>
    [Fact]
    public void Evaluate_returns_HumanGapOnly_even_when_the_corner_proxy_fires()
    {
        var (stamped, _) = SyntheticAsset.ChibiCutOutWithCornerSignature();

        var outcome = new WatermarkCheck().Evaluate(Subject(stamped));

        outcome.ItemNumber.ShouldBe(8);
        outcome.Verdict.ShouldBe(QaVerdict.HumanGapOnly);
        outcome.Verdict.ShouldNotBe(QaVerdict.Fail);
    }

    /// <summary>The clean image is not a pass either: concluding "no watermark" from four quiet corners would be the proxy masquerading as the human check.</summary>
    [Fact]
    public void Evaluate_returns_HumanGapOnly_for_a_clean_image_rather_than_Pass()
    {
        var outcome = new WatermarkCheck().Evaluate(Subject(SyntheticAsset.ChibiCutOut().Image));

        outcome.Verdict.ShouldBe(QaVerdict.HumanGapOnly);
        outcome.Verdict.ShouldNotBe(QaVerdict.Pass);
    }

    /// <summary>The gap must say why the item is human, so a future reader knows what would have to change before it could be mechanised.</summary>
    [Fact]
    public void Evaluate_names_OCR_as_the_reason_the_item_cannot_be_mechanised()
    {
        var outcome = new WatermarkCheck().Evaluate(Subject(SyntheticAsset.ChibiCutOut().Image));

        outcome.HumanGap.ShouldNotBeNull();
        outcome.HumanGap.ShouldContain("OCR", Case.Sensitive);
    }

    private static double Measurement(SKBitmap image) =>
        new WatermarkCheck().Evaluate(Subject(image)).Measurements
            .Single(m => m.Key == WatermarkCheck.CornerOpacityMeasurement)
            .Value;

    private static QaSubject Subject(SKBitmap image) => QaSubjects.For(
        image,
        ManifestRows.NonBiomeUiIcon,
        TestSpecs.WithTargetSize(
            ManifestRows.NonBiomeUiIcon, SyntheticAsset.Canvas, SyntheticAsset.Canvas));
}
