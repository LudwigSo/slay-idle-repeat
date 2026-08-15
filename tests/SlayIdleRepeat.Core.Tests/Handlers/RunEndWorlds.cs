using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Tests.Model;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// <see cref="WorldSlice"/> fixtures for REVIVE/END_RUN/ABANDON_RUN: runs standing at every
/// combination of Boss-defeated, dead-or-alive and banked rewards these handlers branch on.
/// </summary>
internal static class RunEndWorlds
{
    /// <summary>
    /// A run in progress, with the given banked rewards and Boss/HP state — the shape
    /// <c>Handlers.EndRun</c> and <c>Handlers.AbandonRun</c> branch on.
    /// </summary>
    internal static WorldSlice InProgress(
        bool bossDefeated = false,
        int currentHp = 100,
        int maxHp = 100,
        long bankedLegendXp = 0,
        long bankedSoulShards = 0,
        int chapterId = 1,
        DifficultyTier tier = DifficultyTier.NORMAL,
        int pendingTileStage = 0,
        bool hasPendingTile = false,
        IReadOnlyDictionary<string, long>? adUses = null,
        IReadOnlyDictionary<string, long>? clearedChapterTiers = null) =>
        new(
            Worlds.Rehydrated(PlayerSnapshots.With(clearedChapterTiers: clearedChapterTiers)),
            Rehydrated(RunSnapshots.With(
                chapterId: chapterId,
                tier: tier,
                currentHp: currentHp,
                maxHp: maxHp,
                lastAppliedAtUtc: TileWorlds.NowUtc,
                adUses: adUses,
                pendingTileKind: hasPendingTile ? (int)TileKind.Enemy : RunSnapshots.NoPendingTile,
                pendingTileLinearIndex: hasPendingTile ? 7 : 0,
                pendingTileStage: hasPendingTile ? pendingTileStage : 0,
                phase: RunPhase.InProgress,
                bankedLegendXp: bankedLegendXp,
                bankedSoulShards: bankedSoulShards,
                bossDefeated: bossDefeated)));

    private static RunAggregate Rehydrated(RunSnapshot snapshot)
    {
        var run = RunAggregate.Rehydrate(snapshot);

        return run.IsSuccess
            ? run.Value
            : throw new InvalidOperationException("The fixture RunSnapshot does not rehydrate: " + run.Error);
    }
}
