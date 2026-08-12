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
    public QaOutcome Evaluate(QaSubject subject) => throw new NotImplementedException();
}
