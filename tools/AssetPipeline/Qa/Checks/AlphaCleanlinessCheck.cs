namespace SlayIdleRepeat.AssetPipeline.Qa.Checks;

/// <summary>
/// `15` Part F item 6: <em>"Alpha is clean — no white/black halo, no semi-transparent fringe"</em>.
/// </summary>
/// <remarks>
/// <para>
/// Two claims, two thresholds, both uncalibrated:
/// </para>
/// <list type="bullet">
///   <item><b>no semi-transparent fringe</b> — <see cref="FringeRatioMeasurement"/>: the share of
///   visible pixels whose alpha is neither 0 nor 255, against
///   <see cref="ThresholdKeys.HaloMaxFringeRatio"/>. Some partial alpha is a legitimate soft edge,
///   which is exactly why the permitted amount is a number `15` never states.</item>
///   <item><b>no white/black halo</b> — <see cref="FringeLuminanceDeviationMeasurement"/>: how far
///   the fringe pixels' luminance runs toward pure white or pure black relative to the opaque
///   pixels they border, against <see cref="ThresholdKeys.HaloMaxLuminanceDeviation"/>.</item>
/// </list>
/// <para>
/// 🔒 This grades the output of `15` §B4 step 1, whose matte decontamination exists to remove
/// exactly this. Keeping the measurement out of the step is deliberate: the step un-mixes the
/// background it was told about, and this item asks whether anything is left, including halos the
/// step never saw.
/// </para>
/// </remarks>
public sealed class AlphaCleanlinessCheck : IQaCheck
{
    /// <summary>Share of visible pixels whose alpha is neither fully on nor fully off, 0-1.</summary>
    public const string FringeRatioMeasurement = "fringePixelRatio";

    /// <summary>How far the fringe runs toward white or black, in luminance units.</summary>
    public const string FringeLuminanceDeviationMeasurement = "maxFringeLuminanceDeviation";

    /// <inheritdoc/>
    public int ItemNumber => 6;

    /// <inheritdoc/>
    public string ChecklistText => Doc15PartF.Item6;

    /// <inheritdoc/>
    public QaClassification Classification => QaClassification.MechanicalUncalibratedThreshold;

    /// <inheritdoc/>
    public string DocReference => "15 §A3 background, §B4 step 1";

    /// <inheritdoc/>
    public string? HumanGap => null;

    /// <inheritdoc/>
    public QaOutcome Evaluate(QaSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var image = Raster.From(subject.Image);
        var fringeRatio = FringeRatio(image);
        var deviation = FringeLuminanceDeviation(image);
        IReadOnlyList<StepMeasurement> measurements =
        [
            new StepMeasurement(FringeRatioMeasurement, fringeRatio, "ratio", DocReference),
            new StepMeasurement(
                FringeLuminanceDeviationMeasurement, deviation, "luminance", DocReference),
        ];

        try
        {
            var maxFringe = subject.Thresholds.RequireNumber(ThresholdKeys.HaloMaxFringeRatio);
            var maxDeviation =
                subject.Thresholds.RequireNumber(ThresholdKeys.HaloMaxLuminanceDeviation);

            if (fringeRatio > maxFringe)
            {
                return Failed(
                    measurements,
                    $"{FringeRatioMeasurement} is {QaEvidence.Number(fringeRatio)}, above the " +
                    $"stated maximum of {QaEvidence.Number(maxFringe)}: that share of the visible " +
                    "pixels is neither fully on nor fully off.");
            }

            return deviation > maxDeviation
                ? Failed(
                    measurements,
                    $"{FringeLuminanceDeviationMeasurement} is {QaEvidence.Number(deviation)}, " +
                    $"above the stated maximum of {QaEvidence.Number(maxDeviation)}: the " +
                    "partial-alpha edge runs that far toward white or black from the opaque pixels " +
                    "it borders.")
                : new QaOutcome(
                    QaVerdict.Pass,
                    ItemNumber,
                    "The alpha is clean within the stated allowances: " +
                    $"{QaEvidence.Number(fringeRatio)} of the visible pixels are partial and they " +
                    $"run {QaEvidence.Number(deviation)} luminance units from what they border.",
                    measurements,
                    HumanGap: null);
        }
        catch (UncalibratedThresholdException uncalibrated)
        {
            return QaEvidence.Uncalibrated(ItemNumber, uncalibrated, measurements);
        }
    }

    /// <summary>The share of visible pixels whose alpha is neither fully on nor fully off.</summary>
    /// <param name="image">The processed image.</param>
    private static double FringeRatio(Raster image)
    {
        var visible = 0;
        var partial = 0;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var alpha = image.AlphaAt(x, y);
                if (alpha == 0)
                {
                    continue;
                }

                visible++;
                partial += alpha == byte.MaxValue ? 0 : 1;
            }
        }

        return visible == 0 ? 0d : partial / (double)visible;
    }

    /// <summary>
    /// How far the furthest partial-alpha pixel's luminance runs from the fully opaque pixels it
    /// borders.
    /// </summary>
    /// <remarks>
    /// 🔒 Relative to the neighbours rather than to pure white or pure black in the absolute. A
    /// legitimately pale asset is not a halo; a pale edge on a dark subject is exactly one, and it
    /// is the difference that says which. A partial pixel with no fully opaque neighbour is not
    /// measured — there is nothing it is a halo <em>of</em>.
    /// </remarks>
    /// <param name="image">The processed image.</param>
    private static double FringeLuminanceDeviation(Raster image)
    {
        var worst = 0d;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var alpha = image.AlphaAt(x, y);
                if (alpha is 0 or byte.MaxValue)
                {
                    continue;
                }

                var bordered = OpaqueNeighbourLuminance(image, x, y);
                if (bordered is not null)
                {
                    worst = Math.Max(
                        worst, Math.Abs(QaEvidence.Luminance(image.ColourAt(x, y)) - bordered.Value));
                }
            }
        }

        return worst;
    }

    /// <summary>
    /// The mean luminance of a pixel's fully opaque neighbours, or null where it has none.
    /// </summary>
    /// <param name="image">The processed image.</param>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    private static double? OpaqueNeighbourLuminance(Raster image, int x, int y)
    {
        var total = 0d;
        var counted = 0;

        for (var offsetY = -1; offsetY <= 1; offsetY++)
        {
            for (var offsetX = -1; offsetX <= 1; offsetX++)
            {
                var nextX = x + offsetX;
                var nextY = y + offsetY;
                if (!image.Contains(nextX, nextY) || image.AlphaAt(nextX, nextY) != byte.MaxValue)
                {
                    continue;
                }

                total += QaEvidence.Luminance(image.ColourAt(nextX, nextY));
                counted++;
            }
        }

        return counted == 0 ? null : total / counted;
    }

    /// <summary>A failing outcome carrying both measurements.</summary>
    /// <param name="measurements">The two measurements.</param>
    /// <param name="reason">Which half broke.</param>
    private QaOutcome Failed(IReadOnlyList<StepMeasurement> measurements, string reason) =>
        new(QaVerdict.Fail, ItemNumber, reason, measurements, HumanGap: null);
}
