using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// Hermetic <c>tuning/currencies.json</c> fixtures — the <c>loginCalendar</c> block — and
/// the <b>combined</b> snapshot a command reading more than one tuning document needs.
/// </summary>
/// <remarks>
/// <c>Core.Tests</c> is hermetic, so this mirrors the shipped file rather than reading it;
/// an <c>Application.Tests</c> rule pins the two halves together. Neither is sufficient alone: this
/// proves the rules are right about the numbers, that one proves those are the numbers we ship.
/// <para>
/// Only <c>cycleDays</c> is authored — transcribing the twenty-eight reward rows would imply
/// something reads them, and nothing does yet.
/// </para>
/// </remarks>
internal static class TuningDocuments
{
    /// <summary>Where the calendar block lives.</summary>
    internal const string CurrenciesPath = "tuning/currencies.json";

    /// <summary>The calendar runs 28 days and then restarts at day 1.</summary>
    /// <remarks>
    /// <c>const</c> rather than <c>static readonly</c> so <c>[InlineData]</c> can take it: a wrap
    /// test that restated 28 as a literal would keep passing after the data moved.
    /// </remarks>
    internal const int ShippedCycleDays = 28;

    /// <summary>
    /// The whole shipped tuning set these readers use: the energy and Legend Level blocks of
    /// <c>progression.json</c>, plus the calendar block of <c>currencies.json</c>.
    /// </summary>
    internal static ContentSnapshot Shipped { get; } = With();

    /// <summary>
    /// The shipped set with individual calendar leaves replaced. Pass
    /// <see cref="ContentValue.Unauthorised"/> for a deliberate <c>null</c> hole, or omit to keep the
    /// shipped value.
    /// </summary>
    /// <remarks>
    /// ⚠️ <paramref name="legendLevelMin"/> exists because the shipped floor is 1 and so is the literal
    /// anyone would write, so replacing <c>LegendTuning</c>'s read with <c>1</c> left every test green.
    /// Proving the read needs a content set whose floor is <em>not</em> the shipped one.
    /// </remarks>
    internal static ContentSnapshot With(
        ContentValue? cycleDays = null, ContentValue? legendLevelMin = null) =>
        new(
            ProgressionDocuments.Shipped.Version,
            [
                ProgressionDocuments.With(legendLevelMin: legendLevelMin)
                    .GetDocument(ProgressionDocuments.DocumentPath),
                Currencies(cycleDays),
                ChapterDocuments.Document(chapterId: 1, ChapterDocuments.ChapterOnePath),
            ]);

    /// <summary>
    /// A content set holding <b>only</b> <c>tuning/currencies.json</c>.
    /// </summary>
    /// <remarks>
    /// For <c>LoginCalendarTuning</c>'s own tests, which are about that reader and must not be able
    /// to pass because some other document happened to be present.
    /// </remarks>
    internal static ContentSnapshot CurrenciesOnly(ContentValue? cycleDays = null) =>
        new(ProgressionDocuments.Shipped.Version, [Currencies(cycleDays)]);

    /// <summary>A content set with <b>no</b> <c>tuning/currencies.json</c> at all.</summary>
    /// <remarks>
    /// The "missing document" case, which is a different failure from "the pointer holds null" and
    /// from "the value is unusable" — three doors into <c>LoginCalendarTuning.Read</c>, and the whole
    /// point of typed readers is that they are told apart.
    /// </remarks>
    internal static ContentSnapshot WithoutCurrencies() => ProgressionDocuments.Shipped;

    /// <summary>The chapter-scaling factor, shared with ad bundles and the shop.</summary>
    internal const double ShippedAdBundleScalar = 0.35;

    /// <summary>The fork-bias boost multiplier, as shipped.</summary>
    internal const double ShippedForkBiasPlusMultiplier = 2.5;

    /// <summary>The fork-bias suppression multiplier, as shipped.</summary>
    internal const double ShippedForkBiasMinusMultiplier = 0.2;

    private static ContentDocument Currencies(ContentValue? cycleDays)
    {
        var calendar = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["cycleDays"] = cycleDays ?? ContentValue.Number(ShippedCycleDays),
        });

        return new ContentDocument(
            CurrenciesPath,
            ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
            {
                ["loginCalendar"] = calendar,
                ["chapterScalars"] = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
                {
                    ["adBundleScalar"] = ContentValue.Number((decimal)ShippedAdBundleScalar),
                }),
                ["boardGeneration"] = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
                {
                    ["forkBiasPlusMultiplier"] = ContentValue.Number((decimal)ShippedForkBiasPlusMultiplier),
                    ["forkBiasMinusMultiplier"] = ContentValue.Number((decimal)ShippedForkBiasMinusMultiplier),
                }),
                ["minigameRewards"] = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
                {
                    ["MG_CHEST_PICK"] = Rewards(
                        (150, 0, 0, 0, 0),
                        (300, 20, 0, 0, 0),
                        (500, 60, 15, 0, 0)),
                    ["MG_TIMING_BAR"] = Rewards(
                        (100, 0, 0, 0, 0),
                        (250, 0, 0, 0, 0),
                        (400, 30, 0, 0, 0),
                        (600, 80, 0, 5, 0)),
                    ["MG_DICE_DUEL"] = Rewards(
                        (150, 0, 0, 0, 0),
                        (400, 40, 0, 0, 0),
                        (550, 50, 0, 0, 1)),
                    ["MG_MEMORY_RUNE"] = Rewards(
                        (100, 0, 0, 0, 0),
                        (300, 25, 0, 0, 0),
                        (550, 70, 20, 0, 0)),
                }),
            }));
    }

    /// <summary>Per-minigame reward array, in (gold, crowns, beastFeed, enhanceStones, rerollCharges) order.</summary>
    private static ContentValue Rewards(params (int Gold, int Crowns, int BeastFeed, int EnhanceStones, int RerollCharges)[] rows) =>
        ContentValue.Array(rows.Select(row => ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["gold"] = ContentValue.Number(row.Gold),
            ["crowns"] = ContentValue.Number(row.Crowns),
            ["beastFeed"] = ContentValue.Number(row.BeastFeed),
            ["enhanceStones"] = ContentValue.Number(row.EnhanceStones),
            ["rerollCharges"] = ContentValue.Number(row.RerollCharges),
        })));
}
