namespace SlayIdleRepeat.AssetPipeline.Qa.Checks;

/// <summary>
/// `15` Part F item 1: <em>"Silhouette test passed at 64 px (characters)"</em>.
/// </summary>
/// <remarks>
/// <para>
/// Delegates to <see cref="SilhouetteGate"/> and turns its four measurements into a verdict. The
/// four cutoffs are uncalibrated, so a threshold set that states none of them yields
/// <see cref="QaVerdict.Uncalibrated"/> naming the key — never a pass.
/// </para>
/// <para>
/// 🔒 <b>This item is classified with a human gap even though it is mechanised.</b> `15` §A4's
/// acceptance test is <em>"If you cannot tell which character it is, regenerate it"</em>. Four pixel
/// measurements do not perform that test; they catch silhouettes so degenerate that nobody would
/// need to try. <see cref="HumanGap"/> is non-null on every outcome this check produces, including
/// the passing ones, so no report can say "§A4 passed" on the strength of a machine.
/// </para>
/// </remarks>
public sealed class SilhouetteCheck : IQaCheck
{
    /// <summary>The judgement `15` §A4 asks for and this project does not make.</summary>
    public const string SilhouetteHumanGap =
        "15 §A4's acceptance test is a human judgement: \"" +
        Doc15PartF.SilhouetteAcceptanceSentence +
        "\" Nothing here performs it. The four measurements are a mechanical floor beneath §A4 — " +
        "they catch a silhouette that has collapsed, split apart or duplicated one already " +
        "accepted. A pass means the silhouette cleared that floor; it never means §A4 passed.";

    /// <inheritdoc/>
    public int ItemNumber => 1;

    /// <inheritdoc/>
    public string ChecklistText => Doc15PartF.Item1;

    /// <inheritdoc/>
    public QaClassification Classification => QaClassification.MechanicalUncalibratedThreshold;

    /// <inheritdoc/>
    public string DocReference => "15 §A4";

    /// <inheritdoc/>
    public string? HumanGap => SilhouetteHumanGap;

    /// <inheritdoc/>
    public QaOutcome Evaluate(QaSubject subject) => throw new NotImplementedException();
}
