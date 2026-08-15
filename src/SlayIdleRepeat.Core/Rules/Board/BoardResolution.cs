using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>
/// 🔒 M3-02 — the one seam every movement handler (<c>Handlers.RollDice</c>,
/// <c>Handlers.ChooseFork</c>) uses to get at a run's board, shared so the same replay rule cannot
/// drift between the two.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Generated at most <em>once</em>, ever, on the run's committed <c>board</c> stream —
/// every later command replays the identical layout instead of drawing again.</b>
/// <see cref="RngStreams.Board"/> serves two purposes in sequence (`14` §8.1: "Board layout
/// generation; Portal jump draws") — <see cref="BoardGenerator.GenerateBoard"/>'s own draws first,
/// then any real Portal jump draw after them — so the SAME committed position cannot be replayed
/// through <em>and</em> continued from without colliding the two.
/// </para>
/// <para>
/// The fix mirrors <c>Rules.Dice.FairDiceBag.Replay</c> exactly: on this run's <b>first</b> need for
/// a board (<c>run.StreamPosition(RngStreams.Board) == 0</c>, meaning nothing has ever drawn from
/// it), generation runs for real on the tracked, committed stream — <c>rngScope.Stream(Board)</c> —
/// so its draw count becomes the run's committed <c>board</c> position once <c>Apply</c> folds it
/// back. On every later command that position is already past generation, so re-running
/// <c>GenerateBoard</c> on the tracked stream would draw <em>different</em> numbers and produce a
/// different board — instead, generation is replayed on an ephemeral, uncommitted
/// <see cref="DeterministicRng.OpenAt"/> stream reopened at draw 0, which is a pure function of
/// <see cref="Run.RunSeed"/> and this chapter's content and therefore reconstructs the identical
/// graph every time, leaving the tracked stream free for whatever real Portal draw this command
/// might still need.
/// </para>
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

        var replay = DeterministicRng.OpenAt(run.RunSeed, RngStreams.Board, 0);
        return BoardGenerator.GenerateBoard(config, replay);
    }
}
