using System.Globalization;
using SkiaSharp;

namespace SlayIdleRepeat.AssetPipeline.Qa;

/// <summary>
/// The three things the `15` §A4 gate and several Part F items share and none of them owns: how a
/// measured number is written, how an uncalibrated threshold becomes a verdict, and how a pixel's
/// brightness is defined.
/// </summary>
/// <remarks>
/// 🔒 Shared rather than repeated because these are decisions, not conveniences. If each check
/// caught <see cref="UncalibratedThresholdException"/> its own way, one of them would eventually
/// catch it and carry on; and if each check defined luminance its own way, items 2 and 6 would
/// disagree about how bright the same fringe is.
/// </remarks>
internal static class QaEvidence
{
    /// <summary>
    /// A measured number, written the same way on every machine.
    /// </summary>
    /// <remarks>
    /// 🔒 Invariant explicitly rather than by relying on <c>InvariantGlobalization</c>. A reason is
    /// compared ordinally by the QA cases and read by a human in a batch report, and a decimal comma
    /// appearing on one machine and not another is a difference in a deliverable.
    /// </remarks>
    /// <param name="value">The measured value.</param>
    internal static string Number(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// Turns the `15` §B4 refusal to invent a threshold into a <see cref="QaVerdict.Uncalibrated"/>
    /// outcome, keeping the exception's message — which names the one open key and what wanted it.
    /// </summary>
    /// <remarks>
    /// 🔒 The seam steering rule S6 needs at the checklist level. The instrument underneath throws,
    /// because an instrument with no scale is broken; the item reports, because one hole in
    /// <c>assets/pipeline/thresholds.json</c> must not abort a 942-asset batch and hide the other
    /// ten items' findings.
    /// </remarks>
    /// <param name="itemNumber">The `15` Part F ordinal reporting.</param>
    /// <param name="uncalibrated">The refusal, whose message names the key.</param>
    /// <param name="measurements">Whatever was measured before the hole was reached.</param>
    /// <param name="humanGap">The item's human gap, or null where it has none.</param>
    internal static QaOutcome Uncalibrated(
        int itemNumber,
        UncalibratedThresholdException uncalibrated,
        IReadOnlyList<StepMeasurement> measurements,
        string? humanGap = null) =>
        new(QaVerdict.Uncalibrated, itemNumber, uncalibrated.Message, measurements, humanGap);

    /// <summary>
    /// A colour's relative luminance on the 0-255 scale, by Rec. 709's coefficients.
    /// </summary>
    /// <remarks>
    /// Rec. 709 is `15` §C's own colour space (sRGB) stating what "brighter" means; it is a
    /// definition rather than a threshold, and there is no `15` number hiding in it. Alpha is not
    /// folded in: items 2 and 6 both ask how bright a pixel is <em>painted</em>, and premultiplying
    /// by coverage would make a white fringe at alpha 64 read as almost black.
    /// </remarks>
    /// <param name="colour">The colour to measure.</param>
    internal static double Luminance(SKColor colour) =>
        (0.2126d * colour.Red) + (0.7152d * colour.Green) + (0.0722d * colour.Blue);
}
