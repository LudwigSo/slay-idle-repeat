namespace SlayIdleRepeat.AssetPipeline.Qa.Checks;

/// <summary>Checklist item 10: file named correctly and packed into the correct atlas.</summary>
/// <remarks>
/// <para>
/// Two mechanical claims and no thresholds. The name goes through <see cref="AssetNaming"/>. The
/// packing is read off the atlas-pack result the caller supplies: the pack must be for the atlas
/// assigned to the row, and it must hold a placement for the row's id.
/// </para>
/// <para>
/// Name is checked before atlas, and the first failure is the reported one: a pack is searched by
/// asset id, so a file whose stem does not parse makes the membership question unanswerable rather
/// than answerable in the negative.
/// </para>
/// <para>
/// A missing pack is a failure, not an absence: a row assigned an atlas that arrives with
/// <see cref="QaSubject.AtlasPack"/> null has not been packed. The one case where null is right is a
/// row assigned no atlas (backgrounds), which passes on the name alone.
/// </para>
/// </remarks>
public sealed class NamingAndAtlasCheck : IQaCheck
{
    /// <summary>
    /// How many placements the pack holds for this asset. One is right; zero means it was not
    /// packed, and more than one means it was packed twice.
    /// </summary>
    public const string PlacementCountMeasurement = "atlasPlacementCount";

    /// <inheritdoc/>
    public int ItemNumber => 10;

    /// <inheritdoc/>
    public string ChecklistText => Doc15PartF.Item10;

    /// <inheritdoc/>
    public QaClassification Classification => QaClassification.Mechanical;

    /// <inheritdoc/>
    public string DocReference => "15 §D1, §D2";

    /// <inheritdoc/>
    public string? HumanGap => null;

    /// <inheritdoc/>
    public QaOutcome Evaluate(QaSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var name = AssetNaming.Validate(subject.DeliveredFileName, subject.Asset.Id);
        var placements = PlacementsFor(subject);
        IReadOnlyList<StepMeasurement> measurements =
        [
            new StepMeasurement(PlacementCountMeasurement, placements, "count", DocReference),
        ];

        // Name is checked first: a pack is searched by asset id, so an unparseable stem makes
        // membership unanswerable rather than answerable in the negative.
        if (!name.IsValid)
        {
            return new QaOutcome(
                QaVerdict.Fail,
                ItemNumber,
                $"`15` §D1: {name.Rejection}. {name.Reason}",
                measurements,
                HumanGap: null);
        }

        var atlas = subject.Asset.Atlas;
        if (atlas is null)
        {
            return new QaOutcome(
                QaVerdict.Pass,
                ItemNumber,
                $"'{subject.DeliveredFileName}' is named per `15` §D1, and §D2 assigns this row no " +
                "atlas: \"Backgrounds are not atlased (they are full-screen and streamed per " +
                "biome).\" There is nothing to be packed into.",
                measurements,
                HumanGap: null);
        }

        if (subject.AtlasPack is null)
        {
            return new QaOutcome(
                QaVerdict.Fail,
                ItemNumber,
                $"`15` §D2 assigns '{subject.Asset.Id}' to {atlas} and no `15` §B4 step 7 pack was " +
                "supplied, so it has not been packed. An absent pack is not an absent requirement.",
                measurements,
                HumanGap: null);
        }

        if (!string.Equals(subject.AtlasPack.AtlasId, atlas, StringComparison.Ordinal))
        {
            return new QaOutcome(
                QaVerdict.Fail,
                ItemNumber,
                $"The pack supplied is for {subject.AtlasPack.AtlasId} and `15` §D2 assigns " +
                $"'{subject.Asset.Id}' to {atlas}. A pack for the wrong atlas holding the right " +
                "asset is still the wrong atlas.",
                measurements,
                HumanGap: null);
        }

        return placements == 1
            ? new QaOutcome(
                QaVerdict.Pass,
                ItemNumber,
                $"'{subject.DeliveredFileName}' is named per `15` §D1 and is placed once in {atlas}.",
                measurements,
                HumanGap: null)
            : new QaOutcome(
                QaVerdict.Fail,
                ItemNumber,
                $"{PlacementCountMeasurement} is {placements} in {atlas}: " +
                (placements == 0
                    ? "the pack does not hold this asset at all."
                    : "the pack holds it more than once, so it ships twice and two sets of UVs " +
                      "disagree about which is authoritative."),
                measurements,
                HumanGap: null);
    }

    /// <summary>How many placements the supplied pack holds for this asset.</summary>
    /// <param name="subject">The asset under judgement.</param>
    private static int PlacementsFor(QaSubject subject) =>
        subject.AtlasPack?.Placements.Count(placement =>
            string.Equals(placement.AssetId, subject.Asset.Id, StringComparison.Ordinal)) ?? 0;
}
