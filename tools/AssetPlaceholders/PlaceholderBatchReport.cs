using SlayIdleRepeat.AssetPipeline;
using SlayIdleRepeat.AssetPipeline.Qa;

namespace SlayIdleRepeat.AssetPlaceholders;

/// <summary>Why a register row got no placeholder. Exactly one reason, the first that applied.</summary>
/// <remarks>
/// 🔒 Steering rule S2: pin the identity, not the symptom. "941 rows were skipped" is three
/// different facts about `15` with three different owners, and O30 needs the three numbers
/// separately at M11-01.
/// </remarks>
public enum PlaceholderSkipReason
{
    /// <summary>A ruling removed the row. All 32 `15` §E19 VFX sheets, after ruling O8.</summary>
    CutByRuling,

    /// <summary>
    /// `15` §C states no delivery size for the row, so there is no canvas to deliver it on.
    /// </summary>
    NoDeliverySize,

    /// <summary>
    /// `15` §C authorises a pivot for characters and for icons and for nothing else, and the row is
    /// neither. §B4 step 2 pads against the pivot, so there is nothing to invent.
    /// </summary>
    NoPivot,
}

/// <summary>One register row that got no placeholder, and why.</summary>
/// <param name="AssetId">The `15` §D1 id.</param>
/// <param name="Section">The `15` §E-section the row was transcribed from.</param>
/// <param name="Reason">Which of the three states the row is in.</param>
/// <param name="Detail">The refusal in the words the register or the pipeline used.</param>
public sealed record PlaceholderSkip(
    string AssetId, string Section, PlaceholderSkipReason Reason, string Detail);

/// <summary>One row that should have produced a placeholder and threw instead.</summary>
/// <remarks>
/// 🔒 Kept apart from <see cref="PlaceholderSkip"/>. A skip is `15` declining to state something; a
/// failure is a defect in this generator or in the pipeline, and a report that added the two
/// together would hide the second behind the first.
/// </remarks>
/// <param name="AssetId">The `15` §D1 id.</param>
/// <param name="Section">The `15` §E-section.</param>
/// <param name="ExceptionType">The exception's type name.</param>
/// <param name="Message">Its message.</param>
public sealed record PlaceholderFailure(
    string AssetId, string Section, string ExceptionType, string Message);

/// <summary>One placeholder that was generated, delivered and graded.</summary>
/// <param name="AssetId">The `15` §D1 id.</param>
/// <param name="FileName">The delivered `15` §D1 file name.</param>
/// <param name="Atlas">The `15` §D2 atlas the row names, or null where §D2 assigns none.</param>
/// <param name="EncodedBytes">The size of the PNG-32 `15` §B4 step 6 produced.</param>
/// <param name="Qa">Every `15` Part F item's outcome for it.</param>
public sealed record GeneratedPlaceholder(
    string AssetId, string FileName, string? Atlas, int EncodedBytes, QaBatchResult Qa);

/// <summary>
/// One generated placeholder that failed a fully mechanical `15` Part F item.
/// </summary>
/// <param name="AssetId">The `15` §D1 id.</param>
/// <param name="Outcome">The failing item's outcome, carrying which item and what it measured.</param>
public sealed record MechanicalFailure(string AssetId, QaOutcome Outcome);

/// <summary>
/// What one whole run over M8-09's register did, reconciled against the register's own totals.
/// </summary>
/// <remarks>
/// 🔒 <b>The reconciliation is a member, not a paragraph in a report.</b>
/// <see cref="Reconciles"/> is what stops "641 placeholders generated" being read as "641 of 641":
/// generated + skipped + failed has to equal the number of art rows the register holds, or one of
/// the four numbers is wrong and nobody can tell which.
/// </remarks>
public sealed record PlaceholderBatchReport
{
    /// <summary>Every art row in M8-09's register — cut, size-less, pivot-less and generatable alike.</summary>
    public required int ArtRowsInRegister { get; init; }

    /// <summary>Every audio row in M8-09's register. 🔒 Out of scope; carried so the 1,080 reconciles.</summary>
    public required int AudioRowsInRegister { get; init; }

    /// <summary>The placeholders that were generated, in `15` §D1 id order.</summary>
    public required IReadOnlyList<GeneratedPlaceholder> Generated { get; init; }

    /// <summary>The rows that got none, in `15` §D1 id order.</summary>
    public required IReadOnlyList<PlaceholderSkip> Skipped { get; init; }

    /// <summary>The rows that threw, in `15` §D1 id order.</summary>
    public required IReadOnlyList<PlaceholderFailure> Failed { get; init; }

    /// <summary>Every `15` §B4 deviation the run took, by id, with how many assets took it.</summary>
    public required IReadOnlyDictionary<string, int> Deviations { get; init; }

    /// <summary>Every `15` contradiction the run ran into, by id, with how many assets hit it.</summary>
    public required IReadOnlyDictionary<string, int> Contradictions { get; init; }

    /// <summary>Every atlas that was packed, with its page count and placement count.</summary>
    public required IReadOnlyList<AtlasPackResult> Atlases { get; init; }

