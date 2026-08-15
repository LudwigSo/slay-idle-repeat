using System.Collections.ObjectModel;
using System.Globalization;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Model;

/// <summary>
/// 🔒 The <c>Run</c> aggregate root (`30` §4) — a <b>child</b> of <c>Player</c>: the committed run
/// seed and the per-stream RNG counters (`14` §8.1), the position, the hero's hit points, the
/// run-scoped <c>GOLD</c> balance and the per-run ad uses.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Why this type lives in <c>Core/Model/Run/</c> but in namespace
/// <c>SlayIdleRepeat.Core.Model</c>.</b> The same measurement <c>Player</c> records, and this is the
/// type it was measured <em>for</em>: a namespace <c>SlayIdleRepeat.Core.Model.Run</c> containing a
/// type <c>Run</c> makes the type <b>unnameable</b> from anywhere inside
/// <c>SlayIdleRepeat.Core.Model</c> — the child namespace shadows it and the compiler answers
/// <c>error CS0118: 'Run' is a namespace but is used like a type</c>. `30` §4.1 writes
/// <c>WorldSlice(Player Player, Run? Run, …)</c> and puts it in <c>Model/</c>, so <b>M1-06 would hit
/// it on its first line</b>. `30` §11.4's structure block draws <b>directories</b>
/// (<c>Model/ ├── Player/ Run/ Guild/</c>) and never says the namespace mirrors them. The directory
/// is the file layout; the namespace is the layer.
/// </para>
/// <para>
/// 🔒 <b>Public getters, private constructor, <c>internal</c> mutators</b> (`30` §11.2): <em>"The
/// only public way to change state in this game is <c>GameRules.Apply</c>. Everything else the
/// outside world can see is a getter."</em> The two public non-getters are <see cref="ToSnapshot"/>
/// and <see cref="Rehydrate"/> — `30` §11.3's validating factory pair, the one hole <c>internal</c>
/// would otherwise leave, since the persistence adapter has to rebuild a run from a row without
/// <c>InternalsVisibleTo</c>.
/// </para>
/// <para>
/// 🔒 <b>It holds state and invariants; it does not compute</b> (`30` §11.5). There is no dice
/// arithmetic here, no damage formula and no cap check against <c>ads.json</c>: a handler computes
/// and hands the answer to <see cref="SetHitPoints"/> or <see cref="CountAdUse"/>, and the
/// aggregate's job is to refuse an answer that would break an invariant. Overheal is clamped by the
/// rule that computes it, not accepted here.
/// </para>
/// <para>
/// ⚠️ <b>There is no factory for a <em>new</em> run.</b> <c>START_RUN</c> is M3-15's, and a starting
/// position, a starting HP and a starting Gold are decisions `02` §1 and `03` leave to the milestone
/// that builds the board — inventing them here to make a convenient constructor is exactly what
/// steering <b>S6</b> forbids. <see cref="Rehydrate"/> is the only way to obtain one, which is
/// precisely what `30` §11.3 says it should be.
/// </para>
/// <para>
/// ⚠️ <b>What `30` §4 lists on <c>Run</c> and this aggregate deliberately does not carry.</b> The
/// drafted perks, the held consumables (with the armed Escape Rope flag) and the curses — three of
/// §4's ten items — are still deferred with an entry in
/// <c>SlayIdleRepeat.Architecture.Tests.GapRegister</c>, each keyed on a type that must not yet
/// exist, so the build fails on the day each becomes writable. The <b>board</b> and the <b>pending
/// fork choice</b> are M3-02's: the board is never stored (it regenerates deterministically from
/// <see cref="RunSeed"/> and the run's committed <c>board</c> stream position, `14` §8.1), and the
/// pending fork choice is <see cref="PendingFork"/>. The run's <b>phase</b> is deferred too, and
/// that one has a consequence worth stating: without it <c>Apply</c> cannot produce `14` §16.2's
/// <c>RUN_ALREADY_ENDED</c> or <c>ILLEGAL_STATE</c>, and M3-05 pays a
/// <see cref="SnapshotSchema.SchemaVersion"/> bump for it.
/// </para>
/// </remarks>
public sealed class Run
{
    /// <summary>
    /// 🔒 `03` §1.1 (ruled in `16` A7) — the lowest position a run can stand at: the <b>virtual
    /// trailhead</b>, one step before node 0.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Minus one is an authored position, not a sentinel and not an invented bound.</b> §1.1
    /// writes it out — <em>"the hero begins every run at a virtual trailhead one step before node 0
    /// (position −1) … a first roll of <c>1</c> therefore lands on node 0"</em> — and the movement
    /// arithmetic only closes from there: −1 + 1 = 0, so a run stored at 0 instead would skip node 0
    /// forever. It is therefore the position <b>every</b> run holds between <c>START_RUN</c> (M3-15)
    /// and its first <c>ROLL_DICE</c>: exactly the state a player who starts a run and closes the app
    /// leaves behind, and exactly the state `14` §16.3's sliding 48-hour TTL exists to preserve. A
    /// floor of zero would refuse to store or rehydrate it.
    /// </para>
    /// <para>
    /// A named constant rather than a literal, so the floor is greppable and the two places that
    /// hold it — <see cref="MoveTo"/> and <see cref="Rehydrate"/> — cannot drift apart.
    /// </para>
    /// </remarks>
    internal const int TrailheadPosition = -1;

    /// <summary>
    /// 🔒 The run's <c>GOLD</c> balance — and the field name is <b>load-bearing</b>, not stylistic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>DomainPurityTests.CurrencyFields()</c> recognises a currency-carrying field either by its
    /// <b>type</b> (flattening to <c>CurrencyId</c>, or a type name containing <c>Wallet</c>) or by
    /// its <b>name</b> (containing <c>currenc</c> or <c>wallet</c>, case-insensitively). A
    /// <c>long _gold</c> would match <b>neither</b>, so `30` §7's
    /// <c>Every_currency_mutation_emits_CurrencyChanged</c> would be blind to the game's only
    /// run-scoped currency — steering <b>S3</b>'s failure mode, arriving through a field name.
    /// <c>Player</c> solved the same problem for <c>_energy</c> by routing its write through the
    /// method that writes <c>_wallet</c>; <c>Run</c> has no second currency store to ride on, so the
    /// name is the hook. It is also accurate English: this is the run's wallet, and `10` §1 puts
    /// exactly one currency in it.
    /// </para>
    /// <para>
    /// 🔒 <b>The fragility is closed, not accepted.</b>
    /// <c>Every_currency_mutation_emits_CurrencyChanged</c> carries a floor row asserting its
    /// subject set contains <c>Run::_wallet</c>, beside the existing <c>Player::_wallet</c> one, so
    /// renaming this field fails the build instead of quietly emptying the rule.
    /// </para>
    /// <para>
    /// ⚠️ Not a one-row <c>IReadOnlyDictionary&lt;CurrencyId, long&gt;</c>: `10` §1 has exactly one
    /// <c>RUN</c>-scoped currency, so a map would buy generality nothing asks for at the cost of a
    /// dictionary allocation per kill (`14` §2.4 recomputes the <c>stateHash</c> per command on a
    /// handset) and two extra field-order pin slots.
    /// </para>
    /// </remarks>
    private long _wallet;

    /// <summary>
    /// 🔒 `14` §8.1's per-stream draw counters. Replaced <b>wholesale</b> by
    /// <see cref="CommitStreamPositions"/>, never edited in place and never per key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A map, not nine fields.</b> §8.1's ninth row is <em>parameterised</em> —
    /// <c>minigame:{index}</c> — so the registry is not a fixed list of names and nine slots could
    /// not hold <c>minigame:7</c> at all. <c>RngStreams.IsRegistered</c> is already the arbiter of
    /// what a stream name is (it accepts <c>minigame:3</c>, rejects <c>minigame:03</c>, rejects
    /// everything else), so an open map validated by that same predicate inherits the registry's
    /// canonicality for free and is the only shape that spans the fixed eight and the parameterised
    /// ninth.
    /// </para>
    /// <para>
    /// <b>Sparse; absent means zero.</b> A stream never drawn from genuinely stands at 0 — the same
    /// reasoning as <c>Player.DailyCount</c>, not the S6 hole-filled-with-a-default — and `14` §2.3's
    /// wire echo <c>{"dice":12,"board":8}</c> shows only the streams that moved. Storing eager zeros
    /// for the eight fixed streams would put dead bytes in every <c>stateHash</c>.
    /// </para>
    /// <para>
    /// 🔒 Copied into an <b>ordinal</b> <see cref="ReadOnlyDictionary{TKey,TValue}"/> on commit and
    /// on rehydrate. <c>CanonicalStateWriter</c> orders string keys ordinally, so a map comparing
    /// keys any other way would round-trip to a different hash than the one it was stored under —
    /// <c>Player.ReadCounters</c> says the same thing about the counter maps.
    /// </para>
    /// </remarks>
    private IReadOnlyDictionary<string, ulong> _streamPositions;

    /// <summary>
    /// `12` §4.3's per-run ad counts, and the read-only view handed out by <see cref="AdUses"/>.
    /// </summary>
    /// <remarks>
    /// Mutated in place — unlike <see cref="_streamPositions"/>, which is replaced wholesale — so
    /// the view is built once and stays valid across every increment. Identical in shape to
    /// <c>Player</c>'s <c>key → long</c> counter mechanism and deliberately so: open keys, a blank
    /// key refused, a negative amount refused, checked overflow, and
    /// <see cref="AdUseCount"/> answering 0 for a placement nobody has used.
    /// </remarks>
    private readonly Dictionary<string, long> _adUses;

    /// <inheritdoc cref="_adUses"/>
    private readonly ReadOnlyDictionary<string, long> _adUsesView;

    /// <summary>
    /// 🔒 M3-03c — `03` §6.2's per-tile legality gate: the linear node index of every tile whose
    /// minigame has already been resolved this run, mapped to which `03` §6 <c>MG_*</c> id resolved
    /// there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Keyed on <see cref="Position"/> as the tile-instance identity — a recorded assumption,
    /// not `03` §3's tile system speaking.</b> §6.2 requires "exactly one submission per tile", but
    /// `RESOLVE_TILE` and the pending-tile-resolution state that would name a tile instance are
    /// M3-03's, not yet built (<c>GapRegister</c>'s <c>PendingFork</c>/<c>Board</c> entries). The
    /// run's own linear node index is the one tile-instance proxy that already exists: `03` §1 makes
    /// movement forward-only with no backtracking, so one position is visited at most once per run,
    /// and the day M3-03's real pending-tile state lands, a tile instance and a position coincide for
    /// exactly the tiles this gate protects.
    /// </para>
    /// <para>
    /// A map rather than a count, for the same reason <see cref="_adUses"/> is: `03` §6 has four
    /// minigames and a run can face more than one, at different positions, in one run.
    /// <see cref="Handlers.MinigameSubmit"/> also reads <c>Count</c> as the next `14` §8.1
    /// <c>minigame:{index}</c> stream to draw a server-rolled outcome from — one index per resolved
    /// instance, the same shape <c>combat</c>'s <c>battleIndex</c> already uses.
    /// </para>
    /// </remarks>
    private readonly Dictionary<int, string> _resolvedMinigames;

    /// <inheritdoc cref="_resolvedMinigames"/>
    private readonly ReadOnlyDictionary<int, string> _resolvedMinigamesView;

    /// <summary>
    /// 🔒 M3-03 — the <c>(int)TileKind</c> of the tile this run has arrived at and not yet resolved,
    /// or <see cref="NoPendingTile"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Held as an <c>int</c> rather than a <c>TileKind?</c> because <see cref="RunSnapshot"/> carries
    /// it as one — <c>Rules.Board.TileKind</c> is <c>internal</c> and a <b>public</b> snapshot record
    /// may not name it, which is the same accessibility wall <c>ResolvedMinigames</c> hits with the
    /// <c>MG_*</c> ids it stores as strings. The aggregate converts at
    /// <see cref="PendingTileKind"/> so that no rule outside this file ever sees the raw integer.
    /// </para>
    /// <para>
    /// ⚠️ Three fields rather than one small record, for the reason <see cref="_wallet"/> is a bare
    /// <c>long</c>: <see cref="RunSnapshot"/> is <em>flat</em> (`30` §11.3), so a nested record would
    /// buy nothing in storage and would add nested slots to the field-order pin.
    /// </para>
    /// </remarks>
    private int _pendingTileKind;

    /// <inheritdoc cref="_pendingTileKind"/>
    private int _pendingTileLinearIndex;

    /// <inheritdoc cref="_pendingTileKind"/>
    private int _pendingTileStage;

    /// <summary>
    /// 🔒 M3-03 — the `19` Part A card a pending <c>TILE_EVENT</c> has already drawn, or <c>null</c>.
    /// See <see cref="RunSnapshot.PendingEventCardId"/> for why re-drawing must be impossible.
    /// </summary>
    /// <remarks>
    /// <c>null</c> in the aggregate, <c>""</c> in the snapshot: the aggregate's <c>null</c> is the
    /// language's own "no value" and reads correctly at every call site, while the snapshot's empty
    /// string keeps <c>CanonicalStateWriter</c> encoding a <c>System.String</c> slot rather than a
    /// nullable one. <see cref="Rehydrate"/> is the seam that translates between them.
    /// </remarks>
    private string? _pendingEventCardId;

    private DateTimeOffset _lastAppliedAtUtc;
    private int _position;
    private int _currentHp;
    private int _maxHp;

    /// <summary>
    /// 🔒 M3-05, `02` §1.1 — the subset of the run's state machine that is genuine server-side
    /// aggregate state. See <see cref="Primitives.RunPhase"/> for the full ruling.
    /// </summary>
    private RunPhase _phase;

    /// <summary>
    /// 🔒 M3-05 — set when <c>CONFIRM_BATTLE_RESULT</c> closes a won battle, and the documented hook
    /// M3-06's perk-draft trigger reads: a future <c>PickPerkCommand</c>/<c>SkipDraftCommand</c>
    /// clears it once the draft resolves. <c>Handlers.ConfirmBattleResult</c> is the exact call site
    /// that sets it — see that type's remarks.
    /// </summary>
    private bool _draftPending;

    /// <summary>
    /// 🔒 M3-06 — the <c>(int)TileKind</c> of the battle <c>CONFIRM_BATTLE_RESULT</c> just closed
    /// when it set <see cref="_draftPending"/>: Enemy, Elite or Boss. Meaningless while
    /// <see cref="_draftPending"/> is false — <see cref="ClearDraftPending"/> resets it to
    /// <see cref="NoDraftBattleKind"/> for the same reason <see cref="ClearPendingTile"/> resets its
    /// own fields: `30` §11.3's snapshot is flat and hashed whole, so two runs with no draft pending
    /// must produce the same bytes for this slot. Held as an <c>int</c> rather than
    /// <c>Rules.Board.TileKind</c> for the same layering reason <see cref="_pendingTileKind"/> is —
    /// <c>Model</c> may not name <c>Rules</c>' vocabulary (`30` §11.4).
    /// </summary>
    private int _draftBattleKind;

