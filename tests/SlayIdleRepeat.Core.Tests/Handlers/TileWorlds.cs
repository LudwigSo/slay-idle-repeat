using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// <see cref="WorldSlice"/> and <see cref="GameContext"/> fixtures for the tile-resolution suite: a
/// run standing on a pending tile, over a content set that carries the in-run income blocks, curses
/// and a purpose-built card list.
/// </summary>
/// <remarks>
/// Distinct from <c>Worlds</c> rather than an extension of it: <c>Worlds.Context</c> carries the
/// login-calendar/minigame tuning set, which holds none of the documents these handlers read. Every
/// run here is built through <c>Run.Rehydrate</c>, the only way to obtain one, so nothing in this
/// file invents a starting state.
/// </remarks>
internal static class TileWorlds
{
    /// <summary>The instant every fixture applies at — <c>Worlds.NowUtc</c>, so the two agree.</summary>
    internal static readonly DateTimeOffset NowUtc = Worlds.NowUtc;

    /// <summary>The run seed every fixture uses unless a test is about a different draw.</summary>
    /// <remarks>
    /// <c>RunSnapshots.Seed</c>, so a determinism assertion here and one in the snapshot suite are
    /// talking about the same stream.
    /// </remarks>
    internal const ulong Seed = RunSnapshots.Seed;

    /// <summary>A context at <see cref="NowUtc"/> over the shipped in-run income content.</summary>
    internal static GameContext Context { get; } = ContextOver(InRunIncomeDocuments.Shipped);

    /// <summary>A context over a content set with individual leaves replaced.</summary>
    internal static GameContext ContextOver(ContentSnapshot content) => new(
        NowUtc,
        CommandSeed: null,
        content,
        TestSupport.GameContexts.WithoutPlus,
        TestSupport.GameContexts.NoKillSwitchThrown);

    /// <summary>
    /// A slice whose run is standing on an unresolved tile of <paramref name="kind"/>.
    /// </summary>
    /// <param name="kind">The tile kind the run has landed on.</param>
    /// <param name="chapterId">The chapter. Chapter 1 unless a scaling test needs otherwise.</param>
    /// <param name="gold">The run's starting Gold, for the cost and affordability cases.</param>
    /// <param name="currentHp">The hero's hit points, for the heal and HP-cost cases.</param>
    /// <param name="eventCardId">The card a pending event tile has already drawn, if any.</param>
    /// <param name="runSeed">The committed run seed, for the determinism cases.</param>
    internal static WorldSlice OnTile(
        TileKind kind,
        int chapterId = 1,
        long gold = 0L,
        int currentHp = 100,
        string? eventCardId = null,
        ulong runSeed = Seed,
        int linearIndex = 7,
        int stage = 1,
        RunPhase phase = RunPhase.InProgress) =>
        new(
            Worlds.NewPlayer(),
            Rehydrated(RunSnapshots.With(
                runSeed: runSeed,
                chapterId: chapterId,
                gold: gold,
                currentHp: currentHp,
                lastAppliedAtUtc: NowUtc,
                pendingTileKind: (int)kind,
                pendingTileLinearIndex: linearIndex,
                pendingTileStage: stage,
                pendingEventCardId: eventCardId ?? RunSnapshots.NoPendingEventCard,
                phase: phase)));

    /// <summary>A slice whose run is standing on no tile at all — every handler's first refusal.</summary>
    internal static WorldSlice OnNoTile(long gold = 0L, RunPhase phase = RunPhase.InProgress) =>
        new(
            Worlds.NewPlayer(),
            Rehydrated(RunSnapshots.With(gold: gold, lastAppliedAtUtc: NowUtc, phase: phase)));

    private static RunAggregate Rehydrated(RunSnapshot snapshot)
    {
        var run = RunAggregate.Rehydrate(snapshot);

        return run.IsSuccess
            ? run.Value
            : throw new InvalidOperationException("The fixture RunSnapshot does not rehydrate: " + run.Error);
    }
}
