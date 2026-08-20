using System.Collections.ObjectModel;
using System.Globalization;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Model;

/// <summary>
/// The <c>Run</c> aggregate root — a child of <c>Player</c>: the committed run seed and the
/// per-stream RNG counters, the position, the hero's hit points, the run-scoped <c>GOLD</c> balance
/// and the per-run ad uses.
/// </summary>
/// <remarks>
/// <para>
/// Lives in <c>Core/Model/Run/</c> but in namespace <c>SlayIdleRepeat.Core.Model</c>, not
/// <c>...Model.Run</c>, for the same reason as <c>Player</c>: a child namespace named <c>Run</c>
/// would shadow the type <c>Run</c> and make it unnameable from inside <c>Model</c>.
/// </para>
/// <para>
/// Public getters, private constructor, <c>internal</c> mutators: the only public way to change
/// state is <c>GameRules.Apply</c>. <see cref="ToSnapshot"/> and <see cref="Rehydrate"/> are the
/// exception, the validating factory pair the persistence adapter needs.
/// </para>
/// <para>
/// It holds state and invariants; it does not compute. There is no dice arithmetic here, no damage
/// formula, no cap check — a handler computes and hands the answer to <see cref="SetHitPoints"/> or
/// <see cref="CountAdUse"/>, and the aggregate's job is to refuse an answer that breaks an invariant.
/// Overheal is clamped by the rule that computes it, not accepted here.
/// </para>
/// <para>
/// There is no factory for a new run: starting position, HP and Gold are a later milestone's
/// decisions, and <see cref="Rehydrate"/> is the only way to obtain one.
/// </para>
/// <para>
/// Deliberately absent: the drafted perks, held consumables (with the armed Escape Rope flag) and
/// curses, each deferred with a <c>GapRegister</c> entry keyed on a type that must not yet exist. The
/// board is never stored — it regenerates deterministically from <see cref="RunSeed"/> and the run's
/// committed <c>board</c> stream position. The pending fork choice is <see cref="PendingFork"/>.
/// </para>
/// </remarks>
public sealed class Run
{
    /// <summary>The lowest position a run can stand at: the virtual trailhead, one step before node 0.</summary>
    /// <remarks>
    /// An authored position, not an invented bound: the hero begins every run at a virtual trailhead
    /// one step before node 0 (position -1), so a first roll of 1 lands on node 0. It is the position
    /// every run holds between <c>START_RUN</c> and its first <c>ROLL_DICE</c> — exactly the state a
    /// player who starts a run and closes the app leaves behind. A named constant so the two places
    /// that hold it, <see cref="MoveTo"/> and <see cref="Rehydrate"/>, cannot drift apart.
    /// </remarks>
    internal const int TrailheadPosition = -1;

    /// <summary>The run's <c>GOLD</c> balance — and the field name is load-bearing, not stylistic.</summary>
    /// <remarks>
    /// An IL scan recognises a currency-carrying field by its type or by a name containing
    /// <c>currenc</c>/<c>wallet</c>; a bare <c>long _gold</c> would match neither, leaving the game's
    /// only run-scoped currency invisible to the rule that every currency mutation emits a
    /// <c>CurrencyChanged</c>. Not a one-row dictionary either: there is exactly one run-scoped
    /// currency, so a map would buy nothing at the cost of an allocation per kill.
    /// </remarks>
    private long _wallet;

    /// <summary>Per-stream draw counters. Replaced wholesale by <see cref="CommitStreamPositions"/>, never edited in place.</summary>
    /// <remarks>
    /// A map, not fixed fields: the ninth stream is parameterised (<c>minigame:{index}</c>), so a
    /// fixed set of slots could not hold it. Sparse — a stream never drawn from stands at 0, and
    /// storing eager zeros would put dead bytes in every <c>stateHash</c>. Copied into an ordinal
    /// dictionary on commit and on rehydrate, since <c>CanonicalStateWriter</c> orders string keys
    /// ordinally.
    /// </remarks>
    private IReadOnlyDictionary<string, ulong> _streamPositions;

    /// <summary>Per-run ad counts, and the read-only view handed out by <see cref="AdUses"/>.</summary>
    /// <remarks>Mutated in place, unlike <see cref="_streamPositions"/>, so the view stays valid across every increment.</remarks>
    private readonly Dictionary<string, long> _adUses;

    /// <inheritdoc cref="_adUses"/>
    private readonly ReadOnlyDictionary<string, long> _adUsesView;

    /// <summary>
    /// The per-tile minigame legality gate: the linear node index of every tile whose minigame has
    /// already been resolved this run, mapped to which minigame id resolved there.
    /// </summary>
    /// <remarks>
    /// Keyed on <see cref="Position"/> as a stand-in for tile-instance identity: movement is
    /// forward-only with no backtracking, so one position is visited at most once per run, which
    /// makes the linear node index a safe proxy until a real pending-tile-instance concept exists.
    /// </remarks>
    private readonly Dictionary<int, string> _resolvedMinigames;

    /// <inheritdoc cref="_resolvedMinigames"/>
    private readonly ReadOnlyDictionary<int, string> _resolvedMinigamesView;

    /// <summary>The tile kind of the tile this run has arrived at and not yet resolved, or <see cref="NoPendingTile"/>.</summary>
    /// <remarks>
    /// Held as an <c>int</c> rather than the tile-kind enum because the enum is <c>internal</c> to
    /// <c>Rules.Board</c> and this snapshot is public. Three flat fields rather than one nested
    /// record, since the snapshot is flat and a nested record would only add path depth to the
    /// field-order pin.
    /// </remarks>
    private int _pendingTileKind;

    /// <inheritdoc cref="_pendingTileKind"/>
    private int _pendingTileLinearIndex;

    /// <inheritdoc cref="_pendingTileKind"/>
    private int _pendingTileStage;

    /// <summary>The event card a pending <c>TILE_EVENT</c> has already drawn, or <c>null</c>.</summary>
    /// <remarks>
    /// <c>null</c> here, <c>""</c> in the snapshot: the aggregate's <c>null</c> reads correctly at
    /// every call site, while the snapshot's empty string keeps the canonical encoding a plain string
    /// slot rather than a nullable one. <see cref="Rehydrate"/> translates between the two.
    /// </remarks>
    private string? _pendingEventCardId;

    private DateTimeOffset _lastAppliedAtUtc;
    private int _position;
    private int _currentHp;
    private int _maxHp;

    /// <summary>The subset of the run's state machine that is genuine server-side aggregate state.</summary>
    private RunPhase _phase;

    /// <summary>Set when a won battle closes and a perk draft is waiting to resolve.</summary>
    private bool _draftPending;

    /// <summary>The tile kind of the battle that set <see cref="_draftPending"/>. Meaningless while it is false.</summary>
    private int _draftBattleKind;

    /// <summary>The stage the just-closed battle belonged to. Meaningless while <see cref="_draftPending"/> is false.</summary>
    private int _draftBattleStage;

    /// <summary>The perks this run has drafted: perk id → owned internal tier (1-3).</summary>
    private readonly Dictionary<string, int> _ownedPerkTiers;

    /// <inheritdoc cref="_ownedPerkTiers"/>
    /// <remarks>
    /// The view <see cref="DraftedPerks"/> hands out, built once, on <c>Player</c>'s
    /// <c>_featCountersView</c> precedent. The map above is mutated in place by
    /// <see cref="UpsertPerkTier"/>, so a single wrapper stays live — and a fresh wrapper plus a
    /// fresh <see cref="ReadOnlyDictionary{TKey,TValue}"/> per read was two allocations on a property
    /// the draft engine reads on every option of every draft.
    /// </remarks>
    private readonly DraftedPerks _draftedPerksView;

    /// <summary>
    /// The three draft guarantee counters. Plain integers on the run, not entries in the player's
    /// pity counter map: the draft class is scoped per run and authors no counter key, so there is
    /// no id to form and nothing the profile could store them under.
    /// </summary>
    /// <remarks>
    /// 🔒 And that is also why moving one emits no <c>PityCounterAdvanced</c>: the event names the
    /// counter it moved, the name is an authored key formed in one place, and this class authors
    /// none. Spelling an id here to have something to emit would be inventing the very key the
    /// document declined to author. They ride the run snapshot instead, which is where every other
    /// piece of run-scoped state a client mirrors comes from.
    /// </remarks>
    private int _draftsSinceLegendaryOffered;
    private int _draftsWithoutAboveCommon;
    private int _draftsWithoutOwnedUpgrade;

    /// <summary>A movement paused mid-move at a junction, waiting for <c>CHOOSE_FORK</c>. Null except while standing there.</summary>
    private PendingFork? _pendingFork;

    /// <summary>Legend XP banked so far this run, pending the run-end payout (<see cref="BankRewards"/>).</summary>
    private long _bankedLegendXp;

    /// <summary>Soul Shards banked so far this run, pending the same run-end payout as <see cref="_bankedLegendXp"/>.</summary>
    private long _bankedSoulShards;

    /// <summary>Whether this run's Boss has been killed.</summary>
    private bool _bossDefeated;

    /// <summary>
    /// How many items at or above the session floor's band this run has produced.
    /// </summary>
    /// <remarks>
    /// Persisted rather than recomputed at run end: the floor asks what the RUN produced, and the
    /// stock it produced into is the player's — an item salvaged, merged away or banked by an
    /// earlier run is indistinguishable there, so a tally taken from the inventory would pay the
    /// floor to a run that dropped nothing and refuse it to one that dropped and then spent.
    /// </remarks>
    private int _itemsAtOrAboveFloorBand;

