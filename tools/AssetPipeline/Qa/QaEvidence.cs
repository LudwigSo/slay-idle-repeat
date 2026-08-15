using System.Globalization;
using SkiaSharp;

namespace SlayIdleRepeat.AssetPipeline.Qa;

/// <summary>
/// Shared helpers several checklist items need: how a measured number is written, how an
/// uncalibrated threshold becomes a verdict, and how a pixel's brightness is defined.
/// </summary>
/// <remarks>
/// Shared rather than repeated so every check's numbers and verdicts are consistent — otherwise
/// items 2 and 6 could disagree about how bright the same fringe is.
/// </remarks>
internal static class QaEvidence
{
    /// <summary>A measured number, written the same way on every machine.</summary>
    /// <remarks>
    /// Invariant culture explicitly: a reason is compared ordinally by the QA cases and read by a
    /// human in a batch report, and a decimal comma appearing on one machine and not another is a
    /// difference in a deliverable.
    /// </remarks>
    /// <param name="value">The measured value.</param>
    internal static string Number(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// Turns a refusal to invent a threshold into a <see cref="QaVerdict.Uncalibrated"/> outcome,
    /// keeping the exception's message — which names the open key and what wanted it.
    /// </summary>
    /// <remarks>
    /// The instrument underneath throws, because an instrument with no scale is broken; the item
    /// reports instead, so one uncalibrated threshold doesn't abort a whole batch and hide the
    /// other items' findings.
    /// </remarks>
    /// <param name="itemNumber">The item reporting.</param>
    /// <param name="uncalibrated">The refusal, whose message names the key.</param>
    /// <param name="measurements">Whatever was measured before the hole was reached.</param>
    /// <param name="humanGap">The item's human gap, or null where it has none.</param>
    internal static QaOutcome Uncalibrated(
        int itemNumber,
        UncalibratedThresholdException uncalibrated,
        IReadOnlyList<StepMeasurement> measurements,
        string? humanGap = null) =>
        new(QaVerdict.Uncalibrated, itemNumber, uncalibrated.Message, measurements, humanGap);

    /// <summary>A colour's relative luminance on the 0-255 scale, by Rec. 709's coefficients.</summary>
    /// <remarks>
    /// Alpha is not folded in: brightness is asked of the pixel as <em>painted</em>, and
    /// premultiplying by coverage would make a white fringe at low alpha read as almost black.
    /// </remarks>
    /// <param name="colour">The colour to measure.</param>
    internal static double Luminance(SKColor colour) =>
        (0.2126d * colour.Red) + (0.7152d * colour.Green) + (0.0722d * colour.Blue);
}
