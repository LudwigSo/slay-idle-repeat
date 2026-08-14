namespace SlayIdleRepeat.AssetPipeline.Qa;

/// <summary>What one `15` Part F item concluded about one asset.</summary>
public enum QaVerdict
{
    /// <summary>
    /// The item's mechanical content held. Reachable only from
    /// <see cref="QaClassification.Mechanical"/> and
    /// <see cref="QaClassification.MechanicalUncalibratedThreshold"/>.
    /// </summary>
    Pass,

    /// <summary>The item's mechanical content did not hold. <see cref="QaOutcome.Reason"/> says which part.</summary>
    Fail,

    /// <summary>
    /// The item needs a threshold `15` does not authorise and nobody has stated.
    /// <see cref="QaOutcome.Reason"/> names the <see cref="ThresholdKeys"/> constant.
    /// </summary>
    /// <remarks>
    /// 🔒 This value exists so that an uncalibrated check can never be mistaken for a passing one.
    /// Steering rule S6 is about exactly this failure: a hole filled with a plausible value passes
    /// forever and nobody finds out.
    /// </remarks>
    Uncalibrated,

    /// <summary>
    /// The only verdict a <see cref="QaClassification.Human"/> item ever returns: this project
    /// gathered whatever evidence it could and decided nothing.
    /// </summary>
    /// <remarks>
    /// 🔒 A human item that could return <see cref="Pass"/> would be a heuristic wearing the
    /// checklist's clothes, and it would let a batch be accepted by machinery alone.
    /// </remarks>
    HumanGapOnly,
}

/// <summary>What one <see cref="IQaCheck"/> concluded, with the numbers it concluded it from.</summary>
/// <param name="Verdict">The conclusion.</param>
/// <param name="ItemNumber">The `15` Part F ordinal, 1-11, of the item that produced this.</param>
/// <param name="Reason">
/// Why. For <see cref="QaVerdict.Fail"/> it names the measurement that tripped and its value; for
/// <see cref="QaVerdict.Uncalibrated"/> it names the <see cref="ThresholdKeys"/> constant.
/// </param>
/// <param name="Measurements">
/// Everything the check measured, kept whether or not it tripped anything — evidence for the human
/// half, and the only way a reviewer can tell a near miss from a wide one.
/// </param>
/// <param name="HumanGap">
/// The judgement this project does not make, or null where the item has none. Non-null for all five
/// <see cref="QaClassification.Human"/> items <b>and</b> for item 1, whose mechanical floor is not
/// `15` §A4's test.
/// </param>
public sealed record QaOutcome(
    QaVerdict Verdict,
    int ItemNumber,
    string Reason,
    IReadOnlyList<StepMeasurement> Measurements,
    string? HumanGap);