    /// <summary>The shrine buffs taken this run, in the order taken. Duplicates are stacks, not no-ops.</summary>
    /// <remarks>
    /// A list rather than a count map because `03` §7a.5 stacks them additively and the aggregate
    /// stores what happened rather than the arithmetic — how a stack is valued belongs to the effect
    /// source, which is a <c>Rules</c> type this layer may not name.
    /// </remarks>
    private readonly List<string> _shrineBuffs;

    /// <inheritdoc cref="_shrineBuffs"/>
    private readonly ReadOnlyCollection<string> _shrineBuffsView;

    /// <summary>The run buffs bought from a shop's slot 3. Same list semantics as <see cref="_shrineBuffs"/>.</summary>
    private readonly List<string> _runBuffs;

    /// <inheritdoc cref="_runBuffs"/>
    private readonly ReadOnlyCollection<string> _runBuffsView;

    /// <summary>The curses active on this run. A list for ordering, with set semantics.</summary>
    /// <remarks>
    /// `19` Part E gives curses NO stacking, so <see cref="ApplyCurse"/> refuses a duplicate rather
    /// than appending one. Kept as a list anyway because the canonical encoding needs a stable order
    /// and a set has none.
    /// </remarks>
    private readonly List<string> _curses;

    /// <inheritdoc cref="_curses"/>
    private readonly ReadOnlyCollection<string> _cursesView;

    /// <summary>Held consumables: id → count. A zero count is removed, never stored.</summary>
    private readonly Dictionary<string, int> _consumables;

    /// <inheritdoc cref="_consumables"/>
    private readonly ReadOnlyDictionary<string, int> _consumablesView;

    /// <summary>Fixed dice held: pips → count. A zero count is removed, never stored.</summary>
    /// <remarks>
    /// 🔒 A multiset rather than a list, and UNCAPPED. Two dice showing a 3 are the same holding
    /// twice, so a count is the whole truth about them and a list would put an order into the bytes
    /// that nothing means. No ceiling by ruling: a run may bank as many as it earns.
    /// </remarks>
    private readonly Dictionary<int, int> _fixedDice;

    /// <inheritdoc cref="_fixedDice"/>
    private readonly ReadOnlyDictionary<int, int> _fixedDiceView;

    /// <summary>Fixed dice granted but not yet given a number by the player.</summary>
    /// <remarks>
    /// 🔒 A COUNT of outstanding choices, not a list of them, and it is the reason every grant site
    /// can offer a real choice. Most of them have no command a number could ride on — an event
    /// outcome is drawn by weight, a minigame reward is decided by play, an ad and a set bonus are
    /// passive — so the grant records that a choice is owed and <c>CHOOSE_FIXED_DIE</c> answers it.
    /// Non-blocking on purpose: an owed choice does not stop the run, unlike a pending draft, because
    /// a grant can land in the middle of a shop visit the player is not finished with.
    /// </remarks>
    private int _pendingFixedDieChoices;

    /// <summary>Whether an Escape Rope is armed. A flag, not a count: only one may be armed at a time.</summary>
    private bool _escapeRopeArmed;

    /// <summary>Free perk-draft rerolls held, from Draft Tokens. Not per stage — held until spent.</summary>
    private int _freeDraftRerolls;

    /// <summary>The <c>shop</c> stream position the open shop's offer was drawn at, or null when no shop is open.</summary>
    /// <remarks>
    /// The offer is re-derived from this rather than persisted, on the board's and the shrine's
    /// precedent: a persisted offer would be a second copy of something the seed already determines,
    /// and the two could disagree after a content edit.
    /// </remarks>
    private ulong? _shopOfferDraw;

    /// <summary>Bit <c>i</c> set once slot <c>i</c> of the open offer has been bought.</summary>
    private int _shopSlotsPurchased;

    /// <summary>Refreshes spent at the currently open shop. Per visit, not per run.</summary>
    private int _shopRefreshesUsedThisVisit;

