using System.Collections.ObjectModel;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Tests.Model;

// Namespace SlayIdleRepeat.Core.Tests.Model, not ...Tests.Model.Player, and the files still sit
// under Model/Player/. Same reason the production aggregate does it: a child namespace named
// `Player` shadows the type `Player` for everything inside SlayIdleRepeat.Core.Tests.Model, so a
// test written here could not name the very class it is testing (CS0118).

/// <summary>
/// Hermetic <see cref="PlayerSnapshot"/> fixtures — one valid row, and a <c>With(...)</c> that
/// replaces exactly one field so a test names the single thing it is about. Every instant is UTC
/// and every period boundary is 05:00 UTC, because <c>Player.Rehydrate</c> refuses anything else.
/// </summary>
internal static class PlayerSnapshots
{
    /// <summary>2026-08-10 05:00 UTC — a Monday, so a legal game-<b>week</b> boundary.</summary>
    internal static readonly DateTimeOffset Monday = new(2026, 8, 10, 5, 0, 0, TimeSpan.Zero);

    /// <summary>2026-08-12 05:00 UTC — a Wednesday, so a legal game-<b>day</b> boundary only.</summary>
    internal static readonly DateTimeOffset Wednesday = new(2026, 8, 12, 5, 0, 0, TimeSpan.Zero);

    /// <summary>2026-08-12 09:41:07 UTC — an ordinary instant, on no boundary at all.</summary>
    internal static readonly DateTimeOffset Midmorning = new(2026, 8, 12, 9, 41, 7, TimeSpan.Zero);

    /// <summary>The identity every fixture uses unless a test is about identity.</summary>
    internal static readonly PlayerId Id = new("PLAYER_TEST");

    /// <summary>Exactly one run's authored price, in the main bar and nothing in the Reserve.</summary>
    /// <remarks>
    /// 🔴 <b>A run costs Energy.</b> <c>Handlers.StartRun</c> charges
    /// <c>EnergyTuning.RunCost</c> through <c>EnergyMath.Spend</c>, so the empty banks
    /// <see cref="Valid"/> carries buy no runs at all and every <c>START_RUN</c> fixture has to say
    /// what it is paying with. Exactly the price rather than a full bar, so a fixture that opened
    /// two runs off one row is refused loudly instead of quietly paying twice; and the price is read
    /// from the same document the handler reads rather than transcribed here.
    /// </remarks>
    internal static EnergyBanks OneRunsWorth { get; } =
        new(SlayIdleRepeat.Core.Tests.Content.ProgressionDocuments.ShippedRunCost, 0);

    /// <summary>
    /// A wallet holding every player-scoped currency, with the named ones overridden. Built from
    /// <c>Player.WalletCurrencies</c> so a currency added to the aggregate cannot leave the
    /// fixtures silently short of a row.
    /// </summary>
    internal static IReadOnlyDictionary<CurrencyId, long> Wallet(
        params (CurrencyId Currency, long Balance)[] overrides)
    {
        var wallet = Core.Model.Player.WalletCurrencies.ToDictionary(c => c, _ => 0L);

        foreach (var (currency, balance) in overrides)
        {
            wallet[currency] = balance;
        }

        return new ReadOnlyDictionary<CurrencyId, long>(wallet);
    }

    /// <summary>An empty counter map, ordinal, of the shape the snapshot carries.</summary>
    internal static IReadOnlyDictionary<string, long> Counters(
        params (string Key, long Count)[] entries) =>
        new ReadOnlyDictionary<string, long>(
            entries.ToDictionary(e => e.Key, e => e.Count, StringComparer.Ordinal));

    /// <summary>A pity counter map of the shape the snapshot carries. Ordinal, like the aggregate's.</summary>
    internal static IReadOnlyDictionary<string, int> Pity(
        params (string Key, int Misses)[] entries) =>
        new ReadOnlyDictionary<string, int>(
            entries.ToDictionary(e => e.Key, e => e.Misses, StringComparer.Ordinal));

    /// <summary>
    /// A valid row: Legend Level 1, an empty wallet, empty banks, the tutorial at its first beat,
    /// both counter periods open and empty.
    /// </summary>
    internal static PlayerSnapshot Valid { get; } = With();

    /// <summary>
    /// The valid row with one reference-typed field replaced by <c>null</c> — <see cref="With"/>'s
    /// optional parameters read <c>null</c> as "keep the shipped value", so the null cases get
    /// their own door.
    /// </summary>
    /// <remarks>
    /// 🔴 Every parameter is optional and passed BY NAME; a new one is appended LAST — a parameter
    /// inserted mid-signature merges textually clean and silently re-binds later positional
    /// arguments.
    /// </remarks>
    internal static PlayerSnapshot WithNull(
        bool wallet = false,
        bool daily = false,
        bool weekly = false,
        bool cleared = false,
        bool feats = false,
        bool pity = false,
        bool inventory = false,
        bool autoSalvage = false,
        bool loadout = false,
        bool presets = false) =>
        new(
            SnapshotSchema.SchemaVersion,
            Id,
            "Ludwig the Unhurried",
            1,
            0L,
            0L,
            wallet ? null! : Wallet(),
            new EnergyBanks(0, 0),
            Midmorning,
            Midmorning,
            FtueBeat.B0,
            null,
            Wednesday,
            daily ? null! : Counters(),
            Monday,
            weekly ? null! : Counters(),
            LoginCalendarTuning.FirstDay,
            false,
            cleared ? null : Counters(),
            feats ? null! : Counters(),
            pity ? null! : Pity(),
            Inventory: inventory ? null! : EmptyInventory,
            AutoSalvageRules: autoSalvage ? null! : NoAutoSalvage,
            TalentPoints: 0L,
            Loadout: loadout ? null! : EmptyLoadout,
            Presets: presets ? null! : NoPresets);

