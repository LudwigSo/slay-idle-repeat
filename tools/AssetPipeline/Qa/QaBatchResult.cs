namespace SlayIdleRepeat.AssetPipeline.Qa;

/// <summary>What the acceptance gate concluded for a batch.</summary>
/// <remarks>
/// Four outcomes, not two: "rejected", "nobody has calibrated the threshold that would decide it",
/// and "a human has not looked yet" are different states, and collapsing them into a boolean is
/// precisely how an uncalibrated check becomes a passing one.
/// </remarks>
public enum QaDecision
{
    /// <summary>
    /// Every item concluded, and concluded well. Unreachable from a full eleven-item run — see
    /// <see cref="AwaitingHumanReview"/> — and reachable for a caller grading a subset.
    /// </summary>
    Accepted,

    /// <summary>At least one item returned <see cref="QaVerdict.Fail"/>.</summary>
    Rejected,

    /// <summary>
    /// Nothing failed, but at least one item returned <see cref="QaVerdict.Uncalibrated"/>. The
    /// batch is not accepted: a threshold nobody has stated has decided nothing.
    /// </summary>
    BlockedByUncalibratedThreshold,

    /// <summary>
    /// Nothing failed and nothing is uncalibrated, but at least one item is
    /// <see cref="QaVerdict.HumanGapOnly"/> and no machine can close it.
    /// </summary>
    AwaitingHumanReview,
}

/// <summary>Every checklist item's outcome for one asset, plus the acceptance decision they add up to.</summary>
/// <remarks>
/// <para>
/// Machinery cannot accept a batch: a full <see cref="QaChecklist"/> run always includes human-only
/// items, so the best <see cref="Decision"/> it can ever reach is
/// <see cref="QaDecision.AwaitingHumanReview"/>.
/// </para>
/// <para>
/// Precedence: a known defect outranks an unknown one. A <see cref="QaVerdict.Fail"/> anywhere
/// gives <see cref="QaDecision.Rejected"/> even when something else is uncalibrated — the asset has
/// to be regenerated either way.
/// </para>
/// </remarks>
/// <param name="AssetId">The asset these outcomes are about.</param>
/// <param name="Outcomes">
/// One outcome per item run. A run holding no outcomes is a loud failure rather than a vacuous
/// acceptance.
/// </param>
public sealed record QaBatchResult(string AssetId, IReadOnlyList<QaOutcome> Outcomes)
{
    /// <summary>The acceptance decision. See the type's remarks for the precedence.</summary>
    public QaDecision Decision
    {
        get
        {
            var graded = Graded;

            if (graded.Any(outcome => outcome.Verdict == QaVerdict.Fail))
            {
                return QaDecision.Rejected;
            }

            if (graded.Any(outcome => outcome.Verdict == QaVerdict.Uncalibrated))
            {
                return QaDecision.BlockedByUncalibratedThreshold;
            }

            return graded.Any(outcome => outcome.Verdict == QaVerdict.HumanGapOnly)
                ? QaDecision.AwaitingHumanReview
                : QaDecision.Accepted;
        }
    }

    /// <summary>True only for <see cref="QaDecision.Accepted"/>.</summary>
    public bool Accepted => Decision == QaDecision.Accepted;

    /// <summary>Every item that returned <see cref="QaVerdict.Fail"/>, in item order.</summary>
    public IReadOnlyList<QaOutcome> Failures => OfVerdict(QaVerdict.Fail);

    /// <summary>Every item that returned <see cref="QaVerdict.Uncalibrated"/>, in item order.</summary>
    public IReadOnlyList<QaOutcome> Uncalibrated => OfVerdict(QaVerdict.Uncalibrated);

    /// <summary>
    /// Every human gap any item surfaced, in item order — including item 1's, which a mechanical
    /// pass does not close.
    /// </summary>
    public IReadOnlyList<string> HumanGaps =>
    [
        .. Outcomes
            .Where(outcome => !string.IsNullOrWhiteSpace(outcome.HumanGap))
            .OrderBy(outcome => outcome.ItemNumber)
            .Select(outcome => outcome.HumanGap!),
    ];

    /// <summary>The outcomes, or a loud failure when there are none.</summary>
    /// <remarks>
    /// "Nothing failed" over an empty list is vacuously true — exactly the shape of a checklist that
    /// silently stopped running — so a decision over no outcomes is refused rather than answered.
    /// </remarks>
    private IReadOnlyList<QaOutcome> Graded => Outcomes.Count > 0
        ? Outcomes
        : throw new InvalidOperationException(
            $"No `15` Part F item was run against '{AssetId}', so there is no acceptance decision " +
            "to report. A run holding no outcomes would otherwise read as a clean one, which is how " +
            "a checklist that stopped running passes forever.");

    /// <summary>Every outcome carrying one verdict, in item order.</summary>
    /// <param name="verdict">The verdict to filter by.</param>
    private IReadOnlyList<QaOutcome> OfVerdict(QaVerdict verdict) =>
    [
        .. Outcomes
            .Where(outcome => outcome.Verdict == verdict)
            .OrderBy(outcome => outcome.ItemNumber),
    ];
}
