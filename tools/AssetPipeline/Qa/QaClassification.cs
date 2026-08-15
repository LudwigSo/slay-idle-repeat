namespace SlayIdleRepeat.AssetPipeline.Qa;

/// <summary>What kind of thing one checklist item is, once you ask whether a machine can decide it.</summary>
/// <remarks>
/// An item is <see cref="Human"/> when no managed code available here can decide it — and it stays
/// <see cref="Human"/> even when this project ships a mechanical <em>proxy</em> that produces useful
/// evidence for it (item 8), rather than dressing that proxy up as the real test.
/// </remarks>
public enum QaClassification
{
    /// <summary>Decidable in full by managed code against authorised numbers. Items 7 and 10.</summary>
    Mechanical,

    /// <summary>
    /// Decidable in full by managed code, but only once somebody calibrates a threshold. Items 1, 3,
    /// 5 and 6. Until the threshold is stated the check reports <see cref="QaVerdict.Uncalibrated"/>
    /// — never <see cref="QaVerdict.Pass"/>.
    /// </summary>
    MechanicalUncalibratedThreshold,

    /// <summary>
    /// Not decidable by any code this project is allowed to contain. Items 2, 4, 8, 9 and 11. A
    /// human item never returns <see cref="QaVerdict.Pass"/> — see <see cref="QaVerdict"/>.
    /// </summary>
    Human,
}
