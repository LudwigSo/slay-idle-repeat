using SlayIdleRepeat.AssetPipeline;
using SlayIdleRepeat.AssetPipeline.Qa;

namespace SlayIdleRepeat.AssetPlaceholders;

/// <summary>Why a register row got no placeholder. Exactly one reason, the first that applied.</summary>
public enum PlaceholderSkipReason
{
    /// <summary>A ruling removed the row.</summary>
    CutByRuling,

    /// <summary>
    /// The register states no delivery size for the row, so there is no canvas to deliver it on.
    /// </summary>
    NoDeliverySize,

    /// <summary>
    /// The register authorises a pivot for characters and icons and nothing else, and the row is
    /// neither. The margin step pads against the pivot, so there is nothing to invent.
    /// </summary>
    NoPivot,
}

/// <summary>One register row that got no placeholder, and why.</summary>
/// <param name="AssetId">The register id.</param>
/// <param name="Section">The register section the row was transcribed from.</param>
/// <param name="Reason">Which of the three states the row is in.</param>
/// <param name="Detail">The refusal in the words the register or the pipeline used.</param>
public sealed record PlaceholderSkip(
    string AssetId, string Section, PlaceholderSkipReason Reason, string Detail);

/// <summary>One row that should have produced a placeholder and threw instead.</summary>
/// <remarks>
/// Kept apart from <see cref="PlaceholderSkip"/>: a skip is the register declining to state
/// something; a failure is a defect in this generator or in the pipeline.
/// </remarks>
/// <param name="AssetId">The register id.</param>
/// <param name="Section">The register section.</param>
/// <param name="ExceptionType">The exception's type name.</param>
/// <param name="Message">Its message.</param>
public sealed record PlaceholderFailure(
    string AssetId, string Section, string ExceptionType, string Message);

/// <summary>One placeholder that was generated, delivered and graded.</summary>
/// <param name="AssetId">The register id.</param>
/// <param name="FileName">The delivered file name.</param>
/// <param name="Atlas">The atlas the row names, or null where the register assigns none.</param>
/// <param name="EncodedBytes">The size of the encoded PNG.</param>
/// <param name="Stamped">
/// Whether the asset id was legibly stamped on it — false when the card was too small to carry the
/// stamp at any whole scale, so "every placeholder is stamped" must never be claimed unconditionally.
/// </param>
/// <param name="Qa">Every QA checklist item's outcome for it.</param>
public sealed record GeneratedPlaceholder(
    string AssetId, string FileName, string? Atlas, int EncodedBytes, bool Stamped, QaBatchResult Qa);

/// <summary>
/// A place where this generator knowingly does something the design docs forbid, because the task
/// it exists for requires it.
/// </summary>
/// <param name="Id">A stable id, e.g. <c>DEP_A3_ID_STAMP</c>.</param>
/// <param name="DocReference">What the doc says, by section.</param>
/// <param name="Requirement">The requirement, in the doc's own words.</param>
/// <param name="Taken">What this generator does instead.</param>
/// <param name="Why">Why, and who authorised it.</param>
public sealed record PlaceholderDeparture(
    string Id, string DocReference, string Requirement, string Taken, string Why);

/// <summary>
/// One generated placeholder that failed a fully mechanical QA item.
/// </summary>
/// <param name="AssetId">The register id.</param>
/// <param name="Outcome">The failing item's outcome, carrying which item and what it measured.</param>
public sealed record MechanicalFailure(string AssetId, QaOutcome Outcome);

/// <summary>
/// What one whole run over the register did, reconciled against the register's own totals.
/// </summary>
/// <remarks>
/// <see cref="Reconciles"/> is what stops "641 placeholders generated" being read as "641 of 641":
/// generated + skipped + failed has to equal the number of art rows the register holds.
/// </remarks>
public sealed record PlaceholderBatchReport
{
    /// <summary>Every art row in the register — cut, size-less, pivot-less and generatable alike.</summary>
    public required int ArtRowsInRegister { get; init; }

    /// <summary>Every audio row in the register. Out of scope; carried so the totals reconcile.</summary>
    public required int AudioRowsInRegister { get; init; }

    /// <summary>The placeholders that were generated, in id order.</summary>
    public required IReadOnlyList<GeneratedPlaceholder> Generated { get; init; }

