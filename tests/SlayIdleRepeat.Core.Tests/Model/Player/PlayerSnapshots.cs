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
/// Hermetic <see cref="PlayerSnapshot"/> fixtures — one valid row, and a <c>With(...)</c> that replaces
/// exactly one field so a test names the single thing it is about.
/// </summary>
/// <remarks>
/// A test that built a whole snapshot inline would restate fourteen fields to change one, and the
/// reader could not tell which it was asserting about.
/// <para>
/// Every instant is UTC with a zero offset and every period boundary is 05:00 UTC, because
/// <c>Player.Rehydrate</c> refuses anything else. <see cref="Monday"/> and <see cref="Wednesday"/> are
/// real weekdays checked against the calendar, since a fixture quietly naming the wrong one would make
/// the Monday-boundary assertions pass for the wrong reason.
/// </para>
/// </remarks>
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

    /// <summary>A wallet with every player-scoped currency present at zero.</summary>
    internal static IReadOnlyDictionary<CurrencyId, long> EmptyWallet => Wallet();

    /// <summary>
    /// A wallet holding every player-scoped currency, with the named ones overridden.
    /// </summary>
    /// <remarks>
    /// Built from <c>Player.WalletCurrencies</c> rather than from a second list of six, so a
    /// currency added to the aggregate cannot leave the fixtures silently short of a row — which
    /// would make every rehydration test fail for a reason unrelated to what it asserts.
    /// </remarks>
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
    /// The valid row with one of its three reference-typed maps replaced by <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="With"/> cannot express this: its optional parameters read <c>null</c> as "keep
    /// the shipped value", which is what makes it readable — so the null cases get their own door
    /// rather than a sentinel that every other call site would have to understand.
    /// </remarks>
    /// <remarks>
    /// <paramref name="cleared"/> and <paramref name="feats"/> are the two appended maps, and they
    /// are deliberately asymmetric: a null <c>ClearedChapterTiers</c> is READ as "nothing cleared
    /// yet", while a null <c>FeatCounters</c> is a FAULT. Both stay expressible here so that
    /// asymmetry is testable rather than assumed.
    /// </remarks>
    internal static PlayerSnapshot WithNull(
        bool wallet = false,
        bool daily = false,
        bool weekly = false,
        bool cleared = false,
        bool feats = false,
        bool pity = false) =>
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
            pity ? null! : Pity());

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
        IReadOnlyDictionary<string, int>? pityCounters = null) =>
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

            // The starting calendar: day 1 open and UNCLAIMED, which is where a brand-new player
            // stands. A fixture that defaulted to `claimed` would make the paused arm the
            // exception rather than the rule.
            loginCalendarDay ?? LoginCalendarTuning.FirstDay,
            loginCalendarDayClaimed ?? false,
            clearedChapterTiers ?? Counters(),
            featCounters ?? Counters(),
            pityCounters ?? Pity());
}
