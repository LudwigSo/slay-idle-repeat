using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// The paired reward for the four chapter-1 curses, as a narrow, named table keyed on curse id.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a parser: the reward column is free-form prose ("+250 Gold", "+12% ATK", "+2
/// Reroll Charges", ...) with no specified grammar. Writing a general parser would be inventing
/// that grammar and would silently mis-pay any row it guessed wrong. This table instead names the
/// four rows that can actually be paid and refuses every other id.
/// </para>
/// <para>
/// The four are exactly the curses reachable from chapter 1, and also, not coincidentally, the
/// four whose reward is a flat currency amount — the rest pay percentages, reroll charges or gear
/// drops that no system yet exists to grant.
/// </para>
/// <para>
/// The amounts are transcribed, not derived, and are cross-checked against
/// <c>content/curses/curses.json</c>'s own reward prose by <c>CurseRewardsTests</c>.
/// </para>
/// </remarks>
internal static class CurseRewards
{
    /// <summary><c>CUR_SLIPPERY</c>'s reward, <c>"+250 Gold"</c>.</summary>
    internal const string Slippery = "CUR_SLIPPERY";

    /// <summary><c>CUR_MARKED</c>'s reward, <c>"+2 Enhance Stones"</c>.</summary>
    internal const string Marked = "CUR_MARKED";

    /// <summary><c>CUR_FRACTURED</c>'s reward, <c>"+500 Gold"</c>.</summary>
    internal const string Fractured = "CUR_FRACTURED";

    /// <summary>Whether this curse's reward is one this table can pay.</summary>
    internal static bool IsPayable(string? curseId) => curseId switch
    {
        Slippery or Marked or Fractured => true,
        _ => false,
    };

    /// <summary>The three ids this table pays, in the design document's own order.</summary>
    /// <remarks>
    /// ⚠️ Three, not four: <c>CUR_DIZZY</c> was removed with the reroll — "the next 3 rolls cannot be
    /// rerolled" is a penalty with nothing left to deny.
    /// </remarks>
    internal static IReadOnlyList<string> PayableIds { get; } =
        Array.AsReadOnly(new[] { Slippery, Marked, Fractured });

    /// <summary>The currency and amount paired with this curse.</summary>
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
        Fractured => (CurrencyId.GOLD, 500L),
        _ => throw new ArgumentException(
            "19 Part E authors no PAYABLE reward for '" + curseId + "'. This table deliberately " +
            "covers only the chapter-1 curses whose reward is a flat currency amount (" +
            string.Join(", ", PayableIds) + "); the others pay percentages or gear drops that no " +
            "system exists to grant, and parsing the prose column generally would be inventing a " +
            "grammar nothing specifies.",
            nameof(curseId)),
    };
}
