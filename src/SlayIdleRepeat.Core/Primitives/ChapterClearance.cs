using System.Globalization;

namespace SlayIdleRepeat.Core.Primitives;

/// <summary>
/// The spelling of a cleared-chapter-tier key, and the one derivation over a set of them.
/// </summary>
/// <remarks>
/// <para>
/// It lives in <c>Primitives</c> for <c>GameCalendar</c>'s reason: two readers need the same format
/// and neither may reference the other. <c>Player</c> writes these keys and reads them back, and the
/// inbox's segment predicate asks how far a player has got from a stored profile — so the format
/// exists in exactly one place rather than in an aggregate and again in whatever asks it a question.
/// </para>
/// <para>
/// A second transcription of this format would decide who a compensation grant reaches, and would
/// go on deciding it correctly right up until the separator changed.
/// </para>
/// </remarks>
public static class ChapterClearance
{
    /// <summary>The chapter this game starts at, and the answer for a player who has cleared nothing.</summary>
    /// <remarks>
    /// Floored at one rather than zero: a player who has cleared nothing is still playing chapter
    /// one, so anything scaled against "how far they have got" has a chapter to scale against on the
    /// first run.
    /// </remarks>
    public const int FirstChapter = 1;

    /// <summary>What separates the chapter from the tier in a cleared-chapter-tier key.</summary>
    private const char Separator = ':';

    /// <summary>The key a cleared (chapter, tier) pair is recorded under.</summary>
    /// <param name="chapterId">The chapter.</param>
    /// <param name="tier">The difficulty tier it was cleared on.</param>
    public static string Key(int chapterId, DifficultyTier tier) =>
        chapterId.ToString(CultureInfo.InvariantCulture) + Separator + tier;

    /// <summary>The highest chapter these keys record a clear of, on any tier.</summary>
    /// <param name="keys">The cleared-chapter-tier keys. <c>null</c> reads as none.</param>
    /// <returns>The highest chapter cleared, never below <see cref="FirstChapter"/>.</returns>
    /// <remarks>
    /// Any tier counts: clearing a chapter on Normal is as much a statement about how far the player
    /// has come as clearing it on Mythic. A key that does not parse is skipped rather than throwing —
    /// this reads stored rows, and one unreadable key must not make the whole question unanswerable.
    /// </remarks>
    public static int HighestClearedIn(IEnumerable<string>? keys)
    {
        var highest = FirstChapter;

        foreach (var key in keys ?? Enumerable.Empty<string>())
        {
            var separator = key.IndexOf(Separator, StringComparison.Ordinal);

            if (separator > 0 &&
                int.TryParse(
                    key.AsSpan(0, separator),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var chapterId) &&
                chapterId > highest)
            {
                highest = chapterId;
            }
        }

        return highest;
    }
}
