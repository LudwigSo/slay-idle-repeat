using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Model.Snapshots;

/// <summary>The persisted shape of the <c>Player</c> aggregate — flat, serialisable fields.</summary>
/// <param name="SchemaVersion"><see cref="SnapshotSchema.SchemaVersion"/> as it was when this row was written. Always the first field.</param>
/// <param name="Id">The aggregate root's identity.</param>
/// <param name="DisplayName">The player's display name, stored and never interpreted. <c>Rehydrate</c> refuses blank and nothing else.</param>
/// <param name="LegendLevel">The player's Legend Level.</param>
/// <param name="LegendXp">Lifetime Legend XP. Never negative.</param>
/// <param name="RunsStarted">The lifetime runs-started counter, incremented by every <c>START_RUN</c> and fed into <c>runSeed</c> derivation. Never negative, never reset.</param>
/// <param name="Wallet">The six player-scoped wallet currencies; every one present, none negative. <c>GOLD</c> is run-scoped (<see cref="RunSnapshot"/>); <c>ENERGY</c> is carried separately by <see cref="Energy"/>.</param>
/// <param name="Energy">The two Energy banks. This is where the <c>ENERGY</c> currency lives.</param>
/// <param name="EnergyAnchorUtc">The instant regeneration has been accrued up to, not the last time anything happened — accrual advances this by whole units so the sub-unit remainder survives.</param>
/// <param name="LastAppliedAtUtc">The instant the last command was applied. Distinct from <see cref="EnergyAnchorUtc"/>, which advances in whole regeneration units instead.</param>
/// <param name="FtueBeatId">The tutorial beat this player has reached.</param>
/// <param name="FtueCompletedAtUtc"><c>null</c> until beat 10's spend commits; set exactly once.</param>
/// <param name="DailyPeriodStartUtc">The 05:00 UTC game-day boundary <see cref="DailyCounters"/> were last reset at.</param>
/// <param name="DailyCounters">The daily counter mechanism: counter key → count for the current game day. Deliberately open.</param>
/// <param name="WeeklyPeriodStartUtc">The Monday 05:00 UTC game-week boundary <see cref="WeeklyCounters"/> were last reset at.</param>
/// <param name="WeeklyCounters">The weekly counter mechanism. Same shape, same openness.</param>
/// <param name="LoginCalendarDay">The login-calendar day currently open, counted from 1. Never below 1.</param>
/// <param name="LoginCalendarDayClaimed">Whether <see cref="LoginCalendarDay"/> has been claimed. A missed or unclaimed day pauses the calendar rather than skipping it.</param>
/// <param name="ClearedChapterTiers">The (Chapter, Tier) pairs cleared at least once, gating the one-time first-clear Soul Shard grant. Defaulted to <c>null</c>, read as "nothing cleared yet".</param>
/// <param name="FeatCounters">
/// The lifetime feat counters: counter id → count, additive, never reset. ⚠️ Unlike
/// <see cref="ClearedChapterTiers"/>, <c>null</c> is a <b>fault</b>, not an empty map — a missing
/// lifetime map read as empty is a whole history silently zeroed. The optional default is a C#
/// requirement, not a permitted value.
/// </param>
/// <remarks>
/// Flat: the only structured members are <see cref="Primitives.PlayerId"/> and
/// <see cref="Primitives.EnergyBanks"/>, plus the counter dictionaries. Every timestamp is refused
/// unless its offset is zero (checked by <c>Player.Rehydrate</c>): <c>CanonicalStateWriter</c> encodes
/// a <see cref="DateTimeOffset"/> as Unix milliseconds, so two differently-offset timestamps naming
/// the same instant would hash identically while record equality calls them different. Adding,
/// removing or reordering any field here bumps <see cref="SnapshotSchema.SchemaVersion"/> and is a
/// versioned migration, never silent; the field list is pinned in
/// <c>tests/SlayIdleRepeat.Core.Tests/Model/Snapshots/SnapshotFieldOrder.json</c>.
/// </remarks>
public sealed record PlayerSnapshot(
    int SchemaVersion,
    PlayerId Id,
    string DisplayName,
    int LegendLevel,
    long LegendXp,
    long RunsStarted,
    IReadOnlyDictionary<CurrencyId, long> Wallet,
    EnergyBanks Energy,
    DateTimeOffset EnergyAnchorUtc,
    DateTimeOffset LastAppliedAtUtc,
    FtueBeat FtueBeatId,
    DateTimeOffset? FtueCompletedAtUtc,
    DateTimeOffset DailyPeriodStartUtc,
    IReadOnlyDictionary<string, long> DailyCounters,
    DateTimeOffset WeeklyPeriodStartUtc,
    IReadOnlyDictionary<string, long> WeeklyCounters,
    int LoginCalendarDay,
    bool LoginCalendarDayClaimed,
    IReadOnlyDictionary<string, long>? ClearedChapterTiers = null,
    IReadOnlyDictionary<string, long>? FeatCounters = null);
