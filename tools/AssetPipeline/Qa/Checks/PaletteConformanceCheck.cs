using SkiaSharp;

namespace SlayIdleRepeat.AssetPipeline.Qa.Checks;

/// <summary>
/// `15` Part F item 5: <em>"Palette conforms to the biome's locked six colours + neutrals"</em>.
/// </summary>
/// <remarks>
/// <para>
/// The permitted set is the row's six `15` §A5 hues, plus §A3's outline colour, plus
/// <see cref="ThresholdKeys.PaletteNeutrals"/>. A visible pixel further than
/// <see cref="ThresholdKeys.PaletteMatchTolerance"/> from every member of that set counts toward
/// <see cref="OffPaletteCountMeasurement"/>.
/// </para>
/// <para>
/// 🔒 <b>"+ neutrals" is the hole.</b> `15` §A5 writes the phrase and never enumerates them, so
/// there is no list to check against and this project refuses to invent one — five greys would be
/// as defensible as three, and whichever it picked would then be enforced on 328 biome-scoped rows
/// as though a doc had said so. Both keys ship null and this check reports
/// <see cref="QaVerdict.Uncalibrated"/> naming whichever is missing.
/// </para>
/// <para>
/// 🔒 Non-biome rows are <see cref="QaVerdict.Pass"/> with a stated reason, matching `15` §B4 step
/// 3's "(biome assets only)". 646 of the 974 rows carry no palette, and grading the UI kit against a
/// biome's six hues would reject all of it.
/// </para>
/// </remarks>
public sealed class PaletteConformanceCheck : IQaCheck
{
    /// <summary>How many visible pixels are off the permitted set.</summary>
    public const string OffPaletteCountMeasurement = "offPalettePixelCount";

    /// <summary>The furthest any visible pixel sits from the permitted set, in RGB units.</summary>
    public const string WorstPaletteDistanceMeasurement = "worstPaletteDistance";

    /// <inheritdoc/>
    public int ItemNumber => 5;

    /// <inheritdoc/>
    public string ChecklistText => Doc15PartF.Item5;

    /// <inheritdoc/>
    public QaClassification Classification => QaClassification.MechanicalUncalibratedThreshold;

    /// <inheritdoc/>
    public string DocReference => "15 §A5";

    /// <inheritdoc/>
    public string? HumanGap => null;

    /// <inheritdoc/>
    public QaOutcome Evaluate(QaSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        // 🔒 `15` §B4 step 3 is "(biome assets only)" and 646 of the 974 rows carry no palette.
        // Stated as a pass with a reason rather than skipped: a silent skip and a clean grade look
        // identical in a report.
        if (!subject.Spec.IsBiomeScoped)
        {
            return new QaOutcome(
                QaVerdict.Pass,
                ItemNumber,
                $"`15` §A5 locks no palette for '{subject.Spec.Id}' — it is not biome-scoped, and " +
                "§B4 step 3 is \"(biome assets only)\". There are no six hues to conform to.",
                [],
                HumanGap: null);
        }

        try
        {
            return Grade(subject, Permitted(subject));
        }
        catch (UncalibratedThresholdException uncalibrated)
        {
            return QaEvidence.Uncalibrated(ItemNumber, uncalibrated, []);
        }
    }

    /// <summary>
    /// The colours a biome-scoped asset may be painted from: the row's six `15` §A5 hues, plus §A3's
    /// outline colour, plus whatever somebody has stated for §A5's unenumerated "+ neutrals".
    /// </summary>
    /// <param name="subject">The asset under judgement.</param>
    private static IReadOnlyList<SKColor> Permitted(QaSubject subject)
    {
        var palette = subject.Spec.PaletteColours
            ?? throw new InvalidOperationException(
                $"'{subject.Spec.Id}' is biome-scoped and carries no `15` §A5 palette, which " +
                $"{nameof(AssetSpec)}.{nameof(AssetSpec.IsBiomeScoped)} says cannot happen.");

        return
        [
            .. palette.Hues.Select(SKColor.Parse),
            Doc15Authorised.OutlineColour,
            .. subject.Thresholds
                .RequireColours(ThresholdKeys.PaletteNeutrals)
                .Select(SKColor.Parse),
        ];
    }

    /// <summary>Counts the visible pixels no permitted colour accounts for.</summary>
    /// <param name="subject">The asset under judgement.</param>
    /// <param name="permitted">The permitted set.</param>
    private QaOutcome Grade(QaSubject subject, IReadOnlyList<SKColor> permitted)
    {
        var tolerance = subject.Thresholds.RequireNumber(ThresholdKeys.PaletteMatchTolerance);
        var image = Raster.From(subject.Image);
        var offPalette = 0;
        var worst = 0d;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (image.AlphaAt(x, y) == 0)
                {
                    continue;
                }

                var colour = image.ColourAt(x, y);
                var nearest = permitted.Min(hue => Raster.RgbDistance(colour, hue));
                worst = Math.Max(worst, nearest);
                offPalette += nearest > tolerance ? 1 : 0;
            }
        }

        IReadOnlyList<StepMeasurement> measurements =
        [
            new StepMeasurement(OffPaletteCountMeasurement, offPalette, "count", DocReference),
            new StepMeasurement(WorstPaletteDistanceMeasurement, worst, "rgb", DocReference),
        ];

        return offPalette == 0
            ? new QaOutcome(
                QaVerdict.Pass,
                ItemNumber,
                $"Every visible pixel of '{subject.Spec.Id}' is one of the {permitted.Count} " +
                $"permitted colours for biome '{subject.Spec.Biome}'.",
                measurements,
                HumanGap: null)
            : new QaOutcome(
                QaVerdict.Fail,
                ItemNumber,
                $"{OffPaletteCountMeasurement} is {offPalette}: that many visible pixels sit " +
                $"further than the stated tolerance from all {permitted.Count} permitted colours " +
                $"of biome '{subject.Spec.Biome}', the worst by {QaEvidence.Number(worst)} RGB " +
                "units.",
                measurements,
                HumanGap: null);
    }
}
