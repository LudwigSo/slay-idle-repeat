using SlayIdleRepeat.Core.Model.Snapshots;

namespace SlayIdleRepeat.Application.Wire;

/// <summary>The one place a persisted snapshot becomes its wire projection, and the wire <c>stateHash</c> over the result.</summary>
/// <remarks>
/// <para>
/// Two factories and two hashes, nothing else. The factories are field-by-field copies in
/// declaration order — mechanical on purpose, so the projection can never quietly recompute a
/// value the snapshot holds. The hashes go through <c>CanonicalStateWriter</c>'s two named modes,
/// which is 14 §16.6's one-serialiser rule: this class concatenates nothing and encodes nothing
/// itself.
/// </para>
/// <para>
/// Which hash a response carries is the endpoint split, not run presence: a run-endpoint command
/// (and <c>START_RUN</c>, the run command on the player endpoint) hashes player-then-run, a meta
/// command hashes the player alone even while a run is open. The one seam in that rule is a
/// refused <c>START_RUN</c> on a player with no run — there is no run snapshot to hash, so the
/// unchanged state is the player alone, which is also the only reading a mirror on the other end
/// can reproduce.
/// </para>
/// </remarks>
public static class WireProjections
{
    /// <summary>The client-visible projection of one player snapshot.</summary>
    /// <param name="snapshot">The persisted snapshot.</param>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is null.</exception>
    public static PlayerWireProjection Of(PlayerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new PlayerWireProjection(
            snapshot.SchemaVersion,
            snapshot.Id,
            snapshot.DisplayName,
            snapshot.LegendLevel,
            snapshot.LegendXp,
            snapshot.RunsStarted,
            snapshot.Wallet,
            snapshot.Energy,
            snapshot.EnergyAnchorUtc,
            snapshot.LastAppliedAtUtc,
            snapshot.FtueBeatId,
            snapshot.FtueCompletedAtUtc,
            snapshot.DailyPeriodStartUtc,
            snapshot.DailyCounters,
            snapshot.WeeklyPeriodStartUtc,
            snapshot.WeeklyCounters,
            snapshot.LoginCalendarDay,
            snapshot.LoginCalendarDayClaimed,
            snapshot.ClearedChapterTiers,
            snapshot.FeatCounters,
            snapshot.PityCounters,
            snapshot.Inventory,
            snapshot.AutoSalvageRules,
            snapshot.TalentPoints,
            snapshot.Loadout,
            snapshot.Presets);
    }

    /// <summary>The client-visible projection of one run snapshot. <c>RunSeed</c> does not cross this line.</summary>
    /// <param name="snapshot">The persisted snapshot.</param>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is null.</exception>
    public static RunWireProjection Of(RunSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new RunWireProjection(
            snapshot.SchemaVersion,
            snapshot.Id,
            snapshot.PlayerId,
            snapshot.ChapterId,
            snapshot.Tier,
            snapshot.LastAppliedAtUtc,
            snapshot.Position,
            snapshot.CurrentHp,
            snapshot.MaxHp,
            snapshot.Gold,
            snapshot.RngStreamPositions,
            snapshot.AdUses,
            snapshot.ResolvedMinigames,
            snapshot.PendingForkJunctionPosition,
            snapshot.PendingForkRemainingSteps,
            snapshot.PendingTileKind,
            snapshot.PendingTileLinearIndex,
            snapshot.PendingTileStage,
            snapshot.PendingEventCardId,
            snapshot.Phase,
            snapshot.DraftPending,
            snapshot.DraftBattleKind,
            snapshot.DraftBattleStage,
            snapshot.OwnedPerkTiers,
            snapshot.BankedLegendXp,
            snapshot.BankedSoulShards,
            snapshot.BossDefeated,
            snapshot.DraftsSinceLegendaryOffered,
            snapshot.DraftsWithoutAboveCommon,
            snapshot.DraftsWithoutOwnedUpgrade,
            snapshot.StartingLoadout,
            snapshot.ItemsAtOrAboveFloorBand,
            snapshot.ShrineBuffs,
            snapshot.RunBuffs,
            snapshot.Curses,
            snapshot.Consumables,
            snapshot.FixedDice,
            snapshot.PendingFixedDieChoices,
            snapshot.EscapeRopeArmed,
            snapshot.FreeDraftRerolls,
            snapshot.ShopOfferDraw,
            snapshot.ShopSlotsPurchased,
            snapshot.ShopRefreshesUsedThisVisit);
    }

    /// <summary>The wire <c>stateHash</c> of a run-scoped command: player then run, concatenated by the canonical writer.</summary>
    /// <param name="player">The persisted player snapshot.</param>
    /// <param name="run">The persisted run snapshot.</param>
    /// <returns><c>"fnv1a:"</c> + 16 lowercase hex characters.</returns>
    /// <exception cref="ArgumentNullException">Either snapshot is null.</exception>
    public static string HashPlayerAndRun(PlayerSnapshot player, RunSnapshot run)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(run);

        return CanonicalStateWriter.HashRunCommandState(Of(player), Of(run));
    }

    /// <summary>The wire <c>stateHash</c> of a meta command — and of a run-less refusal: the player alone.</summary>
    /// <param name="player">The persisted player snapshot.</param>
    /// <returns><c>"fnv1a:"</c> + 16 lowercase hex characters.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="player"/> is null.</exception>
    public static string HashPlayerAlone(PlayerSnapshot player)
    {
        ArgumentNullException.ThrowIfNull(player);

        return CanonicalStateWriter.HashMetaCommandState(Of(player));
    }
}