    /// <summary>The rows that got none, in id order.</summary>
    public required IReadOnlyList<PlaceholderSkip> Skipped { get; init; }

    /// <summary>The rows that threw, in id order.</summary>
    public required IReadOnlyList<PlaceholderFailure> Failed { get; init; }

    /// <summary>Every pipeline deviation the run took, by id, with how many assets took it.</summary>
    public required IReadOnlyDictionary<string, int> Deviations { get; init; }

    /// <summary>Every contradiction the run ran into, by id, with how many assets hit it.</summary>
    public required IReadOnlyDictionary<string, int> Contradictions { get; init; }

    /// <summary>Every atlas that was packed, with its page count and placement count.</summary>
    public required IReadOnlyList<AtlasPackResult> Atlases { get; init; }

    /// <summary>
    /// Every place this generator knowingly departs from the design docs, empty when it drew nothing.
    /// </summary>
    /// <remarks>
    /// Exactly one departure exists: the id stamp, authorised for placeholders specifically by the
    /// M8 kickoff so a missing asset is identifiable on screen. It is reported here because the QA
    /// checklist's own text-in-image item is a human-review item that returns no verdict of its own.
    /// </remarks>
    public IReadOnlyList<PlaceholderDeparture> Departures => Generated.Count == 0
        ? []
        : [IdStampDeparture];

    /// <summary>
    /// The generated placeholders whose card was too small to carry a legible id stamp.
    /// </summary>
    /// <remarks>
    /// An illegible smear would be worse than no stamp, so the renderer draws nothing rather than
    /// something unreadable — this is what stops that silent choice reading as "every placeholder
    /// is stamped".
    /// </remarks>
    public IReadOnlyList<string> Unstamped =>
    [
        .. Generated
            .Where(placeholder => !placeholder.Stamped)
            .Select(placeholder => placeholder.AssetId)
            .OrderBy(id => id, StringComparer.Ordinal),
    ];

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
    /// The worst QA decision any generated placeholder reached — the batch's decision.
    /// </summary>
    /// <remarks>
    /// A run holding no graded asset has no decision, and says so rather than answering Accepted
    /// over an empty batch — the shape a generator that silently produced nothing would otherwise take.
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
    /// Every generated placeholder whose fully mechanical QA items (canvas/pivot and naming/atlas)
    /// returned <see cref="QaVerdict.Fail"/>.
    /// </summary>
    /// <remarks>
    /// These two items are the only ones this generator entirely controls — the canvas, the pivot,
    /// the name and the atlas it packs into — so a failure here is a defect in this tool, never an
    /// uncalibrated threshold or a missing human.
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
    /// How bad a QA decision is, so a batch can take the worst of its assets'.
    /// </summary>
    /// <remarks>
    /// An explicit map, not the enum's declaration order — ranking by declaration order would put
    /// "not yet reviewed" above "an item failed".
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
    /// The checklist's two fully mechanical items: canvas/pivot and naming/atlas membership.
    /// </summary>
    /// <remarks>
    /// Asked from <see cref="QaChecklist"/> rather than hardcoded, so an item that changed
    /// classification cannot leave this list saying otherwise.
    /// </remarks>
    public static IReadOnlySet<int> MechanicalItems { get; } = new HashSet<int>(
        new QaChecklist()
            .OfClassification(QaClassification.Mechanical)
            .Select(item => item.ItemNumber));

    /// <summary>The one departure this generator takes. See <see cref="Departures"/>.</summary>
    public static PlaceholderDeparture IdStampDeparture { get; } = new(
        "DEP_A3_ID_STAMP",
        "15 §A3, Part F item 8",
        "Never render text inside a generated image. All text is engine-rendered. / No text, " +
        "watermark or signature anywhere in the image.",
        "Every placeholder carries its `15` §D1 asset id, the word \"placeholder\" and its §C " +
        "delivery size, drawn from a 5×7 bitmap font declared in this tool.",
        "Authorised for placeholders specifically by the M8 kickoff, 2026-08-12. A placeholder " +
        "exists so that a missing asset is self-identifying on screen; an unlabelled box tells " +
        "nobody which of hundreds of slots is empty. 🔒 It is authorised for THIS batch and for " +
        "nothing else — every one of these files is scaffolding a real asset overwrites, and a " +
        "delivered asset carrying text fails §A3 exactly as it always did.");
}
