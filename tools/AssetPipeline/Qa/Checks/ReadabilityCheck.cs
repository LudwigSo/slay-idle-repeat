namespace SlayIdleRepeat.AssetPipeline.Qa.Checks;

/// <summary>
/// `15` Part F item 2: <em>"Readable at the smallest in-game display size"</em>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Human.</b> "Readable" is not a predicate over pixels. `15` §A3's detail budget says
/// <em>"If a detail is not readable at 64 px, remove it"</em> — a sentence addressed to an artist,
/// and one that presupposes somebody deciding what counts as a detail.
/// </para>
/// <para>
/// What this emits is <see cref="ContrastRetentionMeasurement"/>: how much of the asset's contrast
/// survives a downscale to the smallest size the manifest ships it at. It is evidence for a
/// reviewer sorting a batch, and it gates nothing — the verdict is always
/// <see cref="QaVerdict.HumanGapOnly"/>.
/// </para>
/// </remarks>
public sealed class ReadabilityCheck : IQaCheck
{
    /// <summary>
    /// Share of the asset's full-size luminance spread that survives the downscale, 0-1. Evidence,
    /// not a gate — there is no threshold for it and there must not be one.
    /// </summary>
    public const string ContrastRetentionMeasurement = "contrastRetentionRatio";

    /// <summary>The judgement this project does not make.</summary>
    public const string ReadabilityHumanGap =
        "\"Readable\" is not a predicate over pixels, and 15 §A3's detail budget (\"If a detail " +
        "is not readable at 64 px, remove it\") is addressed to an artist. The contrast-retention " +
        "measurement is evidence for a reviewer, not a verdict: a human must look at the asset at " +
        "its smallest in-game size and say.";

    /// <inheritdoc/>
    public int ItemNumber => 2;

    /// <inheritdoc/>
    public string ChecklistText => Doc15PartF.Item2;

    /// <inheritdoc/>
    public QaClassification Classification => QaClassification.Human;

    /// <inheritdoc/>
    public string DocReference => "15 §A3 detail budget";

    /// <inheritdoc/>
    public string? HumanGap => ReadabilityHumanGap;

    /// <inheritdoc/>
    public QaOutcome Evaluate(QaSubject subject) => throw new NotImplementedException();
}