    /// <summary>
    /// 🔒 M3-06 — the stage (1, 2, 3, or <see cref="BossStage"/>) the just-closed battle belonged
    /// to. Meaningless while <see cref="_draftPending"/> is false, reset to 0 alongside
    /// <see cref="_draftBattleKind"/> for the same reason.
    /// </summary>
    private int _draftBattleStage;

    /// <summary>
    /// 🔒 M3-06, `30` §4 — the perks this run has drafted: perk id → owned internal tier (1-3).
    /// M3-06's answer to <c>GapRegister</c>'s <c>DraftedPerks</c> entry (see
    /// <see cref="Model.DraftedPerks"/>, the public wrapper type this dictionary is exposed through).
    /// </summary>
    private readonly Dictionary<string, int> _ownedPerkTiers;

    /// <summary>
    /// 🔒 M3-05, `04` §3 — reroll charges spent since the run's current stage began. Reset to 0 at
    /// every Stage Gate (<see cref="ApplyStageGate"/>); compared against
    /// <see cref="Rules.Dice.RerollEconomy.TotalCharges"/> by <c>Handlers.UseReroll</c>, which is the
    /// "a caller with a real spent-count... compares it against TotalCharges" seam
    /// <see cref="Rules.Dice.RerollEconomy"/>'s own remarks left open.
    /// </summary>
    private int _rerollChargesSpentThisStage;

    /// <summary>
    /// 🔒 M3-05 — the <c>dice</c> stream draw index the run's current stage began at:
    /// <see cref="Rules.Dice.FairDiceBag.Replay"/>'s <c>resetAtDraw</c> argument, once a real Stage
    /// Gate exists to feed it (M3-04's handlers hard-coded 0, documented as a placeholder — see
    /// <c>Handlers.RollDice</c>'s and <c>Handlers.UseReroll</c>'s prior remarks).
    /// </summary>
    private ulong _stageGateDiceAnchor;

    /// <summary>
    /// 🔒 M3-02, `03` §1.1 / `30` §4 — a movement paused mid-move at a junction, waiting for
    /// <c>CHOOSE_FORK</c>. Null on every run that is not, right now, standing at a junction with
    /// movement still to spend.
    /// </summary>
    private PendingFork? _pendingFork;

    /// <summary>
    /// 🔒 M3-13, `02` §5.1a — Legend XP banked so far this run, pending the run-end
    /// <c>CompletionMultiplier</c>/<c>AdDoubleMultiplier</c> payout (<see cref="BankRewards"/>).
    /// Unlike <see cref="_wallet"/>'s Gold, never paid to <c>Player</c> until the run ends.
    /// </summary>
    private long _bankedLegendXp;

    /// <summary>
    /// 🔒 M3-13, `02` §5.3 / `10` §2 — Soul Shards banked so far this run (Boss kills and the
    /// one-time first-clear grant), pending the same run-end payout as <see cref="_bankedLegendXp"/>.
    /// </summary>
    private long _bankedSoulShards;

    /// <summary>
    /// 🔒 M3-13, `02` §5.2 — whether this run's Boss has been killed. The Victory/Death split
    /// <c>Handlers.EndRun</c> reads to pick a <c>CompletionMultiplier</c> row, and the gate on
    /// `02` §5.3's first-clear bonus.
    /// </summary>
    private bool _bossDefeated;

    /// <summary>
    /// The one constructor. Private, and it <b>trusts</b>: every value has already been checked by
    /// <see cref="Rehydrate"/>, which is the only caller.
    /// </summary>
    /// <remarks>
    /// Validation lives in one place rather than two — the same reasoning as <c>Player</c>'s private
    /// constructor. A constructor that re-checked would either duplicate the rules (two lists that
    /// drift) or throw where `30` §11.3 promises a <see cref="Result{T}"/>, which is the difference
    /// between a corrupt row failing at the seam with a description and a corrupt row failing three
    /// rules later with a stack trace.
    /// </remarks>
    private Run(
        RunId id,
        PlayerId playerId,
        ulong runSeed,
        int chapterId,
        DifficultyTier tier,
        DateTimeOffset lastAppliedAtUtc,
        int position,
        int currentHp,
        int maxHp,
        long gold,
        IReadOnlyDictionary<string, ulong> streamPositions,
        Dictionary<string, long> adUses,
        Dictionary<int, string> resolvedMinigames,
        PendingFork? pendingFork,
        int pendingTileKind,
        int pendingTileLinearIndex,
        int pendingTileStage,
        string? pendingEventCardId,
        RunPhase phase,
        bool draftPending,
        int draftBattleKind,
        int draftBattleStage,
        Dictionary<string, int> ownedPerkTiers,
        int rerollChargesSpentThisStage,
        ulong stageGateDiceAnchor,
        long bankedLegendXp,
        long bankedSoulShards,
        bool bossDefeated)
    {
        Id = id;
        PlayerId = playerId;
        RunSeed = runSeed;
        ChapterId = chapterId;
        Tier = tier;
        _lastAppliedAtUtc = lastAppliedAtUtc;
        _position = position;
        _currentHp = currentHp;
        _maxHp = maxHp;
        _wallet = gold;
        _streamPositions = streamPositions;
        _adUses = adUses;
        _adUsesView = new ReadOnlyDictionary<string, long>(adUses);
        _resolvedMinigames = resolvedMinigames;
        _resolvedMinigamesView = new ReadOnlyDictionary<int, string>(resolvedMinigames);
        _pendingFork = pendingFork;
        _pendingTileKind = pendingTileKind;
        _pendingTileLinearIndex = pendingTileLinearIndex;
        _pendingTileStage = pendingTileStage;
        _pendingEventCardId = pendingEventCardId;
        _phase = phase;
        _draftPending = draftPending;
        _draftBattleKind = draftBattleKind;
        _draftBattleStage = draftBattleStage;
        _ownedPerkTiers = ownedPerkTiers;
        _rerollChargesSpentThisStage = rerollChargesSpentThisStage;
        _stageGateDiceAnchor = stageGateDiceAnchor;
        _bankedLegendXp = bankedLegendXp;
        _bankedSoulShards = bankedSoulShards;
        _bossDefeated = bossDefeated;
    }

    /// <summary>
    /// 🔒 M3-03 — what <see cref="RunSnapshot.PendingTileKind"/> holds when no tile is pending.
    /// </summary>
    /// <remarks>
    /// −1 rather than a nullable, and it is safe rather than lucky: <c>Rules.Board.TileKind</c>'s
    /// fourteen members are declared with no explicit values and therefore run <c>0..13</c>, so no
    /// legal kind can ever collide with it. A named constant so the sentinel is greppable and the
    /// three places that hold it — the constructor's default, <see cref="ClearPendingTile"/> and
    /// <see cref="Rehydrate"/>'s validation — cannot drift apart.
    /// </remarks>
    private const int NoPendingTile = -1;

    /// <summary>
    /// 🔒 M3-06 — what <see cref="_draftBattleKind"/> holds while <see cref="_draftPending"/> is
    /// false. −1 for the same reason <see cref="NoPendingTile"/> is: <c>Rules.Board.TileKind</c>'s
    /// members run <c>0..13</c>, so no legal kind can collide with it.
    /// </summary>
    private const int NoDraftBattleKind = -1;

    /// <summary>
    /// 🔒 M3-03 — `03` §1's stage value for the boss node, which belongs to no stage.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Restated here rather than read off the board graph's own constant, and that is the
    /// layering rather than duplication for its own sake.</b> `30` §11.4 puts <c>Model</c> below
    /// <c>Rules</c>, and <c>AccessibilityBoundaryTests</c> scans <b>source</b> as well as metadata
    /// precisely because the compiler inlines a <c>const</c> and metadata alone cannot see the
    /// reference. So this aggregate holds its own copy of the value it validates against. The graph's
    /// constant remains the definition; a test in the <c>Rules</c> layer — which may name both — is
    /// what holds the two together.
    /// </remarks>
    private const int BossStage = 0;

    /// <summary>The aggregate root's identity (`30` §4).</summary>
    public RunId Id { get; }

    /// <summary>
    /// 🔒 The player this run belongs to. `30` §4 makes <c>Run</c> a child of <c>Player</c>, and
    /// this is where that parentage is held — a run does not exist on its own.
    /// </summary>
    public PlayerId PlayerId { get; }

    /// <summary>
    /// 🔒 `02` §2's committed <c>runSeed</c>. Derived once by <c>SeedDerivation.RunSeed</c> at
    /// <c>START_RUN</c> and never recomputed: `14` §8.1 makes it authoritative run state, and a
    /// re-derivation from a different <c>NowUtc</c> would silently hand the player a different
    /// board on resume.
    /// </summary>
    /// <remarks>
    /// Get-only with no mutator anywhere: nothing in the game legitimately re-seeds a run in flight.
    /// </remarks>
    public ulong RunSeed { get; }

    /// <summary>
    /// The chapter being played. `02` §1 runs chapters 1–8 and <c>chapter.schema.json</c> sets
    /// <c>"minimum": 1</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>No upper bound and no existence check, deliberately.</b> M3-14 landed
    /// <c>content/chapters/CH_01_GREENWOOD_VALE.json</c> and <c>CH_02_ASHEN_MIRE.json</c>, so
    /// <c>chapter.schema.json</c> no longer sits on <c>ContentLoader.SchemasAwaitingContent</c> — but
    /// chapters 3-8 remain M11-02's unauthored rows, so the content set still does not span `02` §1's
    /// full range. Hard-coding <c>8</c> in <c>Core</c> would put a content bound in code (`21` §3.1)
    /// and — worse — would be a <em>partial</em> invariant wearing the real one's name, the same trap
    /// as <see cref="Position"/>. The deferral already has a live, self-expiring mechanism:
    /// <c>RealDataSetTests.An_exemption_that_outlived_its_milestone_fails_the_build</c> pins the same
    /// mechanism against <c>event.schema.json</c> now that chapter's own exemption has expired for
    /// real, so no second mechanism is built here (steering S4).
    /// </remarks>
    public int ChapterId { get; }

    /// <summary>The difficulty tier (`10` §7), and `02` §2's <c>tierId</c> in <see cref="RunSeed"/>.</summary>
    public DifficultyTier Tier { get; }

    /// <summary>
    /// 🔒 `14` §16.3 — the instant the last command was applied <b>to this run</b>, which is what
    /// the 48-hour run TTL slides from.
    /// </summary>
    /// <remarks>
    /// Distinct from <c>Player.LastAppliedAtUtc</c> and it has to be: the player's advances on meta
    /// commands too, so sliding the run's expiry off it would keep a run alive because its owner
    /// opened the shop. `14` §16.2 makes <c>RUN_EXPIRED</c> a <b>domain-tier</b> rejection returned
    /// by <c>Apply</c>, so the field the rejection is computed from belongs on the aggregate.
    /// </remarks>
    public DateTimeOffset LastAppliedAtUtc => _lastAppliedAtUtc;

    /// <summary>
    /// The node the run stands on (`14` §2.3's <c>newPosition</c>) — a
    /// <see cref="Rules.Board.NodeId"/>'s <c>Value</c>, once M3-02's movement engine has generated
    /// this run's board.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>M3-02's ruling, recorded here rather than discovered by diffing a wire trace against
    /// `03` §1.1's prose.</b> The design document calls this "the linear node index" throughout, and
    /// for every position a run can persist <em>between commands while standing on the spine or the
    /// boss</em>, it is exactly that: <c>Rules.Board.BoardGenerator</c> assigns every spine node's
    /// <c>NodeId</c> in increasing linear-index order, before any fork branch is built, so a spine or
    /// boss node's <c>NodeId.Value</c> and its <see cref="Rules.Board.BoardNode.LinearIndex"/> are the
    /// same number by construction. They diverge only while a run is genuinely standing <em>inside a
    /// fork branch</em> — the one case `03` §1.1 itself says a linear index cannot name uniquely
    /// (<see cref="Rules.Board.BoardNode.LinearIndex"/>'s own remarks): "a branch node shares its
    /// linear index with the spine node at the same forward distance from its junction." A plain
    /// linear index stored here could not tell those two nodes apart the moment a
    /// <c>CHOOSE_FORK</c>'d move ends its command short of the branch's rejoin — so this field is the
    /// node's actual graph identity, which happens to equal the linear index everywhere a wire reader
    /// would expect it to.
    /// </para>
    /// <para>
    /// ⚠️ <b>Stored, and still checked only against `03` §1.1's trailhead floor.</b> `30` §11.5 names
    /// <em>"a run's position is a valid node"</em> as an invariant of this aggregate — and `30` §11.4
    /// forbids <c>Model</c> from referencing <c>Rules</c> at all, so this aggregate structurally
    /// cannot hold a <see cref="Rules.Board.BoardGraph"/> to check itself against. The real
    /// enforcement is that <b>only</b> M3-02's movement engine (<c>Handlers.RollDice</c>,
    /// <c>Handlers.ChooseFork</c>) ever calls <see cref="MoveTo"/>, and it only ever does so with a
    /// value it has itself just read off a node of the run's own regenerated board — the same shape
    /// <c>SetHitPoints</c>'s overheal clamp already takes (`30` §11.5: the aggregate refuses an
    /// impossible answer, the caller that computed it keeps the arithmetic honest).
    /// </para>
    /// <para>
    /// 🔒 The one bound that <b>is</b> checked here is not invented either: see
    /// <see cref="TrailheadPosition"/>. `03` §1.1 authors −1 as the position every run stands at
    /// before its first roll, so that — and not zero — is the floor.
    /// </para>
    /// </remarks>
    public int Position => _position;

    /// <summary>The hero's current hit points. Never negative, never above <see cref="MaxHp"/>.</summary>
    public int CurrentHp => _currentHp;

    /// <summary>
    /// The hero's maximum hit points for this run. Never below 1.
    /// </summary>
    /// <remarks>
    /// Stored rather than derived from the player's build: a resumed run must render its HP bar
    /// without recomputing the whole power calculation, and `03` §7a.5's <c>SHR_HP</c> shrine raises
    /// <c>MAX_HP</c> <em>for the run</em>, so the run's maximum is genuinely run state.
    /// </remarks>
    public int MaxHp => _maxHp;

    /// <summary>
    /// 🔒 The run's <c>GOLD</c> balance — `10` §1's one <c>RUN</c>-scoped currency (assumption
    /// <b>A3</b>). Never negative.
    /// </summary>
    public long Gold => _wallet;

