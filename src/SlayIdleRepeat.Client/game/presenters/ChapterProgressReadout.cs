using System.Globalization;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>The furthest chapter a profile has cleared, at the highest tier it cleared it at.</summary>
/// <param name="ChapterId">The chapter.</param>
/// <param name="ChapterName">Its authored name, resolved.</param>
/// <param name="Tier">The highest tier that chapter was cleared at.</param>
public sealed record HighestChapterClear(int ChapterId, string ChapterName, DifficultyTier Tier);

/// <summary>Where a continuable run stands: its chapter, its stage, and the hero's hit points.</summary>
/// <param name="ChapterId">The chapter being played.</param>
/// <param name="ChapterName">Its authored name, resolved.</param>
/// <param name="Stage">The stage the run stands in, 1 to <see cref="ChapterProgressReadout.StageCount"/>.</param>
/// <param name="CurrentHp">The hero's hit points, as the run row holds them.</param>
/// <param name="MaxHp">The pool they are out of, as the run row holds it.</param>
public sealed record RunStageProgress(int ChapterId, string ChapterName, int Stage, int CurrentHp, int MaxHp);

/// <summary>The two progress readings the Home screen shows.</summary>
/// <remarks>
/// Both are read off rows and a board the rules already produced; nothing here decides what counts
/// as a clear or where a stage ends. What it does decide is what to show when a row names a chapter
/// the screen cannot name back — and the answer is to skip it, not to draw half a tile or throw.
/// </remarks>
public static class ChapterProgressReadout
{
    /// <summary>How many stages a chapter's board has. The boss ends the last of them.</summary>
    public const int StageCount = 3;

    /// <summary>The stage a run stands in before it has rolled onto any node.</summary>
    private const int FirstStage = 1;

    /// <summary>What separates the chapter from the tier in a cleared-chapter key.</summary>
    private const char ChapterTierSeparator = ':';

    /// <summary>
    /// The furthest chapter cleared at any tier, tier breaking the tie — or <c>null</c> when nothing
    /// the content authors has been cleared.
    /// </summary>
    /// <param name="clearedChapterTiers">The row's cleared (chapter, tier) keys; <c>null</c> reads as nothing cleared.</param>
    /// <param name="chapters">The chapters the content authors. A clear of any other chapter is skipped.</param>
    /// <exception cref="ArgumentNullException"><paramref name="chapters"/> is null.</exception>
    public static HighestChapterClear? HighestClear(
        IReadOnlyDictionary<string, long>? clearedChapterTiers, IReadOnlyList<ChapterDocument> chapters)
    {
        ArgumentNullException.ThrowIfNull(chapters);

        if (clearedChapterTiers is null)
        {
            return null;
        }

        HighestChapterClear? highest = null;

        foreach (var key in clearedChapterTiers.Keys)
        {
            if (!TryParseClear(key, out var chapterId, out var tier) || ChapterNamed(chapters, chapterId) is not { } chapter)
            {
                continue;
            }

            if (highest is null || chapterId > highest.ChapterId || (chapterId == highest.ChapterId && tier > highest.Tier))
            {
                highest = new HighestChapterClear(chapterId, chapter.ChapterName, tier);
            }
        }

        return highest;
    }

    /// <summary>
    /// Where <paramref name="run"/> stands — or <c>null</c> when its chapter is one the list does
    /// not name or the content cannot generate a board for.
    /// </summary>
    /// <param name="run">The run row.</param>
    /// <param name="content">The loaded content set, read for the run's chapter board.</param>
    /// <param name="chapters">The chapters the screen names.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public static RunStageProgress? RunProgress(
        RunSnapshot run, ContentSnapshot content, IReadOnlyList<ChapterDocument> chapters)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(chapters);

        if (ChapterNamed(chapters, run.ChapterId) is not { } chapter)
        {
            return null;
        }

        BoardView board;

        try
        {
            board = BoardView.Project(run, content);
        }
        catch (MissingContentException)
        {
            // A row written by a newer build, or against a chapter since retired: no board, no stage.
            return null;
        }

        return new RunStageProgress(
            chapter.ChapterId, chapter.ChapterName, StageOf(board.StandingOn), run.CurrentHp, run.MaxHp);
    }

    /// <summary>The stage a node belongs to as a player counts them: the boss's own stage is the last.</summary>
    private static int StageOf(BoardTrackNode? standingOn) =>
        standingOn is null
            ? FirstStage
            : standingOn.Stage is >= FirstStage and <= StageCount ? standingOn.Stage : StageCount;

    private static ChapterDocument? ChapterNamed(IReadOnlyList<ChapterDocument> chapters, int chapterId) =>
        chapters.FirstOrDefault(chapter => chapter.ChapterId == chapterId);

    /// <summary>Reads a <c>{chapter}:{TIER}</c> key strictly; anything else is a key this format does not describe.</summary>
    private static bool TryParseClear(string key, out int chapterId, out DifficultyTier tier)
    {
        chapterId = 0;
        tier = default;

        var parts = key.Split(ChapterTierSeparator);

        // Enum.TryParse would accept "2" and "normal"; only the tier's own spelling is a tier here.
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out chapterId)
            && Enum.TryParse(parts[1], ignoreCase: false, out tier)
            && string.Equals(tier.ToString(), parts[1], StringComparison.Ordinal);
    }
}
