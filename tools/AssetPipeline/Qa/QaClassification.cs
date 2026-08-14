namespace SlayIdleRepeat.AssetPipeline.Qa;

/// <summary>
/// What kind of thing one `15` Part F checklist item is, once you ask whether a machine can decide it.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 The split is a deliverable of M8-06, not an implementation detail: <b>2 mechanical · 4
/// mechanical-with-an-uncalibrated-threshold · 5 human</b>. It is pinned by
/// <c>QaChecklistTests</c> so the day someone reclassifies a human item as mechanical is the day a
/// test goes red and a human has to defend the change.
/// </para>
/// <para>
/// 🔒 Assumption A5: <em>do not dress a heuristic up as the real test.</em> An item is
/// <see cref="Human"/> when no managed code available here can decide it — and it stays
/// <see cref="Human"/> even when this project ships a mechanical <em>proxy</em> that produces
/// useful evidence for it. Item 8 is exactly that case.
/// </para>
/// </remarks>
public enum QaClassification
{
    /// <summary>
    /// Decidable in full by managed code against numbers `15` itself authorises. Two items:
    /// 7 (canvas size and pivot per §C) and 10 (named per §D1 and packed into the correct atlas).
    /// </summary>
    Mechanical,

    /// <summary>
    /// Decidable in full by managed code, but only once somebody calibrates a threshold `15` does
    /// not authorise. Four items: 1, 3, 5 and 6. Until the threshold is stated the check reports
    /// <see cref="QaVerdict.Uncalibrated"/> — never <see cref="QaVerdict.Pass"/>.
    /// </summary>
    MechanicalUncalibratedThreshold,

    /// <summary>
    /// Not decidable by any code this project is allowed to contain. Five items: 2, 4, 8, 9 and 11.
    /// A human item never returns <see cref="QaVerdict.Pass"/> — see <see cref="QaVerdict"/>.
    /// </summary>
    Human,
}
