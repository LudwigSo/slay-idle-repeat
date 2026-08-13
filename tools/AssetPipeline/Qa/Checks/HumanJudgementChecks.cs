namespace SlayIdleRepeat.AssetPipeline.Qa.Checks;

/// <summary>
/// `15` Part F item 4: <em>"Key light from upper left, consistent with the batch"</em>.
/// </summary>
/// <remarks>
/// 🔒 <b>Human, with no proxy at all.</b> Light direction cannot be recovered from a finished 2D
/// asset: a chibi drawn with two-tone cel shading (`15` §A3) carries a shadow whose placement is an
/// artistic choice, not a physical shading of a known geometry, and inferring a light vector from it
/// would need a normal map nobody has. "Consistent with the batch" is worse — it compares against a
/// set whose own lighting is the thing in question. This check emits no measurement, because a
/// number here would be read as evidence and there is none.
/// </remarks>
public sealed class KeyLightCheck : IQaCheck
{
    /// <summary>The judgement this project does not make, and why it cannot.</summary>
    public const string KeyLightHumanGap =
        "Light direction cannot be recovered from a finished 2D asset — 15 §A3's two-tone cel " +
        "shading places a shadow by artistic choice over geometry nobody has a normal map for — " +
        "and \"consistent with the batch\" compares against a set whose own lighting is the thing " +
        "in question. No measurement is emitted, because any number here would be read as evidence " +
        "and there is none. A human compares the batch.";

    /// <inheritdoc/>
    public int ItemNumber => 4;

    /// <inheritdoc/>
    public string ChecklistText => Doc15PartF.Item4;

    /// <inheritdoc/>
    public QaClassification Classification => QaClassification.Human;

    /// <inheritdoc/>
    public string DocReference => "15 §A3 lighting";

    /// <inheritdoc/>
    public string? HumanGap => KeyLightHumanGap;

    /// <inheritdoc/>
    public QaOutcome Evaluate(QaSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        return new QaOutcome(
            QaVerdict.HumanGapOnly,
            ItemNumber,
            "Nothing was measured and nothing was decided: `15` Part F item 4 asks where the light " +
            "came from, and a finished 2D asset does not carry that.",
            [],
            KeyLightHumanGap);
    }
}

/// <summary>
/// `15` Part F item 9: <em>"Proportions match the Style Anchor Sheet (2.5–3 heads)"</em>.
/// </summary>
/// <remarks>
/// 🔒 <b>Human, with no proxy at all.</b> A head count needs the head found first, and finding a
/// chibi's head in a silhouette whose whole design premise is "big head, small body, tiny or no
/// neck" (`15` §A3) is the hard half of the problem. A bounding-box aspect ratio would correlate
/// with head count across a batch and be wrong on exactly the assets that matter — a crouching pose,
/// a mount, a boss with a tail.
/// </remarks>
public sealed class ProportionsCheck : IQaCheck
{
    /// <summary>The judgement this project does not make, and why it cannot.</summary>
    public const string ProportionsHumanGap =
        "Counting heads needs the head located first, and 15 §A3's chibi premise (\"big head, " +
        "small body, tiny or no neck\") is exactly what makes that hard. A bounding-box aspect " +
        "ratio would correlate across a batch and be wrong on the assets that matter — a crouching " +
        "pose, a mount, a boss with a tail — so none is emitted. A human measures against the " +
        "Style Anchor Sheet.";

    /// <inheritdoc/>
    public int ItemNumber => 9;

    /// <inheritdoc/>
    public string ChecklistText => Doc15PartF.Item9;

    /// <inheritdoc/>
    public QaClassification Classification => QaClassification.Human;

    /// <inheritdoc/>
    public string DocReference => "15 §A3 proportions";

    /// <inheritdoc/>
    public string? HumanGap => ProportionsHumanGap;

    /// <inheritdoc/>
    public QaOutcome Evaluate(QaSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        return new QaOutcome(
            QaVerdict.HumanGapOnly,
            ItemNumber,
            "Nothing was measured and nothing was decided: `15` Part F item 9 counts heads, and " +
            "locating a chibi's head is the hard half of that.",
            [],
            ProportionsHumanGap);
    }
}

/// <summary>
/// `15` Part F item 11: <em>"Side-by-side comparison against 3 previously-approved assets in the
/// same category shows no style drift"</em>.
/// </summary>
/// <remarks>
/// 🔒 <b>Human, and the item says so itself.</b> "Side-by-side comparison … shows no style drift"
/// names the method as well as the criterion, and the method is a person looking at four images at
/// once. <see cref="SilhouetteRegistry"/> holds what a reviewer would put beside the new asset, but
/// nothing here performs the comparison.
/// </remarks>
public sealed class StyleDriftCheck : IQaCheck
{
    /// <summary>The judgement this project does not make.</summary>
    public const string StyleDriftHumanGap =
        "15 Part F item 11 names its own method: a side-by-side comparison against three " +
        "previously-approved assets, performed by a person looking at four images at once. " +
        "SilhouetteRegistry holds the comparison set, and nothing here performs the comparison.";

    /// <inheritdoc/>
    public int ItemNumber => 11;

    /// <inheritdoc/>
    public string ChecklistText => Doc15PartF.Item11;

    /// <inheritdoc/>
    public QaClassification Classification => QaClassification.Human;

    /// <inheritdoc/>
    public string DocReference => "15 Part F item 11";

    /// <inheritdoc/>
    public string? HumanGap => StyleDriftHumanGap;

    /// <inheritdoc/>
    public QaOutcome Evaluate(QaSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        // 🔒 The count of what a reviewer would have to put beside this asset, and not a step toward
        // comparing them. Item 11 names its own method — a person looking at four images at once —
        // so the only honest thing a machine adds here is how many are available to look at.
        var category = AssetNaming.CategoryOf(subject.Asset.Id);
        var available = subject.Registry.InCategory(category).Count;

        return new QaOutcome(
            QaVerdict.HumanGapOnly,
            ItemNumber,
            $"No comparison was performed. The registry holds {available} previously-accepted " +
            $"silhouette(s) in category '{category}' for a reviewer to set beside this asset; `15` " +
            "Part F item 11 asks for three.",
            [],
            StyleDriftHumanGap);
    }
}
