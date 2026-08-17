using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>The one seam every movement handler uses to get at a run's board, so the replay rule cannot drift between callers.</summary>
/// <remarks>
/// Generated at most once, ever, on the run's committed <c>board</c> stream — every later command
/// replays the identical layout instead of drawing again. <see cref="RngStreams.Board"/> is later
/// reused for Portal jump draws, so the same committed position cannot be replayed through and
/// continued from without colliding the two. On this run's first need for a board
/// (<c>run.StreamPosition(RngStreams.Board) == 0</c>), generation runs for real on the tracked
/// stream so its draw count becomes the committed position. On every later call, replaying
/// generation on that same tracked stream would draw different numbers and produce a different
/// board — so generation is instead replayed on an ephemeral <see cref="DeterministicRng.OpenAt"/>
/// stream reopened at draw 0, a pure function of the run seed and chapter content that
/// reconstructs the identical graph, leaving the tracked stream free for any real Portal draw.
/// </remarks>
internal static class BoardResolution
{
    /// <summary>Resolves <paramref name="run"/>'s board — generating it for real on the first call, replaying it identically on every later one.</summary>
    /// <param name="run">The run whose board is needed.</param>
    /// <param name="content">The loaded content set, read for <paramref name="run"/>'s chapter.</param>
    /// <param name="rngScope">This command's <see cref="RunRngScope"/>.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    internal static BoardGraph Resolve(Run run, Content.ContentSnapshot content, RunRngScope rngScope)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(rngScope);

        var config = ChapterBoardTuning.Read(content, run.ChapterId);

        if (run.StreamPosition(RngStreams.Board) == 0)
        {
            return BoardGenerator.GenerateBoard(config, rngScope.Stream(RngStreams.Board));
        }

        return Replay(config, run.RunSeed);
    }

    /// <summary>
    /// Rebuilds a run's board off an ephemeral stream reopened at draw 0, touching no tracked
    /// counter. The one replay in the codebase: <see cref="BoardView"/> reads the board through it
    /// too, so the track a player is shown cannot drift onto a different layout from the one the
    /// movement handlers walk.
    /// </summary>
    /// <param name="config">The run's chapter's board-relevant content.</param>
    /// <param name="runSeed">The run's committed seed, the board's only other input.</param>
    internal static BoardGraph Replay(ChapterBoardConfig config, ulong runSeed) =>
        BoardGenerator.GenerateBoard(config, DeterministicRng.OpenAt(runSeed, RngStreams.Board, 0));
}