    /// <summary>
    /// 🔒 `14` §8.1's per-stream draw counters: stream name → next draw index. Read-only, sparse,
    /// and ordinal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>A frozen view.</b> <see cref="CommitStreamPositions"/> replaces the map wholesale, so
    /// the object a caller holds is the positions as they stood when it read them and never changes
    /// afterwards. <see cref="AdUses"/> is the opposite; the asymmetry is stated on both getters so
    /// a caller does not have to infer it.
    /// </para>
    /// <para>
    /// 🔒 <b>What <c>combat</c>'s position means, and it is not what the others' means.</b> `14`
    /// §8.1 derives <c>battleSeed = Hash64(runSeed, "combat", battleIndex)</c>, and combat draw
    /// <c>i</c> of that battle is <c>Hash64(battleSeed, "combat", i)</c>. So the <c>combat</c> stream
    /// <b>rooted at <see cref="RunSeed"/></b> is consumed exactly once per battle — to derive that
    /// battle's seed — which makes its position <b>the number of battles started in this run, i.e.
    /// the next <c>battleIndex</c></b>, not a count of combat draws. The draws <em>inside</em> a
    /// battle are rooted at the battle seed, restart at 0 for every battle and are <b>never
    /// persisted</b>, which is precisely what makes §8.1's <em>"a revived battle restarts from draw 0
    /// of the same battle stream: reproducible by construction"</em> true. Same type, same
    /// monotonicity, same seam — different <b>unit</b>.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, ulong> RngStreamPositions => _streamPositions;

    /// <summary>
    /// `12` §4.3's per-run ad uses: placement id → uses so far. Read-only; empty is the normal state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>A live view, unlike <see cref="RngStreamPositions"/>.</b> The counts are mutated in
    /// place, so a caller holding this reference across a <see cref="CountAdUse"/> sees the new
    /// values. Read it, do not hold it; <see cref="ToSnapshot"/> hands out a copy for exactly this
    /// reason.
    /// </para>
    /// <para>
    /// 🔒 <b>Open string keys, not a type.</b> The thirteen in-run placement ids are authored in
    /// <c>game-data/tuning/ads.json</c> under <c>inRunPlacements</c>, and <c>AdPlacementId</c> is an
    /// <b><c>Application</c>-layer</b> type in `12` §7's port signature — <c>Core</c> may not name
    /// it. <c>CanonicalStateWriter.KeyOrderFor</c> also defines an ascending order for strings and
    /// numeric ids only, so a wrapper-keyed map would have no canonical encoding at all.
    /// </para>
    /// <para>
    /// 🔒 <b>No period anchor and no reset mutator, ever.</b> `12` §4.3 makes in-run caps <em>per
    /// run</em>, and the run <b>is</b> the period — a run never crosses an in-run cap boundary, so
    /// there is nothing to reset, and a reset mutator would be a way to hand a player their
    /// seventeen impressions twice. <c>AD_REVIVE</c> is also what carries `02` §6's once-per-run
    /// revive; there is no separate <c>RevivesUsed</c> field, because that would be a second source
    /// of truth for one count.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, long> AdUses => _adUsesView;

    /// <inheritdoc cref="_resolvedMinigames"/>
    public IReadOnlyDictionary<int, string> ResolvedMinigames => _resolvedMinigamesView;

    /// <inheritdoc cref="_pendingFork"/>
    public PendingFork? PendingFork => _pendingFork;

    /// <inheritdoc cref="_phase"/>
    public RunPhase Phase => _phase;

    /// <inheritdoc cref="_draftPending"/>
    internal bool DraftPending => _draftPending;

    /// <inheritdoc cref="_draftBattleKind"/>
    /// <exception cref="InvalidOperationException">No draft is pending.</exception>
    internal int DraftBattleKindValue =>
        _draftPending ? _draftBattleKind : throw NoDraftPending(nameof(DraftBattleKindValue));

    /// <inheritdoc cref="_draftBattleStage"/>
    /// <exception cref="InvalidOperationException">No draft is pending.</exception>
    internal int DraftBattleStage =>
        _draftPending ? _draftBattleStage : throw NoDraftPending(nameof(DraftBattleStage));

    /// <inheritdoc cref="_ownedPerkTiers"/>
    internal DraftedPerks DraftedPerks => new(new ReadOnlyDictionary<string, int>(_ownedPerkTiers));

    /// <inheritdoc cref="_rerollChargesSpentThisStage"/>
    internal int RerollChargesSpentThisStage => _rerollChargesSpentThisStage;

    /// <summary>🔒 M3-13 — Legend XP banked so far this run. See <see cref="_bankedLegendXp"/>.</summary>
    internal long BankedLegendXp => _bankedLegendXp;

    /// <summary>🔒 M3-13 — Soul Shards banked so far this run. See <see cref="_bankedSoulShards"/>.</summary>
    internal long BankedSoulShards => _bankedSoulShards;

    /// <summary>🔒 M3-13 — whether this run's Boss has been killed. See <see cref="_bossDefeated"/>.</summary>
    internal bool BossDefeated => _bossDefeated;

    /// <inheritdoc cref="_stageGateDiceAnchor"/>
    internal ulong StageGateDiceAnchor => _stageGateDiceAnchor;

    /// <summary>
    /// The next draw index of one `14` §8.1 stream, or <b>zero</b> for a registered stream this run
    /// has never drawn from.
    /// </summary>
    /// <param name="streamName">A row of <c>RngStreams</c> — one of the eight fixed names, or <c>minigame:{index}</c>.</param>
    /// <remarks>
    /// Zero for an undrawn stream is correct rather than the S6 hole-filled-with-a-default: a stream
    /// nothing has drawn from genuinely stands at draw 0, which is exactly what
    /// <c>new DeterministicRng(runSeed, name)</c> starts at. An <b>unregistered</b> name is refused
    /// instead, because there is no such stream to answer about.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="streamName"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="streamName"/> is not in the registry of `14` §8.1.</exception>
    public ulong StreamPosition(string streamName)
    {
        // Null is checked HERE rather than inside RequireRegisteredStream because here the null is
        // genuinely the argument, while a null KEY inside CommitStreamPositions' map is not — see
        // that guard's remarks.
        ArgumentNullException.ThrowIfNull(streamName);
        RequireRegisteredStream(streamName, nameof(streamName));

        return _streamPositions.TryGetValue(streamName, out var position) ? position : 0UL;
    }

    /// <summary>
    /// How many times one `12` §4.3 in-run ad placement has been used in this run, or zero.
    /// </summary>
    /// <param name="placementId">The placement id, as authored in <c>tuning/ads.json</c>. Never blank.</param>
    /// <remarks>
    /// Zero for an unknown key, for the same reason <c>Player.DailyCount</c> answers zero: a
    /// placement nobody has watched genuinely stands at zero, and the alternative — every placement
    /// pre-registering itself at run start — would put a content list inside the aggregate.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="placementId"/> is blank.</exception>
    public long AdUseCount(string placementId)
    {
        RequirePlacementId(placementId, nameof(placementId));

        return _adUses.TryGetValue(placementId, out var uses) ? uses : 0L;
    }

    /// <summary>
    /// The balance of one <b>run-scoped</b> currency.
    /// </summary>
    /// <param name="currency">Must be <see cref="CurrencyId.GOLD"/>: `10` §1 scopes exactly one currency to the run.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="currency"/> is anything but <c>GOLD</c>. Refused with the reason and a pointer
    /// at <c>Player</c> — the mirror image of <c>Player.RequireWalletCurrency</c>'s refusal of
    /// <c>GOLD</c> — rather than answered with a zero, which would read as "the run has none" about
    /// a balance that lives on the other aggregate.
    /// </exception>
    public long BalanceOf(CurrencyId currency)
    {
        RequireRunCurrency(currency, nameof(currency));

        return _wallet;
    }

    /// <summary>
    /// 🔒 `30` §11.3 — the persisted shape of this aggregate, stamped with the <b>current</b>
    /// <see cref="SnapshotSchema.SchemaVersion"/>.
    /// </summary>
    /// <remarks>
    /// The ad-use dictionary is <b>copied</b>; the stream-position map is not. That asymmetry is the
    /// storage decision showing through: <c>_streamPositions</c> is replaced wholesale on every
    /// commit, so the object handed out here can never change afterwards, while the ad counts are
    /// mutated in place and a shared reference would let a later increment rewrite a snapshot already
    /// handed to a persistence adapter.
    /// </remarks>
    public RunSnapshot ToSnapshot() => new(
        SnapshotSchema.SchemaVersion,
        Id,
        PlayerId,
        RunSeed,
        ChapterId,
        Tier,
        _lastAppliedAtUtc,
        _position,
        _currentHp,
        _maxHp,
        _wallet,
        _streamPositions,
        CopyAdUses(_adUses),
        CopyResolvedMinigames(_resolvedMinigames),
        _pendingFork?.JunctionPosition,
        _pendingFork?.RemainingSteps,
        _pendingTileKind,
        _pendingTileLinearIndex,
        _pendingTileStage,
        // 🔒 null becomes "" on the way out — see _pendingEventCardId for why the two sides of this
        // seam spell "absent" differently.
        _pendingEventCardId ?? string.Empty,
        _phase,
        _draftPending,
        _rerollChargesSpentThisStage,
        _stageGateDiceAnchor,
        _draftBattleKind,
        _draftBattleStage,
        CopyOwnedPerkTiers(_ownedPerkTiers),
        _bankedLegendXp,
        _bankedSoulShards,
        _bossDefeated);

    /// <summary>
    /// 🔒 `30` §11.3 — the one validated entry point for a persisted run: <em>"a corrupt row fails
    /// loudly at the seam rather than silently three rules later."</em>
    /// </summary>
    /// <param name="snapshot">The persisted row.</param>
    /// <returns>
    /// The rehydrated aggregate, or a failure listing <b>every</b> validation the row failed — not
    /// just the first. A corrupt row is usually corrupt in more than one way, and one round trip per
    /// defect is one round trip too many when the row is already in production.
    /// </returns>
    /// <remarks>
    /// <para>
    /// 🔒 <b>An unknown <see cref="RunSnapshot.SchemaVersion"/> hard-fails, loudly, first and
    /// alone.</b> The M1 kickoff ruled that no migration code is written before soft launch and that
    /// written migrations become mandatory at M18. Until then a row from another schema version has
    /// no reader, and guessing that "close enough" layouts are compatible is how a field silently
    /// shifts by one position across an entire player base. Every validation below reads fields whose
    /// meaning the version defines, so the version is checked before any of them runs.
    /// </para>
    /// <para>
    /// 🔒 <b>No <c>ContentSnapshot</c> parameter, and that is a ruling rather than an omission.</b>
    /// `30` §11.3's sketch carries one because <c>Player</c>'s validation genuinely needs a tunable
    /// (the Legend Level range). Nothing <see cref="RunSnapshot"/> carries has a content-derived
    /// bound <em>today</em>: the two that will — position→node and chapter→content — are both
    /// deferred with named owners (M3-01, M3-14). A parameter accepted and ignored tells every caller
    /// this validation consults the data set when it does not, and it keeps compiling on the day
    /// someone needs it and forgets to use it. Adding it later is a compile error inside <c>Core</c>
    /// and <c>Application</c>, not a persistence break.
    /// </para>
    /// <para>
    /// 🔒 <b>Every fault names <em>which</em> field failed and why</b> (steering S2). Several
    /// validations here can produce a failed result, so a message that only said "this row is
    /// invalid" would let any one of them be deleted without anything going red.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is null.</exception>
    public static Result<Run> Rehydrate(RunSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.SchemaVersion != SnapshotSchema.SchemaVersion)
        {
            return Result<Run>.Failure(
                "RunSnapshot.SchemaVersion is " + Text(snapshot.SchemaVersion) + "; this build reads " +
                Text(SnapshotSchema.SchemaVersion) + " and NO MIGRATION EXISTS. 14 §16.6 makes a " +
                "field added, removed or reordered a versioned migration, and the M1 kickoff ruled " +
                "that no migration code is written before soft launch (written migrations become " +
                "mandatory at M18). Reading this row against the current layout would shift every " +
                "field after the first change by one place, silently, for every run that has it. " +
                "Refusing is the loud failure at the seam 30 §11.3 asks for.");
        }

        var faults = new List<string>();

        RequireIdentity(snapshot, faults);
        RequireChapterAndTier(snapshot, faults);
        RequireTimestamp(snapshot, faults);
        RequireVitals(snapshot, faults);
        RequireGold(snapshot, faults);
        var streams = ReadStreamPositions(snapshot, faults);
        var adUses = ReadAdUses(snapshot, faults);
        var resolvedMinigames = ReadResolvedMinigames(snapshot, faults);
        RequirePendingFork(snapshot, faults);
        RequirePendingTile(snapshot, faults);
        RequirePhase(snapshot, faults);
        RequireRerollCharges(snapshot, faults);
        RequireDraftBattle(snapshot, faults);
        var ownedPerkTiers = ReadOwnedPerkTiers(snapshot, faults);
        RequireBankedRewards(snapshot, faults);

        // The four `is null` arms are unreachable while `faults` is empty — every path that returns
        // null also adds a fault — but they are written as a pattern rather than as four `!`
        // operators so the correlation is checked rather than asserted at the compiler.
        if (faults.Count > 0 || streams is null || adUses is null || resolvedMinigames is null ||
            ownedPerkTiers is null)
        {
            return Result<Run>.Failure(
                "This RunSnapshot is not a state the game can be in (" + Text(faults.Count) +
                " problem(s)): " + string.Join(" | ", faults));
        }

        // Safe only now: RequirePendingFork already proved the pair is either both absent or both
        // present and in range, and no fault was added for it (faults.Count == 0, just checked).
        var pendingFork = snapshot.PendingForkJunctionPosition is { } junctionPosition
            ? new PendingFork(junctionPosition, snapshot.PendingForkRemainingSteps!.Value)
            : (PendingFork?)null;

