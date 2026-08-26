using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Wire;

/// <summary>
/// The client-visible projection of <c>PlayerSnapshot</c> — what rides the wire as <c>profile</c>
/// and what the wire <c>stateHash</c> covers on the player's side (14 §16.6).
/// </summary>
/// <remarks>
/// <para>
/// Field-for-field the persisted snapshot, in the persisted order, minus what the client may never
/// see. One field is withheld: <c>BattleHashMismatches</c>, whose own contract is
/// "never player-facing" (14 §9's <em>no player-facing error</em> clause) — an anti-cheat tally on
/// the wire would be a cheat-tool progress bar, and a mirror cannot hash a field it is not sent.
/// </para>
/// <para>
/// A positional record with no members outside the primary constructor, because
/// <c>CanonicalStateWriter</c> hashes it and the field-order pin describes it — the same shape
/// discipline the snapshots carry, pinned in
/// <c>tests/SlayIdleRepeat.Application.Tests/Wire/WireProjectionFieldOrder.json</c>. Adding,
/// removing or reordering a field here changes every wire <c>stateHash</c> in existence and is
/// handled as the pinned, deliberate change it is — in step with the snapshot it projects, whose
/// own change bumps <c>SchemaVersion</c> first.
/// </para>
/// <para>
/// Field meanings are the snapshot's; they are deliberately not restated, because a copy would rot.
/// Only construction through <see cref="WireProjections.Of(PlayerSnapshot)"/> keeps the two aligned.
/// </para>
/// </remarks>
public sealed record PlayerWireProjection(
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
    IReadOnlyDictionary<string, long>? ClearedChapterTiers,
    IReadOnlyDictionary<string, long>? FeatCounters,
    IReadOnlyDictionary<string, int>? PityCounters,
    InventorySnapshot? Inventory,
    IReadOnlyList<AutoSalvageRule>? AutoSalvageRules,
    long TalentPoints,
    LoadoutSnapshot? Loadout,
    IReadOnlyList<LoadoutPresetSnapshot>? Presets);
