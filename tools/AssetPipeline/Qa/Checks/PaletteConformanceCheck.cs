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
    public QaOutcome Evaluate(QaSubject subject) => throw new NotImplementedException();
}
