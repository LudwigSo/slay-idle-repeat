using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using CorePlayer = SlayIdleRepeat.Core.Model.Player;
using CoreRun = SlayIdleRepeat.Core.Model.Run;

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

    /// <summary>
    /// A run started wearing nothing — an EMPTY loadout, which is not the same as an absent one.
    /// </summary>
    /// <remarks>
    /// 🔒 The distinction is the domain's, not this fixture's: a run whose starting loadout is null
    /// is refused at rehydration as a state the game could never have persisted, while a run whose
    /// loadout holds no gear is an ordinary first run. No case here is about equipment, so every
    /// fixture run is the second — and stating it once is what keeps that from reading as an
    /// oversight in each.
    /// </remarks>
    private static readonly LoadoutSnapshot BareHanded =
        new(new Dictionary<GearSlot, GearInstanceId>());

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

    /// <summary>A run row in a named phase, with whatever board state a case is about.</summary>
    /// <remarks>
    /// Every board-facing field is a defaulted parameter rather than a second builder, so a case's
    /// arrangement names the one or two facts it depends on and the rest reads as "an ordinary run".
    /// The defaults are a freshly started run: standing at the trailhead, nothing pending, nothing
    /// spent.
    /// </remarks>
    /// <param name="id">The run's identity — what a CONTINUE has to carry.</param>
    /// <param name="player">Whose run it is.</param>
    /// <param name="phase">The phase the run stands in.</param>
    /// <param name="position">The node the run stands on.</param>
    /// <param name="currentHp">The hero's hit points.</param>
    /// <param name="maxHp">The hero's maximum hit points.</param>
    /// <param name="gold">The run's Gold balance.</param>
    /// <param name="chapterId">Which chapter is being played — what the stage lengths are read for.</param>
    /// <param name="pendingTileKind">The unresolved tile's kind, or -1 for none.</param>
    /// <param name="pendingTileLinearIndex">Where that tile sits along the track.</param>
    /// <param name="pendingTileStage">Which stage it belongs to.</param>
    /// <param name="pendingForkJunctionPosition">The paused junction, or null when movement is not paused.</param>
    /// <param name="pendingForkRemainingSteps">Steps left once the chosen edge is taken.</param>
    /// <param name="draftPending">Whether a won battle's draft is open.</param>
    /// <param name="rerollChargesSpentThisStage">Reroll charges spent since the stage began.</param>
    /// <param name="runSeed">
    /// The run's committed seed. Defaulted rather than left to a case, because only the cases about
    /// the battle replay depend on it — every other screen reads a run that has one and does not
    /// care which.
    /// </param>
    /// <param name="rngStreamPositions">
    /// The per-stream draw counters, whose <c>combat</c> row counts battles STARTED. Defaulted to
    /// the empty map a fresh run carries, which is also the shape a replay has to report as "this
    /// run does not say which battle this is" rather than reading as battle zero.
    /// </param>
    internal static RunSnapshot Run(
        RunId id,
        PlayerId player,
        RunPhase phase,
        int position = -1,
        int currentHp = 100,
        int maxHp = 100,
        long gold = 0,
        int chapterId = 1,
        int pendingTileKind = -1,
        int pendingTileLinearIndex = 0,
        int pendingTileStage = 0,
        int? pendingForkJunctionPosition = null,
        int? pendingForkRemainingSteps = null,
        bool draftPending = false,
        int rerollChargesSpentThisStage = 0,
        ulong runSeed = 1,
        IReadOnlyDictionary<string, ulong>? rngStreamPositions = null) =>
        new(
            SnapshotSchema.SchemaVersion,
            id,
            player,
            runSeed,
            chapterId,
            Tier: DifficultyTier.NORMAL,
            LastAppliedAtUtc: FixtureInstant,
            position,
            currentHp,
            maxHp,
            gold,
            RngStreamPositions: rngStreamPositions ?? new Dictionary<string, ulong>(),
            AdUses: new Dictionary<string, long>(),
            ResolvedMinigames: new Dictionary<int, string>(),
            pendingForkJunctionPosition,
            pendingForkRemainingSteps,
            pendingTileKind,
            pendingTileLinearIndex,
            pendingTileStage,
            PendingEventCardId: "",
            Phase: phase,
            DraftPending: draftPending,
            RerollChargesSpentThisStage: rerollChargesSpentThisStage,
            StartingLoadout: BareHanded);

    /// <summary>The same slice, carrying a run rehydrated from the given row.</summary>
    /// <remarks>
    /// 🔒 Through <c>Run.Rehydrate</c> rather than around it. That is the domain's only construction
    /// path for a stored run and it validates every field, so a row a case invented but the game
    /// could never persist fails here, in the arrangement, instead of proving a screen against a
    /// state that cannot occur.
    /// </remarks>
    /// <param name="slice">The slice whose player is kept.</param>
    /// <param name="run">The row to rehydrate, or null to leave the slice without a run.</param>
    internal static WorldSlice SliceWith(WorldSlice slice, RunSnapshot? run)
    {
        ArgumentNullException.ThrowIfNull(slice);

        if (run is null)
        {
            return slice;
        }

        var rehydrated = CoreRun.Rehydrate(run);

        if (rehydrated.IsFailure)
        {
            throw new InvalidOperationException(
                "The run row a case arranged is not one the domain can rehydrate, so the fixture " +
                $"describes a state the game could never have persisted: {rehydrated.Error}");
        }

        return slice with { Run = rehydrated.Value };
    }

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
