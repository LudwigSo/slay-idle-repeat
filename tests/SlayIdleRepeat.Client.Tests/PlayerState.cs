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
    /// <summary>
    /// A profile row that <c>Player.Rehydrate</c> accepts, which <see cref="Player"/> alone does not.
    /// </summary>
    /// <remarks>
    /// 🔴 <b><see cref="Player"/> builds a row this project never rehydrated, and that only became
    /// visible when M7-11 projected one.</b> It leaves <c>Inventory</c>, <c>Loadout</c> and
    /// <c>Presets</c> at their <c>null</c> defaults, and all three are <b>faults</b> to the domain — an
    /// absent inventory and an empty one are indistinguishable once read, and only one of them is a row
    /// the game ever wrote. Every earlier client test read the snapshot's fields directly, so a row that
    /// could not become an aggregate was never asked to be one.
    /// </remarks>
    /// <param name="id">Whose row this is.</param>
    /// <param name="legendLevel">
    /// The Legend Level the hero is composed at. It reaches the battle screen's own prediction, where
    /// it decides whether the hero can win at all — so a case about a fight names it rather than
    /// taking a default that happens to lose.
    /// </param>
    internal static PlayerSnapshot Rehydratable(PlayerId id, int legendLevel = 1) =>
        Player(id, legendLevel: legendLevel) with
    {
        // All six, because the domain refuses a partial wallet: a missing row read as zero is
        // indistinguishable from a balance a migration dropped.
        Wallet = new Dictionary<CurrencyId, long>
        {
            [CurrencyId.CROWNS] = 0,
            [CurrencyId.SOUL_SHARDS] = 0,
            [CurrencyId.ENHANCE_STONES] = 0,
            [CurrencyId.MERGE_DUST] = 0,
            [CurrencyId.BEAST_FEED] = 0,
            [CurrencyId.HONOR] = 0,
        },

        // 05:00 UTC is the game-day boundary and the game WEEK starts Monday 05:00 UTC, so the two
        // cannot share an instant that is merely convenient.
        DailyPeriodStartUtc = GameDayStart,
        WeeklyPeriodStartUtc = GameWeekStart,

        // Each of these is a FAULT when absent rather than an empty default, and each says why in its
        // own message: an absent pity map read as empty would put every guarantee a player has been
        // building towards back at zero, invisibly.
        FeatCounters = new Dictionary<string, long>(),
        PityCounters = new Dictionary<string, int>(),
        AutoSalvageRules = [],
        Inventory = new InventorySnapshot(0, [], []),
        Loadout = BareHanded,
        Presets = [],
    };

    /// <summary>A 05:00 UTC game-day boundary (`30` §2.3).</summary>
    private static readonly DateTimeOffset GameDayStart =
        new(2026, 4, 27, 5, 0, 0, TimeSpan.Zero);

    /// <summary>A MONDAY 05:00 UTC game-week boundary — 27 April 2026 is a Monday.</summary>
    private static readonly DateTimeOffset GameWeekStart = GameDayStart;

    /// <summary>
    /// A profile row carrying the given luck-protection counters — what the S14 footer reads.
    /// </summary>
    /// <remarks>
    /// 🔒 Keyed by the caller rather than by this fixture, because the KEY is the thing under test
    /// on the screens that read one: <c>LuckTuning.CounterKey</c> is the only place a counter key is
    /// spelled, and a fixture that spelled its own would prove a screen against counters the game never
    /// writes. A case wanting a standing counter therefore takes the key off the projection first.
    /// </remarks>
    /// <param name="id">Whose row this is.</param>
    /// <param name="pityCounters">The counters, keyed as the game keys them.</param>
    internal static PlayerSnapshot WithPityCounters(
        PlayerId id, IReadOnlyDictionary<string, int> pityCounters) => Rehydratable(id) with
    {
        PityCounters = pityCounters,
    };

    /// <summary>A profile whose gear stock is empty — where a fresh account stands.</summary>
    /// <remarks>
    /// An <see cref="InventorySnapshot"/> holding nothing, never <c>null</c>: an absent inventory is a
    /// rehydrate fault, and the state this fixture is for is a real one a player can be in.
    /// </remarks>
    internal static PlayerSnapshot WithEmptyStock(PlayerId id) => Rehydratable(id) with
    {
        Inventory = new InventorySnapshot(0, [], []),
    };

    /// <summary>
    /// A profile carrying one item in the bag and one the bag is HOLDING — <c>08</c> §5's overflow.
    /// </summary>
    /// <remarks>
    /// 🔒 Both bands populated, because the cases about this fixture are about the difference between
    /// them: what is stored can be equipped and what is held cannot, and a fixture with only one band
    /// could not tell a screen that honours that from one that ignores it.
    /// </remarks>
    internal static PlayerSnapshot WithOverflowingStock(PlayerId id) => Rehydratable(id) with
    {
        Inventory = new InventorySnapshot(
            0,
            [Item("stored_blade", GearSlot.WEAPON, GearFamily.BLADE)],
            [Item("held_blade", GearSlot.WEAPON, GearFamily.BLADE)]),
    };

    /// <summary>One persisted gear row, at the bottom band and unenhanced.</summary>
    /// <remarks>
    /// Written out here rather than borrowed from a Core fixture: this project may not reference the
    /// Core test assembly, and a row is a handful of fields whose shape the compiler checks anyway.
    /// </remarks>
    private static GearInstanceSnapshot Item(string id, GearSlot slot, GearFamily family) => new(
        new GearInstanceId(id),
        DefId: "GEAR_" + family,
        slot,
        family,
        Rarity.C,
        ChapterOrigin: 1,
        Quality: 0.5,
        EnhanceLevel: 0,
        EnhanceFailures: 0,
        Affixes: [],
        Locked: false);

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
    /// <param name="draftBattleKind">
    /// The tile kind of the battle that opened that draft, or -1 for none.
    /// <para>
    /// 🔒 A case that sets <paramref name="draftPending"/> has to set this and
    /// <paramref name="draftBattleStage"/> as well. <c>Run.Rehydrate</c> refuses a row whose draft
    /// is open while these two stand at their no-draft values, and the rules layer's own draft
    /// derivation is keyed on the stage — so a row left at the defaults describes a state the game
    /// could never have persisted, and any screen projecting a draft from one is being proven
    /// against a run that cannot occur.
    /// </para>
    /// </param>
    /// <param name="draftBattleStage">The stage that battle belonged to, 1-3, or 0 for no draft.</param>
    /// <param name="runSeed">
    /// The run's committed seed. Defaulted rather than left to a case, because only the cases about
    /// the battle replay depend on it — every other screen reads a run that has one and does not
    /// care which.
    /// </param>
    /// <param name="rngStreamPositions">
    /// <param name="draftsSinceLegendaryOffered">
    /// The <c>DRAFT</c> Legendary-pity counter — drafts stood since one offered a Legendary.
    /// </param>
    /// <param name="draftsWithoutAboveCommon">The quality-floor counter (<c>24</c> §4.7 F1).</param>
    /// <param name="draftsWithoutOwnedUpgrade">The upgrade-famine counter (<c>24</c> §4.7 F3).</param>
    /// <param name="ownedPerkTiers">
    /// The perks this run holds and their tiers. Load-bearing for the famine, whose guarantee is only
    /// due while at least one owned perk sits below its top tier.
    /// </param>
    /// <param name="runSeed">The run's committed seed.</param>
    /// <param name="bankedLegendXp">Legend XP the run banked, before the completion multiplier.</param>
    /// <param name="bankedSoulShards">Soul Shards the run banked, before the same multiplier.</param>
    /// <param name="bossDefeated">
    /// Whether the Boss is dead — what makes a run-end a victory rather than a death or an abandonment.
    /// </param>
    /// <param name="itemsAtOrAboveFloorBand">
    /// How many items at or above the session floor's band the run produced (<c>24</c> §4.3 D3).
    /// </param>
    /// <param name="adUses">
    /// Per-run ad counts by placement — where the run's ONE revive is counted (<c>02</c> §6). Defaulted
    /// to the empty map a run that has used nothing carries.
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
        int draftBattleKind = -1,
        int draftBattleStage = 0,
        int draftsSinceLegendaryOffered = 0,
        int draftsWithoutAboveCommon = 0,
        int draftsWithoutOwnedUpgrade = 0,
        IReadOnlyDictionary<string, int>? ownedPerkTiers = null,
        ulong runSeed = 1,
        IReadOnlyDictionary<string, ulong>? rngStreamPositions = null,
        long bankedLegendXp = 0,
        long bankedSoulShards = 0,
        bool bossDefeated = false,
        int itemsAtOrAboveFloorBand = 0,
        IReadOnlyDictionary<string, long>? adUses = null,
        IReadOnlyList<string>? curses = null,
        IReadOnlyList<string>? shrineBuffs = null,
        ulong? shopOfferDraw = null,
        int shopSlotsPurchased = 0,
        int shopRefreshesUsedThisVisit = 0) =>
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
            AdUses: adUses ?? new Dictionary<string, long>(),
            ResolvedMinigames: new Dictionary<int, string>(),
            pendingForkJunctionPosition,
            pendingForkRemainingSteps,
            pendingTileKind,
            pendingTileLinearIndex,
            pendingTileStage,
            PendingEventCardId: "",
            Phase: phase,
            DraftPending: draftPending,
            DraftBattleKind: draftBattleKind,
            DraftBattleStage: draftBattleStage,
            OwnedPerkTiers: ownedPerkTiers,
            DraftsSinceLegendaryOffered: draftsSinceLegendaryOffered,
            DraftsWithoutAboveCommon: draftsWithoutAboveCommon,
            DraftsWithoutOwnedUpgrade: draftsWithoutOwnedUpgrade,
            StartingLoadout: BareHanded,
            BankedLegendXp: bankedLegendXp,
            BankedSoulShards: bankedSoulShards,
            BossDefeated: bossDefeated,
            ItemsAtOrAboveFloorBand: itemsAtOrAboveFloorBand,
            ShrineBuffs: shrineBuffs,
            Curses: curses,
            ShopOfferDraw: shopOfferDraw,
            ShopSlotsPurchased: shopSlotsPurchased,
            ShopRefreshesUsedThisVisit: shopRefreshesUsedThisVisit);

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
    /// Built through <c>Player.CreateStartingNamedAfterItsOwnId</c> over the checkout's own content,
    /// because the aggregate is the only validated construction path and a hand-assembled one would
    /// be a second. The identity is replaced afterwards so the slice names the player the case is
    /// about — which is also why the fixture name is the identity rather than a label of its own.
    /// </remarks>
    internal static WorldSlice EmptySlice(PlayerId player)
    {
        var created = CorePlayer.CreateStartingNamedAfterItsOwnId(
            player, FixtureInstant, BootContent.Shipped);

        if (created.IsFailure)
        {
            throw new InvalidOperationException(
                "The checkout's own content could not produce a starting player row, so the " +
                $"submission fake has no state to answer with: {created.Error}");
        }

        return new WorldSlice(created.Value, Run: null);
    }
}
