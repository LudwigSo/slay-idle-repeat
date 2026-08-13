using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// Hermetic <c>tuning/currencies.json</c> fixtures — `19` Part G's <c>loginCalendar</c> block — and
/// the <b>combined</b> snapshot a command that reads more than one tuning document needs.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <c>Core.Tests</c> is hermetic: no file, no adapter, no parser. So this fixture <em>mirrors</em>
/// the shipped <c>game-data/tuning/currencies.json</c> rather than reading it, exactly as
/// <see cref="ProgressionDocuments"/> mirrors <c>progression.json</c>, and the two halves are pinned
/// together from the other side by an <c>Application.Tests</c> rule that reads the real file. Neither
/// half is sufficient alone: this one proves the rules are right about numbers, that one proves those
/// are the numbers the game ships.
/// </para>
/// <para>
/// 🔒 <b>Why <see cref="Shipped"/> carries BOTH documents.</b> M1-09's <c>BEGIN_SESSION</c> is the
/// first command to read two tuning documents in one <c>Apply</c> — <c>progression.json</c> for the
/// Energy refill and <c>currencies.json</c> for the calendar's cycle length — and a
/// <see cref="ContentSnapshot"/> is `30` §3's <em>whole</em> content set, not one document. Building
/// the pair here rather than in <c>Worlds</c> keeps the "what does the game ship" question in
/// <c>Content/</c> with its sibling.
/// </para>
/// <para>
/// ⚠️ <b>Only <c>cycleDays</c> is authored, and the twenty-eight reward rows deliberately are
/// not.</b> A fixture that transcribed them would imply something reads them; nothing does, because
/// paying them is <c>CLAIM_CALENDAR</c>'s (M4-09) and <c>LoginCalendarTuning</c>'s own remarks record
/// why. The same restraint <see cref="ProgressionDocuments"/> shows for the Legend Level curve.
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
    /// <see cref="ContentValue.Unauthorised"/> to model a deliberate <c>null</c> hole, or omit a
    /// parameter to keep the shipped value.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b><paramref name="legendLevelMin"/> is here because M1-11's review found a test that could
    /// not fail without it.</b> <c>InMemoryGame.CreatePlayer</c> reads `07` §1.1's authored floor
    /// through <c>LegendTuning</c> rather than writing the literal 1 — and the only assertion of that
    /// compared against <c>ProgressionDocuments.ShippedLegendLevelMin</c>, which <em>is</em> 1, so
    /// replacing the read with the literal left every test green. Proving the read needs a content
    /// set whose floor is not the shipped one, which needs this document to be rebuildable rather
    /// than lifted whole out of <c>ProgressionDocuments.Shipped</c>.
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
