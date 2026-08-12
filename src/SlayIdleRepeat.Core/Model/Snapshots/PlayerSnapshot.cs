using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Model.Snapshots;

/// <summary>
/// 🔒 The persisted shape of the <c>Player</c> aggregate (`30` §11.3) — <em>"flat, serialisable
/// fields"</em> — and the first snapshot record in the game.
/// </summary>
/// <param name="SchemaVersion">
/// 🔒 <see cref="SnapshotSchema.SchemaVersion"/> as it was when this row was written, and the
/// <b>first</b> field of every <c>*Snapshot</c> record (`14` §16.6, `30` §11.3). It is first so
/// that a reader knows the layout before it reads anything laid out by it, and
/// <c>SnapshotFieldOrderPinTests.Every_snapshot_record_carries_SchemaVersion_as_its_first_field</c>
/// enforces the position.
/// </param>
/// <param name="Id">The aggregate root's identity (`30` §4).</param>
/// <param name="DisplayName">
/// The player's display name. Stored, never interpreted: ⚠️ the whole display-name
/// <b>lifecycle</b> — uniqueness, rename, sanction outcome — is `16` <b>O34</b>, open until the
/// launch ladder, and the profanity filter is <b>M4-10</b>'s (`07` §1). <c>Rehydrate</c> refuses
/// blank and nothing else.
/// </param>
/// <param name="LegendLevel">The player's Legend Level. `07` §1.1 runs it 1..200.</param>
/// <param name="LegendXp">Lifetime Legend XP. Never negative.</param>
/// <param name="RunsStarted">
/// 🔒 `02` §2's <c>runCounter</c> — <em>"the player's lifetime runs-started counter, incremented by
/// every <c>START_RUN</c>"</em>, and the fourth argument of
/// <c>runSeed = Hash64(playerId, chapterId, tierId, utcUnixSeconds, runCounter)</c>. Never
/// negative, never reset. It is player-scoped and lifetime, so neither period-cleared counter map
/// could hold it, and the <c>Run</c> aggregate could not either — it has to exist before the run
/// does and outlive it. M1-05 cannot write <c>START_RUN</c> without it, and it is authored rather
/// than guessed, so it ships in v1 instead of costing a <c>SchemaVersion</c> bump on the very next
/// task.
/// </param>
/// <param name="Wallet">
/// 🔒 The <b>six</b> player-scoped wallet currencies of `10` §1 — <c>CROWNS</c>,
/// <c>SOUL_SHARDS</c>, <c>ENHANCE_STONES</c>, <c>MERGE_DUST</c>, <c>BEAST_FEED</c>, <c>HONOR</c> —
/// every one present, none negative. <c>GOLD</c> is <b>run-scoped</b> (milestone assumption A3,
/// <c>tuning/currencies.json</c>) and belongs to <c>RunSnapshot</c>; <c>ENERGY</c> is the seventh
/// player-scoped currency and is carried by <see cref="Energy"/>, because it has two banks and one
/// <c>long</c> could not describe both. Keyed by an enum, so <c>CanonicalStateWriter</c> hashes it
/// in ascending numeric key order with no extra rule (`14` §16.6).
/// </param>
/// <param name="Energy">
/// 🔒 The two Energy banks of `10` §3 and `28` C. This is where the <c>ENERGY</c> currency lives —
/// there is no <c>ENERGY</c> row in <see cref="Wallet"/>, because two sources of truth for one
/// balance is the failure the split exists to avoid.
/// </param>
/// <param name="EnergyAnchorUtc">
/// 🔒 The instant regeneration has been accrued <b>up to</b> — not the last time anything happened.
/// Recorded assumption <b>A1</b> (M1-10): an accrual advances this by
/// <c>wholeUnits × regenInterval</c> and <b>never</b> to the instant asked about, so the sub-unit
/// remainder survives across commands. A player sending a hundred commands an hour must regenerate
/// exactly as much as one sending a single command, and this field is the only thing that makes
/// that true.
/// </param>
/// <param name="LastAppliedAtUtc">
/// `30` §2.3's <c>state.LastAppliedAtUtc</c> — the instant the last command was applied, which is
/// the point <c>AdvanceTime</c> (M1-08) rolls forward <b>from</b>. Distinct from
/// <see cref="EnergyAnchorUtc"/> on purpose: this one advances to <c>NowUtc</c>, that one advances
/// in whole regeneration units.
/// </param>
/// <param name="FtueBeatId">`19` D7's <c>beatId</c> — the tutorial beat this player has reached.</param>
/// <param name="FtueCompletedAtUtc">
/// `19` D7's <c>completedAtUtc</c>. <c>null</c> until beat 10's spend commits; set exactly once,
/// and only at <see cref="Primitives.FtueBeat.B10"/>.
/// </param>
/// <param name="DailyPeriodStartUtc">
/// The 05:00 UTC game-day boundary <see cref="DailyCounters"/> were last reset at (`30` §2.3).
/// </param>
/// <param name="DailyCounters">
/// The daily counter mechanism: counter key → count for the current game day. Deliberately
/// <b>open</b> — see the remarks.
/// </param>
/// <param name="WeeklyPeriodStartUtc">
/// The Monday 05:00 UTC game-week boundary <see cref="WeeklyCounters"/> were last reset at
/// (milestone assumption <b>A2</b>, derived from `27` §4).
/// </param>
/// <param name="WeeklyCounters">The weekly counter mechanism. Same shape, same openness.</param>
/// <remarks>
/// <para>
/// 🔒 <b>Flat, and that is `30` §11.3's word.</b> The only structured members are
/// <see cref="Primitives.PlayerId"/> and <see cref="Primitives.EnergyBanks"/>, both of which are
/// positional value types in <c>Primitives/</c> that <c>CanonicalStateWriter</c> already encodes
/// (<c>CanonicalEncodingTests.CanonicalBytes_encodes_EnergyBanks_as_two_widened_fields</c>), and
/// the two dictionaries. Nesting a <c>PlayerProfile</c> or an <c>FtueProgress</c> record inside
/// would read better and would cost the field-order pin a whole extra level of path for no
/// serialisation benefit; it would also have to live in <c>Model/Snapshots/</c> or
/// <c>Primitives/</c>, because a <b>public</b> positional record under <c>Model/Player/</c> has a
/// public constructor and fails <c>Apply_is_the_only_public_mutation</c>.
/// </para>
/// <para>
/// 🔒 <b>Every timestamp is refused unless its offset is zero</b> — by <c>Player.Rehydrate</c>, not
/// here. <c>CanonicalStateWriter</c> encodes a <see cref="DateTimeOffset"/> as Unix milliseconds,
/// so <c>12:00+02:00</c> and <c>10:00Z</c> hash <b>identically</b> while record equality correctly
/// calls them different — the same class of defect as the <c>-0.0</c> the writer already refuses,
/// arriving through a different door. Normalising here would edit persisted state on its way in;
/// the seam refuses it instead.
/// </para>
/// <para>
/// ⚠️ <b>The counter mechanism is for <em>caps and counts</em>, and it does not cover all five of
/// `30` §2.3's daily resets.</b> A period anchor plus an open key→count map is the right seam for
/// <b>ad caps</b>, <b>dungeon entries</b>, <b>the wheel's free spin</b> and the count-shaped flags
/// M1-08 needs ("has today's <c>BEGIN_SESSION</c> run", "was the daily free refill paid") — none of
/// those systems exists yet (M4-09, M10, `12`), and freezing a closed enum of counter keys now
/// would invent the vocabulary they are the ones placed to name (S6). It does <b>not</b> cover the
/// other two: `30` §2.3's <b>quest expiry</b> persists the day's three drawn quests (`19` B) and
/// <b>daily-shop stock</b> the day's six-offer block (`10` §5.1). Those are drawn <em>slates</em> —
/// ordered lists of content ids with per-entry progress — and a <c>key → long</c> map could only
/// encode them by abusing keys as a set, which would put content ids into the <c>stateHash</c> key
/// space through the back door. <b>M4-09 and M10 will each add a real field here and bump
/// <see cref="SnapshotSchema.SchemaVersion"/>;</b> that cost is named rather than discovered.
/// </para>
/// <para>
/// The counter key is a bare <c>string</c> and not a wrapper id for a mechanical reason:
/// <c>CanonicalStateWriter.KeyOrderFor</c> defines an ascending order for strings and numeric ids
/// <b>only</b>, so an <c>IReadOnlyDictionary&lt;CounterKey, long&gt;</c> would have no canonical
/// encoding at all.
/// </para>
/// <para>
/// 🔒 <b>There is no entitlement field, and that is a ruling rather than an omission.</b> `30` §4
/// lists "entitlement" among <c>Player</c>'s contents; `30` §3 and `12` §2.1 put it on the
/// <b>session</b>, resolved by the composition root into <c>GameContext.Entitlements</c>. §3 wins:
/// two sources of truth for the Plus flag is exactly the failure the single-source rule exists to
/// avoid. Plus expiry stays implementable without stored state — it is
/// <c>GameContext.Entitlements.ExpiresAtUtc</c> compared against <c>GameContext.NowUtc</c>, a pure
/// comparison.
/// </para>
/// <para>
/// ⚠️ <b>What `30` §4 lists on <c>Player</c> and this record does not carry.</b> Inventory and gear
/// instances, the unopened-container shelf (`24` §4.0), pity counters (`24`) and lifetime feat
/// counters (`28` D) are all deferred, each with an entry in
/// <c>SlayIdleRepeat.Architecture.Tests.GapRegister</c> keyed on a type that must not yet exist —
/// so the build fails on the day each becomes writable rather than the hole waiting to be noticed.
/// The feat counters are the sharpest case: `28` D2.2 catalogues 140 feats, but `16` <b>O29</b>
/// defers what each counter <em>measures</em> until the M16 kickoff, and `30` §12.7 forbids
/// rebuilding a counter after the fact. Pets, mounts, talents, presets and unlocks are absent for
/// the same reason and belong to M4-06/07/08/10.
/// </para>
/// <para>
/// Adding, removing or reordering any field here is a <b>serialisation change</b>: it bumps
/// <see cref="SnapshotSchema.SchemaVersion"/> and is handled as a versioned migration, never
/// silently. The field list is pinned in
/// <c>tests/SlayIdleRepeat.Core.Tests/Model/Snapshots/SnapshotFieldOrder.json</c>.
/// </para>
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
    IReadOnlyDictionary<string, long> WeeklyCounters);