    /// <summary>The valid row with individual fields replaced. Omit a parameter to keep it.</summary>
    internal static PlayerSnapshot With(
        int? schemaVersion = null,
        PlayerId? id = null,
        string? displayName = null,
        int? legendLevel = null,
        long? legendXp = null,
        long? runsStarted = null,
        IReadOnlyDictionary<CurrencyId, long>? wallet = null,
        EnergyBanks? energy = null,
        DateTimeOffset? energyAnchorUtc = null,
        DateTimeOffset? lastAppliedAtUtc = null,
        FtueBeat? ftueBeatId = null,
        DateTimeOffset? ftueCompletedAtUtc = null,
        DateTimeOffset? dailyPeriodStartUtc = null,
        IReadOnlyDictionary<string, long>? dailyCounters = null,
        DateTimeOffset? weeklyPeriodStartUtc = null,
        IReadOnlyDictionary<string, long>? weeklyCounters = null,
        int? loginCalendarDay = null,
        bool? loginCalendarDayClaimed = null,
        IReadOnlyDictionary<string, long>? clearedChapterTiers = null,
        IReadOnlyDictionary<string, long>? featCounters = null,
        IReadOnlyDictionary<string, int>? pityCounters = null,
        InventorySnapshot? inventory = null,
        IReadOnlyList<AutoSalvageRule>? autoSalvageRules = null,
        long? talentPoints = null,
        LoadoutSnapshot? loadout = null,
        IReadOnlyList<LoadoutPresetSnapshot>? presets = null,
        int? battleHashMismatches = null) =>
        new(
            schemaVersion ?? SnapshotSchema.SchemaVersion,
            id ?? Id,
            displayName ?? "Ludwig the Unhurried",
            legendLevel ?? 1,
            legendXp ?? 0L,
            runsStarted ?? 0L,
            wallet ?? Wallet(),
            energy ?? new EnergyBanks(0, 0),
            energyAnchorUtc ?? Midmorning,
            lastAppliedAtUtc ?? Midmorning,
            ftueBeatId ?? FtueBeat.B0,
            ftueCompletedAtUtc,
            dailyPeriodStartUtc ?? Wednesday,
            dailyCounters ?? Counters(),
            weeklyPeriodStartUtc ?? Monday,
            weeklyCounters ?? Counters(),
            loginCalendarDay ?? LoginCalendarTuning.FirstDay,
            loginCalendarDayClaimed ?? false,
            clearedChapterTiers ?? Counters(),
            featCounters ?? Counters(),
            pityCounters ?? Pity(),
            Inventory: inventory ?? EmptyInventory,
            AutoSalvageRules: autoSalvageRules ?? NoAutoSalvage,
            TalentPoints: talentPoints ?? 0L,
            Loadout: loadout ?? EmptyLoadout,
            Presets: presets ?? NoPresets,
            BattleHashMismatches: battleHashMismatches ?? 0);

    /// <summary>Empty, never <c>null</c> — an absent loadout is a fault. Expression-bodied for <see cref="EmptyInventory"/>'s reason.</summary>
    internal static LoadoutSnapshot EmptyLoadout => new(Gear());

    /// <summary>Empty, never <c>null</c>, for the reason <see cref="EmptyLoadout"/> records.</summary>
    internal static IReadOnlyList<LoadoutPresetSnapshot> NoPresets => [];

    /// <summary>A slot → instance map of the shape a loadout carries.</summary>
    internal static IReadOnlyDictionary<GearSlot, GearInstanceId> Gear(
        params (GearSlot Slot, string InstanceId)[] entries) =>
        new ReadOnlyDictionary<GearSlot, GearInstanceId>(
            entries.ToDictionary(e => e.Slot, e => new GearInstanceId(e.InstanceId)));

    /// <summary>
    /// Empty, never <c>null</c> — an absent inventory is a fault. Expression-bodied and that is
    /// load-bearing: static initialisers run in declaration order, so an initialised property below
    /// <see cref="Valid"/> would still be <c>null</c> when <see cref="Valid"/> was built.
    /// </summary>
    internal static InventorySnapshot EmptyInventory => new(0, [], []);

    /// <summary>Empty, never <c>null</c>, and expression-bodied, for <see cref="EmptyInventory"/>'s two reasons.</summary>
    internal static IReadOnlyList<AutoSalvageRule> NoAutoSalvage => [];
}
