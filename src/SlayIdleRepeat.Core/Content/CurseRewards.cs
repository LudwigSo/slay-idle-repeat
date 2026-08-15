using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// 🔒 `19` Part E — the paired reward for the four chapter-1 curses, as a <b>narrow, named table</b>
/// keyed on curse id.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Deliberately not a parser.</b> `19` Part E's <c>reward</c> column is prose — <c>"+250
/// Gold"</c>, <c>"+2 Enhance Stones"</c>, but also <c>"+12% ATK"</c>, <c>"+2 Reroll Charges"</c> and
/// <c>"+1 gear drop per Elite"</c> — and no document specifies a grammar for it. Writing a
/// general reward-string parser would be inventing that grammar and would silently mis-pay every row
/// it guessed wrong (steering <b>S6</b>). This table names the four rows M3-03 can actually pay and
/// refuses every other id, so an unpayable reward is a loud refusal at the seam rather than a
/// plausible number.
/// </para>
/// <para>
/// ⚠️ <b>The four are exactly `19` Part E's <c>availableFromChapter: 1</c> rows</b> — the only ones a
/// chapter-1 or chapter-2 run can draw at all, which is the reachable scope of M3-03's board — and
/// they are also, not coincidentally, the four whose reward is a flat currency amount. The other
/// eight open from chapter 3 or 5 and pay percentages, reroll charges or gear drops, none of which
/// has a system to grant it yet.
/// </para>
/// <para>
/// 🔒 <b>The amounts are transcribed, not derived</b>, and they are cross-checked against
/// <c>content/curses/curses.json</c>'s own <c>reward</c> prose by
/// <c>CurseRewardsTests</c> — so a content edit that changed <c>"+250 Gold"</c> to something else
/// fails the build instead of leaving this table quietly wrong.
/// </para>
/// </remarks>
internal static class CurseRewards
{
    /// <summary>`19` Part E — <c>CUR_SLIPPERY</c>'s reward, <c>"+250 Gold"</c>.</summary>
    internal const string Slippery = "CUR_SLIPPERY";

    /// <summary>`19` Part E — <c>CUR_MARKED</c>'s reward, <c>"+2 Enhance Stones"</c>.</summary>
    internal const string Marked = "CUR_MARKED";

    /// <summary>`19` Part E — <c>CUR_DIZZY</c>'s reward, <c>"+180 Gold"</c>.</summary>
    internal const string Dizzy = "CUR_DIZZY";

    /// <summary>`19` Part E — <c>CUR_FRACTURED</c>'s reward, <c>"+500 Gold"</c>.</summary>
    internal const string Fractured = "CUR_FRACTURED";

    /// <summary>Whether this curse's `19` Part E reward is one this table can pay.</summary>
    internal static bool IsPayable(string? curseId) => curseId switch
    {
        Slippery or Marked or Dizzy or Fractured => true,
        _ => false,
    };

    /// <summary>The four ids this table pays, in `19` Part E's own order.</summary>
    internal static IReadOnlyList<string> PayableIds { get; } =
        Array.AsReadOnly(new[] { Slippery, Marked, Dizzy, Fractured });

    /// <summary>
    /// The currency and amount `19` Part E pairs with this curse.
    /// </summary>
    /// <param name="curseId">One of <see cref="PayableIds"/>.</param>
    /// <exception cref="ArgumentException">
    /// The curse has no payable reward — see this type's remarks. A defect rather than a rejection:
    /// content is validated at build time, so an unpayable id reaching a resolver means a card or a
    /// draw offered a curse this table was never extended to cover.
    /// </exception>
    internal static (CurrencyId Currency, long Amount) For(string curseId) => curseId switch
    {
        Slippery => (CurrencyId.GOLD, 250L),
        Marked => (CurrencyId.ENHANCE_STONES, 2L),
        Dizzy => (CurrencyId.GOLD, 180L),
        Fractured => (CurrencyId.GOLD, 500L),
        _ => throw new ArgumentException(
            "19 Part E authors no PAYABLE reward for '" + curseId + "'. This table deliberately " +
            "covers only the four chapter-1 curses whose reward is a flat currency amount (" +
            string.Join(", ", PayableIds) + "); the other eight pay percentages, reroll charges or " +
            "gear drops that no system exists to grant, and parsing the prose column generally " +
            "would be inventing a grammar nothing specifies.",
            nameof(curseId)),
    };
}
