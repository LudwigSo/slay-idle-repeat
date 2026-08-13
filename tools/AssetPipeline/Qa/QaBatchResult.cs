namespace SlayIdleRepeat.AssetPipeline.Qa;

/// <summary>
/// What `15` Part F's gate — <em>"Before an asset batch is accepted"</em> — concluded.
/// </summary>
/// <remarks>
/// 🔒 Four outcomes, not two. "Rejected" and "nobody has calibrated the threshold that would decide
/// it" and "a human has not looked yet" are three different states of the world, and collapsing
/// them into a boolean is precisely how an uncalibrated check becomes a passing one.
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

/// <summary>
/// Every `15` Part F item's outcome for one asset, plus the acceptance decision they add up to.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Machinery cannot accept a batch.</b> A full <see cref="QaChecklist"/> run always includes
/// five <see cref="QaClassification.Human"/> items, so the best <see cref="Decision"/> it can ever
/// reach is <see cref="QaDecision.AwaitingHumanReview"/>. That is assumption A5 expressed as a
/// return value rather than as a comment, and <c>QaBatchResultTests</c> pins it.
/// </para>
/// <para>
/// 🔒 <b>Precedence: a known defect outranks an unknown one.</b> A <see cref="QaVerdict.Fail"/>
/// anywhere gives <see cref="QaDecision.Rejected"/> even when something else is uncalibrated — the
/// asset has to be regenerated either way, and reporting "blocked on calibration" would send a
/// reviewer to measure a threshold for an asset that is already going back.
/// </para>
/// </remarks>
/// <param name="AssetId">The asset these outcomes are about.</param>
/// <param name="Outcomes">
/// One outcome per item run, in `15` Part F order. A run holding no outcomes is a loud failure
/// rather than a vacuous acceptance.
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

    /// <summary>
    /// The outcomes, or a loud failure when there are none.
    /// </summary>
    /// <remarks>
    /// 🔒 Steering rule S3. "Nothing failed" over an empty list is vacuously true, and that is
    /// exactly the shape of a checklist that silently stopped running — so a decision over no
    /// outcomes is refused rather than answered. The lists below do not refuse: an empty
    /// <see cref="Failures"/> is a real, useful answer; an empty acceptance is not.
    /// </remarks>
    private IReadOnlyList<QaOutcome> Graded => Outcomes.Count > 0
        ? Outcomes
        : throw new InvalidOperationException(
            $"No `15` Part F item was run against '{AssetId}', so there is no acceptance decision " +
            "to report. A run holding no outcomes would otherwise read as a clean one, which is how " +
            "a checklist that stopped running passes forever.");

    /// <summary>Every outcome carrying one verdict, in `15` Part F item order.</summary>
    /// <param name="verdict">The verdict to filter by.</param>
    private IReadOnlyList<QaOutcome> OfVerdict(QaVerdict verdict) =>
    [
        .. Outcomes
            .Where(outcome => outcome.Verdict == verdict)
            .OrderBy(outcome => outcome.ItemNumber),
    ];
}
