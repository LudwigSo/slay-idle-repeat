using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

using PlayerAggregate = SlayIdleRepeat.Core.Model.Player;

namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>
/// The chapter the campaign offers a player next, and the power that chapter is balanced against.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The sanctioned route out of <c>Core</c> for the par table.</b> <see cref="ParPowerTuning"/>
/// is <c>internal</c> and stays so, so a screen wanting the recommendation had the same two bad
/// options <c>HomeEnergyView</c>'s numbers had: go without it — which leaves the Home screen's
/// "your hero is under the recommendation" state unreachable outside a test fake — or transcribe
/// <c>tuning/par_power.json</c> into C#, where no reader would ever see it move. This is the third,
/// and it is the same construction as <c>HomeEnergyView</c>, <c>Rules.Economy.RunEndView</c> and
/// <c>Rules.Economy.ShopView</c>.
/// </para>
/// <para>
/// Lives under <c>Rules/Board/</c> beside <see cref="ChapterBoardTuning"/>, which reads the same
/// <c>content/chapters/</c> documents: the chapter this answers with is exactly the one whose board
/// the player would generate next, and <c>Content/</c> sits below <c>Rules</c> so a reader spanning
/// the par table and the chapter documents could not live one layer lower.
/// </para>
/// <para>
/// ⚠️ <b>It answers no NAME.</b> A chapter document authors its <c>displayName</c> as a loc key
/// (<c>loc.chapter.1.name</c>), and resolving one is <c>LocaleStringCatalogue</c>'s job in the
/// client assembly — which <c>Core</c> may not reference and must not duplicate. Handing back the
/// key under a property called a name would put <c>loc.chapter.1.name</c> on the screen the first
/// time somebody trusted it, so the name stays absent here and the client names the chapter from
/// <see cref="ChapterId"/> through the catalogue it already holds (steering S6).
/// </para>
/// </remarks>
/// <param name="ChapterId">The chapter to offer. Authored as a chapter document and as a par row.</param>
/// <param name="RecommendedPower">
/// The power that chapter's Normal tier is balanced against — <c>par_power.json</c>'s own cell, not
/// a curve fitted to it.
/// </param>
public sealed record NextChapterView(int ChapterId, double RecommendedPower)
{
    /// <summary>
    /// The chapter <paramref name="player"/> is being pointed at, or <c>null</c> when the content
    /// authors none to point at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first authored chapter the player has not yet cleared on Normal. That is the ladder's own
    /// answer rather than an invented one: <c>tuning/progression.json#/chapterGating</c> opens a
    /// chapter's Normal rung on <c>PREVIOUS_CHAPTER_NORMAL</c>, so the chapters a player may enter
    /// are exactly the cleared ones plus the first they have not cleared, and the last of those is
    /// the only one that is new to them.
    /// </para>
    /// <para>
    /// 🔒 <b>Absent rather than approximated in both of the two ways this can run out</b> (steering
    /// S6): a player who has cleared every authored chapter is pointed at nothing, and a chapter the
    /// par table authors no cell for is answered as no chapter at all rather than extrapolated —
    /// nor is it skipped for a LATER one, which would offer a chapter the ladder has not opened.
    /// Extrapolating would invent the balance point of a chapter nobody has tuned, which is what
    /// <see cref="ParPowerTuning.ChapterPowerTarget"/> refuses to do.
    /// </para>
    /// </remarks>
    /// <param name="player">The player's row — its cleared chapter/tier history is what is read.</param>
    /// <param name="content">The loaded content set: the chapter documents and the par table.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="MissingContentException">The par table is not authored.</exception>
    public static NextChapterView? Project(PlayerSnapshot player, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(content);

        var cleared = player.ClearedChapterTiers;
        var pars = ParPowerTuning.Read(content);

        foreach (var chapter in ChapterBoardTuning.AuthoredChapterIds(content))
        {
            if (HasCleared(cleared, chapter))
            {
                continue;
            }

            return pars.Chapters.Contains(chapter)
                ? new NextChapterView(chapter, pars.ChapterPowerTarget(chapter))
                : null;
        }

        return null;
    }

    /// <summary>
    /// Whether the row records a Normal clear of one chapter, read through the aggregate's own key
    /// format rather than a second transcription of it.
    /// </summary>
    private static bool HasCleared(IReadOnlyDictionary<string, long>? cleared, int chapter) =>
        cleared is not null &&
        cleared.ContainsKey(PlayerAggregate.ChapterTierKey(chapter, DifficultyTier.NORMAL));
}
