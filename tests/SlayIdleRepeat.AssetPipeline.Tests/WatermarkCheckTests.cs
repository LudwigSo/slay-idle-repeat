using SkiaSharp;
using Shouldly;
using SlayIdleRepeat.AssetPipeline.Qa;
using SlayIdleRepeat.AssetPipeline.Qa.Checks;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// `15` Part F item 8: <em>"No text, watermark or signature anywhere in the image"</em> — the
/// deliberate deviation from what M8-06 was dispatched expecting.
/// </summary>
/// <remarks>
/// 🔒 The item is <see cref="QaClassification.Human"/> because deciding it needs OCR, which needs a
/// model or a native binary, and both are forbidden here. A mechanical corner-opacity proxy ships as
/// <em>evidence</em>. These cases pin the exact thing assumption A5 is about: the proxy works, and
/// it still cannot conclude the item.
/// </remarks>
public sealed class WatermarkCheckTests
{
    /// <summary>
    /// 🔒 The proxy is real, not decorative: a signature stamped into a corner moves the
    /// measurement, and moves it past the ceiling this suite states.
    /// </summary>
    [Fact]
    public void Evaluate_measures_a_higher_corner_opacity_for_a_stamped_corner_than_a_clean_one()
    {
        var (stamped, signature) = SyntheticAsset.ChibiCutOutWithCornerSignature();
        signature.Width.ShouldBe(SyntheticAsset.CornerSignatureSize);

        var clean = Measurement(SyntheticAsset.ChibiCutOut().Image);
        var dirty = Measurement(stamped);

        dirty.ShouldBeGreaterThan(clean);
    }

    /// <summary>
    /// 🔒 <b>The A5 assertion.</b> The proxy fired and the verdict is still
    /// <see cref="QaVerdict.HumanGapOnly"/>. Anything else would mean a batch could be accepted with
    /// text across the middle of every asset in it, reported as "item 8 passed".
    /// </summary>
    [Fact]
    public void Evaluate_returns_HumanGapOnly_even_when_the_corner_proxy_fires()
    {
        var (stamped, _) = SyntheticAsset.ChibiCutOutWithCornerSignature();

        var outcome = new WatermarkCheck().Evaluate(Subject(stamped));

        outcome.ItemNumber.ShouldBe(8);
        outcome.Verdict.ShouldBe(QaVerdict.HumanGapOnly);
        outcome.Verdict.ShouldNotBe(QaVerdict.Fail);
    }

    /// <summary>
    /// 🔒 And the clean image is not a pass either. A human item that concluded "no watermark" from
    /// four quiet corners would be exactly the heuristic-in-the-checklist's-clothes A5 forbids.
    /// </summary>
    [Fact]
    public void Evaluate_returns_HumanGapOnly_for_a_clean_image_rather_than_Pass()
    {
        var outcome = new WatermarkCheck().Evaluate(Subject(SyntheticAsset.ChibiCutOut().Image));

        outcome.Verdict.ShouldBe(QaVerdict.HumanGapOnly);
        outcome.Verdict.ShouldNotBe(QaVerdict.Pass);
    }

    /// <summary>
    /// 🔒 The gap says <em>why</em> the item is human, not merely that it is. "OCR needs a model or
    /// a native binary" is the fact a future reader needs in order to know what would have to change
    /// before this could be mechanised.
    /// </summary>
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
