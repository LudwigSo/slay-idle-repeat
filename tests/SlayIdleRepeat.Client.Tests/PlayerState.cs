using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using CorePlayer = SlayIdleRepeat.Core.Model.Player;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The persisted rows the Home and Chapter Select screens read, built to order.
/// </summary>
/// <remarks>
/// <para>
/// Built as snapshots rather than through the aggregate, because that is what these screens
/// actually receive: <c>ReadOwnStateAsync</c> answers with the stored rows and nothing rehydrates
/// them on the way. A fixture that went through the aggregate would be exercising a construction
/// path the screens never see.
/// </para>
/// <para>
/// Every value not named by a case is stated once here and never varied, so a case's arrangement
/// reads as the two or three facts it actually depends on.
/// </para>
/// </remarks>
internal static class PlayerState
{
    /// <summary>Anchors every timestamp a row needs but no case cares about.</summary>
    private static readonly DateTimeOffset FixtureInstant = new(2026, 5, 2, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A player row with the header values a case names and defaults for the rest.</summary>
    /// <param name="id">Whose row this is.</param>
    /// <param name="displayName">The name the header draws.</param>
    /// <param name="legendLevel">The Legend Level the header draws and the tier ladder is checked against.</param>
    /// <param name="energy">The main Energy bar's amount.</param>
    /// <param name="reserve">The Energy Reserve's amount.</param>
    /// <param name="clearedChapterTiers">
    /// The clear history, keyed the way <c>Player</c> keys it. Left <c>null</c> by default because
    /// that is the row's own documented "nothing cleared yet".
    /// </param>
    internal static PlayerSnapshot Player(
        PlayerId id,
        string displayName = "Fixture Hero",
        int legendLevel = 1,
        int energy = 0,
        int reserve = 0,
        IReadOnlyDictionary<string, long>? clearedChapterTiers = null) =>
        new(
            SnapshotSchema.SchemaVersion,
            id,
            displayName,
            legendLevel,
            LegendXp: 0,
            RunsStarted: 0,
            Wallet: new Dictionary<CurrencyId, long>(),
            Energy: new EnergyBanks(energy, reserve),
            EnergyAnchorUtc: FixtureInstant,
            LastAppliedAtUtc: FixtureInstant,
            FtueBeatId: FtueBeat.B0,
            FtueCompletedAtUtc: null,
            DailyPeriodStartUtc: FixtureInstant,
            DailyCounters: new Dictionary<string, long>(),
            WeeklyPeriodStartUtc: FixtureInstant,
            WeeklyCounters: new Dictionary<string, long>(),
            LoginCalendarDay: 1,
            LoginCalendarDayClaimed: false,
            ClearedChapterTiers: clearedChapterTiers);

    /// <summary>A run row in a named phase.</summary>
    /// <param name="id">The run's identity — what a CONTINUE has to carry.</param>
    /// <param name="player">Whose run it is.</param>
    /// <param name="phase">The phase the run stands in.</param>
    internal static RunSnapshot Run(RunId id, PlayerId player, RunPhase phase) =>
        new(
            SnapshotSchema.SchemaVersion,
            id,
            player,
            RunSeed: 1,
            ChapterId: 1,
            Tier: DifficultyTier.NORMAL,
            LastAppliedAtUtc: FixtureInstant,
            Position: -1,
            CurrentHp: 100,
            MaxHp: 100,
            Gold: 0,
            RngStreamPositions: new Dictionary<string, ulong>(),
            AdUses: new Dictionary<string, long>(),
            ResolvedMinigames: new Dictionary<int, string>(),
            PendingForkJunctionPosition: null,
            PendingForkRemainingSteps: null,
            PendingTileKind: -1,
            PendingTileLinearIndex: 0,
            PendingTileStage: 0,
            PendingEventCardId: "",
            Phase: phase);

    /// <summary>The key <c>Player</c> stores one cleared (chapter, tier) pair under.</summary>
    /// <remarks>
    /// 🔒 Re-formed here from the same two parts rather than called: <c>Player.ChapterTierKey</c> is
    /// <c>internal</c>, and only <c>SlayIdleRepeat.Core.Tests</c> can see it. This is therefore a
    /// transcription of a format another assembly owns, which is exactly why one case pins the
    /// literal shape rather than only using this helper — a transcription that agrees with itself
    /// is not evidence of anything.
    /// </remarks>
    internal static string ClearedKey(int chapterId, DifficultyTier tier) => $"{chapterId}:{tier}";

    /// <summary>A clear history holding exactly the given pairs.</summary>
    internal static IReadOnlyDictionary<string, long> Cleared(params (int Chapter, DifficultyTier Tier)[] pairs) =>
        pairs.ToDictionary(p => ClearedKey(p.Chapter, p.Tier), _ => 1L, StringComparer.Ordinal);

    /// <summary>An empty clear history — the state <c>null</c> is documented to mean.</summary>
    internal static IReadOnlyDictionary<string, long> NothingCleared() =>
        new Dictionary<string, long>(StringComparer.Ordinal);

    /// <summary>
    /// A state a submission fake can hand back, so a presenter that reads the outcome finds a real
    /// one rather than a null.
    /// </summary>
    /// <remarks>
    /// Built through <c>Player.CreateStarting</c> over the checkout's own content, because the
    /// aggregate is the only validated construction path and a hand-assembled one would be a second.
    /// The identity is replaced afterwards so the slice names the player the case is about.
    /// </remarks>
    internal static WorldSlice EmptySlice(PlayerId player)
    {
        var created = CorePlayer.CreateStarting(
            player, "Fixture Hero", FixtureInstant, BootContent.Shipped);

        if (created.IsFailure)
        {
            throw new InvalidOperationException(
                "The checkout's own content could not produce a starting player row, so the " +
                $"submission fake has no state to answer with: {created.Error}");
        }

        return new WorldSlice(created.Value, Run: null);
    }
}