        return Result<Run>.Success(new Run(
            snapshot.Id,
            snapshot.PlayerId,
            snapshot.RunSeed,
            snapshot.ChapterId,
            snapshot.Tier,
            snapshot.LastAppliedAtUtc,
            snapshot.Position,
            snapshot.CurrentHp,
            snapshot.MaxHp,
            snapshot.Gold,
            streams,
            adUses,
            resolvedMinigames,
            pendingFork,
            snapshot.PendingTileKind,
            snapshot.PendingTileLinearIndex,
            snapshot.PendingTileStage,
            // 🔒 "" becomes null on the way in, and a blank-but-not-empty string ("  ") does too:
            // both mean "no card has been drawn", and carrying whitespace through would give
            // SetPendingEventCard's own blank check something to disagree with.
            string.IsNullOrWhiteSpace(snapshot.PendingEventCardId) ? null : snapshot.PendingEventCardId,
            snapshot.Phase,
            snapshot.DraftPending,
            snapshot.DraftBattleKind,
            snapshot.DraftBattleStage,
            ownedPerkTiers,
            snapshot.RerollChargesSpentThisStage,
            snapshot.StageGateDiceAnchor,
            snapshot.BankedLegendXp,
            snapshot.BankedSoulShards,
            snapshot.BossDefeated));
    }

    /// <summary>🔒 M3-13 — the two banked-reward pools are never negative.</summary>
    private static void RequireBankedRewards(RunSnapshot snapshot, List<string> faults)
    {
        if (snapshot.BankedLegendXp < 0)
        {
            faults.Add(
                nameof(RunSnapshot.BankedLegendXp) + " is " + Text(snapshot.BankedLegendXp) +
                ". Banked Legend XP is a pending grant and never goes negative.");
        }

        if (snapshot.BankedSoulShards < 0)
        {
            faults.Add(
                nameof(RunSnapshot.BankedSoulShards) + " is " + Text(snapshot.BankedSoulShards) +
                ". Banked Soul Shards are a pending grant and never go negative.");
        }
    }

    /// <summary>
    /// 🔒 Moves the run's <c>GOLD</c> and produces the `30` §7 <c>CurrencyChanged</c> that attributes
    /// it. The <b>one</b> place <c>_wallet</c> is written outside the constructor.
    /// </summary>
    /// <param name="currency">Must be <see cref="CurrencyId.GOLD"/>.</param>
    /// <param name="delta">Signed: positive is income, negative is a spend. Zero is permitted.</param>
    /// <param name="reason">
    /// 🔒 Why it moved — the attribution column of `21` §8.3's <c>income_attribution.csv</c>. A
    /// stable <c>lower_snake_case</c> token. Never blank; <c>CurrencyChanged</c> refuses that.
    /// </param>
    /// <returns>The event, with <see cref="DomainEvent.Sequence"/> left at <c>DomainEvent.UnstampedSequence</c>.</returns>
    /// <remarks>
    /// <c>internal</c>, so the only public route to it is <c>GameRules.Apply</c> (`30` §11.2). It
    /// throws rather than returning a <see cref="Result{T}"/> on an unaffordable spend: refusing a
    /// player's request is a <c>RejectionReason</c> the handler produces <em>before</em> it gets
    /// here, so a negative balance reaching this point is a rule that forgot to check, not a player
    /// who cannot pay. The <c>CurrencyChanged</c> is constructed <b>before</b> the field write, as
    /// <c>Player.MoveBalance</c> does and for the same reason: the event refuses a blank reason in
    /// its own initialiser, so building it second would leave the balance moved and the throw
    /// unrecoverable — a currency movement with no attribution.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="currency"/> is not run-scoped, or the movement would overflow.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank.</exception>
    /// <exception cref="InvalidOperationException">The movement would take the balance negative.</exception>
    internal CurrencyChanged MoveCurrency(CurrencyId currency, long delta, string reason)
    {
        RequireRunCurrency(currency, nameof(currency));

        var balance = _wallet;

        // Checked, because `long.MaxValue + 1` wraps to a large negative in the default unchecked
        // context — a grant that silently bankrupts the run, straight past the guard below that
        // exists to stop exactly that.
        long next;
        try
        {
            next = checked(balance + delta);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delta),
                delta,
                "Moving " + Text(currency) + " by " + Text(delta) + " from a balance of " +
                Text(balance) + " overflows a 64-bit balance. A movement this size is an economy " +
                "defect upstream — a multiplier chain, most likely — not an amount to store.");
        }

        if (next < 0)
        {
            throw new InvalidOperationException(
                "Moving " + Text(currency) + " by " + Text(delta) + " would take the balance from " +
                Text(balance) + " to " + Text(next) + ". 30 §11.5 makes 'a currency never goes " +
                "negative' an invariant of the Run aggregate. A spend the player cannot afford is " +
                "refused by the handler as a RejectionReason before it reaches the aggregate; " +
                "reaching here means a rule debited without checking.");
        }

        // 🔒 Built BEFORE the write, not after. CurrencyChanged refuses a blank Reason in its own
        // property initialiser, so constructing it second would leave the balance already moved and
        // the throw unrecoverable — a currency movement with no attribution, which is the one
        // outcome 30 §7 and this whole seam exist to make impossible. The newobj and the stfld stay
        // in the same method body either way, which is what
        // DomainPurityTests.Every_currency_mutation_emits_CurrencyChanged reads.
        var change = new CurrencyChanged(DomainEvent.UnstampedSequence, currency, delta, reason);

        _wallet = next;

        return change;
    }

    /// <summary>
    /// `14` §2.3 — records the node index the run has moved to.
    /// </summary>
    /// <param name="position">
    /// The new linear node index. Never below <see cref="TrailheadPosition"/>, `03` §1.1's virtual
    /// trailhead.
    /// </param>
    /// <remarks>
    /// <para>
    /// ⚠️ The trailhead floor is the <b>whole</b> check, and see <see cref="Position"/> for why: `30`
    /// §11.5's <em>"a run's position is a valid node"</em> needs the specific board a specific run
    /// stands on, which is M3-02's to compute and check before calling <see cref="MoveTo"/> — node
    /// identity itself (M3-01's half) already exists.
    /// </para>
    /// <para>
    /// ⚠️ <b>There is no monotonicity guard either — and the reason is that same deferral, not a
    /// claim about the board.</b> `03` §1 is explicit the other way (<em>"movement is always forward.
    /// There is no backtracking"</em>, and §1.1 lists Portal jumps under <em>forward</em> movement),
    /// so a forwards-only rule would refuse nothing the design authorises. It is still not written
    /// here: which index may follow which is a property of the board graph, and this aggregate holds
    /// no graph — a rule about the direction of travel would be the same partial invariant wearing
    /// the real one's name that a range check would be. Movement legality is M3-01's (the graph) and
    /// M3-02's (the movement engine, including `03` §1.1's junction pause); this seam records the
    /// index they computed.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="position"/> is below <see cref="TrailheadPosition"/>.
    /// </exception>
    internal void MoveTo(int position)
    {
        if (position < TrailheadPosition)
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                position,
                "The lowest position a run can stand at is " + Text(TrailheadPosition) + " — 03 " +
                "§1.1's virtual trailhead, one step before node 0, where every run stands before " +
                "its first roll — and " + Text(position) + " is below it. That floor is the WHOLE " +
                "check this seam runs: 30 §11.5's 'a run's position is a valid node' is enforced " +
                "structurally instead, by M3-02's movement engine being the only caller of MoveTo " +
                "and only ever calling it with a node it has itself just read off this run's own " +
                "regenerated board — 30 §11.4 forbids Model from referencing Rules at all, so this " +
                "aggregate cannot check itself against a board even if it wanted to. A range check " +
                "invented here would be a partial invariant wearing the real one's name.");
        }

        _position = position;
    }

    /// <summary>
    /// 🔒 The <b>one</b> HP seam: writes the current and maximum hit points a rule computed, in one
    /// call.
    /// </summary>
    /// <param name="current">The hero's hit points after the rule. Never negative, never above <paramref name="max"/>.</param>
    /// <param name="max">The run's maximum hit points after the rule. Never below 1.</param>
    /// <remarks>
    /// <para>
    /// 🔒 <b>It takes both halves, and that is the invariant.</b> The same reasoning as
    /// <c>Player.AccrueEnergy</c> taking both halves of one accrual: a caller that raised
    /// <see cref="MaxHp"/> and forgot <see cref="CurrentHp"/> — or the reverse — would leave the pair
    /// in a state neither individual write is illegal in, and no aggregate-level invariant could
    /// catch it afterwards. `03` §7a.5's <c>SHR_HP</c> raises the maximum <em>and</em> heals, which
    /// is exactly one fact with two components.
    /// </para>
    /// <para>
    /// ⚠️ Overheal is <b>clamped by the rule that computes it</b>, not accepted and trimmed here
    /// (`30` §11.5): a silent clamp would make a healing rule that over-delivered look correct.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="max"/> is below 1, <paramref name="current"/> is negative, or
    /// <paramref name="current"/> exceeds <paramref name="max"/>.
    /// </exception>
    internal void SetHitPoints(int current, int max)
    {
        // Both halves are checked BEFORE either is written, so a refusal cannot leave the pair
        // half-updated — which would be the very state taking both arguments exists to prevent.
        if (max < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(max),
                max,
                "A run's maximum hit points is at least 1; " + Text(max) + " is not a maximum a hero " +
                "can be alive under. 03 §7a.5's SHR_HP raises it for the run, so it moves — but it " +
                "never reaches zero, and a run whose maximum is zero has no HP bar to render.");
        }

        if (current < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(current),
                current,
                "Hit points are never negative; " + Text(current) + " is. 02 §6's revive acts on a " +
                "hero standing at zero, so zero is the floor and the state a downed hero is in — " +
                "anything below it is a damage rule that subtracted without clamping.");
        }

        if (current > max)
        {
            throw new ArgumentOutOfRangeException(
                nameof(current),
                current,
                Text(current) + " exceeds the maximum of " + Text(max) + ". 30 §11.5 keeps the " +
                "arithmetic in the rule that computes it: overheal is clamped by the healing rule, " +
                "not accepted and trimmed here, because a silent clamp would make a rule that " +
                "over-delivered look correct.");
        }

        _currentHp = current;
        _maxHp = max;
    }

    /// <summary>
    /// `14` §16.3 — records that a command has been applied to this run at
    /// <paramref name="nowUtc"/>, which is what the sliding 48-hour TTL is measured from.
    /// </summary>
    /// <param name="nowUtc">
    /// <c>GameContext.NowUtc</c>. Must carry a zero offset and must not precede
    /// <see cref="LastAppliedAtUtc"/>.
    /// </param>
    /// <remarks>
    /// ⚠️ Equal is allowed, strictly-earlier is not — the same rule and the same message shape as
    /// <c>Player.MarkApplied</c>. Two commands can legitimately share an instant, whereas an earlier
    /// instant means a clock moved backwards, and moving this field backwards would extend a run's
    /// TTL past the point `14` §16.3 expires it.
    /// <para>
    /// 🔒 <b>M1-12 — <c>GameRules.MarkApplied</c> floors the instant it passes here</b>, so host
    /// clock skew reaches this method as the stored value rather than as a throw out of
    /// <c>Apply</c> (`30` §2.1's <b>P3</b>, carried-forward item 20). The refusal below is kept for
    /// the reason <c>Player.MarkApplied</c>'s remarks record: the clamp belongs to the caller, and
    /// this aggregate goes on treating a backwards anchor as the persistence defect it would be.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="nowUtc"/> is offset or goes backwards.</exception>
    internal void MarkApplied(DateTimeOffset nowUtc)
    {
        RequireZeroOffset(nowUtc, nameof(nowUtc));

        if (nowUtc < _lastAppliedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nowUtc),
                nowUtc,
                "This run last accepted a command at " + Text(_lastAppliedAtUtc) + ", which is after " +
                Text(nowUtc) + ". 14 §16.3 measures the sliding 48-hour run TTL FROM this instant, " +
                "so moving it backwards would keep a run alive past the point it expires.");
        }

        _lastAppliedAtUtc = nowUtc;
    }

    /// <summary>
    /// `12` §4.3 — registers and advances one in-run ad placement's count. The placement comes into
    /// existence on its first use; nothing declares it in advance.
    /// </summary>
    /// <param name="placementId">
    /// The placement id as authored in <c>tuning/ads.json</c>'s <c>inRunPlacements</c>. Never blank.
    /// ⚠️ Deliberately an open string and not a closed type — see <see cref="AdUses"/>.
    /// </param>
    /// <param name="amount">How much to add. Never negative — a use counter counts, it does not settle.</param>
    /// <remarks>
    /// ⚠️ <b>The cap is not enforced here.</b> `12` §4.3's per-run caps and the <c>CAP_REACHED</c>
    /// rejection belong to the handler that reads <c>ads.json</c>; `30` §11.5 keeps that computation
    /// out of the aggregate, which holds the count and refuses a count that is not a count.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="placementId"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is negative, or the count overflows.</exception>
    internal void CountAdUse(string placementId, long amount)
    {
        RequirePlacementId(placementId, nameof(placementId));

        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "An ad-use counter counts upwards; '" + placementId + "' cannot be advanced by " +
                Text(amount) + ". 12 §4.3's in-run caps are per run and the run IS the period, so " +
                "there is nothing to settle back down — a negative advance would be a way to hand a " +
                "player an impression they have already watched.");
        }

        _adUses.TryGetValue(placementId, out var current);

        try
        {
            _adUses[placementId] = checked(current + amount);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Advancing the ad-use counter '" + placementId + "' by " + Text(amount) + " from " +
                Text(current) + " overflows a 64-bit count. A count that large inside one run is a " +
                "loop that did not terminate.");
        }
    }

    /// <summary>
    /// 🔒 M3-03c, `03` §6.2 — whether a minigame has already been resolved at
    /// <paramref name="position"/> this run. See <see cref="_resolvedMinigames"/> for why the
    /// position stands in for a tile instance.
    /// </summary>
    internal bool HasResolvedMinigameAt(int position) => _resolvedMinigames.ContainsKey(position);

    /// <summary>
    /// 🔒 M3-03 — whether this run has arrived at a tile it has not yet resolved.
    /// </summary>
    /// <remarks>
    /// The gate every one of `14` §2.3's tile-resolution commands checks first: <c>RESOLVE_TILE</c>,
    /// <c>EVENT_CHOOSE</c> and <c>CAMPFIRE_CHOOSE</c> all answer <c>ILLEGAL_STATE</c> when there is
    /// nothing pending, rather than resolving a tile the run is not standing on.
    /// </remarks>
    internal bool HasPendingTile => _pendingTileKind != NoPendingTile;

    /// <summary>
    /// 🔒 M3-03 — which `03` §2 tile kind is pending, as its <b>underlying integer</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>An <c>int</c> and not the tile-kind enum, and the reason is the layering rather than
    /// taste.</b> `30` §11.4 orders <c>Core</c> as Handlers → Rules → Model → Content → Primitives,
    /// and <c>AccessibilityBoundaryTests.Core_internal_layering_holds</c> enforces it in both
    /// metadata <em>and</em> source — so <c>Model</c> may not name the tile vocabulary, which lives
    /// under <c>Rules</c>. The aggregate therefore holds the value and the <c>Rules</c>/<c>Handlers</c>
    /// layer above it does the interpreting, which is also `30` §11.5's division: this type holds
    /// state and invariants, it does not compute.
    /// </para>
    /// <para>
    /// 🔒 <b>The consequence, stated rather than discovered.</b> This aggregate cannot check the value
    /// is one of `03` §2's fourteen — it cannot see them — so it checks only that it is not below
    /// the "nothing pending" sentinel. An out-of-vocabulary value therefore survives
    /// <see cref="Rehydrate"/> and is caught one layer up, by <c>Handlers.ResolveTile</c>'s switch,
    /// which throws rather than accepting it as a tile that does nothing. That is the same shape as
    /// <see cref="Position"/>'s board bound: the real check needs a layer this one may not name.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">No tile is pending.</exception>
    internal int PendingTileKindValue =>
        HasPendingTile ? _pendingTileKind : throw NothingPending(nameof(PendingTileKindValue));

    /// <summary>🔒 M3-03 — the pending tile's `03` §1.1 linear node index.</summary>
    /// <exception cref="InvalidOperationException">No tile is pending.</exception>
    internal int PendingTileLinearIndex =>
        HasPendingTile ? _pendingTileLinearIndex : throw NothingPending(nameof(PendingTileLinearIndex));

    /// <summary>🔒 M3-03 — the pending tile's `03` §1 stage, or <c>BoardGraph.BossStage</c>.</summary>
    /// <exception cref="InvalidOperationException">No tile is pending.</exception>
    internal int PendingTileStage =>
        HasPendingTile ? _pendingTileStage : throw NothingPending(nameof(PendingTileStage));

    /// <summary>
    /// 🔒 M3-03 — the `19` Part A card a pending <c>TILE_EVENT</c> has drawn, or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ Answers <c>null</c> rather than throwing when nothing is pending, unlike the three getters
    /// above, and the asymmetry is deliberate: those three describe a tile and have no meaning
    /// without one, whereas "no card has been drawn" is a real answer that <c>EVENT_CHOOSE</c>'s
    /// legality check asks for <em>before</em> it knows whether a tile is pending.
    /// </remarks>
    internal string? PendingEventCardId => _pendingEventCardId;

    /// <summary>
    /// 🔒 M3-03 — records that the run has arrived at a tile, which is what makes it resolvable.
    /// </summary>
    /// <param name="kind">
    /// The `03` §2 tile kind landed on, as its underlying integer — see
    /// <see cref="PendingTileKindValue"/> for why this aggregate takes an <c>int</c> and what it can
    /// therefore not check about it. Never negative: the caller has a real tile.
    /// </param>
    /// <param name="linearIndex">
    /// The tile's `03` §1.1 linear node index. ⚠️ Checked against a floor of 0 and <b>no ceiling</b>,
    /// for exactly the reason <see cref="Position"/> is checked against its trailhead floor and
    /// nothing else: the real bound is a property of the specific board this run generated, which is
    /// M3-02's to compute, and "a range check invented here would be a partial invariant wearing the
    /// real one's name".
    /// </param>
    /// <param name="stage">
    /// The tile's `03` §1 stage — 1, 2, 3, or <c>Rules.Board.BoardGraph.BossStage</c>.
    /// </param>
    /// <remarks>
    /// A second arrival with one already pending is a <b>defect</b>, not a rejection — the same shape
    /// <see cref="RecordMinigameResolution"/>'s duplicate guard takes, and for the same reason: the
    /// caller (M3-02's movement engine, once it exists) is what decides a run may move, and moving
    /// onto a second tile while the first is unresolved is a miswired engine rather than a player
    /// asking twice.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="kind"/> is not one of `03` §2's fourteen, <paramref name="linearIndex"/> is
    /// negative, or <paramref name="stage"/> is not one of the four.
    /// </exception>
    /// <exception cref="InvalidOperationException">A tile is already pending.</exception>
    internal void ArriveAtTile(int kind, int linearIndex, int stage)
    {
        if (kind < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "A tile kind's underlying value is non-negative — 03 §2's fourteen kinds are declared " +
                "with no explicit values and therefore run 0..13 — and " + Text(kind) + " is below " +
                "that, which is where the 'no tile pending' sentinel lives. ⚠️ That floor is the " +
                "WHOLE check this aggregate can make: 30 §11.4 forbids Model from naming the tile " +
                "vocabulary, which lives under Rules, so whether the value is one of the fourteen is " +
                "checked one layer up — see PendingTileKindValue's remarks.");
        }

        if (linearIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(linearIndex),
                linearIndex,
                "03 §1.1's linear node index runs from 0 upwards and " + Text(linearIndex) + " is " +
                "below it. ⚠️ That floor is the WHOLE index check, for the reason Position's own " +
                "remarks give: 30 §11.5's 'a run's position is a valid node' needs the specific " +
                "board a specific run stands on (M3-02's), and a range check invented here would be " +
                "a partial invariant wearing the real one's name.");
        }

        if (stage is not (1 or 2 or 3 or BossStage))
        {
            throw new ArgumentOutOfRangeException(
                nameof(stage),
                stage,
                "03 §1 gives a board three stages (1, 2, 3) plus a boss node that belongs to none of " +
                "them and is carried as stage " + Text(BossStage) + ". " + Text(stage) + " is not " +
                "one of the four.");
        }

        if (HasPendingTile)
        {
            throw new InvalidOperationException(
                "This run is already standing on an unresolved tile of kind " +
                Text(_pendingTileKind) + " at " +
                "linear index " + Text(_pendingTileLinearIndex) + ". A run resolves the tile it " +
                "landed on before it moves again (03 §1's forward-only movement gives it no way " +
                "back), so arriving at a second tile with the first still pending is a miswired " +
                "movement engine rather than a player asking twice — the same line " +
                "RecordMinigameResolution draws for a duplicate submission.");
        }

        _pendingTileKind = kind;
        _pendingTileLinearIndex = linearIndex;
        _pendingTileStage = stage;
        _pendingEventCardId = null;
    }

    /// <summary>
    /// 🔒 M3-03 — records which `19` Part A card the pending <c>TILE_EVENT</c> drew, so that
    /// <c>EVENT_CHOOSE</c> resolves the card the player was shown and no resubmission can draw a new
    /// one.
    /// </summary>
    /// <param name="cardId">The drawn card's id. Never blank.</param>
    /// <remarks>
    /// <para>
    /// Both refusals are <b>defects</b>: <c>RESOLVE_TILE</c>'s handler checks the tile kind and the
    /// already-drawn state itself, and answers the player <c>ILLEGAL_STATE</c>, before this seam is
    /// reached.
    /// </para>
    /// <para>
    /// ⚠️ <b>It does not check the pending tile is an <em>event</em> tile, and cannot.</b> That
    /// would mean naming `03` §2's tile vocabulary, which lives under <c>Rules</c> and which `30`
    /// §11.4 forbids <c>Model</c> from reaching — see <see cref="PendingTileKindValue"/>. The check
    /// is real and lives one layer up, in <c>Handlers.ResolveTile</c>, which only reaches this seam
    /// from inside its own <c>Event</c> branch.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="cardId"/> is blank.</exception>
    /// <exception cref="InvalidOperationException">
    /// No tile is pending, or a card is already drawn.
    /// </exception>
    internal void SetPendingEventCard(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            throw new ArgumentException(
                "A pending event card is recorded against the 19 Part A EVT_* id that was drawn, " +
                "never blank — a blank id is indistinguishable from 'no card drawn', which is what " +
                "the absence of one already means.",
                nameof(cardId));
        }

        if (!HasPendingTile)
        {
            throw new InvalidOperationException(
                "A pending event card belongs to a pending TILE_EVENT, and this run has no pending " +
                "tile at all. RESOLVE_TILE's handler branches on the tile kind and answers " +
                "ILLEGAL_STATE itself before reaching this seam, so arriving here is a miswired " +
                "handler. ⚠️ That the pending tile is specifically an EVENT tile is checked by that " +
                "handler and not here — see this method's remarks for the layering reason.");
        }

        if (_pendingEventCardId is not null)
        {
            throw new InvalidOperationException(
                "This run has already drawn '" + _pendingEventCardId + "' for its pending event " +
                "tile. Overwriting it would be exactly the re-draw the field exists to prevent: a " +
                "client that disliked its card could resubmit RESOLVE_TILE until it liked one, " +
                "which is the unreproducible run 14 §8.1's counter model exists to make impossible.");
        }

        _pendingEventCardId = cardId;
    }

    /// <summary>
    /// 🔒 M3-03 — clears the pending tile once it has resolved. Idempotent.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Safe to call with nothing pending, deliberately, and the choice is recorded rather than
    /// left to the reader.</b> The alternative — throwing — would make every caller ask
    /// <see cref="HasPendingTile"/> first, and the one thing this method promises is a
    /// <em>postcondition</em> ("no tile is pending"), not a transition. That postcondition is
    /// already true when nothing is pending, so refusing would be refusing to do what has been done.
    /// It is the opposite call from <see cref="ArriveAtTile"/>'s, which refuses a duplicate because
    /// arriving twice destroys information; clearing twice destroys none.
    /// </remarks>
    internal void ClearPendingTile()
    {
        _pendingTileKind = NoPendingTile;

        // 🔒 Reset to 0 rather than left where they were: RunSnapshot is hashed whole (14 §16.6), so
        // two runs that both have no pending tile must produce the same bytes for these slots. Stale
        // values would give the same logical state two different stateHashes.
        _pendingTileLinearIndex = 0;
        _pendingTileStage = 0;
        _pendingEventCardId = null;
    }

    private static InvalidOperationException NothingPending(string member) =>
        new("Run." + member + " describes the tile this run has arrived at and not yet resolved, " +
            "and this run has no pending tile. Ask Run.HasPendingTile first — it is the gate every " +
            "tile-resolution handler checks before anything else, and answering a default here " +
            "would let a rule resolve a tile the run is not standing on.");

    /// <summary>
    /// 🔒 M3-03c, `03` §6.2 — records that <paramref name="minigameId"/> was resolved at
    /// <paramref name="position"/>, closing the legality gate for that tile.
    /// </summary>
    /// <param name="position">The run's node index at resolution — see <see cref="_resolvedMinigames"/>.</param>
    /// <param name="minigameId">`03` §6's <c>MG_*</c> id that resolved.</param>
    /// <remarks>
    /// A defect, not a rejection, on a duplicate: <see cref="Handlers.MinigameSubmit"/> calls
    /// <see cref="HasResolvedMinigameAt"/> and answers the player <c>ILLEGAL_STATE</c> itself before
    /// this seam is ever reached, exactly as <c>StartRun.Handle</c> rejects an already-active run
    /// before <see cref="HandlerInput.OpenRun"/>. Reaching here with a duplicate means that check was
    /// skipped, which is a miswired handler and not a player asking for something twice.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="minigameId"/> is blank.</exception>
    /// <exception cref="InvalidOperationException">A minigame is already recorded at <paramref name="position"/>.</exception>
    internal void RecordMinigameResolution(int position, string minigameId)
    {
        if (string.IsNullOrWhiteSpace(minigameId))
        {
            throw new ArgumentException(
                "A resolved minigame is recorded against the MG_* id that resolved, never blank.",
                nameof(minigameId));
        }

        if (_resolvedMinigames.ContainsKey(position))
        {
            throw new InvalidOperationException(
                "Position " + Text(position) + " already has a resolved minigame recorded ('" +
                _resolvedMinigames[position] + "'). 03 §6.2 allows exactly one submission per tile, " +
                "and the caller's legality check (Run.HasResolvedMinigameAt) is what is supposed to " +
                "refuse a duplicate BEFORE this seam is reached, as a RejectionReason. Reaching here " +
                "with one already recorded means that check was skipped — a miswired handler, not a " +
                "player asking twice.");
        }

        _resolvedMinigames[position] = minigameId;
    }

    /// <summary>
    /// 🔒 M3-02, `03` §1.1 — pauses movement at a junction, waiting for <c>CHOOSE_FORK</c>. Called by
    /// the movement engine (<c>Handlers.RollDice</c>, <c>Handlers.ChooseFork</c>) instead of
    /// finishing the move.
    /// </summary>
    /// <param name="pending">The junction and the movement still unspent once it is left.</param>
    /// <remarks>
    /// A defect, not a rejection, on a run that already has one pending: `03` §1.1 pauses only when
    /// movement must leave a junction, and a run cannot be mid-move at two junctions at once — the
    /// caller resolves (or never opened) any prior pause before it can reach a second one.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A fork is already pending.</exception>
    internal void BeginPendingFork(PendingFork pending)
    {
        if (_pendingFork is not null)
        {
            throw new InvalidOperationException(
                "This run already has a PendingFork (junction " + Text(_pendingFork.Value.JunctionPosition) +
                "). 03 §1.1 pauses movement at ONE junction at a time — a caller reaching here with a " +
                "second pause already open has not resolved (or never checked) the first, which is a " +
                "miswired handler, not a player mid-move at two junctions.");
        }

        _pendingFork = pending;
    }

    /// <summary>
    /// 🔒 M3-02, `03` §1.1 — clears a resolved <see cref="PendingFork"/>, once <c>CHOOSE_FORK</c> has
    /// taken the chosen edge and finished the interrupted movement.
    /// </summary>
    /// <remarks>
    /// A defect, not a rejection, on a run with nothing pending: <c>Handlers.ChooseFork</c> refuses
    /// that as <c>RejectionReason.ILLEGAL_STATE</c> before this seam is ever reached, exactly as
    /// <c>RecordMinigameResolution</c>'s own duplicate check is refused earlier by its caller.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No fork is pending.</exception>
    internal void ClearPendingFork()
    {
        if (_pendingFork is null)
        {
            throw new InvalidOperationException(
                "This run has no PendingFork to clear. Handlers.ChooseFork's own legality check is " +
                "what is supposed to refuse CHOOSE_FORK on a run with nothing pending, as a " +
                "RejectionReason, before this seam is ever reached. Reaching here means that check " +
                "was skipped — a miswired handler, not a player choosing a fork that was never open.");
        }

        _pendingFork = null;
    }

    /// <summary>
    /// 🔒 M3-05 — opens a battle: <see cref="Phase"/> moves from <see cref="RunPhase.InProgress"/> to
    /// <see cref="RunPhase.BattlePending"/>. Called by <c>Handlers.StartBattle</c> after it has
    /// itself checked the pending tile is an <c>Enemy</c>/<c>Elite</c>/<c>Boss</c> — this seam cannot
    /// make that check (30 §11.4 forbids <c>Model</c> from naming the tile vocabulary), so it only
    /// enforces what it can see.
    /// </summary>
    /// <remarks>
    /// A defect, not a rejection, on either failure: <c>Handlers.StartBattle</c>'s own legality
    /// checks (a pending fight tile, and <c>GameRules.Execute</c>'s phase gate) are what are supposed
    /// to refuse an illegal <c>START_BATTLE</c> as a <c>RejectionReason</c> before this seam is ever
    /// reached — the same shape <see cref="BeginPendingFork"/> draws for a second pending fork.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Phase"/> is not <see cref="RunPhase.InProgress"/>, or no tile is pending.
    /// </exception>
    internal void EnterBattle()
    {
        if (_phase != RunPhase.InProgress)
        {
            throw new InvalidOperationException(
                "This run's phase is " + _phase + ", not " + RunPhase.InProgress + ". A battle can " +
                "only open from the run's default phase — GameRules.Execute's phase gate is what is " +
                "supposed to refuse a second START_BATTLE (or any other run command) while a battle " +
                "is already pending, as a RejectionReason, before this seam is ever reached. Reaching " +
                "here means that gate was skipped — a miswired caller, not a player asking twice.");
        }

        if (!HasPendingTile)
        {
            throw new InvalidOperationException(
                "This run has no pending tile. A battle opens against the tile the run has arrived " +
                "at and not yet resolved — Handlers.StartBattle's own legality check is what is " +
                "supposed to refuse START_BATTLE with nothing pending, as a RejectionReason, before " +
                "this seam is ever reached.");
        }

        _phase = RunPhase.BattlePending;
    }

    /// <summary>
    /// 🔒 M3-05 — closes a battle: <see cref="Phase"/> moves back from
    /// <see cref="RunPhase.BattlePending"/> to <see cref="RunPhase.InProgress"/>. Called by
    /// <c>Handlers.ConfirmBattleResult</c>. Does <b>not</b> clear the pending tile — the caller calls
    /// <see cref="ClearPendingTile"/> itself, once it has applied whatever outcome the battle had.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="Phase"/> is not <see cref="RunPhase.BattlePending"/>.</exception>
    internal void ExitBattle()
    {
        if (_phase != RunPhase.BattlePending)
        {
            throw new InvalidOperationException(
                "This run's phase is " + _phase + ", not " + RunPhase.BattlePending + ". " +
                "Handlers.ConfirmBattleResult's own legality check is what is supposed to refuse " +
                "CONFIRM_BATTLE_RESULT with no battle open, as a RejectionReason, before this seam " +
                "is ever reached.");
        }

        _phase = RunPhase.InProgress;
    }

    /// <summary>
    /// 🔒 M3-05 — the documented hook for M3-06's perk draft: records that a won battle has a draft
    /// waiting. <c>Handlers.ConfirmBattleResult</c> is the exact call site (see its remarks); a
    /// future M3-06 command (<c>PICK_PERK</c>/<c>REROLL_DRAFT</c>/<c>SKIP_DRAFT</c>) reads
    /// <see cref="DraftPending"/> and calls <see cref="ClearDraftPending"/> once the draft resolves.
    /// </summary>
    /// <param name="battleKind">
    /// 🔒 M3-06 — the <c>(int)TileKind</c> of the battle just closed (Enemy, Elite or Boss), so
    /// M3-06's <c>RarityWeights(stage, isElite, isBoss)</c> can tell which table to draw from once
    /// <see cref="Handlers.ConfirmBattleResult"/> has already cleared <c>PendingTileKind</c>. Never
    /// negative — the caller reads it off <see cref="PendingTileKindValue"/> before clearing it.
    /// </param>
    /// <param name="battleStage">The stage the battle belonged to — 1, 2, 3, or <see cref="BossStage"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="battleKind"/> is negative, or <paramref name="battleStage"/> is not one of the four.
    /// </exception>
    /// <exception cref="InvalidOperationException">A draft is already pending.</exception>
    internal void MarkDraftPending(int battleKind, int battleStage)
    {
        if (battleKind < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(battleKind), battleKind,
                "A battle's tile kind is Enemy, Elite or Boss, all non-negative 03 §2 values. The " +
                "caller reads this off PendingTileKindValue before ClearPendingTile wipes it — see " +
                "PendingTileKindValue's remarks for why this aggregate cannot check it is one of " +
                "those three specifically.");
        }

        if (battleStage is not (1 or 2 or 3 or BossStage))
        {
            throw new ArgumentOutOfRangeException(
                nameof(battleStage), battleStage,
                "03 §1 gives a board three stages (1, 2, 3) plus a boss node carried as stage " +
                Text(BossStage) + ". " + Text(battleStage) + " is not one of the four.");
        }

        if (_draftPending)
        {
            throw new InvalidOperationException(
                "This run already has a draft pending. Two wins without an intervening draft " +
                "resolution is not a state Handlers.ConfirmBattleResult should ever reach — a run " +
                "cannot open a second battle while DraftPending is still set once M3-06 gates on it, " +
                "so calling this twice is a miswired caller.");
        }

        _draftPending = true;
        _draftBattleKind = battleKind;
        _draftBattleStage = battleStage;
    }

    /// <summary>🔒 M3-05 — clears the draft-pending hook. Idempotent, for the reason <see cref="ClearPendingTile"/> is.</summary>
    internal void ClearDraftPending()
    {
        _draftPending = false;

        // 🔒 Reset for the same determinism reason ClearPendingTile resets its own fields: two runs
        // with no draft pending must hash identically (30 §11.3's snapshot is flat and hashed whole).
        _draftBattleKind = NoDraftBattleKind;
        _draftBattleStage = 0;
    }

    /// <summary>
    /// 🔒 M3-06, `06` §1.1 — grants or upgrades a drafted perk: a fresh grant at Tier I, or an
    /// upgrade to <paramref name="newTier"/> for a perk already owned one tier below it.
    /// </summary>
    /// <param name="perkId">The `06` §3 perk id. Never blank.</param>
    /// <param name="newTier">1 for a fresh grant, or the perk's next tier (2 or 3) for an upgrade.</param>
    /// <exception cref="ArgumentException"><paramref name="perkId"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="newTier"/> is not 1, 2 or 3, or is not exactly one tier above the perk's
    /// currently-owned tier (0 for an unowned perk).
    /// </exception>
    internal void UpsertPerkTier(string perkId, int newTier)
    {
        if (string.IsNullOrWhiteSpace(perkId))
        {
            throw new ArgumentException(
                "A drafted perk is recorded against the 06 §3 PK_* id that was taken, never blank.",
                nameof(perkId));
        }

        if (newTier is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newTier), newTier,
                "06 §1.1 gives every perk exactly three internal tiers, numbered 1-3. " +
                Text(newTier) + " is outside that range.");
        }

        var ownedTier = _ownedPerkTiers.TryGetValue(perkId, out var tier) ? tier : 0;

        if (newTier != ownedTier + 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newTier), newTier,
                "'" + perkId + "' is owned at tier " + Text(ownedTier) + " (0 meaning unowned). " +
                "06 §1.1's draft either grants a fresh perk at Tier I or upgrades an owned one by " +
                "exactly one tier — " + Text(newTier) + " is neither.");
        }

        _ownedPerkTiers[perkId] = newTier;
    }

    /// <summary>🔒 M3-06 — the "nothing pending" refusal for the draft hook, on <see cref="NothingPending"/>'s pattern.</summary>
    private static InvalidOperationException NoDraftPending(string member) =>
        new("Run." + member + " describes the battle a pending perk draft was opened by, and this " +
            "run has no draft pending. Ask Run.DraftPending first.");

    /// <summary>
    /// 🔒 M3-13, `02` §5.1a / §5.3 — banks Legend XP and/or Soul Shards a kill (or a Victory/first-clear
    /// bonus) just earned. Unlike <see cref="MoveCurrency"/>, this is <b>not</b> a currency movement —
    /// nothing has yet reached <c>Player</c>'s wallet — so it emits no <c>CurrencyChanged</c>; the real
    /// currency movement happens once at run end, in <see cref="EndRun"/>'s caller.
    /// </summary>
    /// <param name="legendXp">Legend XP to add to <see cref="BankedLegendXp"/>. Never negative.</param>
    /// <param name="soulShards">Soul Shards to add to <see cref="BankedSoulShards"/>. Never negative.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either amount is negative, or either pool would overflow.</exception>
    internal void BankRewards(long legendXp, long soulShards)
    {
        if (legendXp < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(legendXp), legendXp, "Banked Legend XP only ever grows during a run.");
        }

        if (soulShards < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(soulShards), soulShards, "Banked Soul Shards only ever grow during a run.");
        }

        long nextLegendXp;
        long nextSoulShards;

        // 🔒 Both sums computed into locals before either field is written — the same "validate/compute
        // fully before mutating" discipline SetHitPoints/MoveCurrency/CommitStreamPositions all follow,
        // so a second-sum overflow can never leave _bankedLegendXp written while this call still throws.
        try
        {
            nextLegendXp = checked(_bankedLegendXp + legendXp);
            nextSoulShards = checked(_bankedSoulShards + soulShards);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(legendXp),
                legendXp,
                "Banking " + Text(legendXp) + " Legend XP and/or " + Text(soulShards) + " Soul " +
                "Shards overflows a 64-bit pool. An amount this size is an economy defect upstream, " +
                "not a reward to store.");
        }

        _bankedLegendXp = nextLegendXp;
        _bankedSoulShards = nextSoulShards;
    }

    /// <summary>
    /// 🔒 M3-13, `02` §5.2 — records that this run's Boss has been killed. Called by
    /// <c>Handlers.ConfirmBattleResult</c> the instant a Boss-kind battle is won; read by
    /// <c>Handlers.EndRun</c> to pick the <c>VICTORY</c> row of <c>CompletionMultiplier</c> and by the
    /// first-clear gate to know a clear actually happened.
    /// </summary>
    /// <exception cref="InvalidOperationException">The Boss is already recorded as defeated.</exception>
    internal void MarkBossDefeated()
    {
        if (_bossDefeated)
        {
            throw new InvalidOperationException(
                "This run's Boss is already recorded as defeated. A run has exactly one Boss tile, " +
                "so a second call is a miswired caller, not a player winning twice.");
        }

        _bossDefeated = true;
    }

    /// <summary>
    /// 🔒 M3-13, `02` §1.1 — closes the run: <see cref="Phase"/> moves to <see cref="RunPhase.Ended"/>,
    /// the terminal phase authored (with no producer) by M3-05. <c>GameRules.Execute</c>'s phase gate
    /// already refuses every <c>CommandKind.Run</c> command against a run at this phase as
    /// <c>RUN_ALREADY_ENDED</c>. Called by <c>Handlers.EndRun</c> and <c>Handlers.AbandonRun</c>, after
    /// each has computed and paid the run's <c>FinalPayout</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">This run has already ended.</exception>
    internal void EndRun()
    {
        if (_phase == RunPhase.Ended)
        {
            throw new InvalidOperationException(
                "This run has already ended. Handlers.EndRun/Handlers.AbandonRun's own legality " +
                "checks (RunPhase.Ended answering RUN_ALREADY_ENDED) are what are supposed to refuse " +
                "a second END_RUN/ABANDON_RUN as a RejectionReason before this seam is ever reached.");
        }

        _phase = RunPhase.Ended;
    }

    /// <summary>
    /// 🔒 M3-05, `04` §3 — records that one reroll charge was spent this stage.
    /// <c>Handlers.UseReroll</c> calls this only after checking
    /// <see cref="Rules.Dice.RerollEconomy.CanAffordReroll"/> itself.
    /// </summary>
    internal void SpendReroll() => _rerollChargesSpentThisStage++;

    /// <summary>
    /// 🔒 M3-05, `03` §1.1 — applies a Stage Gate: heals to the caller-computed hit points (`21`
    /// §3.1's tunable percentage of Max HP, computed by the caller — this seam does not compute, `30`
    /// §11.5), refreshes reroll charges to the stage's base allotment, and advances the Fair-Dice
    /// bag's reset anchor to this stage's start.
    /// </summary>
    /// <param name="healedCurrentHp">
    /// The hero's hit points after the Stage Gate heal — already computed and clamped by the caller.
    /// </param>
    /// <param name="diceStreamPositionAtGate">
    /// The <c>dice</c> stream's draw index at the instant the gate fired — <see cref="StageGateDiceAnchor"/>'s
    /// new value, and the <c>resetAtDraw</c> a subsequent <see cref="Rules.Dice.FairDiceBag.Replay"/>
    /// call reads.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="healedCurrentHp"/> is negative or above <see cref="MaxHp"/>.</exception>
    internal void ApplyStageGate(int healedCurrentHp, ulong diceStreamPositionAtGate)
    {
        // 🔒 Reuses SetHitPoints rather than writing _currentHp directly: one seam validates the
        // pair, and a Stage Gate heal is not exempt from "never above MaxHp" just because it is a
        // gate rather than a tile reward.
        SetHitPoints(healedCurrentHp, _maxHp);
        _rerollChargesSpentThisStage = 0;
        _stageGateDiceAnchor = diceStreamPositionAtGate;
    }

    /// <summary>
    /// 🔒 `14` §8.1 — the <b>one</b> seam that writes the per-stream draw counters: it replaces the
    /// whole map, and it refuses a map that is not a superset of the one already committed.
    /// </summary>
    /// <param name="positions">
    /// The scope's final positions for <b>every</b> stream this run has ever drawn from, plus any it
    /// has newly opened. Every key must be a row of <c>RngStreams</c>.
    /// </param>
    /// <remarks>
    /// <para>
    /// 🔒 <b>It refuses a partial map.</b> Every key already committed must be present in
    /// <paramref name="positions"/>. A dropped key would silently reset that stream to 0, and the
    /// next draw from it would repeat a sequence the player has already played — an unreproducible
    /// run, which is the one failure `14` §8.1's whole counter model exists to prevent. This is also
    /// what makes "<c>Apply</c> folds the scope's final positions into the new <c>Run</c>" the
    /// <em>only</em> expressible call: a handler that wanted to hand-write one position would have to
    /// reconstruct the entire committed set to do it.
    /// </para>
    /// <para>
    /// 🔒 <b>Monotone or throw, and it throws rather than rejecting.</b> A value below the committed
    /// one raises an <see cref="InvalidOperationException"/>: a draw counter going backwards is a
    /// <b>determinism defect</b>, not a request to refuse. A <c>RejectionReason</c> would hand a
    /// corrupt scope back to the player as a polite "no" and leave the run in it.
    /// </para>
    /// <para>
    /// 🔒 <b>Registry-validated keys.</b> A key <c>RngStreams.IsRegistered</c> rejects raises an
    /// <see cref="ArgumentException"/> naming the key and the registry — the same predicate
    /// <c>DeterministicRng</c>'s constructor uses, because a name that cannot be drawn from cannot be
    /// persisted either.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="positions"/> is null.</exception>
    /// <exception cref="ArgumentException">A key is not a row of the `14` §8.1 registry.</exception>
    /// <exception cref="InvalidOperationException">
    /// A committed stream is missing from <paramref name="positions"/>, or its position moved
    /// backwards.
    /// </exception>
    internal void CommitStreamPositions(IReadOnlyDictionary<string, ulong> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        // 🔒 Copied into an ORDINAL dictionary rather than adopted. The caller may hold a mutable
        // reference to the map it handed in, and it may have built it with any comparer at all —
        // under OrdinalIgnoreCase the key "DICE" IS "dice", so a run that adopted the caller's
        // comparer would answer for a stream 14 §8.1 does not have. CanonicalStateWriter orders
        // string keys ordinally, so the stateHash follows that order too.
        var next = new Dictionary<string, ulong>(positions.Count, StringComparer.Ordinal);

        foreach (var (streamName, position) in positions)
        {
            RequireRegisteredStream(streamName, nameof(positions));
            next[streamName] = position;
        }

        // Both refusals run over the WHOLE incoming map before anything is written, so a rejected
        // commit leaves the run exactly as it was rather than half folded in.
        foreach (var (streamName, committed) in _streamPositions)
        {
            if (!next.TryGetValue(streamName, out var incoming))
            {
                throw new InvalidOperationException(
                    "The incoming map has no row for the stream '" + streamName + "', which this " +
                    "run has already committed at draw " + Text(committed) + ". A dropped key " +
                    "would silently reset that stream to 0 and the next draw from it would repeat " +
                    "a sequence the player has already played — the unreproducible run 14 §8.1's " +
                    "counter model exists to prevent. Commit the scope's final positions for every " +
                    "stream, which is the only call this seam accepts.");
            }

            if (incoming < committed)
            {
                throw new InvalidOperationException(
                    "The stream '" + streamName + "' is committed at draw " + Text(committed) +
                    " and this map puts it back at " + Text(incoming) + ". A draw counter that " +
                    "moves backwards is a determinism defect, not a request to refuse: the next " +
                    "draw would repeat a sequence the player has already played (14 §8.1). It " +
                    "throws rather than producing a RejectionReason because a rejection would hand " +
                    "the corrupt scope back to the player as a polite 'no' and leave the run in it.");
            }
        }

        _streamPositions = next.Count == 0
            ? NoStreamPositions
            : new ReadOnlyDictionary<string, ulong>(next);
    }

    /// <summary>
    /// The empty stream map every run that has drawn nothing shares, and the empty ad-use map every
    /// snapshot of a run with no impressions shares.
    /// </summary>
    /// <remarks>
    /// Safe to share precisely because they are read-only and empty: nothing can write to them, and
    /// two runs holding the same empty map are indistinguishable from two holding their own. Worth
    /// having because `14` §2.4 has the <b>client</b> recompute a <c>stateHash</c> — and therefore
    /// call <see cref="ToSnapshot"/> — on every command, on a mid-range handset.
    /// </remarks>
    private static readonly ReadOnlyDictionary<string, ulong> NoStreamPositions =
        new(new Dictionary<string, ulong>(0, StringComparer.Ordinal));

    /// <inheritdoc cref="NoStreamPositions"/>
    private static readonly ReadOnlyDictionary<string, long> NoAdUses =
        new(new Dictionary<string, long>(0, StringComparer.Ordinal));

    /// <summary>An ordinal copy of the ad counts, so no caller shares the aggregate's dictionary.</summary>
    /// <remarks>
    /// Short-circuits on empty, which is the normal state: most runs never watch an ad, and this
    /// runs once per <c>stateHash</c>. <see cref="_streamPositions"/> needs no equivalent — it is
    /// replaced wholesale, so the object handed out can never change afterwards.
    /// </remarks>
    private static ReadOnlyDictionary<string, long> CopyAdUses(Dictionary<string, long> adUses) =>
        adUses.Count == 0
            ? NoAdUses
            : new ReadOnlyDictionary<string, long>(new Dictionary<string, long>(adUses, StringComparer.Ordinal));

    /// <inheritdoc cref="NoStreamPositions"/>
    private static readonly ReadOnlyDictionary<int, string> NoResolvedMinigames = new(new Dictionary<int, string>(0));

    /// <inheritdoc cref="CopyAdUses"/>
    private static ReadOnlyDictionary<int, string> CopyResolvedMinigames(Dictionary<int, string> resolvedMinigames) =>
        resolvedMinigames.Count == 0
            ? NoResolvedMinigames
            : new ReadOnlyDictionary<int, string>(new Dictionary<int, string>(resolvedMinigames));

    /// <inheritdoc cref="NoResolvedMinigames"/>
    private static readonly ReadOnlyDictionary<string, int> NoOwnedPerkTiers =
        new(new Dictionary<string, int>(0, StringComparer.Ordinal));

    /// <inheritdoc cref="CopyAdUses"/>
    private static ReadOnlyDictionary<string, int> CopyOwnedPerkTiers(Dictionary<string, int> ownedPerkTiers) =>
        ownedPerkTiers.Count == 0
            ? NoOwnedPerkTiers
            : new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(ownedPerkTiers, StringComparer.Ordinal));

    /// <summary>
    /// 🔒 <c>GOLD</c> is the one <c>RUN</c>-scoped currency (`10` §1, assumption <b>A3</b>). The
    /// mirror image of <c>Player.RequireWalletCurrency</c>, and it names the aggregate that does
    /// hold the currency rather than answering a zero about the wrong one.
    /// </summary>
    private static void RequireRunCurrency(CurrencyId currency, string parameterName)
    {
        if (currency == CurrencyId.GOLD)
        {
            return;
        }

        var because = Enum.IsDefined(currency)
            ? Text(currency) + " is player-scoped (10 §1, tuning/currencies.json, milestone " +
              "assumption A3). It lives on the Player aggregate — as a wallet row, or as " +
              "Player.Energy for ENERGY's two banks — and moves through Player.MoveCurrency or " +
              "Player.SetEnergy. GOLD is the only currency scoped to a run."
            : "10 §1 fixes eight currencies and this is not one of them; an undefined CurrencyId " +
              "is an uninitialised field, not a balance.";

        throw new ArgumentOutOfRangeException(parameterName, currency, because);
    }

    /// <summary>
    /// 🔒 The same <c>RngStreams.IsRegistered</c> predicate <c>DeterministicRng</c>'s constructor
    /// uses: a name that cannot be drawn from cannot be read or persisted either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The offending key is quoted <b>exactly</b> as it arrived, case and all. The registry is
    /// ordinal, so <c>DICE</c> and <c>dice</c> are two different questions, and a message that
    /// normalised the key would point a reader at a row the data does not carry.
    /// </para>
    /// <para>
    /// ⚠️ It takes a <c>string?</c> and answers a <b>membership</b> question about null, exactly as
    /// <c>RngStreams.IsRegistered</c> does, rather than raising
    /// <see cref="ArgumentNullException"/>. Its two callers differ: <see cref="StreamPosition"/>'s
    /// null <em>is</em> the argument and checks for it itself, whereas a null <b>key</b> inside
    /// <see cref="CommitStreamPositions"/>' map is a bad row of a perfectly present map — reporting
    /// that as "positions is null" would send the reader looking for a map that is right in front of
    /// them.
    /// </para>
    /// </remarks>
    private static void RequireRegisteredStream(string? streamName, string parameterName)
    {
        if (RngStreams.IsRegistered(streamName))
        {
            return;
        }

        throw new ArgumentException(
            "'" + (streamName ?? "null") + "' is not a row of the 14 §8.1 stream registry, which is " +
            "the eight fixed names (" + string.Join(", ", RngStreams.FixedNames) + ") plus " +
            "minigame:{index} for a non-negative index in canonical decimal form. The comparison " +
            "is ordinal and case-sensitive, and minigame:03 is deliberately a different string " +
            "from minigame:3: a name the registry does not recognise cannot be drawn from, so a " +
            "run cannot stand at a position in it either.",
            parameterName);
    }

    /// <summary>A placement id names the placement it counts, so it is never blank.</summary>
    private static void RequirePlacementId(string placementId, string parameterName)
    {
        if (!string.IsNullOrWhiteSpace(placementId))
        {
            return;
        }

        throw new ArgumentException(
            "An ad-use key is one of 12 §4.3's in-run placement ids, as authored in " +
            "tuning/ads.json's inRunPlacements. The keys are open rather than a closed type because " +
            "AdPlacementId is an Application-layer type (12 §7) that Core may not name — but 'open' " +
            "means the caller picks the id, not that there is no id.",
            parameterName);
    }

    private static void RequireZeroOffset(DateTimeOffset instant, string parameterName)
    {
        if (instant.Offset == TimeSpan.Zero)
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            parameterName,
            instant,
            Text(instant) + " carries a " + Text(instant.Offset) + " offset. Every instant in this " +
            "aggregate is UTC: CanonicalStateWriter encodes a DateTimeOffset as Unix milliseconds, " +
            "so two offsets naming the same instant hash IDENTICALLY while record equality calls " +
            "them different. Convert at the edge; the domain stores UTC.");
    }

    private static void RequireIdentity(RunSnapshot snapshot, List<string> faults)
    {
        // default(RunId) runs no constructor, so its Value is null rather than validated — RunId's
        // own remarks name Rehydrate as the seam that has to catch it. Same for PlayerId.
        if (string.IsNullOrWhiteSpace(snapshot.Id.Value))
        {
            faults.Add(
                nameof(RunSnapshot.Id) + " is blank or default(RunId), so this row names no run. " +
                "RunId validates in its constructor, which default(RunId) never runs.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.PlayerId.Value))
        {
            faults.Add(
                nameof(RunSnapshot.PlayerId) + " is blank or default(PlayerId). 30 §4 makes Run a " +
                "CHILD of Player, so a run that names no player is an orphan rather than a run.");
        }
    }

    private static void RequireChapterAndTier(RunSnapshot snapshot, List<string> faults)
    {
        if (snapshot.ChapterId < 1)
        {
            faults.Add(
                nameof(RunSnapshot.ChapterId) + " is " + Text(snapshot.ChapterId) + ". 02 §1 runs " +
                "chapters from 1 and chapter.schema.json sets \"minimum\": 1. ⚠️ There is " +
                "deliberately no upper bound: content/chapters/ holds only chapters 1-2 (M3-14); " +
                "chapters 3-8 are M11-02's unauthored rows, so a ceiling here would be a content " +
                "bound in code (21 §3.1) and a partial invariant wearing the real one's name.");
        }

        if (!Enum.IsDefined(snapshot.Tier))
        {
            faults.Add(
                nameof(RunSnapshot.Tier) + " is " + Text((int)snapshot.Tier) + ", which is not one " +
                "of 10 §7's three tiers (NORMAL, HEROIC, MYTHIC). DifficultyTier has no zero member " +
                "on purpose, so this is what an uninitialised column reads as — and 02 §2 widens " +
                "the tier straight into runSeed, so an undefined one would seed a difficulty the " +
                "game does not have.");
        }
    }

    private static void RequireTimestamp(RunSnapshot snapshot, List<string> faults)
    {
        if (snapshot.LastAppliedAtUtc.Offset == TimeSpan.Zero)
        {
            return;
        }

        faults.Add(
            nameof(RunSnapshot.LastAppliedAtUtc) + " is " + Text(snapshot.LastAppliedAtUtc) +
            ", carrying a " + Text(snapshot.LastAppliedAtUtc.Offset) + " offset. Every persisted " +
            "instant is UTC: CanonicalStateWriter encodes a DateTimeOffset as Unix milliseconds, so " +
            "two offsets naming one instant share a stateHash while record equality calls the two " +
            "snapshots different.");
    }

    private static void RequireVitals(RunSnapshot snapshot, List<string> faults)
    {
        if (snapshot.Position < TrailheadPosition)
        {
            faults.Add(
                nameof(RunSnapshot.Position) + " is " + Text(snapshot.Position) + ", below 03 §1.1's " +
                "virtual trailhead at " + Text(TrailheadPosition) + " — the position every run " +
                "stands at before its first roll, and therefore the lowest one a row can carry. ⚠️ " +
                "That floor is the WHOLE check this validation runs: 30 §11.5's 'a run's position " +
                "is a valid node' is enforced structurally by M3-02's movement engine (Run.Position's " +
                "own remarks explain why this aggregate cannot check itself against a board). A " +
                "range check invented here would be a partial invariant wearing the real one's name.");
        }

        var maxIsValid = snapshot.MaxHp >= 1;

        if (!maxIsValid)
        {
            faults.Add(
                nameof(RunSnapshot.MaxHp) + " is " + Text(snapshot.MaxHp) + ". A run's maximum hit " +
                "points is at least 1; a row whose maximum is zero or below describes a hero the " +
                "game cannot render an HP bar for.");
        }

        if (snapshot.CurrentHp < 0)
        {
            faults.Add(
                nameof(RunSnapshot.CurrentHp) + " is " + Text(snapshot.CurrentHp) + ". Hit points " +
                "are never negative — 02 §6's revive acts on a hero standing at zero, so zero is " +
                "the floor and a legal state rather than a defect.");
        }

        // 🔒 Only when the maximum is itself valid, so ONE defect produces ONE fault. Comparing a
        // current against a maximum the row does not have would report two problems for one, and a
        // reader handed two faults for one defect fixes the wrong one (steering S2).
        else if (maxIsValid && snapshot.CurrentHp > snapshot.MaxHp)
        {
            faults.Add(
                nameof(RunSnapshot.CurrentHp) + " is " + Text(snapshot.CurrentHp) + ", above " +
                nameof(RunSnapshot.MaxHp) + " " + Text(snapshot.MaxHp) + ". Overheal is clamped by " +
                "the rule that computes it (30 §11.5), so a stored current above the maximum is a " +
                "row no rule could have written.");
        }
    }

    private static void RequireGold(RunSnapshot snapshot, List<string> faults)
    {
        if (snapshot.Gold >= 0)
        {
            return;
        }

        faults.Add(
            nameof(RunSnapshot.Gold) + " is " + Text(snapshot.Gold) + ". 30 §11.5 makes 'a currency " +
            "never goes negative' an invariant of this aggregate, and GOLD is the one RUN-scoped " +
            "currency of 10 §1 (assumption A3).");
    }

    private static IReadOnlyDictionary<string, ulong>? ReadStreamPositions(
        RunSnapshot snapshot, List<string> faults)
    {
        if (snapshot.RngStreamPositions is null)
        {
            faults.Add(
                nameof(RunSnapshot.RngStreamPositions) + " is null. An absent counter map is not an " +
                "empty one: the sparse map means 'every stream absent from here stands at draw 0', " +
                "which a null cannot say.");
            return null;
        }

        // Copied into an ORDINAL dictionary rather than kept — the caller may hold a mutable
        // reference to the map it handed in, and CanonicalStateWriter orders string keys ordinally.
        var copy = new Dictionary<string, ulong>(snapshot.RngStreamPositions.Count, StringComparer.Ordinal);
        var faulted = false;

        foreach (var (streamName, position) in snapshot.RngStreamPositions)
        {
            if (!RngStreams.IsRegistered(streamName))
            {
                faults.Add(
                    nameof(RunSnapshot.RngStreamPositions) + " carries the stream name '" +
                    streamName + "', which is not a row of the 14 §8.1 registry — the same " +
                    "RngStreams.IsRegistered predicate DeterministicRng's constructor uses. A name " +
                    "that cannot be drawn from cannot be persisted either.");
                faulted = true;
                continue;
            }

            copy[streamName] = position;
        }

        return faulted
            ? null
            : copy.Count == 0 ? NoStreamPositions : new ReadOnlyDictionary<string, ulong>(copy);
    }

    private static Dictionary<string, long>? ReadAdUses(RunSnapshot snapshot, List<string> faults)
    {
        if (snapshot.AdUses is null)
        {
            faults.Add(
                nameof(RunSnapshot.AdUses) + " is null. An absent counter map is not an empty one.");
            return null;
        }

        var copy = new Dictionary<string, long>(snapshot.AdUses.Count, StringComparer.Ordinal);
        var faulted = false;

        foreach (var (placementId, uses) in snapshot.AdUses)
        {
            if (string.IsNullOrWhiteSpace(placementId))
            {
                faults.Add(
                    nameof(RunSnapshot.AdUses) + " carries a blank placement key. A key names the " +
                    "12 §4.3 in-run placement it counts.");
                faulted = true;
                continue;
            }

            if (uses < 0)
            {
                faults.Add(
                    nameof(RunSnapshot.AdUses) + "['" + placementId + "'] is " + Text(uses) + ". A " +
                    "use counter counts upwards from zero and the run IS the period (12 §4.3), so " +
                    "it is never settled back down.");
                faulted = true;
                continue;
            }

            copy[placementId] = uses;
        }

        return faulted ? null : copy;
    }

    private static Dictionary<int, string>? ReadResolvedMinigames(RunSnapshot snapshot, List<string> faults)
    {
        if (snapshot.ResolvedMinigames is null)
        {
            faults.Add(
                nameof(RunSnapshot.ResolvedMinigames) + " is null. An absent map is not an empty " +
                "one: the sparse map means 'no minigame has resolved at any position', which a null " +
                "cannot say.");
            return null;
        }

        var copy = new Dictionary<int, string>(snapshot.ResolvedMinigames.Count);
        var faulted = false;

        foreach (var (position, minigameId) in snapshot.ResolvedMinigames)
        {
            if (position < TrailheadPosition)
            {
                faults.Add(
                    nameof(RunSnapshot.ResolvedMinigames) + " carries position " + Text(position) +
                    ", below 03 §1.1's virtual trailhead at " + Text(TrailheadPosition) + " — no " +
                    "minigame can have resolved at a position the run could never have stood at.");
                faulted = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(minigameId))
            {
                faults.Add(
                    nameof(RunSnapshot.ResolvedMinigames) + "[" + Text(position) + "] is blank. A " +
                    "resolved minigame is recorded against the 03 §6 MG_* id that resolved.");
                faulted = true;
                continue;
            }

            copy[position] = minigameId;
        }

        return faulted ? null : copy;
    }

    /// <summary>
    /// 🔒 M3-02 — <see cref="RunSnapshot.PendingForkJunctionPosition"/> and
    /// <see cref="RunSnapshot.PendingForkRemainingSteps"/> are one fact stored as a pair (the same
    /// shape <see cref="SetHitPoints"/> takes both halves for): both null, or both present and in
    /// range. One present without the other is a row no <see cref="BeginPendingFork"/> call could
    /// have written.
    /// </summary>
    private static void RequirePendingFork(RunSnapshot snapshot, List<string> faults)
    {
        var junctionPosition = snapshot.PendingForkJunctionPosition;
        var remainingSteps = snapshot.PendingForkRemainingSteps;

        if (junctionPosition is null && remainingSteps is null)
        {
            return;
        }

        if (junctionPosition is null || remainingSteps is null)
        {
            faults.Add(
                nameof(RunSnapshot.PendingForkJunctionPosition) + " is " + TextOrNull(junctionPosition) +
                " and " + nameof(RunSnapshot.PendingForkRemainingSteps) + " is " + TextOrNull(remainingSteps) +
                ". A pending fork is one fact stored as a pair — both present or both absent — and a " +
                "row carrying only one half is not a state Run.BeginPendingFork could have written.");
            return;
        }

        if (junctionPosition.Value < 0)
        {
            faults.Add(
                nameof(RunSnapshot.PendingForkJunctionPosition) + " is " + Text(junctionPosition.Value) +
                ". A junction is a real node of the board, never negative.");
        }

        if (remainingSteps.Value < 1)
        {
            faults.Add(
                nameof(RunSnapshot.PendingForkRemainingSteps) + " is " + Text(remainingSteps.Value) +
                ". 03 §1.1: landing exactly on a junction with zero movement left does not prompt a " +
                "CHOOSE_FORK, so a pending fork with nothing left to spend could never have been " +
                "created.");
        }
    }

    /// <inheritdoc cref="Text(int)"/>
    private static string TextOrNull(int? value) => value is { } v ? Text(v) : "null";

    /// <summary>
    /// 🔒 M3-03 — validates the four pending-tile fields as one fact, because that is what they are.
    /// </summary>
    /// <remarks>
    /// Every fault names <em>which</em> field failed and why (steering <b>S2</b>), and the checks are
    /// written so that <b>one</b> defect produces <b>one</b> fault: the index, stage and card id are
    /// only compared against a pending tile when the kind itself is a legal one, since a reader handed
    /// four faults for one corrupt column fixes the wrong one.
    /// </remarks>
    private static void RequirePendingTile(RunSnapshot snapshot, List<string> faults)
    {
        var kind = snapshot.PendingTileKind;
        var pending = kind != NoPendingTile;

        if (kind < NoPendingTile)
        {
            faults.Add(
                nameof(RunSnapshot.PendingTileKind) + " is " + Text(kind) + ", below " +
                Text(NoPendingTile) + " — the 'no tile pending' sentinel, and the lowest value this " +
                "column legitimately holds. ⚠️ That floor is the WHOLE kind check this seam can " +
                "make: whether a non-negative value is one of 03 §2's fourteen needs the tile " +
                "vocabulary, which lives under Rules and which 30 §11.4 forbids Model from naming " +
                "(see Run.PendingTileKindValue). An out-of-vocabulary kind is caught one layer up, " +
                "by Handlers.ResolveTile's switch, which throws rather than treating it as a tile " +
                "that does nothing.");

            // The remaining three describe a tile this row does not legibly name, so checking them
            // against it would report three more problems for one defect.
            return;
        }

        if (snapshot.PendingEventCardId is null)
        {
            faults.Add(
                nameof(RunSnapshot.PendingEventCardId) + " is null. The field spells 'no card drawn' " +
                "as the EMPTY STRING so that CanonicalStateWriter encodes a String slot rather than " +
                "a nullable one; a null is a row written by something that did not know that.");
        }

        if (!pending)
        {
            // 🔒 With no tile pending the other three carry no meaning, and ClearPendingTile zeroes
            // them precisely so that one logical state has one encoding (14 §16.6). A row that left
            // them populated would hash differently from an identical run — so it is refused rather
            // than normalised on the way in, which would edit persisted state at the seam.
            if (snapshot.PendingTileLinearIndex != 0 || snapshot.PendingTileStage != 0)
            {
                faults.Add(
                    nameof(RunSnapshot.PendingTileKind) + " is " + Text(NoPendingTile) + " ('no tile " +
                    "pending') but " + nameof(RunSnapshot.PendingTileLinearIndex) + " is " +
                    Text(snapshot.PendingTileLinearIndex) + " and " +
                    nameof(RunSnapshot.PendingTileStage) + " is " + Text(snapshot.PendingTileStage) +
                    "; both are zero when nothing is pending. 14 §16.6 hashes the whole row, so a " +
                    "stale index would give two identical runs two different stateHashes.");
            }

            if (!string.IsNullOrEmpty(snapshot.PendingEventCardId))
            {
                faults.Add(
                    nameof(RunSnapshot.PendingEventCardId) + " is '" + snapshot.PendingEventCardId +
                    "' but " + nameof(RunSnapshot.PendingTileKind) + " is " + Text(NoPendingTile) +
                    " ('no tile pending'). A drawn event card belongs to a pending TILE_EVENT; " +
                    "without one there is no command that could ever resolve it, so the card would " +
                    "be stranded on the run for the rest of its life.");
            }

            return;
        }

        if (snapshot.PendingTileLinearIndex < 0)
        {
            faults.Add(
                nameof(RunSnapshot.PendingTileLinearIndex) + " is " +
                Text(snapshot.PendingTileLinearIndex) + ". 03 §1.1's linear node index runs from 0 " +
                "upwards. ⚠️ That floor is the WHOLE check — the ceiling belongs to the specific " +
                "board this run generated (M3-02's), and a range invented here would be a partial " +
                "invariant wearing the real one's name.");
        }

        if (snapshot.PendingTileStage is not (1 or 2 or 3 or BossStage))
        {
            faults.Add(
                nameof(RunSnapshot.PendingTileStage) + " is " + Text(snapshot.PendingTileStage) +
                ". 03 §1 gives a board three stages (1, 2, 3) plus a boss node belonging to none of " +
                "them, carried as stage " + Text(BossStage) + ".");
        }

        // ⚠️ That a stored card id belongs specifically to an EVENT tile is NOT checked here, for
        // the same reason the kind's upper bound is not: it would mean naming 03 §2's vocabulary,
        // which 30 §11.4 puts above this layer. EventChoose is what checks the pairing, and it
        // answers ILLEGAL_STATE rather than resolving a card against the wrong tile.
    }

    /// <summary>🔒 M3-05 — <see cref="RunSnapshot.Phase"/> must be a defined <see cref="RunPhase"/>.</summary>
    private static void RequirePhase(RunSnapshot snapshot, List<string> faults)
    {
        if (!Enum.IsDefined(snapshot.Phase))
        {
            faults.Add(
                nameof(RunSnapshot.Phase) + " is " + Text((int)snapshot.Phase) + ", which names no " +
                "RunPhase member. A row outside the vocabulary is not a state Run.EnterBattle, " +
                "Run.ExitBattle or a future M3-13 mutator could have written.");
        }
    }

    /// <summary>🔒 M3-05 — <see cref="RunSnapshot.RerollChargesSpentThisStage"/> is never negative.</summary>
    private static void RequireRerollCharges(RunSnapshot snapshot, List<string> faults)
    {
        if (snapshot.RerollChargesSpentThisStage < 0)
        {
            faults.Add(
                nameof(RunSnapshot.RerollChargesSpentThisStage) + " is " +
                Text(snapshot.RerollChargesSpentThisStage) + ". A spent count is never negative.");
        }
    }

    /// <summary>
    /// 🔒 M3-06 — <see cref="RunSnapshot.DraftBattleKind"/>/<see cref="RunSnapshot.DraftBattleStage"/>
    /// are meaningless while <see cref="RunSnapshot.DraftPending"/> is false, and must then stand at
    /// their reset values — the same pairing discipline <see cref="RequirePendingFork"/> checks.
    /// </summary>
    private static void RequireDraftBattle(RunSnapshot snapshot, List<string> faults)
    {
        if (!snapshot.DraftPending)
        {
            if (snapshot.DraftBattleKind != NoDraftBattleKind || snapshot.DraftBattleStage != 0)
            {
                faults.Add(
                    nameof(RunSnapshot.DraftPending) + " is false but " +
                    nameof(RunSnapshot.DraftBattleKind) + " is " + Text(snapshot.DraftBattleKind) +
                    " and " + nameof(RunSnapshot.DraftBattleStage) + " is " +
                    Text(snapshot.DraftBattleStage) + ". ClearDraftPending resets both to their " +
                    "sentinel values; a row with no draft pending and non-sentinel values is not a " +
                    "state Run's own mutators could have written.");
            }

            return;
        }

        if (snapshot.DraftBattleKind < 0)
        {
            faults.Add(
                nameof(RunSnapshot.DraftBattleKind) + " is " + Text(snapshot.DraftBattleKind) +
                " while a draft is pending. A battle's tile kind is Enemy, Elite or Boss, all " +
                "non-negative 03 §2 values.");
        }

        if (snapshot.DraftBattleStage is not (1 or 2 or 3 or BossStage))
        {
            faults.Add(
                nameof(RunSnapshot.DraftBattleStage) + " is " + Text(snapshot.DraftBattleStage) +
                ", which names none of 03 §1's three stages plus the boss node (stage " +
                Text(BossStage) + ").");
        }
    }

    private static Dictionary<string, int>? ReadOwnedPerkTiers(RunSnapshot snapshot, List<string> faults)
    {
        // 🔒 Unlike AdUses/ResolvedMinigames, null IS a legitimate empty answer here rather than a
        // fault: OwnedPerkTiers is a trailing DEFAULTED positional field (default null) added by
        // M3-06 so every pre-M3-06 positional RunSnapshot construction still compiles — the same
        // reason Phase/DraftPending/RerollChargesSpentThisStage/StageGateDiceAnchor default to their
        // own "run implicitly held this" values rather than faulting a caller that predates them.
        if (snapshot.OwnedPerkTiers is null)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var copy = new Dictionary<string, int>(snapshot.OwnedPerkTiers.Count, StringComparer.Ordinal);
        var faulted = false;

        foreach (var (perkId, tier) in snapshot.OwnedPerkTiers)
        {
            if (string.IsNullOrWhiteSpace(perkId))
            {
                faults.Add(
                    nameof(RunSnapshot.OwnedPerkTiers) + " carries a blank perk id key.");
                faulted = true;
                continue;
            }

            if (tier is < 1 or > 3)
            {
                faults.Add(
                    nameof(RunSnapshot.OwnedPerkTiers) + "['" + perkId + "'] is " + Text(tier) +
                    ". 06 §1.1 gives every perk exactly three internal tiers, numbered 1-3.");
                faulted = true;
                continue;
            }

            copy[perkId] = tier;
        }

        return faulted ? null : copy;
    }

    /// <summary>
    /// 🔒 Renders a value with <see cref="CultureInfo.InvariantCulture"/>.
    /// </summary>
    /// <remarks>
    /// The same reason <c>Player</c> has one: `14` §8.2 wants <c>Core</c> reading identically
    /// everywhere, and a bare interpolation renders <c>12.08.2026 05:00:00 +00:00</c> on a German
    /// laptop and <c>08/12/2026 05:00:00 +00:00</c> in the container — two diagnostics for one
    /// corrupt row, and a message a reader cannot grep. Enums and strings are rendered directly;
    /// their rendering does not consult a culture.
    /// </remarks>
    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc cref="Text(int)"/>
    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc cref="Text(int)"/>
    private static string Text(ulong value) => value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc cref="Text(int)"/>
    private static string Text(CurrencyId value) => value.ToString();

    /// <inheritdoc cref="Text(int)"/>
    private static string Text(TimeSpan value) => value.ToString("c", CultureInfo.InvariantCulture);

    /// <inheritdoc cref="Text(int)"/>
    private static string Text(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
}
