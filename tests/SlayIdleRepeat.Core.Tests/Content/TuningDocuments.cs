using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// Hermetic <c>tuning/currencies.json</c> fixtures — `19` Part G's <c>loginCalendar</c> block — and
/// the <b>combined</b> snapshot a command reading more than one tuning document needs.
/// </summary>
/// <remarks>
/// 🔒 <c>Core.Tests</c> is hermetic, so this <em>mirrors</em> the shipped file rather than reading it;
/// an <c>Application.Tests</c> rule pins the two halves together. Neither is sufficient alone: this
/// proves the rules are right about the numbers, that one proves those are the numbers we ship.
/// <para>
/// ⚠️ Only <c>cycleDays</c> is authored — transcribing the twenty-eight reward rows would imply
/// something reads them, and nothing does until <c>CLAIM_CALENDAR</c> (M4-09).
/// </para>
/// </remarks>
internal static class TuningDocuments
{
    /// <summary>The document `19` G's calendar block lives in.</summary>
    internal const string CurrenciesPath = "tuning/currencies.json";

    /// <summary>`19` G — the calendar runs 28 days and then restarts at day 1.</summary>
    /// <remarks>
    /// <c>const</c> rather than <c>static readonly</c> so <c>[InlineData]</c> can take it: a wrap
    /// test that restated 28 as a literal would keep passing after the data moved.
    /// </remarks>
    internal const int ShippedCycleDays = 28;

    /// <summary>
    /// The whole shipped tuning set this milestone reads: the energy and Legend Level blocks of
    /// <c>progression.json</c>, plus `19` G's calendar block of <c>currencies.json</c>.
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
    /// point of `30` §3's typed readers is that they are told apart.
    /// </remarks>
    internal static ContentSnapshot WithoutCurrencies() => ProgressionDocuments.Shipped;

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
            }));
    }
}