    /// <summary>How many rows were skipped for one reason.</summary>
    /// <param name="reason">The reason to count.</param>
    public int SkippedFor(PlaceholderSkipReason reason) =>
        Skipped.Count(skip => skip.Reason == reason);

    /// <summary>
    /// How many art rows the run accounted for — generated, skipped or failed.
    /// </summary>
    public int Accounted => Generated.Count + Skipped.Count + Failed.Count;

    /// <summary>
    /// True when every art row in the register was accounted for exactly once.
    /// </summary>
    public bool Reconciles => Accounted == ArtRowsInRegister;

    /// <summary>
    /// The worst `15` Part F decision any generated placeholder reached — the batch's decision.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 Part F gates <em>a batch</em> (<em>"Before an asset batch is accepted"</em>), and a
    /// batch's decision is the worst of its assets'. The order is
    /// <see cref="QaDecision.Rejected"/> ▸ <see cref="QaDecision.BlockedByUncalibratedThreshold"/> ▸
    /// <see cref="QaDecision.AwaitingHumanReview"/> ▸ <see cref="QaDecision.Accepted"/>, which is
    /// <see cref="QaBatchResult.Decision"/>'s own precedence lifted from one asset to many.
    /// </para>
    /// <para>
    /// 🔒 <b>A run holding no graded asset has no decision</b>, and says so rather than answering
    /// <see cref="QaDecision.Accepted"/> over nothing (steering rule S3). That is the shape a
    /// generator that silently produced no output would otherwise take.
    /// </para>
    /// </remarks>
    public QaDecision Decision => Generated.Count > 0
        ? Generated
            .Select(placeholder => placeholder.Qa.Decision)
            .MaxBy(Severity)
        : throw new InvalidOperationException(
            "No placeholder was generated, so there is no `15` Part F decision to report. A gate " +
            "decision over an empty batch reads as a clean one, which is exactly how a generator " +
            "that produced nothing passes.");

    /// <summary>
    /// Every generated placeholder whose `15` Part F item 7 (canvas size and pivot) or item 10
    /// (§D1 naming and atlas membership) returned <see cref="QaVerdict.Fail"/>.
    /// </summary>
    /// <remarks>
    /// 🔒 These two items are the only fully <see cref="QaClassification.Mechanical"/> ones on the
    /// checklist, and they are the two this generator entirely controls: the canvas it draws, the
    /// pivot it centres against, the name it writes and the atlas it packs into. A failure here is a
    /// defect in this tool, never an uncalibrated threshold and never a missing human. Everything
    /// else on Part F is either blocked on a number nobody has stated or waiting for a person.
    /// </remarks>
    public IReadOnlyList<MechanicalFailure> MechanicalFailures =>
    [
        .. Generated
            .SelectMany(placeholder => placeholder.Qa.Failures
                .Where(outcome => MechanicalItems.Contains(outcome.ItemNumber))
                .Select(outcome => new MechanicalFailure(placeholder.AssetId, outcome)))
            .OrderBy(failure => failure.AssetId, StringComparer.Ordinal)
            .ThenBy(failure => failure.Outcome.ItemNumber),
    ];

    /// <summary>
    /// How bad a `15` Part F decision is, so a batch can take the worst of its assets'.
    /// </summary>
    /// <remarks>
    /// 🔒 An explicit map, <b>not</b> the enum's declaration order. <see cref="QaDecision"/> is
    /// declared Accepted, Rejected, BlockedByUncalibratedThreshold, AwaitingHumanReview — so
    /// <c>Max()</c> over the enum would rank "a human has not looked yet" above "an item failed",
    /// and a batch holding one rejected asset would report as merely awaiting review. The order
    /// below is <see cref="QaBatchResult.Decision"/>'s own precedence, lifted from one asset to many.
    /// </remarks>
    /// <param name="decision">The decision to rank.</param>
    private static int Severity(QaDecision decision) => decision switch
    {
        QaDecision.Rejected => 3,
        QaDecision.BlockedByUncalibratedThreshold => 2,
        QaDecision.AwaitingHumanReview => 1,
        QaDecision.Accepted => 0,
        _ => throw new InvalidOperationException(
            $"{decision} is a `15` Part F decision this batch does not know how to rank. A fifth " +
            "outcome needs a place in the precedence, not a default."),
    };

    /// <summary>
    /// `15` Part F's two fully mechanical items: 7 (canvas size and pivot) and 10 (naming and atlas).
    /// </summary>
    /// <remarks>
    /// 🔒 Not a hardcoded pair of literals sitting beside the checklist that decides the same thing:
    /// this asks <see cref="QaChecklist"/> which of its items are
    /// <see cref="QaClassification.Mechanical"/>, so an item that changed classification cannot
    /// leave this list saying otherwise.
    /// </remarks>
    public static IReadOnlySet<int> MechanicalItems { get; } = new HashSet<int>(
        new QaChecklist()
            .OfClassification(QaClassification.Mechanical)
            .Select(item => item.ItemNumber));
}
