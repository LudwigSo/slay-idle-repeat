namespace SlayIdleRepeat.AssetPipeline.Qa.Checks;

/// <summary>Checklist item 8: no text, watermark or signature anywhere in the image.</summary>
/// <remarks>
/// <para>
/// Human: deciding whether an image contains rendered text needs OCR, which means a downloaded
/// model or a native binary, both forbidden here. "Anywhere in the image" is the part that cannot
/// be faked — a signature across the chest of a character is the same defect as one in a corner.
/// </para>
/// <para>
/// What ships instead is <see cref="CornerOpacityMeasurement"/>, a proxy for the single most common
/// version of the failure — a generator's signature sitting in a corner of an otherwise transparent
/// frame. It is evidence, graded against
/// <see cref="ThresholdKeys.WatermarkCornerOpacityCeiling"/>, and it gates nothing: the
/// classification stays <see cref="QaClassification.Human"/> and the verdict is always
/// <see cref="QaVerdict.HumanGapOnly"/>.
/// </para>
/// </remarks>
public sealed class WatermarkCheck : IQaCheck
{
    /// <summary>
    /// The highest mean opacity of the four corner regions, 0-1. Evidence for the
    /// signature-in-a-corner failure, and nothing more.
    /// </summary>
    public const string CornerOpacityMeasurement = "maxCornerOpacity";

    /// <summary>
    /// Which corner <see cref="CornerOpacityMeasurement"/> came from, so a reviewer can look at it:
    /// 0 top-left, 1 top-right, 2 bottom-left, 3 bottom-right.
    /// </summary>
    public const string WorstCornerMeasurement = "maxCornerIndex";

    /// <summary>The judgement this project does not make, and why it cannot.</summary>
    public const string WatermarkHumanGap =
        "Detecting rendered text in an image needs OCR, which means a downloaded model or a native " +
        "binary — both forbidden here, so this project cannot answer the question 15 Part F item 8 " +
        "asks. The corner-opacity measurement is a proxy for one common failure (a signature in a " +
        "corner of an otherwise transparent frame) and proves nothing about text anywhere else in " +
        "the image, which is precisely what the item says. A human looks.";

    /// <inheritdoc/>
    public int ItemNumber => 8;

    /// <inheritdoc/>
    public string ChecklistText => Doc15PartF.SupersededItem8;

    /// <inheritdoc/>
    public QaClassification Classification => QaClassification.Human;

    /// <inheritdoc/>
    public string DocReference => "15 §A3 text";

    /// <inheritdoc/>
    public string? HumanGap => WatermarkHumanGap;

    /// <summary>The four corner regions are the image's four quadrants.</summary>
    /// <remarks>
    /// Quadrants because they are the only corner decomposition with no free parameter to justify —
    /// halving each axis is a statement about the image rather than about signatures.
    /// </remarks>
    private const int QuadrantsPerAxis = 2;

    /// <inheritdoc/>
    public QaOutcome Evaluate(QaSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var image = Raster.From(subject.Image);
        var opacity = CornerOpacities(image);
        var worst = 0;
        for (var corner = 1; corner < opacity.Length; corner++)
        {
            // Strictly greater, so a tie reports the lowest-numbered corner and the same image
            // always names the same one.
            worst = opacity[corner] > opacity[worst] ? corner : worst;
        }

        return new QaOutcome(
            QaVerdict.HumanGapOnly,
            ItemNumber,
            Verdictless(subject.Thresholds, opacity[worst], worst),
            [
                new StepMeasurement(CornerOpacityMeasurement, opacity[worst], "ratio", DocReference),
                new StepMeasurement(WorstCornerMeasurement, worst, "index", DocReference),
            ],
            WatermarkHumanGap);
    }

    /// <summary>What the proxy saw, said in a way that cannot be read as a verdict.</summary>
    /// <remarks>
    /// "Above the ceiling" is a fact about one corner's alpha, not a finding of a watermark — the
    /// item asks about text <em>anywhere</em> in the image. An uncalibrated ceiling is reported as
    /// ungraded rather than filled in; it changes nothing, since the proxy gates nothing.
    /// </remarks>
    /// <param name="thresholds">The threshold set.</param>
    /// <param name="opacity">The worst corner's mean opacity.</param>
    /// <param name="corner">Which corner that was.</param>
    private static string Verdictless(ThresholdSet thresholds, double opacity, int corner)
    {
        var measured =
            $"Measured only: corner {corner} is {QaEvidence.Number(opacity)} opaque on average.";

        if (!thresholds.IsCalibrated(ThresholdKeys.WatermarkCornerOpacityCeiling))
        {
            return measured +
                   $" It is not graded, because {ThresholdKeys.WatermarkCornerOpacityCeiling} is " +
                   "uncalibrated. Nothing was decided either way.";
        }

        var ceiling = thresholds.RequireNumber(ThresholdKeys.WatermarkCornerOpacityCeiling);
        var relation = opacity > ceiling ? "above" : "within";

        return measured +
               $" That is {relation} the stated ceiling of {QaEvidence.Number(ceiling)} — evidence " +
               "for a human, not a verdict: the proxy sees corners and `15` Part F item 8 asks " +
               "about text anywhere in the image.";
    }

    /// <summary>
    /// Each quadrant's mean opacity, 0-1, indexed top-left, top-right, bottom-left, bottom-right.
    /// </summary>
    /// <param name="image">The processed image.</param>
    private static double[] CornerOpacities(Raster image)
    {
        var opacity = new double[QuadrantsPerAxis * QuadrantsPerAxis];
        var middleX = image.Width / QuadrantsPerAxis;
        var middleY = image.Height / QuadrantsPerAxis;

        for (var corner = 0; corner < opacity.Length; corner++)
        {
            var onRight = corner % QuadrantsPerAxis == 1;
            var onBottom = corner / QuadrantsPerAxis == 1;
            opacity[corner] = MeanOpacity(
                image,
                onRight ? middleX : 0,
                onBottom ? middleY : 0,
                onRight ? image.Width : middleX,
                onBottom ? image.Height : middleY);
        }

        return opacity;
    }

    /// <summary>The mean alpha of a region, on the 0-1 scale.</summary>
    /// <param name="image">The processed image.</param>
    /// <param name="left">The region's left edge, inclusive.</param>
    /// <param name="top">The region's top edge, inclusive.</param>
    /// <param name="right">The region's right edge, exclusive.</param>
    /// <param name="bottom">The region's bottom edge, exclusive.</param>
    private static double MeanOpacity(Raster image, int left, int top, int right, int bottom)
    {
        var total = 0d;
        var counted = 0;

        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                total += image.AlphaAt(x, y) / (double)byte.MaxValue;
                counted++;
            }
        }

        return counted == 0 ? 0d : total / counted;
    }
}
