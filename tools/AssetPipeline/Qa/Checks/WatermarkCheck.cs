namespace SlayIdleRepeat.AssetPipeline.Qa.Checks;

/// <summary>
/// `15` Part F item 8: <em>"No text, watermark or signature anywhere in the image"</em>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Human — and this deviates from what the task was dispatched expecting.</b> Deciding
/// whether an image contains rendered text needs OCR, which means either a downloaded model or a
/// native binary, and both are forbidden here. There is no managed, hermetic way to answer the
/// question the item actually asks, and "anywhere in the image" is the part that cannot be faked:
/// a signature across the chest of a character is the same defect as one in a corner.
/// </para>
/// <para>
/// What ships instead is <see cref="CornerOpacityMeasurement"/>, a proxy for the single most common
/// version of the failure — a generator's signature sitting in a corner of an otherwise transparent
/// frame. It is evidence, it is graded against
/// <see cref="ThresholdKeys.WatermarkCornerOpacityCeiling"/>, and it gates nothing: the
/// classification stays <see cref="QaClassification.Human"/> and the verdict is always
/// <see cref="QaVerdict.HumanGapOnly"/>.
/// </para>
/// <para>
/// 🔒 Assumption A5 forbids dressing a heuristic up as the real test. Classifying this item
/// mechanical because a corner heuristic exists would mean a batch could be accepted with text
/// across the middle of every asset in it, reported as "item 8 passed".
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
    public string ChecklistText => Doc15PartF.Item8;

    /// <inheritdoc/>
    public QaClassification Classification => QaClassification.Human;

    /// <inheritdoc/>
    public string DocReference => "15 §A3 text";

    /// <inheritdoc/>
    public string? HumanGap => WatermarkHumanGap;

    /// <inheritdoc/>
    public QaOutcome Evaluate(QaSubject subject) => throw new NotImplementedException();
}
