namespace SlayIdleRepeat.AssetPipeline.Qa.Checks;

/// <summary>
/// `15` Part F item 10: <em>"File named per §D1 and packed into the correct atlas"</em>.
/// </summary>
/// <remarks>
/// <para>
/// Two mechanical claims and no thresholds. The name goes through <see cref="AssetNaming"/> —
/// §D1's grammar, plus the stem equalling the row's id. The packing is read off the `15` §B4 step 7
/// result the caller supplies: the pack must be for the atlas §D2 assigns the row, and it must hold
/// a placement for the row's id.
/// </para>
/// <para>
/// 🔒 <b>Order: name, then atlas</b> — the first failure is the reported one. A pack is searched by
/// asset id, so a file whose stem does not parse as a §D1 id makes the membership question
/// unanswerable rather than answerable in the negative.
/// </para>
/// <para>
/// 🔒 <b>A missing pack is a failure, not an absence.</b> A row §D2 assigns an atlas to that
/// arrives with <see cref="QaSubject.AtlasPack"/> null has not been packed, and the item says
/// "packed into the correct atlas". The one case where null is right is a row §D2 assigns no atlas
/// — "Backgrounds are not atlased (they are full-screen and streamed per biome)" — and that passes
/// on the name alone, with the reason saying so.
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
    public QaOutcome Evaluate(QaSubject subject) => throw new NotImplementedException();
}
