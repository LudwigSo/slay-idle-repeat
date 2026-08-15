using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Model.Snapshots;

/// <summary>
/// 🔒 The persisted shape of the <c>Run</c> aggregate (`30` §11.3) — <em>"flat, serialisable
/// fields"</em> — and the second snapshot record in the game, beside
/// <see cref="PlayerSnapshot"/>.
/// </summary>
/// <param name="SchemaVersion">
/// 🔒 <see cref="SnapshotSchema.SchemaVersion"/> as it was when this row was written, and the
/// <b>first</b> field of every <c>*Snapshot</c> record (`14` §16.6, `30` §11.3). It is first so that
/// a reader knows the layout before it reads anything laid out by it, and
/// <c>SnapshotFieldOrderPinTests.Every_snapshot_record_carries_SchemaVersion_as_its_first_field</c>
/// enforces the position.
/// </param>
/// <param name="Id">The aggregate root's identity (`30` §4).</param>
/// <param name="PlayerId">
/// 🔒 The player this run belongs to. `30` §4 makes <c>Run</c> a <b>child</b> of <c>Player</c>
/// rather than a peer, and this field is what makes that true in storage: a run row that named no
/// player would be an orphan the moment it left the process that created it.
/// </param>
/// <param name="RunSeed">
/// 🔒 `02` §2's <c>runSeed = Hash64(playerId, chapterId, tierId, utcUnixSeconds, runCounter)</c>,
/// derived once by <c>SeedDerivation.RunSeed</c> and then <b>committed</b>. `14` §8.1 is explicit
/// that <em>"the <c>Run</c> aggregate holds <c>runSeed</c> plus the per-stream counters; both are
/// authoritative run state"</em> — the board, the drops and the draft of a resumed run are all
/// re-derived from it, so a run that lost it could not be continued at all.
/// <para>
/// ⚠️ <b>Doc contradiction, carried forward (steering S16).</b> `02` §2 says <c>runSeed</c>
/// <em>never leaves the server</em>, while `14` §16.6 makes the client-mirror <c>stateHash</c> a
/// hash of the snapshot DTOs — and this field is in the DTO. Those two cannot both be literally
/// true: either the client hashes a projection that omits the seed, or the seed reaches the client.
/// <b>The M5 kickoff owns the ruling</b> — reassigned from M1-06 at wave 6, which authors neither the
/// wire envelope (M5-03) nor the parity test (M5-12) and shipped without touching <c>stateHash</c>
/// at all. Tracker carry-forward 10. It is <b>not</b> resolved here:
/// dropping the field would break `14` §8.1's "authoritative run state", and hashing a projection is
/// a decision about the wire, not about the aggregate.
/// </para>
/// </param>
/// <param name="ChapterId">
/// The chapter being played. `02` §1 runs chapters 1–8 and <c>chapter.schema.json</c> sets
/// <c>"minimum": 1</c>. ⚠️ <c>Run.Rehydrate</c> refuses anything below 1 and imposes <b>no upper
/// bound and no existence check</b> — see the aggregate's remarks for why that is a deferral with a
/// live expiry rather than a hole.
/// </param>
/// <param name="Tier">
/// The difficulty tier (`02` §2's <c>tierId</c>, `10` §7). Part of <see cref="RunSeed"/>'s
/// derivation, which is why <see cref="DifficultyTier"/>'s numbers may never be renumbered.
/// </param>
/// <param name="LastAppliedAtUtc">
/// 🔒 The instant the last command was applied <b>to this run</b>. `14` §16.3 makes the 48-hour run
/// TTL <em>sliding, measured from the last accepted command</em>, and `14` §16.2 makes
/// <c>RUN_EXPIRED</c> a domain-tier rejection <c>Apply</c> returns — so the TTL is computed from
/// this field. <c>PlayerSnapshot.LastAppliedAtUtc</c> cannot serve: it advances on meta commands
/// too, which would slide a run's expiry from outside the run.
/// </param>
/// <param name="Position">
/// The linear node index the run stands on. `14` §2.3's <c>"newPosition": 19</c> makes it
/// authoritative run state and an integer.
/// <para>
/// 🔒 Its floor is <b>−1</b>, not 0: `03` §1.1 (ruled in `16` A7) begins every run at a virtual
/// trailhead one step before node 0 — <em>"a first roll of <c>1</c> therefore lands on node 0"</em>
/// — so −1 is the position a started-but-unrolled run legitimately persists at, and a floor of zero
/// would refuse to store the state every run passes through. ⚠️ That floor is the <b>only</b>
/// validation: the real invariant, `30` §11.5's <em>"a run's position is a valid node"</em>, needs
/// node identity and is registered as the <c>Board</c> entry in
/// <c>SlayIdleRepeat.Architecture.Tests.GapRegister</c> against M3-01.
/// </para>
/// </param>
/// <param name="CurrentHp">The hero's current hit points. Never negative, never above <paramref name="MaxHp"/>.</param>
/// <param name="MaxHp">
/// The hero's maximum hit points <b>for this run</b>. Stored rather than derived: a resumed run has
/// to render its HP bar without recomputing the whole build, and `03` §7a.5's <c>SHR_HP</c> shrine
/// raises <c>MAX_HP</c> for the run, so the run's maximum is not a function of the player's gear.
/// Never below 1.
/// </param>
/// <param name="Gold">
/// 🔒 The run's <c>GOLD</c> balance — the one <c>RUN</c>-scoped currency of `10` §1 (milestone
/// assumption <b>A3</b>, authored as <c>"scope": "RUN"</c> in <c>tuning/currencies.json</c>). Never
/// negative. It is a bare <c>long</c> rather than a one-row map because there is exactly one
/// run-scoped currency; the aggregate holds it in a field named <c>_wallet</c> so the `30` §7
/// currency rule can see it, which is a fact about the field and not about this component.
/// </param>
/// <param name="RngStreamPositions">
/// 🔒 `14` §8.1's per-stream counters — stream name → next draw index. Sparse: a stream never drawn
/// from is absent and stands at 0, which is what `14` §2.3's wire echo
/// <c>{"dice":12,"board":8}</c> already shows. Every key is a row of <c>RngStreams</c>, validated by
/// the same <c>RngStreams.IsRegistered</c> predicate <c>DeterministicRng</c>'s constructor uses — a
/// name that cannot be drawn from cannot be persisted either.
/// <para>
/// ⚠️ The <c>combat</c> row's <b>unit is different from every other row's</b>. See
/// <c>Run.RngStreamPositions</c>: the <c>combat</c> stream rooted at <see cref="RunSeed"/> is
/// consumed once per battle, to derive that battle's seed, so its position is the number of battles
/// started — the next <c>battleIndex</c> — and not a count of combat draws.
/// </para>
/// </param>
/// <param name="AdUses">
/// `12` §4.3's per-run ad counts — placement id → uses so far, for the thirteen
/// <c>inRunPlacements</c> of <c>tuning/ads.json</c>, every one of which carries
/// <c>"capWindow": "RUN"</c>. Open string keys, no period anchor: the run <em>is</em> the period.
/// The caps are not enforced here — `30` §11.5 keeps computation out of the aggregate — and
/// <c>AD_REVIVE</c> is what carries `02` §6's once-per-run revive, so there is no separate revive
/// flag.
/// </param>
/// <param name="ResolvedMinigames">
/// 🔒 M3-03c, SchemaVersion 3 — `03` §6.2's per-tile legality gate: linear node index → the `03` §6
/// <c>MG_*</c> id resolved there. Sparse; a position absent from here has not had a minigame
/// resolved at it. See <c>Run</c>'s private field of the same name for why the position stands in
/// for a tile instance no board/pending-tile state exists to name yet.
/// </param>
/// <remarks>
/// <para>
/// 🔒 <b>Flat, and that is `30` §11.3's word.</b> The only structured members are
/// <see cref="Primitives.RunId"/> and <see cref="Primitives.PlayerId"/>, both positional value types
/// in <c>Primitives/</c> that <c>CanonicalStateWriter</c> already encodes, and the two dictionaries.
/// It is a <b>positional record with no members outside the primary constructor</b>: the writer
/// refuses any public property <em>or public field</em> declared outside it, which is what makes the
/// field-order pin able to describe the record at all.
/// </para>
/// <para>
/// 🔒 <b>Every timestamp is refused unless its offset is zero</b> — by <c>Run.Rehydrate</c>, not
/// here, exactly as <see cref="PlayerSnapshot"/> does it. <c>CanonicalStateWriter</c> encodes a
/// <see cref="DateTimeOffset"/> as Unix milliseconds, so <c>12:00+02:00</c> and <c>10:00Z</c> hash
/// <b>identically</b> while record equality correctly calls them different. Normalising here would
/// edit persisted state on its way in; the seam refuses it instead.
/// </para>
/// <para>
/// ⚠️ <b>What `30` §4 lists on <c>Run</c> and this record does not carry.</b> §4's Run row
/// enumerates ten things; five are here (position, HP, run Gold, RNG stream positions, per-run ad
/// uses) and five are not: the <b>board</b>, the <b>drafted perks</b>, the <b>held consumables and
/// armed Escape Rope flag</b>, the <b>pending fork choice</b> and the <b>curses</b>. Each is
/// deferred with an entry in <c>SlayIdleRepeat.Architecture.Tests.GapRegister</c> keyed on a type
/// that must not yet exist, so the build fails on the day each becomes writable rather than the hole
/// waiting to be noticed. The run's <b>phase</b> (`02` §1.1's state machine) is deferred too, for a
/// sharper reason: §1.1's diagram is a <em>client presentation</em> machine while `14` §2.3's
/// <c>ROLL_DICE</c> answers face, movement and landing in one command, so which of its states are
/// server-side aggregate state is M3-05's ruling.
/// </para>
/// <para>
/// ⚠️ <b>Two deliberate omissions with their costs named.</b> There is no <c>sequence</c>: `14`
/// §16.3's per-run monotone command sequence is the <em>idempotency record's</em>, in
/// <c>Application</c>, and <see cref="PlayerSnapshot"/> carries none either. And there is no
/// <c>StartedAtUtc</c>: the TTL is sliding, so nothing needs it, and run-duration analytics comes
/// off the event stream. If a later milestone needs either, it costs a
/// <see cref="SnapshotSchema.SchemaVersion"/> bump — that is the price, stated rather than
/// discovered.
/// </para>
/// <para>
/// Adding, removing or reordering any field here is a <b>serialisation change</b>: it bumps
/// <see cref="SnapshotSchema.SchemaVersion"/> and is handled as a versioned migration, never
/// silently. The field list is pinned in
/// <c>tests/SlayIdleRepeat.Core.Tests/Model/Snapshots/SnapshotFieldOrder.json</c>.
/// </para>
/// </remarks>
public sealed record RunSnapshot(
    int SchemaVersion,
    RunId Id,
    PlayerId PlayerId,
    ulong RunSeed,
    int ChapterId,
    DifficultyTier Tier,
    DateTimeOffset LastAppliedAtUtc,
    int Position,
    int CurrentHp,
    int MaxHp,
    long Gold,
    IReadOnlyDictionary<string, ulong> RngStreamPositions,
    IReadOnlyDictionary<string, long> AdUses,
    IReadOnlyDictionary<int, string> ResolvedMinigames);