    /// <summary>The one constructor. Private; every value has already been checked by <see cref="Rehydrate"/>, the only caller.</summary>
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
        long bankedLegendXp,
        long bankedSoulShards,
        bool bossDefeated,
        int draftsSinceLegendaryOffered,
        int draftsWithoutAboveCommon,
        int draftsWithoutOwnedUpgrade,
        Loadout startingLoadout,
        int itemsAtOrAboveFloorBand,
        List<string> shrineBuffs,
        List<string> runBuffs,
        List<string> curses,
        Dictionary<string, int> consumables,
        Dictionary<int, int> fixedDice,
        int pendingFixedDieChoices,
        bool escapeRopeArmed,
        int freeDraftRerolls,
        ulong? shopOfferDraw,
        int shopSlotsPurchased,
        int shopRefreshesUsedThisVisit)
    {
        StartingLoadout = startingLoadout;
        _itemsAtOrAboveFloorBand = itemsAtOrAboveFloorBand;
        _draftsSinceLegendaryOffered = draftsSinceLegendaryOffered;
        _draftsWithoutAboveCommon = draftsWithoutAboveCommon;
        _draftsWithoutOwnedUpgrade = draftsWithoutOwnedUpgrade;
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
        _draftedPerksView = new DraftedPerks(new ReadOnlyDictionary<string, int>(ownedPerkTiers));
        _bankedLegendXp = bankedLegendXp;
        _bankedSoulShards = bankedSoulShards;
        _bossDefeated = bossDefeated;
        _shrineBuffs = shrineBuffs;
        _shrineBuffsView = new ReadOnlyCollection<string>(shrineBuffs);
        _runBuffs = runBuffs;
        _runBuffsView = new ReadOnlyCollection<string>(runBuffs);
        _curses = curses;
        _cursesView = new ReadOnlyCollection<string>(curses);
        _consumables = consumables;
        _consumablesView = new ReadOnlyDictionary<string, int>(consumables);
        _fixedDice = fixedDice;
        _fixedDiceView = new ReadOnlyDictionary<int, int>(fixedDice);
        _pendingFixedDieChoices = pendingFixedDieChoices;
        _escapeRopeArmed = escapeRopeArmed;
        _freeDraftRerolls = freeDraftRerolls;
        _shopOfferDraw = shopOfferDraw;
        _shopSlotsPurchased = shopSlotsPurchased;
        _shopRefreshesUsedThisVisit = shopRefreshesUsedThisVisit;
    }

    /// <summary>
    /// 🔒 What the hero was wearing when this run started — frozen for the run's whole life.
    /// </summary>
    /// <remarks>
    /// <para>
    /// `07` §4: equipping is free and unlimited <em>outside</em> a run, and the loadout <em>cannot</em>
    /// be changed during one — <em>"It is snapshotted at run start."</em> This field is that snapshot,
    /// and it is a field rather than a rule stated over the player because a rule cannot survive the
    /// player equipping something: read from <c>Player.Loadout</c> mid-run and the answer changes,
    /// however carefully the commands are gated. Read from here and it cannot.
    /// </para>
    /// <para>
    /// A <c>get</c>-only property with no mutator anywhere, deliberately. There is no
    /// <c>ChangeLoadout</c>, no <c>Reequip</c> and no setter, so "cannot be changed during a run" is
    /// enforced by the type's shape rather than by every future command remembering to check.
    /// </para>
    /// <para>
    /// ⚠️ It holds identities, not items — so an enhancement applied to an equipped item mid-run is
    /// worn immediately, and that is correct: `07` §4 freezes <em>which items are equipped</em>, and
    /// what an item IS lives in the stock. The forge is a meta screen and cannot be reached mid-run
    /// anyway.
    /// </para>
    /// </remarks>
    public Loadout StartingLoadout { get; }

    /// <summary>What <see cref="RunSnapshot.PendingTileKind"/> holds when no tile is pending.</summary>
    /// <remarks>
    /// -1 rather than a nullable: the tile-kind enum's members run 0..13 with no explicit values, so
    /// no legal kind can ever collide with it. A named constant so the sentinel is greppable.
    /// </remarks>
    private const int NoPendingTile = -1;

    /// <summary>What <see cref="_draftBattleKind"/> holds while <see cref="_draftPending"/> is false. -1 for the same reason as <see cref="NoPendingTile"/>.</summary>
    private const int NoDraftBattleKind = -1;

    /// <summary>The stage value for the boss node, which belongs to no stage.</summary>
    /// <remarks>
    /// Restated here rather than read off the board graph's own constant: <c>Model</c> sits below
    /// <c>Rules</c> in the internal layering, so this aggregate holds its own copy of the value it
    /// validates against. The graph's constant remains the definition.
    /// </remarks>
    private const int BossStage = 0;

    /// <summary>The aggregate root's identity.</summary>
    public RunId Id { get; }

    /// <summary>The player this run belongs to. A run is a child of its player; it does not exist on its own.</summary>
    public PlayerId PlayerId { get; }

    /// <summary>The committed <c>runSeed</c>. Derived once at <c>START_RUN</c> and never recomputed.</summary>
    /// <remarks>Get-only with no mutator anywhere: nothing in the game legitimately re-seeds a run in flight.</remarks>
    public ulong RunSeed { get; }

    /// <summary>The chapter being played.</summary>
    /// <remarks>
    /// No upper bound and no existence check, deliberately: only chapters 1-2 are authored so far, and
    /// hard-coding a ceiling would put a content bound in code and be a partial invariant wearing the
    /// real one's name. A self-expiring test mechanism catches the day the exemption outlives its
    /// milestone.
    /// </remarks>
    public int ChapterId { get; }

    /// <summary>The difficulty tier, and part of <see cref="RunSeed"/>'s derivation.</summary>
    public DifficultyTier Tier { get; }

    /// <summary>The instant the last command was applied to this run, which the sliding 48-hour run TTL is measured from.</summary>
    /// <remarks>
    /// Distinct from <c>Player.LastAppliedAtUtc</c>, which also advances on meta commands — sliding
    /// the run's expiry off that would keep a run alive because its owner opened the shop.
    /// </remarks>
    public DateTimeOffset LastAppliedAtUtc => _lastAppliedAtUtc;

    /// <summary>The node the run stands on — a board node's linear index, once the board has been generated.</summary>
    /// <remarks>
    /// <para>
    /// The board assigns every spine node's identity in increasing linear-index order before any fork
    /// branch is built, so a spine or boss node's identity and its linear index are the same number by
    /// construction. They diverge only while a run stands inside a fork branch, since a branch node
    /// shares its linear index with the spine node at the same forward distance from its junction —
    /// so this field is the node's actual graph identity, which happens to equal the linear index
    /// everywhere a wire reader would expect it to.
    /// </para>
    /// <para>
    /// Checked only against the trailhead floor: <c>Model</c> cannot hold a board graph to check
    /// itself against, so the real enforcement is structural — only the movement engine ever calls
    /// <see cref="MoveTo"/>, and only with a value it has itself just read off this run's own board.
    /// </para>
    /// </remarks>
    public int Position => _position;

    /// <summary>The hero's current hit points. Never negative, never above <see cref="MaxHp"/>.</summary>
    public int CurrentHp => _currentHp;

    /// <summary>The hero's maximum hit points for this run. Never below 1.</summary>
    /// <remarks>
    /// Stored rather than derived from the player's build: a resumed run must render its HP bar
    /// without recomputing the whole power calculation, and a shrine can raise it for the run alone.
    /// </remarks>
    public int MaxHp => _maxHp;

    /// <summary>The run's <c>GOLD</c> balance — the one run-scoped currency. Never negative.</summary>
    public long Gold => _wallet;

    /// <summary>Per-stream draw counters: stream name → next draw index. Read-only, sparse, ordinal.</summary>
    /// <remarks>
    /// <para>
    /// A frozen view: <see cref="CommitStreamPositions"/> replaces the map wholesale, so the object a
    /// caller holds never changes afterwards. <see cref="AdUses"/> is the opposite.
    /// </para>
    /// <para>
    /// The <c>combat</c> stream's position means something different from every other row's: it is
    /// consumed once per battle to derive that battle's seed, so it counts battles started (the next
    /// <c>battleIndex</c>), not combat draws — the draws inside a battle restart at 0 and are never
    /// persisted, which is what makes a revived battle replayable from draw 0 of its own stream.
    /// </remarks>
    public IReadOnlyDictionary<string, ulong> RngStreamPositions => _streamPositions;

    /// <summary>Per-run ad uses: placement id → uses so far. Read-only; empty is the normal state.</summary>
    /// <remarks>
    /// <para>
    /// A live view, unlike <see cref="RngStreamPositions"/>: the counts are mutated in place. Read it,
    /// do not hold it.
    /// </para>
    /// <para>
    /// Open string keys, not a type: the in-run placement ids are authored in tuning data, and the
    /// wrapper type they'd otherwise use is an <c>Application</c>-layer type <c>Core</c> may not name.
    /// </para>
    /// <para>
    /// No period anchor and no reset mutator: the run is the period, so there is nothing to reset.
    /// The revive placement also carries the once-per-run revive count, so there is no separate field.
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
    /// <remarks>A live view over the run's own map — see <see cref="_draftedPerksView"/>.</remarks>
    internal DraftedPerks DraftedPerks => _draftedPerksView;

    /// <summary>Legend XP banked so far this run. See <see cref="_bankedLegendXp"/>.</summary>
    internal long BankedLegendXp => _bankedLegendXp;

    /// <summary>Soul Shards banked so far this run. See <see cref="_bankedSoulShards"/>.</summary>
    internal long BankedSoulShards => _bankedSoulShards;

    /// <summary>Whether this run's Boss has been killed. See <see cref="_bossDefeated"/>.</summary>
    internal bool BossDefeated => _bossDefeated;

    /// <summary>Drafts picked from since one last offered a Legendary option.</summary>
    internal int DraftsSinceLegendaryOffered => _draftsSinceLegendaryOffered;

    /// <summary>Consecutive drafts picked from that offered nothing above Common.</summary>
    internal int DraftsWithoutAboveCommon => _draftsWithoutAboveCommon;

    /// <summary>Consecutive drafts picked from that offered no owned-perk upgrade.</summary>
    internal int DraftsWithoutOwnedUpgrade => _draftsWithoutOwnedUpgrade;

    /// <inheritdoc cref="_itemsAtOrAboveFloorBand"/>
    internal int ItemsAtOrAboveFloorBand => _itemsAtOrAboveFloorBand;

    /// <summary>Records that this run produced one more item at or above the floor's band.</summary>
    /// <remarks>
    /// A count rather than a flag, because the floor is authored as a threshold on how many such
    /// items a run produced and the document is free to move that threshold off one.
    /// </remarks>
    internal void CountItemAtOrAboveFloorBand() => _itemsAtOrAboveFloorBand++;

    /// <summary>Stores the three draft counters a resolution answered.</summary>
    /// <remarks>
    /// The one writer, and it takes values rather than deltas for the same reason the player's
    /// counter writer does: the two movements that matter are an advance and a reset, and a reset is
    /// badly described as a negative.
    /// </remarks>
    /// <param name="sinceLegendaryOffered">Drafts picked from since a Legendary was last offered. Never negative.</param>
    /// <param name="withoutAboveCommon">Consecutive drafts picked from with nothing above Common. Never negative.</param>
    /// <param name="withoutOwnedUpgrade">Consecutive drafts picked from with no owned upgrade. Never negative.</param>
    /// <exception cref="ArgumentOutOfRangeException">Any argument is negative.</exception>
    internal void SetDraftCounters(
        int sinceLegendaryOffered, int withoutAboveCommon, int withoutOwnedUpgrade)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sinceLegendaryOffered);
        ArgumentOutOfRangeException.ThrowIfNegative(withoutAboveCommon);
        ArgumentOutOfRangeException.ThrowIfNegative(withoutOwnedUpgrade);

        _draftsSinceLegendaryOffered = sinceLegendaryOffered;
        _draftsWithoutAboveCommon = withoutAboveCommon;
        _draftsWithoutOwnedUpgrade = withoutOwnedUpgrade;
    }

    /// <summary>The next draw index of one RNG stream, or zero for a registered stream this run has never drawn from.</summary>
    /// <param name="streamName">A row of the stream registry — one of <c>RngStreams.FixedNames</c>, or <c>minigame:{index}</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="streamName"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="streamName"/> is not in the stream registry.</exception>
    public ulong StreamPosition(string streamName)
    {
        // Null is checked here rather than inside RequireRegisteredStream because here the null is
        // genuinely the argument, while a null KEY inside CommitStreamPositions' map is not.
        ArgumentNullException.ThrowIfNull(streamName);
        RequireRegisteredStream(streamName, nameof(streamName));

        return _streamPositions.TryGetValue(streamName, out var position) ? position : 0UL;
    }

    /// <summary>How many times one in-run ad placement has been used in this run, or zero.</summary>
    /// <param name="placementId">The placement id, as authored in tuning data. Never blank.</param>
    /// <exception cref="ArgumentException"><paramref name="placementId"/> is blank.</exception>
    public long AdUseCount(string placementId)
    {
        RequirePlacementId(placementId, nameof(placementId));

        return _adUses.TryGetValue(placementId, out var uses) ? uses : 0L;
    }

    /// <summary>The balance of one run-scoped currency.</summary>
    /// <param name="currency">Must be <see cref="CurrencyId.GOLD"/>: exactly one currency is scoped to the run.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="currency"/> is anything but <c>GOLD</c>.
    /// </exception>
    public long BalanceOf(CurrencyId currency)
    {
        RequireRunCurrency(currency, nameof(currency));

        return _wallet;
    }

    /// <summary>The persisted shape of this aggregate, stamped with the current <see cref="SnapshotSchema.SchemaVersion"/>.</summary>
    /// <remarks>
    /// The ad-use dictionary is copied; the stream-position map is not — the latter is already
    /// replaced wholesale on every commit, so the object handed out here can never change afterwards.
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
        // null becomes "" on the way out — see _pendingEventCardId for why the two sides spell
        // "absent" differently.
        _pendingEventCardId ?? string.Empty,
        _phase,
        _draftPending,
        _draftBattleKind,
        _draftBattleStage,
        CopyOwnedPerkTiers(_ownedPerkTiers),
        _bankedLegendXp,
        _bankedSoulShards,
        _bossDefeated,
        _draftsSinceLegendaryOffered,
        _draftsWithoutAboveCommon,
        _draftsWithoutOwnedUpgrade,
        StartingLoadout.ToSnapshot(),
        _itemsAtOrAboveFloorBand,
        CopyIds(_shrineBuffs),
        CopyIds(_runBuffs),
        CopyIds(_curses),
        CopyConsumables(_consumables),
        CopyFixedDice(_fixedDice),
        _pendingFixedDieChoices,
        _escapeRopeArmed,
        _freeDraftRerolls,
        _shopOfferDraw,
        _shopSlotsPurchased,
        _shopRefreshesUsedThisVisit);

    /// <summary>
    /// A defensive copy of a run-buff list or consumable pouch — this
    /// aggregate mutates its own in place, so a snapshot handed out uncopied would keep changing
    /// after it was taken.
    /// </summary>
    /// <remarks>
    /// 🔒 Each short-circuits to a shared empty instance, on <see cref="CopyAdUses"/>'s precedent and
    /// for its reason: <see cref="RunSnapshot"/>'s synthesized <c>Equals</c> compares a collection
    /// member BY REFERENCE, so two snapshots of the same empty state would otherwise be unequal as
    /// records while encoding to identical canonical bytes. Safe to share because each is read-only
    /// and empty, so nothing can tell a shared instance from a private one.
    /// </remarks>
    private static IReadOnlyList<string> CopyIds(List<string> ids) =>
        ids.Count == 0 ? NoIds : ids.ToArray();

    /// <inheritdoc cref="CopyIds"/>
    private static IReadOnlyDictionary<int, int> CopyFixedDice(Dictionary<int, int> fixedDice) =>
        fixedDice.Count == 0 ? NoFixedDice : new Dictionary<int, int>(fixedDice);

    /// <inheritdoc cref="CopyIds"/>
    private static readonly IReadOnlyDictionary<int, int> NoFixedDice = new Dictionary<int, int>(0);

    /// <inheritdoc cref="CopyIds"/>
    private static IReadOnlyDictionary<string, int> CopyConsumables(Dictionary<string, int> consumables) =>
        consumables.Count == 0
            ? NoConsumables
            : new Dictionary<string, int>(consumables, StringComparer.Ordinal);

    /// <inheritdoc cref="CopyIds"/>
    private static readonly IReadOnlyList<string> NoIds = Array.Empty<string>();

    /// <inheritdoc cref="CopyIds"/>
    private static readonly IReadOnlyDictionary<string, int> NoConsumables =
        new Dictionary<string, int>(0, StringComparer.Ordinal);

    /// <summary>The one validated entry point for a persisted run: a corrupt row fails loudly at the seam.</summary>
    /// <param name="snapshot">The persisted row.</param>
    /// <returns>
    /// The rehydrated aggregate, or a failure listing every validation the row failed, not just the
    /// first.
    /// </returns>
    /// <remarks>
    /// An unknown <see cref="RunSnapshot.SchemaVersion"/> hard-fails first and alone, for the same
    /// reason <c>Player.Rehydrate</c> does. No <c>ContentSnapshot</c> parameter: nothing this
    /// snapshot carries has a content-derived bound today, and a parameter accepted and ignored would
    /// mislead every caller into thinking this validation consults the data set.
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
        RequireDraftBattle(snapshot, faults);
        var ownedPerkTiers = ReadOwnedPerkTiers(snapshot, faults);
        RequireBankedRewards(snapshot, faults);
        RequireDraftCounters(snapshot, faults);
        RequireFloorBandTally(snapshot, faults);
        var startingLoadout = ReadStartingLoadout(snapshot, faults);
        var shrineBuffs = ReadIdList(snapshot.ShrineBuffs, nameof(RunSnapshot.ShrineBuffs), allowDuplicates: true, faults);
        var runBuffs = ReadIdList(snapshot.RunBuffs, nameof(RunSnapshot.RunBuffs), allowDuplicates: true, faults);
        var curses = ReadIdList(snapshot.Curses, nameof(RunSnapshot.Curses), allowDuplicates: false, faults);
        var consumables = ReadConsumables(snapshot, faults);
        var fixedDice = ReadFixedDice(snapshot, faults);
        RequireGrantCounters(snapshot, faults);
        RequireShopVisit(snapshot, faults);

        // The `is null` arms are unreachable while `faults` is empty — every path that returns
        // null also adds a fault — but they are written as a pattern rather than as `!`
        // operators so the correlation is checked rather than asserted at the compiler.
        if (faults.Count > 0 || streams is null || adUses is null || resolvedMinigames is null ||
            ownedPerkTiers is null || startingLoadout is null || shrineBuffs is null ||
            runBuffs is null || curses is null || consumables is null || fixedDice is null)
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
            // "" becomes null on the way in, and a blank-but-not-empty string does too: both mean
            // "no card has been drawn".
            string.IsNullOrWhiteSpace(snapshot.PendingEventCardId) ? null : snapshot.PendingEventCardId,
            snapshot.Phase,
            snapshot.DraftPending,
            snapshot.DraftBattleKind,
            snapshot.DraftBattleStage,
            ownedPerkTiers,
            snapshot.BankedLegendXp,
            snapshot.BankedSoulShards,
            snapshot.BossDefeated,
            snapshot.DraftsSinceLegendaryOffered,
            snapshot.DraftsWithoutAboveCommon,
            snapshot.DraftsWithoutOwnedUpgrade,
            startingLoadout,
            snapshot.ItemsAtOrAboveFloorBand,
            shrineBuffs,
            runBuffs,
            curses,
            consumables,
            fixedDice,
            snapshot.PendingFixedDieChoices,
            snapshot.EscapeRopeArmed,
            snapshot.FreeDraftRerolls,
            snapshot.ShopOfferDraw,
            snapshot.ShopSlotsPurchased,
            snapshot.ShopRefreshesUsedThisVisit));
    }

    // ------------------------------------------------------ the tile-state readers

    /// <summary>
    /// Reads a list of content ids off the row: never null-holed, never blank, and — where the
    /// caller says so — never repeated.
    /// </summary>
    /// <param name="ids">The persisted list, or <c>null</c> for "none", which is not a fault.</param>
    /// <param name="field">The snapshot field, for the fault text.</param>
    /// <param name="allowDuplicates">
    /// <c>true</c> for the two buff lists, where `03` §7a.5 makes a repeat a second additive stack;
    /// <c>false</c> for the curse list, where `19` Part E gives curses no stacking at all, so a
    /// repeated id is a row that could only have been written by a broken rule.
    /// </param>
    /// <param name="faults">The accumulating fault list.</param>
    private static List<string>? ReadIdList(
        IReadOnlyList<string>? ids, string field, bool allowDuplicates, List<string> faults)
    {
        if (ids is null)
        {
            return new List<string>();
        }

        var read = new List<string>(ids.Count);
        var seen = allowDuplicates ? null : new HashSet<string>(StringComparer.Ordinal);
        var failed = false;

        for (var i = 0; i < ids.Count; i++)
        {
            var id = ids[i];

            if (string.IsNullOrWhiteSpace(id))
            {
                faults.Add(
                    field + "[" + Text(i) + "] is blank. Every entry is a content id the effect " +
                    "source looks a row up by, and a blank one names no row — silently contributing " +
                    "nothing while the count says the player has it.");
                failed = true;
                continue;
            }

            if (seen is not null && !seen.Add(id))
            {
                faults.Add(
                    field + " lists '" + id + "' twice. 19 Part E gives curses NO stacking, so a " +
                    "second copy is not a second stack — it is a row a rule could not have written.");
                failed = true;
                continue;
            }

            read.Add(id);
        }

        return failed ? null : read;
    }

    /// <summary>Reads the held consumables: ids non-blank, counts strictly positive.</summary>
    /// <remarks>
    /// A zero count is a FAULT rather than a silently-dropped entry: the aggregate removes an
    /// exhausted stack instead of storing a zero, so a persisted zero means something wrote the map
    /// another way — and two rows that both mean "none held" hashing differently is exactly what the
    /// canonical encoding must not permit.
    /// </remarks>
    private static Dictionary<string, int>? ReadConsumables(RunSnapshot snapshot, List<string> faults)
    {
        if (snapshot.Consumables is null)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var read = new Dictionary<string, int>(snapshot.Consumables.Count, StringComparer.Ordinal);
        var failed = false;

        foreach (var (id, count) in snapshot.Consumables)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                faults.Add(
                    nameof(RunSnapshot.Consumables) + " holds a blank id. A consumable is used by " +
                    "id, and a blank one can never be used — a stack the player owns and cannot spend.");
                failed = true;
                continue;
            }

            if (count <= 0)
            {
                faults.Add(
                    nameof(RunSnapshot.Consumables) + "['" + id + "'] is " + Text(count) +
                    ". A held stack is at least one: this aggregate REMOVES an exhausted stack " +
                    "rather than storing a zero, so two rows meaning 'none held' would otherwise " +
                    "encode to different bytes.");
                failed = true;
                continue;
            }

            read[id] = count;
        }

        return failed ? null : read;
    }

    /// <summary>Reads the fixed dice held: every key a number the die can show, every count positive.</summary>
    /// <remarks>
    /// 🔒 The pip bound is checked here and the count bound with it, for <c>ReadConsumables</c>'s
    /// reason: a zero count is a FAULT rather than a silently-dropped entry, because this aggregate
    /// removes an exhausted holding instead of storing a zero — so two rows both meaning "none held"
    /// would otherwise encode to different bytes.
    /// </remarks>
    private static Dictionary<int, int>? ReadFixedDice(RunSnapshot snapshot, List<string> faults)
    {
        if (snapshot.FixedDice is null)
        {
            return new Dictionary<int, int>();
        }

        var read = new Dictionary<int, int>(snapshot.FixedDice.Count);
        var failed = false;

        foreach (var (pips, count) in snapshot.FixedDice)
        {
            if (!Content.Dice.Die.IsPips(pips))
            {
                faults.Add(
                    nameof(RunSnapshot.FixedDice) + " holds a die showing " + Text(pips) +
                    ". 04 §1's die shows " + Text(Content.Dice.Die.MinPips) + ".." +
                    Text(Content.Dice.Die.MaxPips) + "; a fixed die outside that range would move the " +
                    "run a distance no roll could.");
                failed = true;
                continue;
            }

            if (count <= 0)
            {
                faults.Add(
                    nameof(RunSnapshot.FixedDice) + "[" + Text(pips) + "] is " + Text(count) +
                    ". A held count is at least one: this aggregate REMOVES an exhausted holding " +
                    "rather than storing a zero, so two rows meaning 'none held' would otherwise " +
                    "encode to different bytes.");
                failed = true;
                continue;
            }

            read[pips] = count;
        }

        return failed ? null : read;
    }

    /// <summary>The grant counter counts grants, so it is never negative.</summary>
    private static void RequireGrantCounters(RunSnapshot snapshot, List<string> faults)
    {
        if (snapshot.PendingFixedDieChoices < 0)
        {
            faults.Add(
                nameof(RunSnapshot.PendingFixedDieChoices) + " is " +
                Text(snapshot.PendingFixedDieChoices) +
                ". It counts fixed dice granted but not yet given a number, and a negative count " +
                "would owe the player a choice they could never take.");
        }

        if (snapshot.FreeDraftRerolls < 0)
        {
            faults.Add(
                nameof(RunSnapshot.FreeDraftRerolls) + " is " + Text(snapshot.FreeDraftRerolls) +
                ". It counts free draft rerolls held, and a negative holding is not a state a " +
                "purchase or a spend can produce.");
        }
    }

    /// <summary>
    /// The shop-visit trio is coherent: no purchases and no refreshes recorded against a shop that
    /// is not open, and neither counter negative.
    /// </summary>
    /// <remarks>
    /// The cross-field check is the one that matters. A purchase mask surviving the close of a shop
    /// would grey out slots of the NEXT shop the run visits — a tile that silently sells less the
    /// second time — and it is invisible to any per-field bound.
    /// </remarks>
    private static void RequireShopVisit(RunSnapshot snapshot, List<string> faults)
    {
        if (snapshot.ShopSlotsPurchased < 0)
        {
            faults.Add(
                nameof(RunSnapshot.ShopSlotsPurchased) + " is " + Text(snapshot.ShopSlotsPurchased) +
                ". It is a bitmask of bought slots, and no bit pattern this game writes is negative.");
        }

        if (snapshot.ShopRefreshesUsedThisVisit < 0)
        {
            faults.Add(
                nameof(RunSnapshot.ShopRefreshesUsedThisVisit) + " is " +
                Text(snapshot.ShopRefreshesUsedThisVisit) + ". A use count is never negative.");
        }

        if (snapshot.ShopOfferDraw is not null)
        {
            return;
        }

        if (snapshot.ShopSlotsPurchased != 0 || snapshot.ShopRefreshesUsedThisVisit != 0)
        {
            faults.Add(
                nameof(RunSnapshot.ShopOfferDraw) + " is null — no shop is open — but " +
                nameof(RunSnapshot.ShopSlotsPurchased) + " is " + Text(snapshot.ShopSlotsPurchased) +
                " and " + nameof(RunSnapshot.ShopRefreshesUsedThisVisit) + " is " +
                Text(snapshot.ShopRefreshesUsedThisVisit) + ". Both belong to the visit and are " +
                "cleared with it; carried forward they would grey out slots of the NEXT shop this " +
                "run walks into.");
        }
    }

    /// <summary>
    /// Reads the loadout the run started with. <c>null</c> is a <b>fault</b>, on the player
    /// inventory's precedent: a run whose starting loadout went missing is not a run fought naked,
    /// and reading it as empty would silently strip the hero's whole build for the rest of the run.
    /// </summary>
    private static Loadout? ReadStartingLoadout(RunSnapshot snapshot, List<string> faults)
    {
        if (snapshot.StartingLoadout is null)
        {
            faults.Add(
                nameof(RunSnapshot.StartingLoadout) + " is null. 07 §4 snapshots the loadout at run " +
                "start and forbids changing it during the run, so every run has one — an absent " +
                "snapshot read as an empty loadout would fight the rest of the run with no gear and " +
                "look exactly like a player who started one that way.");
            return null;
        }

        var loadout = Loadout.Rehydrate(snapshot.StartingLoadout);

        if (loadout.IsFailure)
        {
            faults.Add(nameof(RunSnapshot.StartingLoadout) + ": " + loadout.Error);
            return null;
        }

        return loadout.Value;
    }

    /// <summary>The three draft guarantee counters count drafts, so none of them is negative.</summary>
    /// <remarks>
    /// A negative counter would push the guarantee it protects further away the longer the run went
    /// on — the same fault the player-scoped counter map refuses at its own seam.
    /// </remarks>
    private static void RequireDraftCounters(RunSnapshot snapshot, List<string> faults)
    {
        RequireNonNegativeCounter(
            snapshot.DraftsSinceLegendaryOffered, nameof(RunSnapshot.DraftsSinceLegendaryOffered), faults);
        RequireNonNegativeCounter(
            snapshot.DraftsWithoutAboveCommon, nameof(RunSnapshot.DraftsWithoutAboveCommon), faults);
        RequireNonNegativeCounter(
            snapshot.DraftsWithoutOwnedUpgrade, nameof(RunSnapshot.DraftsWithoutOwnedUpgrade), faults);
    }

    /// <summary>The run's tally of items at or above the session floor's band counts items, so it is never negative.</summary>
    /// <remarks>
    /// A negative tally would read as a run that produced less than nothing worth keeping, and the
    /// floor would pay it a grant on top of the items it already dropped.
    /// </remarks>
    private static void RequireFloorBandTally(RunSnapshot snapshot, List<string> faults)
    {
        if (snapshot.ItemsAtOrAboveFloorBand < 0)
        {
            faults.Add(
                nameof(RunSnapshot.ItemsAtOrAboveFloorBand) + " is " +
                Text(snapshot.ItemsAtOrAboveFloorBand) + ". It counts the items this run produced at " +
                "or above the session floor's band, and a count below zero is not a state a run " +
                "reaches by producing anything.");
        }
    }

    private static void RequireNonNegativeCounter(int value, string field, List<string> faults)
    {
        if (value < 0)
        {
            faults.Add(
                field + " is " + Text(value) + ". A draft guarantee counter counts drafts since it " +
                "last fired, and a negative one moves its guarantee further away the longer the run " +
                "goes on.");
        }
    }

    /// <summary>The two banked-reward pools are never negative.</summary>
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

    /// <summary>Moves the run's <c>GOLD</c> and produces the attributing <c>CurrencyChanged</c>. The one place <c>_wallet</c> is written outside the constructor.</summary>
    /// <param name="currency">Must be <see cref="CurrencyId.GOLD"/>.</param>
    /// <param name="delta">Signed: positive is income, negative is a spend. Zero is permitted.</param>
    /// <param name="reason">Why it moved — a stable <c>lower_snake_case</c> token. Never blank.</param>
    /// <returns>The event, with <see cref="DomainEvent.Sequence"/> left at <c>DomainEvent.UnstampedSequence</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="currency"/> is not run-scoped, or the movement would overflow.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank.</exception>
    /// <exception cref="InvalidOperationException">The movement would take the balance negative.</exception>
    internal CurrencyChanged MoveCurrency(CurrencyId currency, long delta, string reason)
    {
        RequireRunCurrency(currency, nameof(currency));

        var balance = _wallet;

        // Checked: unchecked overflow would wrap a large grant to a negative balance silently.
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

        // Built before the write: CurrencyChanged refuses a blank Reason in its own initialiser, so
        // constructing it after the write would leave the balance moved with no attribution.
        var change = new CurrencyChanged(DomainEvent.UnstampedSequence, currency, delta, reason);

        _wallet = next;

        return change;
    }

    /// <summary>Records the node index the run has moved to.</summary>
    /// <param name="position">The new linear node index. Never below <see cref="TrailheadPosition"/>.</param>
    /// <remarks>
    /// The trailhead floor is the whole check; there is no monotonicity guard either, even though
    /// movement is always forward. Which index may follow which is a property of the board graph, and
    /// this aggregate holds no graph — the movement engine is the only caller and only ever supplies a
    /// value it has itself just read off the run's own board.
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

    /// <summary>The one HP seam: writes the current and maximum hit points a rule computed, in one call.</summary>
    /// <param name="current">The hero's hit points after the rule. Never negative, never above <paramref name="max"/>.</param>
    /// <param name="max">The run's maximum hit points after the rule. Never below 1.</param>
    /// <remarks>
    /// Takes both halves because that is the invariant: a caller that raised <see cref="MaxHp"/> and
    /// forgot <see cref="CurrentHp"/> — or the reverse — would leave the pair in a state neither
    /// individual write is illegal in. Overheal is clamped by the rule that computes it, not accepted
    /// and trimmed here.
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

    /// <summary>Records that a command has been applied to this run at <paramref name="nowUtc"/>, which the sliding 48-hour TTL is measured from.</summary>
    /// <param name="nowUtc">
    /// <c>GameContext.NowUtc</c>. Must carry a zero offset and must not precede
    /// <see cref="LastAppliedAtUtc"/>.
    /// </param>
    /// <remarks>Equal is allowed, strictly-earlier is not — the same rule as <c>Player.MarkApplied</c>.</remarks>
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

    /// <summary>Registers and advances one in-run ad placement's count. The placement comes into existence on its first use.</summary>
    /// <param name="placementId">The placement id as authored in tuning data. Never blank; deliberately an open string, not a closed type.</param>
    /// <param name="amount">How much to add. Never negative — a use counter counts, it does not settle.</param>
    /// <remarks>The cap is not enforced here — that belongs to the handler that reads the tuning data; this aggregate only holds the count.</remarks>
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

    /// <summary>Whether a minigame has already been resolved at <paramref name="position"/> this run.</summary>
    internal bool HasResolvedMinigameAt(int position) => _resolvedMinigames.ContainsKey(position);

    /// <summary>Whether this run has arrived at a tile it has not yet resolved.</summary>
    /// <remarks>The gate every tile-resolution command checks first, answering <c>ILLEGAL_STATE</c> when there is nothing pending.</remarks>
    internal bool HasPendingTile => _pendingTileKind != NoPendingTile;

    /// <summary>Which tile kind is pending, as its underlying integer.</summary>
    /// <remarks>
    /// An <c>int</c> rather than the tile-kind enum for a layering reason: <c>Model</c> may not name
    /// the tile vocabulary, which lives under <c>Rules</c>. This aggregate can therefore only check
    /// that the value is not below the sentinel; an out-of-vocabulary value survives here and is
    /// caught one layer up, by the handler's switch.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No tile is pending.</exception>
    internal int PendingTileKindValue =>
        HasPendingTile ? _pendingTileKind : throw NothingPending(nameof(PendingTileKindValue));

    /// <summary>The pending tile's linear node index.</summary>
    /// <exception cref="InvalidOperationException">No tile is pending.</exception>
    internal int PendingTileLinearIndex =>
        HasPendingTile ? _pendingTileLinearIndex : throw NothingPending(nameof(PendingTileLinearIndex));

    /// <summary>The pending tile's stage, or <c>BoardGraph.BossStage</c>.</summary>
    /// <exception cref="InvalidOperationException">No tile is pending.</exception>
    internal int PendingTileStage =>
        HasPendingTile ? _pendingTileStage : throw NothingPending(nameof(PendingTileStage));

    /// <summary>The event card a pending <c>TILE_EVENT</c> has drawn, or <c>null</c>.</summary>
    /// <remarks>
    /// Answers <c>null</c> rather than throwing when nothing is pending, unlike the three getters
    /// above: "no card has been drawn" is a real answer a legality check needs before it even knows
    /// whether a tile is pending.
    /// </remarks>
    internal string? PendingEventCardId => _pendingEventCardId;

    /// <summary>Records that the run has arrived at a tile, which is what makes it resolvable.</summary>
    /// <param name="kind">The tile kind landed on, as its underlying integer. Never negative.</param>
    /// <param name="linearIndex">The tile's linear node index. Checked against a floor of 0 and no ceiling, for the reason <see cref="Position"/> is.</param>
    /// <param name="stage">The tile's stage — 1, 2, 3, or <see cref="BossStage"/>.</param>
    /// <remarks>
    /// A second arrival with one already pending is a defect, not a rejection: the caller decides a
    /// run may move, and moving onto a second tile while the first is unresolved is a miswired engine.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="kind"/> is negative, <paramref name="linearIndex"/> is negative, or
    /// <paramref name="stage"/> is not one of the four.
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

    /// <summary>Records which event card the pending <c>TILE_EVENT</c> drew, so a resubmission cannot draw a new one.</summary>
    /// <param name="cardId">The drawn card's id. Never blank.</param>
    /// <remarks>
    /// Both refusals are defects, already checked by the handler as a <c>RejectionReason</c> before
    /// reaching here. It does not check the pending tile is specifically an event tile — that would
    /// mean naming the tile vocabulary <c>Model</c> may not reach.
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

    /// <summary>Clears the pending tile once it has resolved. Idempotent.</summary>
    /// <remarks>
    /// Safe to call with nothing pending: this method promises a postcondition ("no tile is
    /// pending"), not a transition, and the postcondition is already true when nothing is pending.
    /// The opposite call from <see cref="ArriveAtTile"/>, which refuses a duplicate because arriving
    /// twice destroys information; clearing twice destroys none.
    /// </remarks>
    internal void ClearPendingTile()
    {
        _pendingTileKind = NoPendingTile;

        // Reset to 0 rather than left where they were: RunSnapshot is hashed whole, so two runs that
        // both have no pending tile must produce the same bytes for these slots.
        _pendingTileLinearIndex = 0;
        _pendingTileStage = 0;
        _pendingEventCardId = null;
    }

    private static InvalidOperationException NothingPending(string member) =>
        new("Run." + member + " describes the tile this run has arrived at and not yet resolved, " +
            "and this run has no pending tile. Ask Run.HasPendingTile first — it is the gate every " +
            "tile-resolution handler checks before anything else, and answering a default here " +
            "would let a rule resolve a tile the run is not standing on.");

    /// <summary>Records that <paramref name="minigameId"/> was resolved at <paramref name="position"/>, closing the legality gate for that tile.</summary>
    /// <param name="position">The run's node index at resolution — see <see cref="_resolvedMinigames"/>.</param>
    /// <param name="minigameId">The minigame id that resolved.</param>
    /// <remarks>A defect, not a rejection, on a duplicate: the handler checks and refuses it as <c>ILLEGAL_STATE</c> before this seam is ever reached.</remarks>
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

    /// <summary>Pauses movement at a junction, waiting for <c>CHOOSE_FORK</c>. Called by the movement engine instead of finishing the move.</summary>
    /// <param name="pending">The junction and the movement still unspent once it is left.</param>
    /// <remarks>A defect, not a rejection, on a run that already has one pending: a run cannot be mid-move at two junctions at once.</remarks>
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

    /// <summary>Clears a resolved <see cref="PendingFork"/>, once <c>CHOOSE_FORK</c> has taken the chosen edge and finished the interrupted movement.</summary>
    /// <remarks>A defect, not a rejection, on a run with nothing pending — refused earlier by the caller's own legality check.</remarks>
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

    /// <summary>Opens a battle: <see cref="Phase"/> moves from <see cref="RunPhase.InProgress"/> to <see cref="RunPhase.BattlePending"/>.</summary>
    /// <remarks>
    /// Called after the caller has itself checked the pending tile is a battle kind — this seam
    /// cannot make that check, since <c>Model</c> may not name the tile vocabulary, so it only
    /// enforces what it can see. A defect, not a rejection, on either failure.
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

    /// <summary>Closes a battle: <see cref="Phase"/> moves back to <see cref="RunPhase.InProgress"/>. Does not clear the pending tile.</summary>
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

    /// <summary>Records that a perk draft is waiting. A later command clears it once the draft resolves.</summary>
    /// <remarks>
    /// Two callers, and they are different events: <c>Handlers.ConfirmBattleResult</c> opens the
    /// draft `06` §1 states, after a won battle; <c>Handlers.StartRun</c> opens the run's first one,
    /// before anything has happened. The pair is why the parameters are documented as what the draft
    /// DRAWS AGAINST rather than as a record of a battle — the opening draft passes a tile kind that
    /// is not a battle at all, and the only question anything asks of the kind is whether it is Elite
    /// or Boss.
    /// </remarks>
    /// <param name="battleKind">The tile kind the draft draws its band against. Never negative.</param>
    /// <param name="battleStage">The stage the draft belongs to — 1, 2, 3, or <see cref="BossStage"/>.</param>
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
                "A tile kind is a non-negative 03 §2 value. The post-battle caller reads this off " +
                "PendingTileKindValue before ClearPendingTile wipes it — see PendingTileKindValue's " +
                "remarks for why this aggregate cannot check which kind it is — and the opening " +
                "draft passes Empty, which is a real kind and not a battle.");
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
                "This run already has a draft pending. Neither caller can reach this: a run cannot " +
                "open a second battle while DraftPending is set, because GameRules.Apply refuses " +
                "every command but the three draft ones, and StartRun opens the draft on a run it " +
                "has just built. So calling this twice is a miswired caller.");
        }

        _draftPending = true;
        _draftBattleKind = battleKind;
        _draftBattleStage = battleStage;
    }

    /// <summary>Clears the draft-pending hook. Idempotent, for the reason <see cref="ClearPendingTile"/> is.</summary>
    internal void ClearDraftPending()
    {
        _draftPending = false;

        // Reset for the same determinism reason ClearPendingTile resets its own fields: two runs with
        // no draft pending must hash identically.
        _draftBattleKind = NoDraftBattleKind;
        _draftBattleStage = 0;
    }

    /// <summary>Grants or upgrades a drafted perk: a fresh grant at Tier I, or an upgrade to <paramref name="newTier"/> for a perk owned one tier below it.</summary>
    /// <param name="perkId">The perk id. Never blank.</param>
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

    /// <summary>The "nothing pending" refusal for the draft hook, on <see cref="NothingPending"/>'s pattern.</summary>
    private static InvalidOperationException NoDraftPending(string member) =>
        new("Run." + member + " describes the battle a pending perk draft was opened by, and this " +
            "run has no draft pending. Ask Run.DraftPending first.");

    /// <summary>Banks Legend XP and/or Soul Shards a kill (or a Victory/first-clear bonus) just earned.</summary>
    /// <remarks>
    /// Unlike <see cref="MoveCurrency"/>, not a currency movement — nothing has reached <c>Player</c>'s
    /// wallet yet — so it emits no <c>CurrencyChanged</c>; the real movement happens once at run end.
    /// </remarks>
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

        // Both sums computed into locals before either field is written, so a second-sum overflow
        // can never leave _bankedLegendXp written while this call still throws.
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

    /// <summary>Records that this run's Boss has been killed.</summary>
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

    /// <summary>Closes the run: <see cref="Phase"/> moves to the terminal <see cref="RunPhase.Ended"/>. Called after the run's final payout has been computed and paid.</summary>
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


    // ---------------------------------------------------------- the tile-state members
    //
    // Everything a board tile leaves behind on the run: the two additive buff lists, the curse set,
    // the run-scoped die, the consumable pouch, the two grant counters and the open shop's visit.
    // Readers are public because a client mirrors them onto a screen; every mutator is internal, so
    // GameRules.Apply stays the only public way any of it changes.

    /// <summary>The shrine buffs taken this run, in the order taken. A repeat is a second additive stack.</summary>
    public IReadOnlyList<string> ShrineBuffs => _shrineBuffsView;

    /// <summary>The run buffs bought this run, in the order bought. Additive, like <see cref="ShrineBuffs"/>.</summary>
    public IReadOnlyList<string> RunBuffs => _runBuffsView;

    /// <summary>The curses active on this run. Never holds one id twice — `19` Part E gives curses no stacking.</summary>
    public IReadOnlyList<string> Curses => _cursesView;

    /// <summary>Held consumables: id → count. Never holds a zero.</summary>
    public IReadOnlyDictionary<string, int> Consumables => _consumablesView;

    /// <summary>The fixed dice this run holds: pips → count. Never holds a zero.</summary>
    /// <remarks>Public because the Board screen draws them — the player picks which one to spend.</remarks>
    public IReadOnlyDictionary<int, int> FixedDice => _fixedDiceView;

    /// <summary>Fixed dice granted but not yet given a number. See <see cref="_pendingFixedDieChoices"/>.</summary>
    public int PendingFixedDieChoices => _pendingFixedDieChoices;

    /// <summary>Whether an Escape Rope is armed and waiting to fire on the next landing.</summary>
    public bool EscapeRopeArmed => _escapeRopeArmed;

    /// <summary>Free perk-draft rerolls held.</summary>
    internal int FreeDraftRerolls => _freeDraftRerolls;

    /// <summary>The <c>shop</c> stream position the open shop's offer was drawn at, or <c>null</c> when none is open.</summary>
    internal ulong? ShopOfferDraw => _shopOfferDraw;

    /// <summary>Whether a shop is open on this run — i.e. an offer has been drawn and not yet left.</summary>
    internal bool HasOpenShop => _shopOfferDraw is not null;

    /// <summary>Refreshes spent at the currently open shop.</summary>
    internal int ShopRefreshesUsedThisVisit => _shopRefreshesUsedThisVisit;

    /// <summary>How many of the given consumable this run holds. Zero for one it holds none of.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="consumableId"/> is null.</exception>
    public int ConsumableCount(string consumableId)
    {
        ArgumentNullException.ThrowIfNull(consumableId);

        return _consumables.GetValueOrDefault(consumableId);
    }

    /// <summary>How many consumables this run holds in total — what `03` §7.1's held cap is counted against.</summary>
    internal int HeldConsumableCount
    {
        get
        {
            var held = 0;

            foreach (var count in _consumables.Values)
            {
                held += count;
            }

            return held;
        }
    }

    /// <summary>Whether the given curse is active on this run.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="curseId"/> is null.</exception>
    public bool HasCurse(string curseId)
    {
        ArgumentNullException.ThrowIfNull(curseId);

        return _curses.Contains(curseId, StringComparer.Ordinal);
    }

    /// <summary>Whether slot <paramref name="slotIndex"/> of the open shop's offer has already been bought.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slotIndex"/> is negative or above 30.</exception>
    internal bool IsShopSlotPurchased(int slotIndex) =>
        (_shopSlotsPurchased & ShopSlotBit(slotIndex)) != 0;

    /// <summary>Records a shrine buff taken. Appends: a repeat is a second additive stack, not a no-op.</summary>
    /// <exception cref="ArgumentException"><paramref name="buffId"/> is blank.</exception>
    internal void AddShrineBuff(string buffId)
    {
        RequireId(buffId, nameof(buffId), "a shrine buff");

        _shrineBuffs.Add(buffId);
    }

    /// <summary>Records a run buff bought. Appends, for <see cref="AddShrineBuff"/>'s reason.</summary>
    /// <exception cref="ArgumentException"><paramref name="buffId"/> is blank.</exception>
    internal void AddRunBuff(string buffId)
    {
        RequireId(buffId, nameof(buffId), "a run buff");

        _runBuffs.Add(buffId);
    }

    /// <summary>
    /// Applies a curse, unless the run already carries it.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the curse was added, <c>false</c> when the run already had it — an answer
    /// rather than a throw, because a second draw of a curse the player already carries is an
    /// ordinary outcome of the tile's own random pool, not a defect.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="curseId"/> is blank.</exception>
    internal bool ApplyCurse(string curseId)
    {
        RequireId(curseId, nameof(curseId), "a curse");

        if (HasCurse(curseId))
        {
            return false;
        }

        _curses.Add(curseId);

        return true;
    }

    /// <summary>Removes a curse — the Shrine's Cleanse (`03` §7a.5) and `AD_SKIP_CURSE`.</summary>
    /// <returns><c>true</c> when one was removed, <c>false</c> when the run did not carry it.</returns>
    /// <exception cref="ArgumentException"><paramref name="curseId"/> is blank.</exception>
    internal bool CleanseCurse(string curseId)
    {
        RequireId(curseId, nameof(curseId), "a curse");

        return _curses.Remove(curseId);
    }

    /// <summary>Adds held consumables to the pouch.</summary>
    /// <param name="consumableId">The consumable id. Never blank.</param>
    /// <param name="count">How many to add. Strictly positive.</param>
    /// <remarks>
    /// The `03` §7.1 held cap of 4 is NOT enforced here, deliberately: the cap is a tunable read from
    /// content, and this aggregate holds no content. The rule that greys out a purchase which would
    /// exceed it lives with the handler that reads the number — this seam enforces only what it can
    /// see, exactly as <see cref="SetHitPoints"/> does with overheal.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="consumableId"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is not positive.</exception>
    internal void AddConsumable(string consumableId, int count)
    {
        RequireId(consumableId, nameof(consumableId), "a consumable");

        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count), count, "Adding zero or fewer consumables is not an addition.");
        }

        _consumables[consumableId] = _consumables.GetValueOrDefault(consumableId) + count;
    }

    /// <summary>Spends one held consumable.</summary>
    /// <returns>
    /// <c>true</c> when one was spent, <c>false</c> when the run holds none — an answer rather than a
    /// throw, since "you do not have one" is a player request the handler refuses, not a defect.
    /// </returns>
    /// <remarks>The last one removes the entry rather than leaving a zero — see <c>ReadConsumables</c> for why.</remarks>
    /// <exception cref="ArgumentException"><paramref name="consumableId"/> is blank.</exception>
    internal bool ConsumeOne(string consumableId)
    {
        RequireId(consumableId, nameof(consumableId), "a consumable");

        if (!_consumables.TryGetValue(consumableId, out var held) || held <= 0)
        {
            return false;
        }

        if (held == 1)
        {
            _consumables.Remove(consumableId);
        }
        else
        {
            _consumables[consumableId] = held - 1;
        }

        return true;
    }

    /// <summary>Records that the run has been granted a fixed die it has not yet numbered.</summary>
    /// <param name="count">How many choices to owe. Strictly positive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is not positive.</exception>
    internal void GrantFixedDieChoices(int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count), count, "Owing zero or fewer choices is not a grant.");
        }

        _pendingFixedDieChoices += count;
    }

    /// <summary>Answers one owed choice by adding a fixed die showing <paramref name="pips"/>.</summary>
    /// <returns>
    /// <c>true</c> when a choice was owed and taken, <c>false</c> when none was — an answer rather
    /// than a throw, on <see cref="ConsumeOne"/>'s precedent: "nothing owes you one" is a player
    /// request the handler refuses, not a defect.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pips"/> is not a number the die can show.</exception>
    internal bool TakeFixedDieChoice(int pips)
    {
        RequirePips(pips);

        if (_pendingFixedDieChoices <= 0)
        {
            return false;
        }

        _pendingFixedDieChoices--;
        _fixedDice[pips] = _fixedDice.GetValueOrDefault(pips) + 1;

        return true;
    }

    /// <summary>Spends one held fixed die showing <paramref name="pips"/>.</summary>
    /// <returns><c>true</c> when one was spent, <c>false</c> when the run holds none of that number.</returns>
    /// <remarks>The last one removes the entry rather than leaving a zero — see <c>ReadFixedDice</c> for why.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pips"/> is not a number the die can show.</exception>
    internal bool SpendFixedDie(int pips)
    {
        RequirePips(pips);

        if (!_fixedDice.TryGetValue(pips, out var held) || held <= 0)
        {
            return false;
        }

        if (held == 1)
        {
            _fixedDice.Remove(pips);
        }
        else
        {
            _fixedDice[pips] = held - 1;
        }

        return true;
    }

    /// <summary>The pip bound both fixed-die seams share.</summary>
    private static void RequirePips(int pips)
    {
        if (!Content.Dice.Die.IsPips(pips))
        {
            throw new ArgumentOutOfRangeException(
                nameof(pips), pips,
                "04 §1's die shows " + Text(Content.Dice.Die.MinPips) + ".." +
                Text(Content.Dice.Die.MaxPips) + " pips.");
        }
    }

    /// <summary>Arms the Escape Rope. Idempotent: only one may ever be armed.</summary>
    internal void ArmEscapeRope() => _escapeRopeArmed = true;

    /// <summary>Clears the armed Escape Rope — it has fired, or the run has left the state it was armed for.</summary>
    internal void DisarmEscapeRope() => _escapeRopeArmed = false;

    /// <summary>Grants free perk-draft rerolls — the Draft Token consumable's instant effect.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rerolls"/> is not positive.</exception>
    internal void GrantFreeDraftRerolls(int rerolls)
    {
        if (rerolls <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rerolls), rerolls, "Granting zero or fewer rerolls is not a grant.");
        }

        _freeDraftRerolls += rerolls;
    }

    /// <summary>Spends one free perk-draft reroll.</summary>
    /// <returns><c>true</c> when one was spent, <c>false</c> when none was held.</returns>
    internal bool SpendFreeDraftReroll()
    {
        if (_freeDraftRerolls <= 0)
        {
            return false;
        }

        _freeDraftRerolls--;

        return true;
    }

    /// <summary>
    /// Opens a shop visit at the given <c>shop</c> stream position, or restocks the open one.
    /// </summary>
    /// <param name="offerDraw">The stream position the offer is drawn at.</param>
    /// <param name="countsAsRefresh">
    /// <c>true</c> when this is a restock of the shop already open, <c>false</c> when the run has
    /// just walked in. The two are one method because they differ in exactly one thing — whether the
    /// visit's refresh counter moves — and splitting them produced two callers that both had to
    /// remember to clear the purchase mask.
    /// </param>
    /// <remarks>
    /// The purchase mask is cleared either way: the offer behind it is gone, so a surviving bit
    /// would grey out an unrelated slot of the new one.
    /// </remarks>
    internal void StockShop(ulong offerDraw, bool countsAsRefresh)
    {
        _shopOfferDraw = offerDraw;
        _shopSlotsPurchased = 0;

        if (countsAsRefresh)
        {
            _shopRefreshesUsedThisVisit++;
        }
        else
        {
            _shopRefreshesUsedThisVisit = 0;
        }
    }

    /// <summary>Records that slot <paramref name="slotIndex"/> of the open offer has been bought.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slotIndex"/> is negative or above 30.</exception>
    /// <exception cref="InvalidOperationException">No shop is open.</exception>
    internal void MarkShopSlotPurchased(int slotIndex)
    {
        if (_shopOfferDraw is null)
        {
            throw new InvalidOperationException(
                "This run has no open shop to buy a slot of. Handlers.ShopBuy's own legality check " +
                "is what refuses SHOP_BUY away from a shop, as a RejectionReason, before this seam " +
                "is reached.");
        }

        _shopSlotsPurchased |= ShopSlotBit(slotIndex);
    }

    /// <summary>Closes the shop visit: the offer, the purchase mask and the refresh count go together.</summary>
    /// <remarks>Idempotent, on <see cref="ClearPendingTile"/>'s argument — it promises a postcondition, not a transition.</remarks>
    internal void CloseShop()
    {
        _shopOfferDraw = null;
        _shopSlotsPurchased = 0;
        _shopRefreshesUsedThisVisit = 0;
    }

    /// <summary>The purchase-mask bit for one slot index.</summary>
    /// <remarks>
    /// Bounded at 30 rather than at the shop's authored 4: the slot count is a tunable this
    /// aggregate cannot read, and what it must refuse is only an index that would shift the sign bit
    /// off the end of an <c>int</c> and set an unrelated slot — or none at all.
    /// </remarks>
    private static int ShopSlotBit(int slotIndex)
    {
        if (slotIndex is < 0 or > MaxShopSlotIndex)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slotIndex), slotIndex,
                "A shop slot index is 0.." + Text(MaxShopSlotIndex) + " here. 03 §7 authors four " +
                "slots; this bound is only what the purchase bitmask can physically hold, since the " +
                "authored count is a tunable Model cannot read.");
        }

        return 1 << slotIndex;
    }

    /// <summary>The highest slot index the purchase bitmask can hold without touching an int's sign bit.</summary>
    private const int MaxShopSlotIndex = 30;

    /// <summary>The shared blank-id refusal for every content id this aggregate stores.</summary>
    private static void RequireId(string id, string parameterName, string what)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "A blank id names " + what + " nothing can look up. Storing it would give the player " +
                "a holding that contributes nothing and cannot be removed by name.",
                parameterName);
        }
    }

    /// <summary>Applies a Stage Gate: heals to the caller-computed hit points.</summary>
    /// <param name="healedCurrentHp">The hero's hit points after the Stage Gate heal — already computed and clamped by the caller.</param>
    /// <remarks>
    /// ⚠️ The heal is all a gate does to the run now. It used to refresh the stage's reroll charges
    /// and re-anchor the Fair-Dice bag as well; both are gone with the reroll and the weighted draw,
    /// and nothing was left in their place — an ordinary die needs no per-stage reset.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="healedCurrentHp"/> is negative or above <see cref="MaxHp"/>.</exception>
    internal void ApplyStageGate(int healedCurrentHp)
    {
        // Reuses SetHitPoints rather than writing _currentHp directly: one seam validates the pair.
        SetHitPoints(healedCurrentHp, _maxHp);
    }

    /// <summary>The one seam that writes the per-stream draw counters: it replaces the whole map, and refuses a map that is not a superset of the one already committed.</summary>
    /// <param name="positions">
    /// The scope's final positions for every stream this run has ever drawn from, plus any it has
    /// newly opened. Every key must be a row of the stream registry.
    /// </param>
    /// <remarks>
    /// <para>
    /// Refuses a partial map: every key already committed must be present, since a dropped key would
    /// silently reset that stream to 0 and the next draw from it would repeat an already-played
    /// sequence.
    /// </para>
    /// <para>
    /// Monotone or throw: a value below the committed one is a determinism defect, not a request to
    /// refuse, so it throws rather than producing a <c>RejectionReason</c>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="positions"/> is null.</exception>
    /// <exception cref="ArgumentException">A key is not a row of the stream registry.</exception>
    /// <exception cref="InvalidOperationException">
    /// A committed stream is missing from <paramref name="positions"/>, or its position moved
    /// backwards.
    /// </exception>
    internal void CommitStreamPositions(IReadOnlyDictionary<string, ulong> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        // Copied into an ORDINAL dictionary rather than adopted: the caller's map may use any
        // comparer, and under OrdinalIgnoreCase "DICE" IS "dice" — a run that adopted it would answer
        // for a stream the registry does not have. CanonicalStateWriter orders keys ordinally too.
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

    /// <summary>The empty stream map every run that has drawn nothing shares, and the empty ad-use map every snapshot of a run with no impressions shares.</summary>
    /// <remarks>Safe to share: read-only and empty, so nothing can distinguish a shared instance from a private one.</remarks>
    private static readonly ReadOnlyDictionary<string, ulong> NoStreamPositions =
        new(new Dictionary<string, ulong>(0, StringComparer.Ordinal));

    /// <inheritdoc cref="NoStreamPositions"/>
    private static readonly ReadOnlyDictionary<string, long> NoAdUses =
        new(new Dictionary<string, long>(0, StringComparer.Ordinal));

    /// <summary>An ordinal copy of the ad counts, so no caller shares the aggregate's dictionary.</summary>
    /// <remarks>Short-circuits on empty, the normal state — this runs once per <c>stateHash</c>.</remarks>
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

    /// <summary><c>GOLD</c> is the one run-scoped currency. Names the aggregate that does hold a currency rather than answering a zero about the wrong one.</summary>
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

    /// <summary>The same registry predicate <c>DeterministicRng</c>'s constructor uses: a name that cannot be drawn from cannot be read or persisted either.</summary>
    private static void RequireRegisteredStream(string? streamName, string parameterName)
    {
        if (RngStreams.IsRegistered(streamName))
        {
            return;
        }

        throw new ArgumentException(
            "'" + (streamName ?? "null") + "' is not a row of the 14 §8.1 stream registry, which is " +
            "the fixed names (" + string.Join(", ", RngStreams.FixedNames) + ") plus " +
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
        // default(RunId)/default(PlayerId) run no constructor, so their Value is null rather than
        // validated — Rehydrate is the seam that has to catch it.
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

        // Only when the maximum is itself valid, so ONE defect produces ONE fault.
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

        // Copied into an ORDINAL dictionary rather than kept — CanonicalStateWriter orders string
        // keys ordinally.
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
    /// <see cref="RunSnapshot.PendingForkJunctionPosition"/> and
    /// <see cref="RunSnapshot.PendingForkRemainingSteps"/> are one fact stored as a pair: both null,
    /// or both present and in range.
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

    /// <summary>Validates the four pending-tile fields as one fact, because that is what they are.</summary>
    /// <remarks>The checks compare the index, stage and card id against a pending tile only when the kind is itself legal, so one defect produces one fault.</remarks>
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
            // With no tile pending the other three carry no meaning, and ClearPendingTile zeroes them
            // so that one logical state has one encoding. A row that left them populated would hash
            // differently from an identical run, so it is refused rather than normalised on the way in.
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

        // Whether a stored card id belongs specifically to an EVENT tile is NOT checked here, for the
        // same layering reason the kind's upper bound is not — EventChoose checks the pairing.
    }

    /// <summary><see cref="RunSnapshot.Phase"/> must be a defined <see cref="RunPhase"/>.</summary>
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

    /// <summary>
    /// <see cref="RunSnapshot.DraftBattleKind"/>/<see cref="RunSnapshot.DraftBattleStage"/> are
    /// meaningless while <see cref="RunSnapshot.DraftPending"/> is false, and must then stand at
    /// their reset values.
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
        // Unlike AdUses/ResolvedMinigames, null IS a legitimate empty answer here rather than a
        // fault: OwnedPerkTiers is a trailing defaulted positional field added after RunSnapshot
        // already existed, so every construction that predates it still compiles.
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

    /// <summary>Renders a value with <see cref="CultureInfo.InvariantCulture"/>, so messages read the same on every host.</summary>
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
