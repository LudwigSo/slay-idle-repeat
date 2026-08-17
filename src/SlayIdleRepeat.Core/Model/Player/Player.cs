using System.Collections.ObjectModel;
using System.Globalization;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Model;

/// <summary>
/// The <c>Player</c> aggregate root — profile, the seven player-scoped currencies, the two Energy
/// banks, FTUE progress and the daily/weekly counter mechanism.
/// </summary>
/// <remarks>
/// <para>
/// Lives in <c>Core/Model/Player/</c> but in namespace <c>SlayIdleRepeat.Core.Model</c>, not
/// <c>...Model.Player</c>: a child namespace named <c>Player</c> would shadow the type
/// <c>Player</c>, making it unnameable from anywhere inside <c>Model</c> (<c>error CS0118</c>). The
/// directory is the file layout; the namespace is the layer.
/// </para>
/// <para>
/// Public getters, private constructor, <c>internal</c> mutators: the only public way to change state
/// is <c>GameRules.Apply</c>. <see cref="ToSnapshot"/> and <see cref="Rehydrate"/> are the exception,
/// the validating factory pair the persistence adapter needs to rebuild a player from a row.
/// </para>
/// <para>
/// It holds state and invariants; it does not compute. A handler computes and hands the answer to
/// <see cref="SetEnergy"/>; the aggregate's job is to refuse an answer that would break an invariant
/// — a currency never goes negative, and Energy never exceeds max + reserve.
/// </para>
/// <para>
/// The energy invariant is enforced on mutation, not on rehydration, and that is forced: a balance
/// patch that lowers the Energy cap leaves real players above the new one, and refusing to load such
/// a row would turn a tuning change into an account outage. So the rule is: a mutation may never push
/// a bank past its cap, and may never make an over-cap bank worse. A player already above the cap
/// stays loadable and drains by playing.
/// </para>
/// <para>
/// <see cref="MoveBalance"/> is the only method outside the constructor that writes <c>_wallet</c>,
/// and it is also the only one that writes <c>_energy</c> — an IL-scanning test requires any method
/// writing a currency-carrying field to construct a <c>CurrencyChanged</c>, and routing Energy through
/// the same method puts it under that guard too.
/// </para>
/// <para>
/// The stock lives here as of M4-05: <see cref="Inventory"/> is a component of this aggregate, and
/// every item operation is the component's rather than the aggregate's. 🔒 <b>No member of this type
/// names or builds a <c>GearInstance</c></b>, and that is a constraint rather than an accident — a
/// convenience member here that took, returned or constructed an item would put a grant outcome on
/// the aggregate, which is the shape the luck-routing rule was narrowed to see.
/// </para>
/// <para>
/// ✅ <b>The component is wired, and this paragraph used to say the opposite.</b> M4-05 landed the
/// container, its numbers and its persistence and left the wiring to the tasks that own the
/// operations; every one of them has since landed. <b>M4-04</b> was the first consumer — merge,
/// enhance and salvage all act on a held item — the in-run drop path banks grants through
/// <see cref="Inventory"/>.<c>Place</c>, M7-00d's <c>EQUIP</c> reads the stock, and the M4 retro
/// ruling of 2026-08-17 added <c>LOCK_ITEM</c>, the first caller of
/// <see cref="Inventory"/>.<c>SetLock</c> and therefore the first thing in the game's life that can
/// make an item <c>LOCKED</c> in production. <c>Content.InventoryTuning.Read</c> has production
/// callers through all of them. A note describing a mechanism as unwired after it was wired is how
/// a reader concludes the whole remark is stale.
/// </para>
/// <para>
/// Still deliberately absent: the unopened-container shelf, pets, mounts, talents, presets and
/// unlocks. Only the <b>first</b> of the six is held by a <c>GapRegister</c> entry
/// (<c>ContainerShelf</c>/M4-02, keyed on a <c>ContainerClass</c> that must not yet exist, so the
/// build fails the day it becomes writable without a home here). The other five are exactly the ones
/// that register says it does <em>not</em> transcribe: each would need the name of a type its
/// milestone has not chosen, and inventing five is the fabrication the register exists to refuse.
/// </para>
/// <para>
/// Pity counters are <b>not</b> among them any more: <see cref="PityCounters"/> is a field here as
/// of M4-01b, together with its snapshot column and the <c>SchemaVersion</c> bump, because the
/// chest-pick guarantee is player-scoped and lifetime and a run-scoped home would reset it every
/// run. <b>M4-02</b> still owns the chest ladders and the in-run drop mercy that write it further;
/// it does not own adding the field again. Entitlement lives on the session instead, reached as
/// <c>GameContext.Entitlements</c>. A brand-new account comes from <see cref="CreateStarting"/>,
/// which builds the starting row and returns it through <see cref="Rehydrate"/> — so rehydration
/// stays the only construction path, and the starting values are declared exactly once.
/// </para>
/// </remarks>
public sealed class Player
{
    /// <summary>The six player-scoped wallet currencies, in <see cref="CurrencyId"/> order.</summary>
    /// <remarks>
    /// <c>GOLD</c> is run-scoped and belongs to <c>Run</c>; <c>ENERGY</c> is player-scoped but held
    /// as <see cref="EnergyBanks"/> since it has two banks. Written out explicitly rather than
    /// filtered from every <c>CurrencyId</c>, so a new currency has to be adopted here deliberately.
    /// Wrapped in <see cref="Array.AsReadOnly{T}"/> rather than exposed as a bare array, so a caller
    /// cannot cast it back to <c>CurrencyId[]</c> and rewrite what a wallet is process-wide.
    /// </remarks>
    public static IReadOnlyList<CurrencyId> WalletCurrencies { get; } = Array.AsReadOnly(new[]
    {
        CurrencyId.CROWNS,
        CurrencyId.SOUL_SHARDS,
        CurrencyId.ENHANCE_STONES,
        CurrencyId.MERGE_DUST,
        CurrencyId.BEAST_FEED,
        CurrencyId.HONOR,
    });

    /// <summary>The wallet. Replaced wholesale on every movement rather than mutated in place.</summary>
    /// <remarks>
    /// Lets <see cref="Wallet"/> hand out the live object with no per-read allocation and no way to
    /// reach a mutable dictionary underneath. A wallet mutated in place would emit no field write for
    /// the IL scan that requires every currency mutation to construct a <c>CurrencyChanged</c> to see.
    /// </remarks>
    private IReadOnlyDictionary<CurrencyId, long> _wallet;

    private long _runsStarted;
    private EnergyBanks _energy;
    private DateTimeOffset _energyAnchorUtc;
    private DateTimeOffset _lastAppliedAtUtc;
    private FtueBeat _ftueBeat;
    private DateTimeOffset? _ftueCompletedAtUtc;
    private DateTimeOffset _dailyPeriodStartUtc;
    private DateTimeOffset _weeklyPeriodStartUtc;
    private int _loginCalendarDay;
    private bool _loginCalendarDayClaimed;

    /// <summary>The daily counters, and the read-only view handed out by <see cref="DailyCounters"/>.</summary>
    /// <remarks>Mutated in place, unlike <c>_wallet</c>, so the view stays valid across every increment and reset.</remarks>
    private readonly Dictionary<string, long> _dailyCounters;
    private readonly ReadOnlyDictionary<string, long> _dailyCountersView;
    private readonly Dictionary<string, long> _weeklyCounters;
    private readonly ReadOnlyDictionary<string, long> _weeklyCountersView;

    /// <summary>
    /// The (Chapter, Tier) pairs this player has cleared at least once, keyed
    /// <c>"{chapterId}:{tier}"</c>. The value is always 1; only the key's presence is read.
    /// </summary>
    private readonly Dictionary<string, long> _clearedChapterTiers;

    /// <inheritdoc cref="_clearedChapterTiers"/>
    private readonly ReadOnlyDictionary<string, long> _clearedChapterTiersView;

    // ---------------------------------------------------------------- feat counters

    /// <summary>The lifetime feat counters, and the view <see cref="FeatCounters"/> hands out.</summary>
    /// <remarks>Mutated in place like the daily and weekly counters, and — unlike them — never cleared.</remarks>
    private readonly Dictionary<string, long> _featCounters;
    private readonly FeatCounters _featCountersView;

    // ---------------------------------------------------------------- pity counters

    /// <summary>
    /// The player-scoped pity counters. Replaced wholesale rather than mutated, because the luck
    /// service answers the map it would leave behind and the aggregate decides whether to keep it.
    /// </summary>
    /// <remarks>
    /// Lifetime, and reset only when the guarantee each counter protects fires: nothing here decays,
    /// and no chapter change, tier change, season roll or logout touches it.
    /// </remarks>
    private PityCounters _pityCounters;

    private long _legendXp;

    // ---------------------------------------------------------------- hero (M4-10)
    //
    // The hero's own state: the name, the Talent Points the Legend Levels have granted, what the
    // hero is wearing, and the saved presets. Kept together and delimited so a sibling task editing
    // this aggregate elsewhere merges cleanly around it.

    /// <summary>The Talent Points the player's Legend Levels have granted. Only ever grows here; nothing spends one yet.</summary>
    private long _talentPoints;

    /// <summary>What the hero is wearing. Replaced wholesale, like the wallet — see <see cref="Model.Loadout"/>.</summary>
    private Loadout _loadout;

    /// <summary>The saved presets, keyed by slot, and the view <see cref="Presets"/> hands out.</summary>
    /// <remarks>
    /// Mutated in place like the daily counters, so the view stays valid across every save. A
    /// <see cref="SortedDictionary{TKey,TValue}"/> rather than a plain one, and that is load-bearing
    /// rather than tidy: the persisted shape is a LIST, whose order the canonical writer preserves,
    /// so the slot order has to be a property of the store rather than of the order the player
    /// happened to save in.
    /// </remarks>
    private readonly SortedDictionary<int, LoadoutPreset> _presets;
    private readonly ReadOnlyDictionary<int, LoadoutPreset> _presetsView;

    /// <summary>The one constructor. Private; every value has already been checked by <see cref="Rehydrate"/>, the only caller.</summary>
    private Player(
        PlayerId id,
        string displayName,
        int legendLevel,
        long legendXp,
        long runsStarted,
        IReadOnlyDictionary<CurrencyId, long> wallet,
        EnergyBanks energy,
        DateTimeOffset energyAnchorUtc,
        DateTimeOffset lastAppliedAtUtc,
        FtueBeat ftueBeat,
        DateTimeOffset? ftueCompletedAtUtc,
        DateTimeOffset dailyPeriodStartUtc,
        Dictionary<string, long> dailyCounters,
        DateTimeOffset weeklyPeriodStartUtc,
        Dictionary<string, long> weeklyCounters,
        int loginCalendarDay,
        bool loginCalendarDayClaimed,
        Dictionary<string, long> clearedChapterTiers,
        Dictionary<string, long> featCounters,
        PityCounters pityCounters,
        Inventory inventory,
        IReadOnlyList<AutoSalvageRule> autoSalvageRules,
        long talentPoints,
        Loadout loadout,
        SortedDictionary<int, LoadoutPreset> presets)
    {
        _talentPoints = talentPoints;
        _loadout = loadout;
        _presets = presets;
        _presetsView = new ReadOnlyDictionary<int, LoadoutPreset>(presets);
        _pityCounters = pityCounters;
        Id = id;
        DisplayName = displayName;
        LegendLevel = legendLevel;
        _legendXp = legendXp;
        _runsStarted = runsStarted;
        _wallet = wallet;
        _energy = energy;
        _energyAnchorUtc = energyAnchorUtc;
        _lastAppliedAtUtc = lastAppliedAtUtc;
        _ftueBeat = ftueBeat;
        _ftueCompletedAtUtc = ftueCompletedAtUtc;
        _dailyPeriodStartUtc = dailyPeriodStartUtc;
        _dailyCounters = dailyCounters;
        _dailyCountersView = new ReadOnlyDictionary<string, long>(dailyCounters);
        _weeklyPeriodStartUtc = weeklyPeriodStartUtc;
        _weeklyCounters = weeklyCounters;
        _weeklyCountersView = new ReadOnlyDictionary<string, long>(weeklyCounters);
        _loginCalendarDay = loginCalendarDay;
        _loginCalendarDayClaimed = loginCalendarDayClaimed;
        _clearedChapterTiers = clearedChapterTiers;
        _clearedChapterTiersView = new ReadOnlyDictionary<string, long>(clearedChapterTiers);
        _featCounters = featCounters;
        _featCountersView = new FeatCounters(new ReadOnlyDictionary<string, long>(featCounters));
        Inventory = inventory;
        AutoSalvageRules = autoSalvageRules;
    }

    /// <summary>
    /// The auto-salvage filter this player configured — one row per band they want swept at run end.
    /// Empty until they set one, and an empty filter sweeps nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b><c>SET_AUTO_SALVAGE_RULES</c> writes it as of the M4 retro ruling of 2026-08-17</b>,
    /// which added the command to `14` §2.3's vocabulary. Until then nothing wrote it, which left
    /// <c>Rules.Forge.AutoSalvageFilter</c> — written, documented and tested against a hand-built
    /// fixture — unreachable from any command a player can send.
    /// </para>
    /// <para>
    /// ⚠️ <b>Still nothing APPLIES it.</b> No run-end payout consults the filter, so a player can
    /// configure rows that sweep nothing; the sweep at run end is not this ruling's scope, and the
    /// forge screen that edits the rows is M9-01's. What changed is that the rows can now exist.
    /// </para>
    /// <para>
    /// Replaced wholesale rather than mutated, like the wallet: the list handed out here can never
    /// change afterwards, so a snapshot that has already been taken cannot be rewritten by a later
    /// command.
    /// </para>
    /// </remarks>
    public IReadOnlyList<AutoSalvageRule> AutoSalvageRules { get; private set; }

    /// <summary>Replaces the auto-salvage filter with the rows a command carried.</summary>
    /// <param name="rules">
    /// The rows to store, <b>already validated</b> — a band that exists, one row per band, a ceiling
    /// inside the authored enhancement range. Which rows are legal is a rule over authored content
    /// and belongs to the handler that read the tuning, not to the aggregate that stores them.
    /// </param>
    /// <remarks>
    /// <para>
    /// Copied on the way in for <see cref="ReadAutoSalvageRules"/>'s reason: the caller's list stays
    /// writable, and a record compares an <c>IReadOnlyList&lt;T&gt;</c> component by reference, so
    /// the sharing would be invisible to every comparison that looked for it.
    /// </para>
    /// <para>
    /// 🔒 <b>No <c>SnapshotSchema.SchemaVersion</c> bump comes with this writer, and that is a
    /// decision rather than an omission.</b> `14` §16.6 makes a field ADDED, REMOVED or REORDERED a
    /// versioned migration; this adds none. <see cref="PlayerSnapshot.AutoSalvageRules"/> has been a
    /// column since M4-05 and its position is unchanged — what changed is that something finally
    /// writes it. The two sibling commands the same ruling added are the same story:
    /// <c>UNEQUIP</c> writes <see cref="PlayerSnapshot.Loadout"/> and <c>LOCK_ITEM</c> writes the
    /// <c>Locked</c> flag on a persisted gear row, both existing columns. A row a player wrote will
    /// now carry values it used to carry only as empty or false, which changes their
    /// <c>stateHash</c> — but a state hash changing when the state changes is the contract, not a
    /// serialisation change.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null.</exception>
    internal void SetAutoSalvageRules(IReadOnlyList<AutoSalvageRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var copy = new AutoSalvageRule[rules.Count];

        for (var i = 0; i < rules.Count; i++)
        {
            copy[i] = rules[i];
        }

        AutoSalvageRules = Array.AsReadOnly(copy);
    }

    /// <summary>The aggregate root's identity.</summary>
    public PlayerId Id { get; }

    /// <summary>The player's display name, exactly as it was persisted or last set. Never null or blank.</summary>
    /// <remarks>
    /// <see cref="Rename"/> is the only mutator, and it takes a <see cref="HeroName"/> — a type only
    /// the name rule can construct — so a name reaches this field having passed the length, character
    /// and word-list checks or not at all. <see cref="Rehydrate"/> deliberately checks less; see
    /// <see cref="RequireIdentity"/>.
    /// </remarks>
    public string DisplayName { get; private set; }

    /// <summary>The player's Legend Level.</summary>
    /// <remarks>
    /// Derived from <see cref="LegendXp"/> by the levelling curve and written by
    /// <see cref="AdvanceLegendLevel"/>, which only ever moves it upwards.
    /// </remarks>
    public int LegendLevel { get; private set; }

    /// <summary>Lifetime Legend XP. Never negative. Mutated by <see cref="GrantLegendXp"/>.</summary>
    public long LegendXp => _legendXp;

    /// <summary>The player's lifetime runs-started counter — the fourth argument fed into <c>runSeed</c> derivation.</summary>
    /// <remarks>
    /// Must be monotonic and never reset: two runs started in the same second draw different boards
    /// only because this advances. <see cref="BeginRun"/> is the sole mutator.
    /// </remarks>
    public long RunsStarted => _runsStarted;

    /// <summary>
    /// The six player-scoped wallet balances. Read-only, and every currency in
    /// <see cref="WalletCurrencies"/> is present — a missing key is a corrupt row, not a zero.
    /// </summary>
    /// <remarks>
    /// A frozen view, unlike <see cref="DailyCounters"/>: the wallet is replaced wholesale on every
    /// movement, so an object a caller holds never changes afterwards.
    /// </remarks>
    public IReadOnlyDictionary<CurrencyId, long> Wallet => _wallet;

    /// <summary>The two Energy banks — where the <c>ENERGY</c> currency lives.</summary>
    public EnergyBanks Energy => _energy;

    /// <summary>The instant Energy regeneration has been accrued up to.</summary>
    public DateTimeOffset EnergyAnchorUtc => _energyAnchorUtc;

    /// <summary>The instant the last command was applied to this player, which <c>AdvanceTime</c> rolls forward from.</summary>
    public DateTimeOffset LastAppliedAtUtc => _lastAppliedAtUtc;

    /// <summary>The tutorial beat this player has reached.</summary>
    public FtueBeat FtueBeat => _ftueBeat;

    /// <summary>When the tutorial completed. <c>null</c> until beat 10's spend commits.</summary>
    public DateTimeOffset? FtueCompletedAtUtc => _ftueCompletedAtUtc;

    /// <summary>Whether the tutorial is finished, defined as <c>completedAtUtc</c> being set.</summary>
    public bool IsFtueComplete => _ftueCompletedAtUtc is not null;

    /// <summary>The 05:00 UTC game-day boundary <see cref="DailyCounters"/> were last reset at.</summary>
    public DateTimeOffset DailyPeriodStartUtc => _dailyPeriodStartUtc;

    /// <summary>The daily counters for the current game day. Read-only; empty is the normal state.</summary>
    /// <remarks>
    /// A live view, unlike <see cref="Wallet"/>: the counters are mutated in place, so a caller
    /// holding this reference across a reset sees the new values. Read it, do not hold it.
    /// </remarks>
    public IReadOnlyDictionary<string, long> DailyCounters => _dailyCountersView;

    /// <summary>The Monday 05:00 UTC game-week boundary <see cref="WeeklyCounters"/> were last reset at.</summary>
    public DateTimeOffset WeeklyPeriodStartUtc => _weeklyPeriodStartUtc;

    /// <summary>The weekly counters for the current game week. Read-only.</summary>
    /// <remarks>A live view, like <see cref="DailyCounters"/> and unlike <see cref="Wallet"/>.</remarks>
    public IReadOnlyDictionary<string, long> WeeklyCounters => _weeklyCountersView;

    /// <summary>The login-calendar day currently open: the one the player may claim, counted from 1.</summary>
    /// <remarks>
    /// Open, not "the last one claimed" — a missed day leaves the same day open rather than skipping
    /// it, and a "last claimed" field would have nothing to say about a brand-new player.
    /// </remarks>
    public int LoginCalendarDay => _loginCalendarDay;

    /// <summary>Whether <see cref="LoginCalendarDay"/> has been claimed. While false, the calendar does not advance.</summary>
    public bool LoginCalendarDayClaimed => _loginCalendarDayClaimed;

    /// <summary>The (Chapter, Tier) pairs cleared at least once. See <see cref="_clearedChapterTiers"/>.</summary>
    internal IReadOnlyDictionary<string, long> ClearedChapterTiers => _clearedChapterTiersView;

    /// <summary>The player's lifetime feat counters. A live view, like <see cref="DailyCounters"/>.</summary>
    /// <remarks>
    /// <see cref="ResetDailyCounters"/> and <see cref="ResetWeeklyCounters"/> deliberately do not
    /// reach it — see <see cref="CountFeat"/>.
    /// </remarks>
    public FeatCounters FeatCounters => _featCountersView;

    /// <summary>The player's pity counters, as the luck service takes them.</summary>
    internal PityCounters PityCounters => _pityCounters;

    /// <summary>
    /// Applies the counter changes one resolution answered.
    /// </summary>
    /// <remarks>
    /// The one writer. A resolution is stateless and reports what it <em>would</em> leave behind; the
    /// aggregate is where that becomes state, which is what keeps a draw that is refused from moving
    /// a counter.
    /// </remarks>
    /// <param name="key">The counter id, as the tuning reader forms it. Never hand-composed.</param>
    /// <param name="value">The counter's value after the draw — not a delta. Never negative.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    internal void SetPityCounter(string key, int value) =>
        _pityCounters = _pityCounters.With(key, value);

    /// <summary>The stock this player carries, and the items a full stock is holding for them.</summary>
    /// <remarks>
    /// A live view of the component, not a copy taken at rehydration: a copy would make every
    /// handler's mutation invisible to the very next read, and the aggregate would persist the state
    /// it started with.
    /// </remarks>
    public Inventory Inventory { get; }

    // ---------------------------------------------------------------- hero (M4-10)

    /// <summary>
    /// The Talent Points this player's Legend Levels have granted, minus nothing at all.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Granted and stored; nothing spends one.</b> The tree, the spend path and the respec are
    /// M4-06's, and this is deliberately a bare total rather than a "spent" and "available" pair —
    /// how a spend is recorded is that task's decision, and inventing the shape of it here would fix
    /// it before anybody had chosen it. What could not wait is the accrual: a point is earned by
    /// levelling, so a counter that starts when the tree ships is a counter of zero for every player
    /// who levelled first.
    /// </remarks>
    public long TalentPoints => _talentPoints;

    /// <summary>What the hero is wearing.</summary>
    /// <remarks>
    /// A frozen value, unlike <see cref="Inventory"/>: the loadout is replaced wholesale on every
    /// change, so an object a caller holds never changes afterwards — which is what lets a preset
    /// store one directly.
    /// </remarks>
    public Loadout Loadout => _loadout;

    /// <summary>The saved loadout presets, keyed by slot and enumerated in ascending slot order.</summary>
    /// <remarks>
    /// A live view, like <see cref="DailyCounters"/> and unlike <see cref="Wallet"/>: read it, do not
    /// hold it. Kept ordered by the store rather than sorted on every read — the persisted shape is
    /// a list, so two players holding the same three presets must not encode differently because of
    /// the order they saved them in.
    /// </remarks>
    public IReadOnlyDictionary<int, LoadoutPreset> Presets => _presetsView;

    /// <summary>The preset in one slot, if the player has saved one there.</summary>
    /// <param name="presetSlot">The slot to read.</param>
    /// <param name="preset">The preset saved there.</param>
    /// <returns><see langword="true"/> when a preset is saved in that slot.</returns>
    public bool TryGetPreset(int presetSlot, out LoadoutPreset? preset) =>
        _presets.TryGetValue(presetSlot, out preset);

    /// <summary>Sets the player's name.</summary>
    /// <param name="name">A name the name rule has already accepted.</param>
    /// <remarks>
    /// Takes a <see cref="HeroName"/> and not a <see cref="string"/>, and that is the whole point of
    /// that type existing: only <c>Rules.Hero.HeroNameRule.Validate</c> can produce one, so there is
    /// no route into this field that skips the filter. `27` §1 filters at creation <em>and on every
    /// edit</em>, and "every edit" is only true if there is one door.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    internal void Rename(HeroName name)
    {
        ArgumentNullException.ThrowIfNull(name);

        DisplayName = name.Value;
    }

    /// <summary>
    /// Applies one reconciliation of the Legend Level against lifetime XP: the new level and the
    /// Talent Points reaching it granted, together.
    /// </summary>
    /// <param name="level">The level the player now stands at. Never below the current one.</param>
    /// <param name="talentPoints">The points the level-ups granted. Never negative.</param>
    /// <param name="tuning">The authored Legend Level range, so the cap is the document's.</param>
    /// <remarks>
    /// Takes both halves together because they are one fact: writing the level and forgetting the
    /// points would silently owe the player one Talent Point per level for the rest of the account's
    /// life, and no invariant here could catch it — each write is individually legal. The same
    /// argument <see cref="AccrueEnergy"/> makes about the banks and the anchor.
    /// <para>
    /// 🔒 It refuses to go backwards. The curve's exponent is the pacing dial and is expected to
    /// move; a steeper curve re-derives an existing player to a lower level, and applying that would
    /// take back Talent Points, Max Energy and every gate they had passed. A balance patch may not
    /// become an account rollback.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The level goes backwards, leaves the range, or the points are negative.</exception>
    internal void AdvanceLegendLevel(int level, long talentPoints, LegendTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        if (level < LegendLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                "This player stands at Legend Level " + Text(LegendLevel) + " and " + Text(level) +
                " is lower. A Legend Level only ever rises: the curve's exponent is a tunable, a " +
                "steeper curve re-derives an existing player DOWN, and applying that would take " +
                "back their Talent Points, their Max Energy and every gate they had passed.");
        }

        if (level > tuning.Maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                "07 §1.1 caps Legend Level at " + Text(tuning.Maximum) + ". Excess Legend XP past " +
                "the cap stays banked and buys nothing, which is what a cap is — a level above it " +
                "is a rule that stopped applying one.");
        }

        if (talentPoints < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(talentPoints),
                talentPoints,
                "07 §1.1 grants points on the way up and nothing anywhere takes one back. A " +
                "negative grant here would be a respec, which is M4-06's and free by design.");
        }

        try
        {
            _talentPoints = checked(_talentPoints + talentPoints);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(talentPoints),
                talentPoints,
                "Granting " + Text(talentPoints) + " Talent Points on top of " + Text(_talentPoints) +
                " overflows a 64-bit total. 07 §1.1's ladder is 199 level-ups; a grant this size is " +
                "a defect in whatever computed it.");
        }

        LegendLevel = level;
    }

    /// <summary>Puts a gear instance in a slot, taking it out of any slot it was already in.</summary>
    /// <param name="slot">The slot to fill.</param>
    /// <param name="item">The instance to wear. Its presence in the stock is the caller's to check.</param>
    /// <remarks>
    /// The aggregate holds the loadout's own invariants (a declared slot, a non-blank identity, one
    /// slot per item) and deliberately not the cross-component one: whether the player actually owns
    /// the item is <c>Rules.Hero.LoadoutRules</c>'s question, asked by the handler before it gets
    /// here, and re-asking it here would put a stock lookup on a path that already did one.
    /// </remarks>
    internal void Equip(GearSlot slot, GearInstanceId item) => _loadout = _loadout.With(slot, item);

    /// <summary>Empties one slot.</summary>
    /// <param name="slot">The slot to empty.</param>
    internal void Unequip(GearSlot slot) => _loadout = _loadout.Without(slot);

    /// <summary>Installs a whole loadout, replacing what the hero was wearing.</summary>
    /// <param name="loadout">The loadout to wear — already filtered to what the player owns.</param>
    /// <exception cref="ArgumentNullException"><paramref name="loadout"/> is null.</exception>
    internal void WearLoadout(Loadout loadout)
    {
        ArgumentNullException.ThrowIfNull(loadout);

        _loadout = loadout;
    }

    /// <summary>
    /// 🔒 Takes an item out of this player's world: out of the stock, and off the hero, in one call.
    /// </summary>
    /// <param name="item">The instance to destroy.</param>
    /// <param name="tuning">The inventory numbers, for the reclaim the removal may open room for.</param>
    /// <returns><see langword="true"/> when the player owned it.</returns>
    /// <remarks>
    /// <para>
    /// 🔴 <b>The seam every destructive item operation must use — merge, salvage, and anything a
    /// later milestone adds.</b> A slot names an item rather than copying one, so removing an item
    /// from the stock without taking it off the hero leaves a dangling reference that
    /// <see cref="RequireLoadoutResolves"/> refuses. That refusal is a <em>defect</em> report, not a
    /// rejection: it exists to stop the corrupt state being persisted, and the way not to trip it is
    /// to do both halves together rather than to remember to.
    /// </para>
    /// <para>
    /// It takes both halves for the reason <see cref="AccrueEnergy"/> and
    /// <see cref="AdvanceLegendLevel"/> take theirs: they are one fact. Removing an item is the
    /// aggregate's business precisely because the item is in one component and the reference to it is
    /// in another, and no component can hold that pairing on its own.
    /// </para>
    /// <para>
    /// ⚠️ It destroys; it does not move. Whatever the operation pays out for the item — Merge Dust, a
    /// partial Enhance Stone refund, a Set Token — is the calling rule's, and this says nothing about
    /// it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
    internal bool DiscardItem(GearInstanceId item, InventoryTuning tuning)
    {
        var removed = Inventory.Remove(item, tuning);

        if (removed)
        {
            _loadout = _loadout.WithoutItem(item);
        }

        return removed;
    }

    /// <summary>
    /// 🔴 The exit-side half of the equipped-item invariant: every slot still names an item the stock
    /// holds, checked <b>after</b> a command rather than only on the way in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists, and why the load-time fault alone would have been the wrong half.</b> A
    /// slot names an item rather than copying one, so any operation that destroys an item — merge,
    /// salvage, and whatever else a later milestone adds — must take it off the hero in the same
    /// change. If only <see cref="Rehydrate"/> checked, the command that broke the pairing would be
    /// ACCEPTED, its state returned to the caller and written to the database, and every later
    /// command would then fail at the clone. The account would be unplayable, permanently, and the
    /// corrupt row would be the persisted one.
    /// </para>
    /// <para>
    /// It throws rather than rejecting, because reaching it is a defect in a handler and never a
    /// player asking for something they cannot have — the same distinction <c>MoveCurrency</c> draws
    /// for an unaffordable spend. <c>Loadout.WithoutItem</c> is the seam the offending handler should
    /// have used.
    /// </para>
    /// <para>
    /// Six lookups against a stock of at most a few hundred items, on an accepted command only. A
    /// full re-validation of the aggregate would be the thorough answer and would double the
    /// rehydration cost of every command; this checks the one invariant that spans two components
    /// and that no single component can hold on its own.
    /// </para>
    /// <para>
    /// 🔒 <b>The accepted set is stated as the two arms that are legal, not as "anything but
    /// <c>UNKNOWN_ITEM</c>".</b> <c>Rules.Hero.LoadoutRules.IsEquippable</c> is the entry-side half of
    /// this same invariant and it admits exactly <c>AVAILABLE</c> and <c>LOCKED</c>; written as a
    /// single exclusion, this half additionally admitted <c>HELD_IN_OVERFLOW</c>, so one invariant was
    /// stated twice and the two statements disagreed. Unreachable today only because
    /// <c>Inventory.Place</c> holds nothing but <em>newly arrived</em> items, which no slot can
    /// already name — which is precisely why it would not have failed on the change that stopped that
    /// being true. An item in the holding list is one <em>nothing can be done to</em> (see
    /// <c>ItemAvailability</c>), and a hero wearing one is a hero wearing something the stock is not
    /// holding for them.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">A slot names an item the stock does not hold in stock.</exception>
    internal void RequireLoadoutResolves()
    {
        foreach (var (slot, item) in _loadout.Gear)
        {
            var availability = Inventory.Availability(item);

            if (availability is ItemAvailability.AVAILABLE or ItemAvailability.LOCKED)
            {
                continue;
            }

            throw new InvalidOperationException(
                "This command left '" + item.Value + "' equipped in " + slot + " while the player's " +
                "stock no longer holds it (" + availability + "). A slot NAMES an item rather than " +
                "copying one, so whatever destroyed the item — or pushed it into the holding list — " +
                "had to take it off the hero in the same change; Loadout.WithoutItem is that seam. " +
                "Persisting this row would make every later command fail at the clone, for good.");
        }
    }

    /// <summary>Writes a preset into its slot, replacing whatever was there.</summary>
    /// <param name="preset">The preset. Its slot is its own.</param>
    /// <remarks>
    /// Whether the player is <em>allowed</em> that slot is not asked here. `12` §2 grants a Plus
    /// subscriber unlimited slots and `12` §2.2 keeps presets beyond the free allowance readable when
    /// Plus lapses, so the allowance is an entitlement question — and no aggregate can see the
    /// session. The handler answers it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="preset"/> is null.</exception>
    internal void SavePreset(LoadoutPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        _presets[preset.Slot] = preset;
    }

    // ------------------------------------------------------------ end hero (M4-10)

    /// <summary>The lifetime count of one feat counter, or zero when nothing has advanced it.</summary>
    /// <param name="counterId">The counter's id. Never null, empty or whitespace.</param>
    /// <exception cref="ArgumentException"><paramref name="counterId"/> is blank.</exception>
    public long FeatCount(string counterId)
    {
        if (string.IsNullOrWhiteSpace(counterId))
        {
            throw new ArgumentException(BlankFeatCounterId, nameof(counterId));
        }

        return _featCounters.TryGetValue(counterId, out var count) ? count : 0L;
    }

    /// <summary>What separates the chapter from the tier in a <see cref="ClearedChapterTiers"/> key.</summary>
    private const char ChapterTierSeparator = ':';

    /// <summary>The key <see cref="ClearedChapterTiers"/> is stored under.</summary>
    internal static string ChapterTierKey(int chapterId, DifficultyTier tier) =>
        chapterId.ToString(CultureInfo.InvariantCulture) + ChapterTierSeparator + tier;

    /// <summary>Whether (<paramref name="chapterId"/>, <paramref name="tier"/>) has been cleared before.</summary>
    internal bool HasClearedChapterTier(int chapterId, DifficultyTier tier) =>
        _clearedChapterTiers.ContainsKey(ChapterTierKey(chapterId, tier));

    /// <summary>Records that (<paramref name="chapterId"/>, <paramref name="tier"/>) has now been cleared. Idempotent.</summary>
    internal void MarkChapterTierCleared(int chapterId, DifficultyTier tier) =>
        _clearedChapterTiers[ChapterTierKey(chapterId, tier)] = 1;

    /// <summary>The highest chapter this player has cleared on any tier, floored at one.</summary>
    /// <remarks>
    /// Derived rather than stored, so it can never disagree with the record it is read from — and
    /// floored at one because a player who has cleared nothing is still playing chapter one, so a
    /// grant scaled against "how far they have got" has a chapter to scale against on the first run.
    /// Any tier counts: clearing a chapter on Normal is as much a statement about how far the
    /// player has come as clearing it on Mythic.
    /// </remarks>
    internal int HighestChapterCleared
    {
        get
        {
            var highest = 1;

            foreach (var key in _clearedChapterTiers.Keys)
            {
                var separator = key.IndexOf(ChapterTierSeparator);

                if (separator > 0 &&
                    int.TryParse(
                        key.AsSpan(0, separator),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var chapterId) &&
                    chapterId > highest)
                {
                    highest = chapterId;
                }
            }

            return highest;
        }
    }

    /// <summary>
    /// The balance of one player-scoped wallet currency.
    /// </summary>
    /// <param name="currency">One of <see cref="WalletCurrencies"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="currency"/> is <c>GOLD</c> (run-scoped), <c>ENERGY</c> (held as
    /// <see cref="Energy"/>) or undefined. Each is refused with the reason rather than answering
    /// zero, which would read as "the player has none" for a balance on the wrong aggregate.
    /// </exception>
    public long BalanceOf(CurrencyId currency)
    {
        RequireWalletCurrency(currency, nameof(currency));

        return _wallet[currency];
    }

    /// <summary>The current count of a daily counter, or zero when nothing has registered it.</summary>
    /// <param name="counterKey">The counter's key. Never null, empty or whitespace.</param>
    /// <exception cref="ArgumentException"><paramref name="counterKey"/> is blank.</exception>
    public long DailyCount(string counterKey) => CountIn(_dailyCounters, counterKey);

    /// <inheritdoc cref="DailyCount"/>
    public long WeeklyCount(string counterKey) => CountIn(_weeklyCounters, counterKey);

    /// <summary>The persisted shape of this aggregate, stamped with the current <see cref="SnapshotSchema.SchemaVersion"/>.</summary>
    /// <remarks>
    /// The counter dictionaries are copied; the wallet is not — the wallet is already replaced
    /// wholesale on every movement, so the object handed out here can never change afterwards, while
    /// a shared counter reference would let a later increment rewrite an already-handed-out snapshot.
    /// </remarks>
    public PlayerSnapshot ToSnapshot() => new(
        SnapshotSchema.SchemaVersion,
        Id,
        DisplayName,
        LegendLevel,
        LegendXp,
        _runsStarted,
        _wallet,
        _energy,
        _energyAnchorUtc,
        _lastAppliedAtUtc,
        _ftueBeat,
        _ftueCompletedAtUtc,
        _dailyPeriodStartUtc,
        Copy(_dailyCounters),
        _weeklyPeriodStartUtc,
        Copy(_weeklyCounters),
        _loginCalendarDay,
        _loginCalendarDayClaimed,
        Copy(_clearedChapterTiers),
        Copy(_featCounters),
        // Already immutable and replaced wholesale on every movement, so no copy is needed — the
        // same argument the wallet makes.
        _pityCounters.Counters,
        Inventory.ToSnapshot(),
        AutoSalvageRules,
        _talentPoints,
        _loadout.ToSnapshot(),
        PersistPresets());

    /// <summary>The presets as rows, in ascending slot order.</summary>
    /// <remarks>
    /// The order is load-bearing: the canonical writer preserves a LIST's order, so two players
    /// holding the same three presets would hash differently depending on which slot each happened
    /// to save first. The store is what keeps them in slot order.
    /// </remarks>
    private IReadOnlyList<LoadoutPresetSnapshot> PersistPresets()
    {
        if (_presets.Count == 0)
        {
            return NoPresets;
        }

        var rows = new LoadoutPresetSnapshot[_presets.Count];
        var i = 0;

        // SortedDictionary enumerates in key order, so the ascending slot order the encoding
        // depends on is the store's rather than something restated here.
        foreach (var preset in _presets.Values)
        {
            rows[i++] = preset.ToSnapshot();
        }

        return Array.AsReadOnly(rows);
    }

    /// <summary>
    /// The starting state of a brand-new account whose player has chosen a name.
    /// </summary>
    /// <param name="id">The identity whatever creates accounts has already issued.</param>
    /// <param name="displayName">
    /// The chosen name, <b>already through `27` §1's filter</b> — that is what the type means and the
    /// only reason it is the parameter's type. Stored exactly as the player typed it.
    /// </param>
    /// <param name="nowUtc">The instant the account is created at, which the period boundaries are derived from.</param>
    /// <param name="content">The content set the authored starting values are read from.</param>
    /// <param name="inventory">The stock the player starts with, or <c>null</c> for the empty one.</param>
    /// <returns>The starting aggregate, or the failure the row was refused with.</returns>
    /// <remarks>
    /// 🔒 <b>The one door player-chosen text comes through, and it is typed rather than trusted.</b>
    /// `27` §1 states the filter as "at creation <em>and</em> on every edit"; <see cref="Rename"/>
    /// already delivered the edit half by taking a <see cref="HeroName"/>, and this delivers the
    /// creation half the same way. It took a plain <c>string</c> until the M4 review, which meant the
    /// one path `27` §1 names by name was the one path the filter did not stand on.
    /// <para>
    /// The two paths that name an account after something the player never typed do not come through
    /// here: see <see cref="CreateStartingNamedAfterItsOwnId"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="displayName"/> or <paramref name="content"/> is null.
    /// </exception>
    /// <exception cref="MissingContentException">The content set does not author a Legend Level range.</exception>
    /// <exception cref="UnauthorisedTunableException">That range holds a deliberate <c>null</c>.</exception>
    /// <exception cref="InvalidTunableException">That range is authorised but unusable.</exception>
    public static Result<Player> CreateStarting(
        PlayerId id,
        HeroName displayName,
        DateTimeOffset nowUtc,
        ContentSnapshot content,
        InventorySnapshot? inventory = null)
    {
        ArgumentNullException.ThrowIfNull(displayName);

        return StartingRow(id, displayName.Value, nowUtc, content, inventory);
    }

    /// <summary>
    /// The starting state of a brand-new account named after its own identity, because nothing has
    /// asked the player for a name.
    /// </summary>
    /// <param name="id">The identity whatever creates accounts has already issued, and the name.</param>
    /// <param name="nowUtc">The instant the account is created at.</param>
    /// <param name="content">The content set the authored starting values are read from.</param>
    /// <param name="inventory">The stock the player starts with, or <c>null</c> for the empty one.</param>
    /// <returns>The starting aggregate, or the failure the row was refused with.</returns>
    /// <remarks>
    /// 🔒 <b>It takes no name parameter at all, and that is the point.</b> A machine-minted identity
    /// is not player-chosen text and must not go through `27` §1's filter — the in-process host's own
    /// id is a prefix plus a GUID, which <c>HeroNameRule</c> would refuse for length before it ever
    /// reached a word list. Passing it as a <c>string</c> to a factory that also accepts player text
    /// is what left the creation path unfiltered in the first place, so there is deliberately no
    /// parameter here for player text to arrive through.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="MissingContentException">The content set does not author a Legend Level range.</exception>
    /// <exception cref="UnauthorisedTunableException">That range holds a deliberate <c>null</c>.</exception>
    /// <exception cref="InvalidTunableException">That range is authorised but unusable.</exception>
    public static Result<Player> CreateStartingNamedAfterItsOwnId(
        PlayerId id,
        DateTimeOffset nowUtc,
        ContentSnapshot content,
        InventorySnapshot? inventory = null) =>
        StartingRow(id, id.Value, nowUtc, content, inventory);

    /// <summary>
    /// ⚠️ The starting state of an account carrying a name <b>nothing filtered</b> — the domain
    /// harness's door and no one else's.
    /// </summary>
    /// <param name="id">The identity the harness minted.</param>
    /// <param name="displayName">The harness's own label for the player. Stored exactly as given.</param>
    /// <param name="nowUtc">The instant the account is created at.</param>
    /// <param name="content">The content set the authored starting values are read from.</param>
    /// <param name="inventory">The stock the player starts with, or <c>null</c> for the empty one.</param>
    /// <returns>The starting aggregate, or the failure the row was refused with.</returns>
    /// <remarks>
    /// 🔒 <b>Named for what it skips, which is the whole design.</b> <c>InMemoryGame</c> lets a caller
    /// label a fixture player ("Ludwig the Unhurried") and runs on hermetic content sets that carry no
    /// word lists at all, so it cannot go through the filter and has nothing player-chosen to filter.
    /// That was true of the old <c>CreateStarting(string)</c> too — the difference is that this is a
    /// <em>named method with an enumerable caller set</em> instead of an unfiltered parameter on the
    /// method every caller reaches for, and <c>HeroNameWritePathRuleTests</c> asserts who calls it.
    /// <para>
    /// ⚠️ <c>public</c> rather than <c>internal</c>, and not by preference: <c>Core/Testing/</c> may
    /// reach <c>Core/Model/</c> through its <b>public</b> seam only (`30` §6, asserted by
    /// <c>AccessibilityBoundaryTests</c>), so an internal door would put the harness in breach of a
    /// rule that matters more than this one's visibility. The IL rule over its callers is what makes
    /// that safe, and it is stricter than <c>internal</c> would have been: it enumerates
    /// <c>Application</c> as well as <c>Core</c>.
    /// </para>
    /// </remarks>
    public static Result<Player> CreateStartingWithUnfilteredName(
        PlayerId id,
        string displayName,
        DateTimeOffset nowUtc,
        ContentSnapshot content,
        InventorySnapshot? inventory = null) =>
        StartingRow(id, displayName, nowUtc, content, inventory);

    /// <summary>The starting state of a brand-new account, as one row, declared in one place.</summary>
    /// <param name="id">The identity whatever creates accounts has already issued.</param>
    /// <param name="displayName">
    /// The name to store. Kept exactly as given and never interpreted; a blank one is refused by the
    /// rehydration this returns through.
    /// </param>
    /// <param name="nowUtc">The instant the account is created at, which the period boundaries are derived from.</param>
    /// <param name="content">The content set the authored starting values are read from.</param>
    /// <param name="inventory">
    /// The stock the player starts with, or <c>null</c> for the empty one a new player has.
    /// 🔴 Appended LAST, and every caller passes it by name — a parameter inserted ahead of an
    /// existing optional one merges textually clean and silently re-binds every positional argument
    /// after it.
    /// </param>
    /// <returns>The starting aggregate, or the failure the row was refused with.</returns>
    /// <remarks>
    /// <para>
    /// Every value here is either authored, derived from <paramref name="nowUtc"/>, or the identity
    /// element — none is invented. Legend Level is the authored floor, never a literal. Wallet
    /// balances and Energy banks start at zero, forced rather than chosen: every currency movement
    /// must be attributed by a <c>CurrencyChanged</c> event, so a player that started with a balance
    /// would hold currency no row attributes. Every collection whose absence is a fault is stated
    /// empty rather than left to a default, because an absent one and an empty one are different
    /// states and only one of them is a row the game wrote; the one collection whose row authors
    /// <c>null</c> as its own "nothing yet" is stated as that, in full, rather than skipped silently.
    /// </para>
    /// <para>
    /// It returns through <see cref="Rehydrate"/> rather than reaching the constructor itself, so
    /// that stays the one validated construction path: this is a row builder, not a second door into
    /// the aggregate, and a starting row that would not load back is a failure here rather than a
    /// created account.
    /// </para>
    /// <para>
    /// 🔒 <b><c>private</c>: the row is declared once and reached through the three factories above,
    /// each of which states what it does about the name.</b> The name is stored here without passing
    /// the hero-name rule because by this point the decision has already been made — a
    /// <see cref="HeroName"/> has been through the filter, an id was never player text, and the
    /// harness's door says in its own name that it skipped it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="MissingContentException">The content set does not author a Legend Level range.</exception>
    /// <exception cref="UnauthorisedTunableException">That range holds a deliberate <c>null</c>.</exception>
    /// <exception cref="InvalidTunableException">That range is authorised but unusable.</exception>
    private static Result<Player> StartingRow(
        PlayerId id,
        string displayName,
        DateTimeOffset nowUtc,
        ContentSnapshot content,
        InventorySnapshot? inventory = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        var legend = LegendTuning.Read(content);

        var snapshot = new PlayerSnapshot(
            SnapshotSchema.SchemaVersion,
            id,
            displayName,
            legend.Minimum,
            LegendXp: 0L,
            RunsStarted: 0L,
            WalletCurrencies.ToDictionary(currency => currency, _ => 0L),
            new EnergyBanks(0, 0),
            EnergyAnchorUtc: nowUtc,
            LastAppliedAtUtc: nowUtc,
            FtueBeat.B0,
            FtueCompletedAtUtc: null,
            GameCalendar.GameDayStartAt(nowUtc),
            new Dictionary<string, long>(StringComparer.Ordinal),
            GameCalendar.GameWeekStartAt(nowUtc),
            new Dictionary<string, long>(StringComparer.Ordinal),
            LoginCalendarTuning.FirstDay,
            LoginCalendarDayClaimed: false,

            // Stated rather than skipped: this is the one field on the row whose own declaration
            // authors null as "nothing cleared yet", so leaving it to the default would be the only
            // value here a reader could not tell from a field somebody forgot.
            ClearedChapterTiers: null,
            FeatCounters: new Dictionary<string, long>(StringComparer.Ordinal),
            PityCounters: new Dictionary<string, int>(StringComparer.Ordinal),
            Inventory: inventory ?? new InventorySnapshot(0, [], []),
            AutoSalvageRules: [],

            // A point is granted on the way up; a player at the floor has made no level-up.
            TalentPoints: 0L,
            Loadout: new LoadoutSnapshot(new Dictionary<GearSlot, GearInstanceId>(0)),
            Presets: []);

        return Rehydrate(snapshot, content);
    }

    /// <summary>The one validated entry point for a persisted player: a corrupt row fails loudly at the seam.</summary>
    /// <param name="snapshot">The persisted row.</param>
    /// <param name="content">
    /// The version-stamped content snapshot the command is reading; the Legend Level range is a
    /// tunable read from it.
    /// </param>
    /// <returns>
    /// The rehydrated aggregate, or a failure listing every validation the row failed, not just the
    /// first.
    /// </returns>
    /// <remarks>
    /// An unknown <see cref="PlayerSnapshot.SchemaVersion"/> hard-fails first and alone: no migration
    /// code exists yet, so a row from another version has no reader and "close enough" layouts would
    /// silently shift a field. A bad content set throws rather than failing: a missing or unauthorised
    /// tunable is every player's problem and belongs at the composition root, not in a per-row Result.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> or <paramref name="content"/> is null.</exception>
    /// <exception cref="MissingContentException">The content set is missing a tunable this validation needs.</exception>
    /// <exception cref="UnauthorisedTunableException">A tunable this validation needs holds a deliberate <c>null</c>.</exception>
    /// <exception cref="InvalidTunableException">A tunable this validation needs is authorised but unusable.</exception>
    public static Result<Player> Rehydrate(PlayerSnapshot snapshot, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(content);

        if (snapshot.SchemaVersion != SnapshotSchema.SchemaVersion)
        {
            return Result<Player>.Failure(
                "PlayerSnapshot.SchemaVersion is " + Text(snapshot.SchemaVersion) + "; this build " +
                "reads " + Text(SnapshotSchema.SchemaVersion) + " and NO MIGRATION EXISTS. 14 §16.6 " +
                "makes a field added, removed or reordered a versioned migration, and the M1 kickoff " +
                "ruled that no migration code is written before soft launch (written migrations " +
                "become mandatory at M18). Reading this row against the current layout would shift " +
                "every field after the first change by one position, silently, for every player who " +
                "has it. Refusing is the loud failure at the seam 30 §11.3 asks for.");
        }

        var legend = LegendTuning.Read(content);
        var faults = new List<string>();

        // No energy check here, deliberately: the upper bound is enforced on mutation instead (see
        // the type's remarks), and the lower bound needs none — EnergyBanks refuses negative amounts
        // itself, and default(EnergyBanks) is (0, 0), a legitimate state.
        RequireIdentity(snapshot, faults);
        RequireProfile(snapshot, legend, faults);
        var wallet = ReadWallet(snapshot, faults);
        RequireTimestamps(snapshot, faults);
        RequireFtue(snapshot, faults);
        var daily = ReadCounters(snapshot.DailyCounters, nameof(PlayerSnapshot.DailyCounters), faults);
        var weekly = ReadCounters(snapshot.WeeklyCounters, nameof(PlayerSnapshot.WeeklyCounters), faults);
        RequireLoginCalendar(snapshot, faults);
        var clearedChapterTiers = ReadClearedChapterTiers(snapshot.ClearedChapterTiers, faults);
        var featCounters = ReadCounters(
            snapshot.FeatCounters, nameof(PlayerSnapshot.FeatCounters), faults, LifetimeLifespan);
        var pityCounters = ReadPityCounters(snapshot, faults);
        var inventory = ReadInventory(snapshot.Inventory, faults);
        var autoSalvageRules = ReadAutoSalvageRules(snapshot.AutoSalvageRules, faults);
        RequireTalentPoints(snapshot, faults);
        var loadout = ReadLoadout(snapshot.Loadout, faults);
        var presets = ReadPresets(snapshot.Presets, faults);
        RequireEquippedItemsAreOwned(loadout, inventory, faults);

        // The `is null` arms are unreachable while `faults` is empty — every path that returns null
        // also adds a fault — but written as a pattern so the correlation is checked, not asserted.
        if (faults.Count > 0 || wallet is null || daily is null || weekly is null ||
            clearedChapterTiers is null || featCounters is null || pityCounters is null ||
            inventory is null || autoSalvageRules is null || loadout is null || presets is null)
        {
            return Result<Player>.Failure(
                "This PlayerSnapshot is not a state the game can be in (" + Text(faults.Count) +
                " problem(s)): " + string.Join(" | ", faults));
        }

        return Result<Player>.Success(new Player(
            snapshot.Id,
            snapshot.DisplayName,
            snapshot.LegendLevel,
            snapshot.LegendXp,
            snapshot.RunsStarted,
            wallet,
            snapshot.Energy,
            snapshot.EnergyAnchorUtc,
            snapshot.LastAppliedAtUtc,
            snapshot.FtueBeatId,
            snapshot.FtueCompletedAtUtc,
            snapshot.DailyPeriodStartUtc,
            daily,
            snapshot.WeeklyPeriodStartUtc,
            weekly,
            snapshot.LoginCalendarDay,
            snapshot.LoginCalendarDayClaimed,
            clearedChapterTiers,
            featCounters,
            pityCounters,
            inventory,
            autoSalvageRules,
            snapshot.TalentPoints,
            loadout,
            presets));
    }

    /// <summary>
    /// Reads the auto-salvage filter. <c>null</c> is a <b>fault</b>, on <c>FeatCounters</c>' and the
    /// inventory's precedent: an absent filter and an empty one are indistinguishable once read, and
    /// only one of them is a row the game wrote.
    /// </summary>
    /// <remarks>
    /// The rows are copied rather than held, for the reason the counter maps are: the caller's list
    /// stays writable, and a record compares an <c>IReadOnlyList&lt;T&gt;</c> component by reference,
    /// so the sharing would be invisible to every comparison that looked for it.
    /// </remarks>
    private static IReadOnlyList<AutoSalvageRule>? ReadAutoSalvageRules(
        IReadOnlyList<AutoSalvageRule>? rules, List<string> faults)
    {
        if (rules is null)
        {
            faults.Add(
                nameof(PlayerSnapshot.AutoSalvageRules) + " is null. An absent filter is not an " +
                "empty one: read as empty it sweeps nothing, which looks exactly like a player who " +
                "has not configured one — so a row that failed to write its filter would silently " +
                "turn the feature off and nothing would ever say so.");

            return null;
        }

        var copy = new AutoSalvageRule[rules.Count];
        var sound = true;

        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];

            if (!Enum.IsDefined(rule.Rarity))
            {
                sound = false;
                faults.Add(
                    nameof(PlayerSnapshot.AutoSalvageRules) + "[" + Text(i) + "] names band '" +
                    rule.Rarity + "', which is not on the rarity ladder. A filter row over a band " +
                    "nothing can be would sweep nothing, forever, invisibly.");
            }
            else if (rule.BelowEnhanceLevel < 0)
            {
                sound = false;
                faults.Add(
                    nameof(PlayerSnapshot.AutoSalvageRules) + "[" + Text(i) + "] sweeps below level " +
                    Text(rule.BelowEnhanceLevel) + ". No item sits below zero, so a negative " +
                    "ceiling is a row nobody could have set.");
            }

            copy[i] = rule;
        }

        return sound ? Array.AsReadOnly(copy) : null;
    }

    // ---------------------------------------------------------------- hero (M4-10)

    /// <summary>The empty preset list every snapshot of a player with no presets shares.</summary>
    /// <remarks>Safe to share, on <see cref="NoCounters"/>' argument: read-only and empty.</remarks>
    private static readonly IReadOnlyList<LoadoutPresetSnapshot> NoPresets =
        Array.AsReadOnly(Array.Empty<LoadoutPresetSnapshot>());

    /// <summary>The Talent Point total only ever grows, so a negative one is a corrupt row.</summary>
    private static void RequireTalentPoints(PlayerSnapshot snapshot, List<string> faults)
    {
        if (snapshot.TalentPoints >= 0)
        {
            return;
        }

        faults.Add(
            nameof(PlayerSnapshot.TalentPoints) + " is " + Text(snapshot.TalentPoints) + ". 07 §1.1 " +
            "grants a point per Legend Level and nothing anywhere takes one back, so the total " +
            "counts upwards from zero.");
    }

    /// <summary>
    /// Reads what the hero is wearing. <c>null</c> is a <b>fault</b>, on <c>Inventory</c>'s precedent:
    /// an absent loadout read as an empty one silently unequips everything on the first load of a row
    /// that merely failed to write it.
    /// </summary>
    private static Loadout? ReadLoadout(LoadoutSnapshot? snapshot, List<string> faults)
    {
        if (snapshot is null)
        {
            faults.Add(
                nameof(PlayerSnapshot.Loadout) + " is null. An absent loadout is not a naked hero: " +
                "read as empty, it strips the player on the first load of a row that merely failed " +
                "to write it, and the result looks exactly like a player who has equipped nothing.");
            return null;
        }

        var loadout = Loadout.Rehydrate(snapshot);

        if (loadout.IsFailure)
        {
            faults.Add(nameof(PlayerSnapshot.Loadout) + ": " + loadout.Error);
            return null;
        }

        return loadout.Value;
    }

    /// <summary>Reads the saved presets. <c>null</c> is a fault for the reason a null loadout is.</summary>
    /// <remarks>
    /// A repeated slot is refused rather than resolved: two presets in slot 2 make every
    /// <c>APPLY_PRESET</c> that names it ambiguous, and which of the two wins would be an ordering
    /// accident of however the row was written.
    /// </remarks>
    private static SortedDictionary<int, LoadoutPreset>? ReadPresets(
        IReadOnlyList<LoadoutPresetSnapshot>? rows, List<string> faults)
    {
        if (rows is null)
        {
            faults.Add(
                nameof(PlayerSnapshot.Presets) + " is null. An absent preset list is not an empty " +
                "one: 12 §2.2 keeps presets a player may no longer WRITE as presets they may still " +
                "LOAD, so reading absent as empty deletes builds the design set promises to keep.");
            return null;
        }

        var presets = new SortedDictionary<int, LoadoutPreset>();
        var faulted = false;

        foreach (var row in rows)
        {
            if (row is null)
            {
                faults.Add(nameof(PlayerSnapshot.Presets) + " carries a null row, which names no preset.");
                faulted = true;
                continue;
            }

            var preset = LoadoutPreset.Rehydrate(row);

            if (preset.IsFailure)
            {
                faults.Add(nameof(PlayerSnapshot.Presets) + ": " + preset.Error);
                faulted = true;
                continue;
            }

            if (!presets.TryAdd(preset.Value.Slot, preset.Value))
            {
                faults.Add(
                    nameof(PlayerSnapshot.Presets) + " carries two presets in slot " +
                    Text(preset.Value.Slot) + ". One slot is one preset; which of the two an " +
                    "APPLY_PRESET naming that slot would load is an ordering accident.");
                faulted = true;
            }
        }

        return faulted ? null : presets;
    }

    /// <summary>
    /// 🔒 Every equipped identity is one the stock actually holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the cross-component invariant, and it is a <b>fault</b> rather than a tolerated state:
    /// a slot names an item rather than copying one, so a slot naming an id the player does not own
    /// is a dangling reference — the hero screen would show an item that cannot be found, and any
    /// operation that resolved the slot would fail on a row the game itself wrote.
    /// </para>
    /// <para>
    /// ⚠️ It is deliberately <b>not</b> applied to presets. A preset is a record of a build rather
    /// than a claim of ownership: `12` §2.2 keeps presets loadable after Plus lapses, an item can be
    /// salvaged long after a preset named it, and applying a preset restores what is still owned. A
    /// preset validated like the live loadout would make a salvage able to corrupt a save.
    /// </para>
    /// <para>
    /// ⚠️ It also means any operation that destroys an item must take it off the hero in the same
    /// change — <c>Loadout.WithoutItem</c> is the seam for that. That is the intended consequence:
    /// the alternative is a stale slot nobody notices until a screen renders it. 🔴 A load-time fault
    /// alone would be the WRONG half to have: the command that broke the pairing would be accepted
    /// and its state persisted, and the row would then refuse to load for the rest of the account's
    /// life. <see cref="RequireLoadoutResolves"/> is the exit-side half, and it is what makes this
    /// one safe to be a fault at all.
    /// </para>
    /// <para>
    /// ⚠️ An item waiting in the overflow holding list counts as OWNED here, deliberately: the
    /// reference resolves, so the row is not corrupt. Whether such an item may be <em>equipped</em>
    /// is a different question with a different answer — <c>Rules.Hero.LoadoutRules.IsEquippable</c>
    /// says no — and the two rules are meant to disagree. A player whose stock overflowed while
    /// wearing an item keeps wearing it; they simply cannot re-equip it until it is reclaimed.
    /// </para>
    /// </remarks>
    private static void RequireEquippedItemsAreOwned(
        Loadout? loadout, Inventory? inventory, List<string> faults)
    {
        // Both halves already faulted on their own if either is null; a second complaint about the
        // same row would report one defect twice.
        if (loadout is null || inventory is null)
        {
            return;
        }

        foreach (var (slot, item) in loadout.Gear)
        {
            if (inventory.Availability(item) != ItemAvailability.UNKNOWN_ITEM)
            {
                continue;
            }

            faults.Add(
                nameof(PlayerSnapshot.Loadout) + " wears '" + item.Value + "' in " + slot + " and " +
                nameof(PlayerSnapshot.Inventory) + " does not hold it. A slot NAMES an item rather " +
                "than copying one, so this row points at nothing: whatever destroyed the item did " +
                "not take it off the hero, and every screen and rule that resolves the slot would " +
                "fail on state the game wrote itself.");
        }
    }

    // ------------------------------------------------------------ end hero (M4-10)

    /// <summary>
    /// Reads the pity counter map. A <c>null</c> map is a fault, never an empty one.
    /// </summary>
    /// <remarks>
    /// The same asymmetry <see cref="PlayerSnapshot.FeatCounters"/> carries, for a sharper reason: a
    /// lifetime pity counter read as zero is every ladder in the game silently started over, and a
    /// counter that silently starts over is the exact failure the "counters never reset" rule exists
    /// to forbid. The optional default on the record is a C# requirement, not a permitted value.
    /// </remarks>
    private static PityCounters? ReadPityCounters(PlayerSnapshot snapshot, List<string> faults)
    {
        if (snapshot.PityCounters is null)
        {
            faults.Add(
                nameof(PlayerSnapshot.PityCounters) + " is null. An absent lifetime counter map is a " +
                "corrupt row, not an empty one: reading it as empty would put every guarantee this " +
                "player has been building towards back at zero, invisibly.");

            return null;
        }

        try
        {
            return Model.PityCounters.Rehydrate(snapshot.PityCounters);
        }
        catch (ArgumentException malformed)
        {
            faults.Add(nameof(PlayerSnapshot.PityCounters) + " is malformed: " + malformed.Message);

            return null;
        }
    }

    /// <summary>
    /// Reads the persisted stock. <c>null</c> is a <b>fault</b>, on <c>FeatCounters</c>' precedent
    /// and unlike <c>ClearedChapterTiers</c>: reading an absent inventory as an empty one would
    /// delete a player's entire stock on the first load of a row that merely failed to write it, and
    /// the deletion would look exactly like a player who owns nothing.
    /// </summary>
    /// <remarks>
    /// The component's own invariants are delegated rather than restated — a repeated identity, a
    /// row the item constructor refuses — and the capacity ceiling is deliberately not among them:
    /// see <c>Inventory.Rehydrate</c>'s remarks for why a tunable ceiling is enforced on mutation
    /// rather than on load, which is the same rule this aggregate applies to Energy.
    /// </remarks>
    private static Inventory? ReadInventory(InventorySnapshot? snapshot, List<string> faults)
    {
        if (snapshot is null)
        {
            faults.Add(
                nameof(PlayerSnapshot.Inventory) + " is null. An absent inventory is not an empty " +
                "one: read as empty, it destroys everything the player owns on the first load.");
            return null;
        }

        var inventory = Inventory.Rehydrate(snapshot);

        if (inventory.IsFailure)
        {
            faults.Add(nameof(PlayerSnapshot.Inventory) + ": " + inventory.Error);
            return null;
        }

        return inventory.Value;
    }

    /// <summary>Moves one player-scoped wallet currency and produces the <c>CurrencyChanged</c> that attributes it.</summary>
    /// <param name="currency">One of <see cref="WalletCurrencies"/>.</param>
    /// <param name="delta">Signed: positive is income, negative is a spend. Zero is permitted.</param>
    /// <param name="reason">
    /// Why it moved — a stable <c>lower_snake_case</c> token. Never blank; <c>CurrencyChanged</c>
    /// refuses that.
    /// </param>
    /// <returns>The event, with <see cref="DomainEvent.Sequence"/> left at <c>DomainEvent.UnstampedSequence</c>.</returns>
    /// <remarks>
    /// <c>internal</c>; the only public route is <c>GameRules.Apply</c>. Throws rather than returning
    /// a <see cref="Result{T}"/> on an unaffordable spend: the handler is expected to have refused it
    /// as a <c>RejectionReason</c> before it reaches here.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="currency"/> is not player-scoped, or the movement would overflow.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank.</exception>
    /// <exception cref="InvalidOperationException">The movement would take the balance negative.</exception>
    internal CurrencyChanged MoveCurrency(CurrencyId currency, long delta, string reason)
    {
        RequireWalletCurrency(currency, nameof(currency));

        var balance = _wallet[currency];

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
                "negative' an invariant of the Player aggregate. A spend the player cannot afford " +
                "is refused by the handler as a RejectionReason before it reaches the aggregate; " +
                "reaching here means a rule debited without checking.");
        }

        return MoveBalance(currency, delta, next, _energy, reason);
    }

    /// <summary>
    /// Grants Legend XP from a run's <c>FinalPayout</c>. Not a <see cref="CurrencyId"/> movement, so
    /// it emits no <c>CurrencyChanged</c>. Turning the new total into a <see cref="LegendLevel"/> is a
    /// later milestone's levelling curve; this seam only ever raises the lifetime total.
    /// </summary>
    /// <param name="amount">Legend XP to add. Never negative.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is negative, or the total would overflow.</exception>
    internal void GrantLegendXp(long amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, "Legend XP is a lifetime total; it only ever grows.");
        }

        try
        {
            _legendXp = checked(_legendXp + amount);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Granting " + amount.ToString(CultureInfo.InvariantCulture) + " Legend XP overflows " +
                "a 64-bit lifetime total. An amount this size is an economy defect upstream, not a " +
                "reward to store.");
        }
    }

    /// <summary>Writes the two Energy banks a rule computed, and produces the attributing <c>CurrencyChanged</c>.</summary>
    /// <param name="banks">
    /// The banks after the rule. The aggregate does not compute them; it refuses them if they break
    /// the invariant.
    /// </param>
    /// <param name="tuning">The energy numbers, so the caps can be derived for this Legend Level.</param>
    /// <param name="reason">Why Energy moved. A stable <c>lower_snake_case</c> token.</param>
    /// <returns>
    /// A <c>CurrencyChanged</c> for <see cref="CurrencyId.ENERGY"/> whose <c>Delta</c> is the change
    /// in both banks together — overflow into the Reserve is one movement, not two.
    /// </returns>
    /// <remarks>
    /// The ceiling is <c>max(cap, where the bank already is)</c>, not <c>cap</c>: a balance patch that
    /// lowers the cap must not throw on a player who was already full and has not been touched.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank.</exception>
    /// <exception cref="InvalidOperationException">The banks would exceed their ceiling.</exception>
    internal CurrencyChanged SetEnergy(EnergyBanks banks, EnergyTuning tuning, string reason)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        var max = tuning.MaxEnergyAt(LegendLevel);

        RequireWithinCeiling(banks.Energy, _energy.Energy, max, "the main Energy bar", "10 §3");
        RequireWithinCeiling(
            banks.Reserve, _energy.Reserve, tuning.ReserveCapacityAt(LegendLevel), "the Energy Reserve", "28 C2");

        var delta = ((long)banks.Energy + banks.Reserve) - ((long)_energy.Energy + _energy.Reserve);

        return MoveBalance(CurrencyId.ENERGY, delta, delta, banks, reason);
    }

    /// <summary>The one accrual seam: writes the regenerated banks and moves the anchor by the span that produced them, in one call.</summary>
    /// <param name="banks"><c>EnergyMath.Accrue(...).Banks</c>.</param>
    /// <param name="anchorAdvance"><c>EnergyMath.Accrue(...).AnchorAdvance</c> — the same accrual's, never another's.</param>
    /// <param name="tuning">The energy numbers, so the ceiling can be derived for this Legend Level.</param>
    /// <param name="reason">Why Energy moved. A stable <c>lower_snake_case</c> token.</param>
    /// <remarks>
    /// Takes both halves of one accrual because they are one fact: writing the banks but forgetting
    /// the anchor would re-grant the same span of regeneration on every later command, and no
    /// aggregate-level invariant could catch it since each write is individually legal.
    /// <see cref="SetEnergy"/> stays for grants and spends, which legitimately move a balance without
    /// moving the anchor.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="anchorAdvance"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">The banks would exceed their ceiling.</exception>
    internal CurrencyChanged AccrueEnergy(
        EnergyBanks banks, TimeSpan anchorAdvance, EnergyTuning tuning, string reason)
    {
        // Checked first so a negative advance cannot leave the banks written and the anchor not.
        RequireForwardAnchor(anchorAdvance);

        var change = SetEnergy(banks, tuning, reason);

        AdvanceEnergyAnchor(anchorAdvance);

        return change;
    }

    /// <summary>Advances the regeneration anchor by the span a rule actually accrued.</summary>
    /// <remarks><c>private</c>, reachable only through <see cref="AccrueEnergy"/> — see its remarks.</remarks>
    private void AdvanceEnergyAnchor(TimeSpan accrued)
    {
        RequireForwardAnchor(accrued);

        _energyAnchorUtc += accrued;
    }

    /// <summary>The anchor only moves forwards.</summary>
    private static void RequireForwardAnchor(TimeSpan accrued)
    {
        if (accrued < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(accrued),
                accrued,
                "The regeneration anchor only moves forwards. A negative advance would hand the " +
                "player the same span of regeneration twice on the next command.");
        }
    }

    /// <summary>Advances the lifetime runs-started counter and answers the value the run being started is seeded with.</summary>
    /// <returns>The counter after the increment — the <c>runCounter</c> argument to <c>runSeed</c> derivation.</returns>
    /// <remarks>
    /// Returns the value rather than leaving the caller to read <see cref="RunsStarted"/> back, so an
    /// interleaved second <c>START_RUN</c> cannot seed two runs the same way.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The counter would overflow a 64-bit count.</exception>
    internal long BeginRun()
    {
        if (_runsStarted == long.MaxValue)
        {
            throw new InvalidOperationException(
                "The lifetime runs-started counter is at long.MaxValue and cannot advance. 02 §2 " +
                "feeds it into runSeed, so wrapping it to a negative would start re-seeding runs " +
                "with values the player has already played.");
        }

        return ++_runsStarted;
    }

    /// <summary>Records that a command has been applied at <paramref name="nowUtc"/>.</summary>
    /// <param name="nowUtc">
    /// <c>GameContext.NowUtc</c>. Must carry a zero offset and must not precede
    /// <see cref="LastAppliedAtUtc"/>.
    /// </param>
    /// <remarks>
    /// Equal is allowed, strictly-earlier is not: two commands can legitimately share an instant, but
    /// an earlier one means a clock moved backwards — a persistence defect this method refuses rather
    /// than silently accepting, since a backwards anchor would never self-correct.
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
                "The last command was applied at " + Text(_lastAppliedAtUtc) + ", which is after " +
                Text(nowUtc) + ". 30 §2.3 rolls state forward FROM this instant, so moving it " +
                "backwards would replay every reset boundary in between.");
        }

        _lastAppliedAtUtc = nowUtc;
    }

    /// <summary>Clears the daily counters and records the 05:00 UTC boundary they were cleared at.</summary>
    /// <param name="periodStartUtc">
    /// The game-day boundary now in force: 05:00:00.000 UTC exactly, offset zero, and never before
    /// the boundary already recorded.
    /// </param>
    /// <remarks>
    /// The aggregate does not work out which boundary that is — computing it is arithmetic, and stays
    /// out of <c>Model</c>. It holds the invariant that whatever it is handed is a boundary the game
    /// actually has.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Not a 05:00 UTC boundary, or it goes backwards.</exception>
    internal void ResetDailyCounters(DateTimeOffset periodStartUtc)
    {
        RequireGameDayBoundary(periodStartUtc, nameof(periodStartUtc));
        RequireNotBefore(periodStartUtc, _dailyPeriodStartUtc, nameof(periodStartUtc), "daily");

        // The boundary already in force is a no-op, not a clear: this runs as lazy catch-up on every
        // command, and clearing on equality would wipe the day's progress several times an hour.
        if (periodStartUtc == _dailyPeriodStartUtc)
        {
            return;
        }

        _dailyCounters.Clear();
        _dailyPeriodStartUtc = periodStartUtc;
    }

    /// <summary>The weekly half. The game week starts Monday 05:00 UTC.</summary>
    /// <param name="periodStartUtc">A Monday at 05:00:00.000 UTC, offset zero, never going backwards.</param>
    /// <exception cref="ArgumentOutOfRangeException">Not a Monday 05:00 UTC boundary, or it goes backwards.</exception>
    internal void ResetWeeklyCounters(DateTimeOffset periodStartUtc)
    {
        RequireGameDayBoundary(periodStartUtc, nameof(periodStartUtc));

        // The weekday is GameCalendar.WeekStart's, the same definition GameRules.AdvanceTime reads.
        if (periodStartUtc.DayOfWeek != GameCalendar.WeekStart)
        {
            throw new ArgumentOutOfRangeException(
                nameof(periodStartUtc),
                periodStartUtc,
                Text(periodStartUtc) + " is a " + periodStartUtc.DayOfWeek + ". The game week starts " +
                "MONDAY at 05:00 UTC (milestone assumption A2, derived from 27 §4), so a weekly " +
                "boundary on any other day would settle guild weeks and weekly counters against a " +
                "week the rest of the game does not have.");
        }

        RequireNotBefore(periodStartUtc, _weeklyPeriodStartUtc, nameof(periodStartUtc), "weekly");

        // The boundary already in force is a no-op — see ResetDailyCounters.
        if (periodStartUtc == _weeklyPeriodStartUtc)
        {
            return;
        }

        _weeklyCounters.Clear();
        _weeklyPeriodStartUtc = periodStartUtc;
    }

    /// <summary>Advances the login calendar to the next day, which becomes open and unclaimed. The pointer only; nothing is paid out.</summary>
    /// <param name="tuning">The calendar numbers, so the cycle wrap is read from tuning rather than hard-coded.</param>
    /// <remarks>
    /// The pause rule is enforced here: it advances only when the currently open day has been
    /// claimed, so an unclaimed day is a silent no-op rather than a refusal — nothing is skipped or
    /// lost. "At most once per game day" is deliberately not checked here; that is the caller's
    /// per-game-day idempotence to enforce. It emits no event (a calendar day is not a currency) and
    /// returns nothing, since <see cref="LoginCalendarDay"/> and <see cref="LoginCalendarDayClaimed"/>
    /// already answer whether it moved.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
    internal void AdvanceLoginCalendar(LoginCalendarTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        if (!_loginCalendarDayClaimed)
        {
            return;
        }

        _loginCalendarDay = tuning.DayAfter(_loginCalendarDay);
        _loginCalendarDayClaimed = false;
    }

    /// <summary>Registers and advances a daily counter. The counter comes into existence on its first increment.</summary>
    /// <param name="counterKey">A stable <c>lower_snake_case</c> key owned by the system that counts. Deliberately not a closed enum.</param>
    /// <param name="amount">How much to add. Never negative — a counter counts, it does not settle.</param>
    /// <exception cref="ArgumentException"><paramref name="counterKey"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is negative, or the count overflows.</exception>
    internal void CountDaily(string counterKey, long amount) =>
        Count(_dailyCounters, counterKey, amount, "daily");

    /// <inheritdoc cref="CountDaily"/>
    internal void CountWeekly(string counterKey, long amount) =>
        Count(_weeklyCounters, counterKey, amount, "weekly");

    /// <summary>Advances a lifetime feat counter. The counter comes into existence on its first increment.</summary>
    /// <param name="counterId">A stable <c>lower_snake_case</c> id owned by the projection that counts. Deliberately not a closed enum.</param>
    /// <param name="amount">How much to add. Always positive.</param>
    /// <remarks>
    /// 🔒 <b>Nothing anywhere lowers or clears one of these.</b> An achievement is claimed
    /// retroactively against the count, so a reset pays out against a history the player did not
    /// have — and the real history cannot be recovered, because nothing retains the events it was
    /// taken from.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="counterId"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is not positive, or the count overflows.</exception>
    internal void CountFeat(string counterId, long amount)
    {
        if (string.IsNullOrWhiteSpace(counterId))
        {
            throw new ArgumentException(BlankFeatCounterId, nameof(counterId));
        }

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "A lifetime feat counter only ever grows; '" + counterId + "' cannot be advanced " +
                "by " + Text(amount) + ". A negative advance is a refund and belongs in the rule " +
                "that granted it; a zero advance registers a counter nothing has counted.");
        }

        _featCounters.TryGetValue(counterId, out var current);

        try
        {
            _featCounters[counterId] = checked(current + amount);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Advancing the lifetime feat counter '" + counterId + "' by " + Text(amount) +
                " from " + Text(current) + " overflows a 64-bit count.");
        }
    }

    /// <summary>Advances the tutorial to the next beat, as each beat's interaction completes.</summary>
    /// <param name="beat">The beat now reached. Strictly after the current one.</param>
    /// <remarks>Strictly forwards, and refused outright once the tutorial is complete.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="beat"/> is undefined or not later.</exception>
    /// <exception cref="InvalidOperationException">The tutorial is already complete.</exception>
    internal void AdvanceFtue(FtueBeat beat)
    {
        if (!Enum.IsDefined(beat))
        {
            throw new ArgumentOutOfRangeException(
                nameof(beat),
                beat,
                "19 D7 runs beatId over B0..B10 plus B6b and nothing else.");
        }

        if (IsFtueComplete)
        {
            throw new InvalidOperationException(
                "The tutorial completed at " + Text(_ftueCompletedAtUtc!.Value) + "; it cannot " +
                "advance to " + beat + ". 19 D6: no FTUE surface ever appears again once it is " +
                "complete.");
        }

        if (beat <= _ftueBeat)
        {
            throw new ArgumentOutOfRangeException(
                nameof(beat),
                beat,
                "The tutorial is at " + _ftueBeat + "; " + beat + " is not later. 19 D7 advances a " +
                "beat as its interaction completes and its resume table re-presents the current " +
                "beat, so a beat that does not move forwards is a replayed command.");
        }

        _ftueBeat = beat;
    }

    /// <summary>Completes the tutorial. Set once beat 10's spend commits; no FTUE surface appears again after.</summary>
    /// <param name="atUtc">When it completed. Offset zero.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="atUtc"/> carries a non-zero offset.</exception>
    /// <exception cref="InvalidOperationException">The tutorial is not at beat 10, or is already complete.</exception>
    internal void CompleteFtue(DateTimeOffset atUtc)
    {
        RequireZeroOffset(atUtc, nameof(atUtc));

        if (IsFtueComplete)
        {
            throw new InvalidOperationException(
                "The tutorial already completed at " + Text(_ftueCompletedAtUtc!.Value) + ". 19 D5's " +
                "payout is granted once, keyed on the run; completing twice would grant it twice.");
        }

        if (_ftueBeat != FtueBeat.B10)
        {
            throw new InvalidOperationException(
                "The tutorial is at " + _ftueBeat + ", not " + FtueBeat.B10 + ". 19 D7 completes it " +
                "when beat 10's spend commits — completing earlier would skip the forced Talent " +
                "Point spend that beat 10 exists for.");
        }

        _ftueCompletedAtUtc = atUtc;
    }

    /// <summary>The one place a balance changes, and therefore the one place that attributes the change.</summary>
    /// <remarks>
    /// Both currency stores are written here — <c>_wallet</c> for the wallet rows, <c>_energy</c> for
    /// <c>ENERGY</c> — so the IL scan requiring a <c>CurrencyChanged</c> on every currency-field write
    /// covers both. Validation belongs to the callers, so this method has exactly one job.
    /// </remarks>
    private CurrencyChanged MoveBalance(
        CurrencyId currency, long delta, long balance, EnergyBanks banks, string reason)
    {
        // Built before the write: CurrencyChanged refuses a blank Reason in its own initialiser, so
        // constructing it after the write would leave the balance moved with no attribution.
        var change = new CurrencyChanged(DomainEvent.UnstampedSequence, currency, delta, reason);

        if (currency == CurrencyId.ENERGY)
        {
            _energy = banks;
        }
        else
        {
            // `balance` is already the CHECKED sum MoveCurrency computed; re-adding here would redo
            // it unchecked, past the overflow guard the caller already ran.
            var next = new Dictionary<CurrencyId, long>(_wallet) { [currency] = balance };
            _wallet = new ReadOnlyDictionary<CurrencyId, long>(next);
        }

        return change;
    }

    private void RequireWithinCeiling(int next, int current, int cap, string bank, string citation)
    {
        var ceiling = Math.Max(cap, current);
        if (next <= ceiling)
        {
            return;
        }

        throw new InvalidOperationException(
            Text(next) + " exceeds " + Text(ceiling) + ", the ceiling for " + bank + " at Legend " +
            "Level " + Text(LegendLevel) + " (" + citation + ", cap " + Text(cap) + ", currently " +
            Text(current) + "). 30 §11.5 makes 'Energy never exceeds max + reserve' an invariant of " +
            "the Player aggregate. The ceiling is the higher of the cap and where the bank already " +
            "stands, so a player left above the cap by a balance patch can still be written back " +
            "unchanged and drain by playing — but nothing may push a bank further past it.");
    }

    private static void RequireWalletCurrency(CurrencyId currency, string parameterName)
    {
        if (WalletCurrencies.Contains(currency))
        {
            return;
        }

        var because = currency switch
        {
            CurrencyId.GOLD =>
                "GOLD is RUN-scoped (10 §1, tuning/currencies.json, milestone assumption A3): it is " +
                "spent or lost when the run ends and lives on the Run aggregate, not here.",
            CurrencyId.ENERGY =>
                "ENERGY is player-scoped but has two banks (28 C), so it is held as Player.Energy " +
                "and moved through Player.SetEnergy — a single wallet row could not describe the " +
                "main bar and the Reserve.",
            _ =>
                "10 §1 fixes eight currencies and this is not one of them; an undefined CurrencyId " +
                "is an uninitialised field, not a balance.",
        };

        throw new ArgumentOutOfRangeException(parameterName, currency, because);
    }

    private static void Count(Dictionary<string, long> counters, string counterKey, long amount, string period)
    {
        RequireCounterKey(counterKey, nameof(counterKey));

        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "A " + period + " counter counts upwards; '" + counterKey + "' cannot be advanced " +
                "by " + Text(amount) + ". 30 §2.3's counters are cleared at their period boundary, " +
                "never settled back down — a negative advance would be a system undoing a use it " +
                "had already recorded, which is a refund and belongs in the rule that granted it.");
        }

        counters.TryGetValue(counterKey, out var current);

        try
        {
            counters[counterKey] = checked(current + amount);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Advancing the " + period + " counter '" + counterKey + "' by " + Text(amount) +
                " from " + Text(current) + " overflows a 64-bit count. A count that large inside " +
                "one period is a loop that did not terminate.");
        }
    }

    private static long CountIn(IReadOnlyDictionary<string, long> counters, string counterKey)
    {
        RequireCounterKey(counterKey, nameof(counterKey));

        return counters.TryGetValue(counterKey, out var count) ? count : 0L;
    }

    /// <summary>The blank-id refusal for a feat counter — a different open key space from <see cref="RequireCounterKey"/>'s.</summary>
    private const string BlankFeatCounterId =
        "A feat counter id names the projection that owns it, so it is never blank. The id space is " +
        "deliberately open rather than a closed enum, because what each Feat measures is a decision " +
        "the milestone that ships Feats still has to take — but 'open' means the owner picks the " +
        "token, not that there is no token.";

    private static void RequireCounterKey(string counterKey, string parameterName)
    {
        if (!string.IsNullOrWhiteSpace(counterKey))
        {
            return;
        }

        throw new ArgumentException(
            "A counter key names the system that owns the counter, so it is never blank. The keys " +
            "are open rather than a closed enum because 30 §2.3's five daily-reset systems do not " +
            "exist yet — but 'open' means the owner picks the token, not that there is no token.",
            parameterName);
    }

    /// <summary>The 05:00 UTC invariant, asked of <see cref="GameCalendar"/> rather than restated.</summary>
    /// <remarks>
    /// This aggregate held its own copy of the boundary hour until <c>GameRules.AdvanceTime</c>
    /// needed the same number and could not call into <c>Model</c> from <c>Rules</c>; both now read
    /// one definition from <c>Primitives</c>.
    /// </remarks>
    private static void RequireGameDayBoundary(DateTimeOffset boundary, string parameterName)
    {
        RequireZeroOffset(boundary, parameterName);

        if (GameCalendar.IsGameDayBoundary(boundary))
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            parameterName,
            boundary,
            Text(boundary) + " is not a game-day boundary. 30 §2.3 resets quest expiry, the wheel's " +
            "free spin, ad caps and dungeon entries at 05:00 UTC 'whether or not anyone logs in', " +
            "so a period that started at any other time of day is a period the rest of the game " +
            "does not agree exists.");
    }

    private static void RequireNotBefore(
        DateTimeOffset boundary, DateTimeOffset current, string parameterName, string period)
    {
        if (boundary >= current)
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            parameterName,
            boundary,
            "The " + period + " counters were last reset at " + Text(current) + ", which is after " +
            Text(boundary) + ". Resetting to an earlier boundary would clear a period that has " +
            "already been counted against, handing back every cap the player has already spent.");
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
            "them different — the same class of defect as the -0.0 the writer already refuses. " +
            "Convert at the edge; the domain stores UTC.");
    }

    private static void RequireIdentity(PlayerSnapshot snapshot, List<string> faults)
    {
        // default(PlayerId) runs no constructor, so its Value is null rather than validated.
        if (string.IsNullOrWhiteSpace(snapshot.Id.Value))
        {
            faults.Add(
                nameof(PlayerSnapshot.Id) + " is blank or default(PlayerId), so this row names no " +
                "player. PlayerId validates in its constructor, which default(PlayerId) never runs.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.DisplayName))
        {
            faults.Add(
                nameof(PlayerSnapshot.DisplayName) + " is blank, which is a row that renders as " +
                "nothing on every screen that shows a name.");
        }

        // 🔒 Blank is still the ONLY thing checked here, and now that the filter exists that is a
        // decision rather than a gap. The word lists are CONTENT and change without a build, so
        // running them at rehydration would make a term added tomorrow refuse to load every account
        // whose name matches it — an authoring edit becoming an outage, which is the same trade the
        // Energy ceiling above and Inventory's capacity already refuse. The filter runs on MUTATION,
        // through Rename, which is the only door into the field. 16 O34 still leaves the rest of the
        // name lifecycle open: uniqueness, rename cost and sanction have no owner here.
    }

    private static void RequireProfile(PlayerSnapshot snapshot, LegendTuning legend, List<string> faults)
    {
        if (snapshot.LegendLevel < legend.Minimum || snapshot.LegendLevel > legend.Maximum)
        {
            faults.Add(
                nameof(PlayerSnapshot.LegendLevel) + " is " + Text(snapshot.LegendLevel) +
                ", outside the " + Text(legend.Minimum) + ".." + Text(legend.Maximum) + " that " +
                "07 §1.1 authors at " + LegendTuning.MinimumReference + " / " +
                LegendTuning.MaximumReference + ". Max Energy is derived from it, and " +
                "EnergyMath.MaxEnergy counts levels GAINED — a level below the minimum subtracts " +
                "from the base tank.");
        }

        if (snapshot.LegendXp < 0)
        {
            faults.Add(
                nameof(PlayerSnapshot.LegendXp) + " is " + Text(snapshot.LegendXp) + ". Legend XP " +
                "is lifetime banked income (02 §5.1a) and is never spent, so it cannot be negative.");
        }

        if (snapshot.RunsStarted < 0)
        {
            faults.Add(
                nameof(PlayerSnapshot.RunsStarted) + " is " + Text(snapshot.RunsStarted) + ". 02 §2 " +
                "calls it the lifetime runs-started counter and feeds it into runSeed, so it counts " +
                "upwards from zero and is never reset — a negative one would seed a run with a " +
                "value no play produced.");
        }
    }

    private static IReadOnlyDictionary<CurrencyId, long>? ReadWallet(PlayerSnapshot snapshot, List<string> faults)
    {
        if (snapshot.Wallet is null)
        {
            faults.Add(nameof(PlayerSnapshot.Wallet) + " is null. An absent wallet is not an empty one.");
            return null;
        }

        var wallet = new Dictionary<CurrencyId, long>(WalletCurrencies.Count);
        var faulted = false;

        foreach (var currency in WalletCurrencies)
        {
            if (!snapshot.Wallet.TryGetValue(currency, out var balance))
            {
                faults.Add(
                    nameof(PlayerSnapshot.Wallet) + " has no row for " + Text(currency) + ". All " +
                    "six player-scoped currencies are always present: a missing row read as zero " +
                    "would be indistinguishable from a balance a migration dropped.");
                faulted = true;
                continue;
            }

            if (balance < 0)
            {
                faults.Add(
                    nameof(PlayerSnapshot.Wallet) + "[" + Text(currency) + "] is " + Text(balance) +
                    ". 30 §11.5 makes 'a currency never goes negative' an invariant of this " +
                    "aggregate.");
                faulted = true;
                continue;
            }

            wallet[currency] = balance;
        }

        foreach (var currency in snapshot.Wallet.Keys)
        {
            if (WalletCurrencies.Contains(currency))
            {
                continue;
            }

            faults.Add(
                nameof(PlayerSnapshot.Wallet) + " carries a row for " + Text(currency) + ", which " +
                "is not player-scoped. GOLD belongs to the Run aggregate (assumption A3) and " +
                "ENERGY is held as PlayerSnapshot.Energy; a second copy of either is a second " +
                "source of truth.");
            faulted = true;
        }

        return faulted ? null : new ReadOnlyDictionary<CurrencyId, long>(wallet);
    }

    private static void RequireTimestamps(PlayerSnapshot snapshot, List<string> faults)
    {
        RequireUtc(snapshot.EnergyAnchorUtc, nameof(PlayerSnapshot.EnergyAnchorUtc), faults);
        RequireUtc(snapshot.LastAppliedAtUtc, nameof(PlayerSnapshot.LastAppliedAtUtc), faults);
        RequireUtc(snapshot.DailyPeriodStartUtc, nameof(PlayerSnapshot.DailyPeriodStartUtc), faults);
        RequireUtc(snapshot.WeeklyPeriodStartUtc, nameof(PlayerSnapshot.WeeklyPeriodStartUtc), faults);

        if (snapshot.FtueCompletedAtUtc is { } completed)
        {
            RequireUtc(completed, nameof(PlayerSnapshot.FtueCompletedAtUtc), faults);
        }

        // Runs only once the instant is confirmed UTC, so one defect reports once, not twice.
        if (snapshot.DailyPeriodStartUtc.Offset == TimeSpan.Zero &&
            !GameCalendar.IsGameDayBoundary(snapshot.DailyPeriodStartUtc))
        {
            faults.Add(
                nameof(PlayerSnapshot.DailyPeriodStartUtc) + " is " +
                Text(snapshot.DailyPeriodStartUtc) + ", which is not a 05:00 UTC game-day boundary " +
                "(30 §2.3).");
        }

        // Both halves of the failure are named, since the guard can fire on either.
        if (snapshot.WeeklyPeriodStartUtc.Offset == TimeSpan.Zero &&
            !GameCalendar.IsGameWeekBoundary(snapshot.WeeklyPeriodStartUtc))
        {
            faults.Add(
                nameof(PlayerSnapshot.WeeklyPeriodStartUtc) + " is " +
                Text(snapshot.WeeklyPeriodStartUtc) + " — a " + snapshot.WeeklyPeriodStartUtc.DayOfWeek +
                " at " + Text(snapshot.WeeklyPeriodStartUtc.TimeOfDay) + " UTC. The game week starts " +
                "MONDAY 05:00 UTC (milestone assumption A2, derived from 27 §4).");
        }
    }

    private static void RequireUtc(DateTimeOffset instant, string field, List<string> faults)
    {
        if (instant.Offset == TimeSpan.Zero)
        {
            return;
        }

        faults.Add(
            field + " is " + Text(instant) + ", carrying a " + Text(instant.Offset) + " offset. " +
            "Every persisted instant is UTC: CanonicalStateWriter encodes a DateTimeOffset as Unix " +
            "milliseconds, so two offsets naming one instant share a stateHash while record " +
            "equality calls the two snapshots different.");
    }

    private static void RequireFtue(PlayerSnapshot snapshot, List<string> faults)
    {
        if (!Enum.IsDefined(snapshot.FtueBeatId))
        {
            faults.Add(
                nameof(PlayerSnapshot.FtueBeatId) + " is " + Text((int)snapshot.FtueBeatId) +
                ", which is not one of 19 D7's beats (B0..B10 plus B6b). FtueBeat has no zero " +
                "member on purpose, so this is what an uninitialised column reads as.");
        }

        if (snapshot.FtueCompletedAtUtc is not null && snapshot.FtueBeatId != FtueBeat.B10)
        {
            faults.Add(
                nameof(PlayerSnapshot.FtueCompletedAtUtc) + " is set while " +
                nameof(PlayerSnapshot.FtueBeatId) + " is " + snapshot.FtueBeatId + ". 19 D7 " +
                "completes the tutorial when beat 10's spend commits, so a completed tutorial is " +
                "always at B10 — this row claims a payout was banked at a beat that never reached it.");
        }
    }

    /// <summary>The login calendar's floor, and only its floor.</summary>
    /// <remarks>
    /// No upper bound here either: a shortened cycle from a balance patch would otherwise turn a
    /// tuning change into an outage. <c>LoginCalendarTuning.DayAfter</c> wraps on the next advance
    /// instead of failing to load.
    /// </remarks>
    private static void RequireLoginCalendar(PlayerSnapshot snapshot, List<string> faults)
    {
        if (snapshot.LoginCalendarDay < LoginCalendarTuning.FirstDay)
        {
            faults.Add(
                nameof(PlayerSnapshot.LoginCalendarDay) + " is " + Text(snapshot.LoginCalendarDay) +
                ". 19 G numbers the login calendar from day " + Text(LoginCalendarTuning.FirstDay) +
                "; day 0 is what an uninitialised column reads as, and a player standing on it would " +
                "be paid one day behind the table for the rest of the cycle.");
        }
    }

    private static Dictionary<string, long>? ReadCounters(
        IReadOnlyDictionary<string, long>? counters,
        string field,
        List<string> faults,
        string lifespan = PeriodicLifespan)
    {
        if (counters is null)
        {
            faults.Add(field + " is null. An absent counter map is not an empty one.");
            return null;
        }

        // Copied into an ORDINAL dictionary: CanonicalStateWriter orders string keys ordinally, so
        // any other comparer would round-trip to a different hash than the one it was stored under.
        var copy = new Dictionary<string, long>(counters.Count, StringComparer.Ordinal);
        var faulted = false;

        foreach (var (key, count) in counters)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                faults.Add(field + " carries a blank counter key. A counter key names its owner.");
                faulted = true;
                continue;
            }

            if (count < 0)
            {
                faults.Add(
                    field + "['" + key + "'] is " + Text(count) + ". A counter counts upwards from " +
                    "zero and " + lifespan + "; it is never settled back down.");
                faulted = true;
                continue;
            }

            copy[key] = count;
        }

        return faulted ? null : copy;
    }

    /// <summary>Reads <see cref="PlayerSnapshot.ClearedChapterTiers"/>. Unlike <see cref="ReadCounters"/>, <c>null</c> is not a fault.</summary>
    /// <remarks>This field was appended with a defaulted parameter, so a row from before it existed reads as "nothing cleared yet".</remarks>
    private static Dictionary<string, long>? ReadClearedChapterTiers(
        IReadOnlyDictionary<string, long>? clearedChapterTiers, List<string> faults)
    {
        if (clearedChapterTiers is null)
        {
            return new Dictionary<string, long>(StringComparer.Ordinal);
        }

        return ReadCounters(clearedChapterTiers, nameof(PlayerSnapshot.ClearedChapterTiers), faults);
    }

    /// <summary>How long a counter read by <see cref="ReadCounters"/> lives, for the corrupt-row message.</summary>
    private const string PeriodicLifespan = "is cleared at its period boundary";

    /// <inheritdoc cref="PeriodicLifespan"/>
    private const string LifetimeLifespan = "is never cleared at all";

    /// <summary>The empty counter map every snapshot of a player with no counters shares.</summary>
    /// <remarks>Safe to share: it is read-only and empty, so nothing can distinguish a shared instance from a private one.</remarks>
    private static readonly ReadOnlyDictionary<string, long> NoCounters =
        new(new Dictionary<string, long>(0, StringComparer.Ordinal));

    /// <summary>An ordinal copy of a counter map, so no caller shares the aggregate's dictionary.</summary>
    /// <remarks>
    /// Short-circuits on empty — this runs on every command, since the client recomputes its own
    /// state hash too. That is still the normal case for the daily and weekly maps, which clear at
    /// their boundary, and stops being one for the lifetime map after a player's first counted
    /// event: from then on it copies, and it must.
    /// </remarks>
    private static ReadOnlyDictionary<string, long> Copy(Dictionary<string, long> counters) =>
        counters.Count == 0
            ? NoCounters
            : new ReadOnlyDictionary<string, long>(new Dictionary<string, long>(counters, StringComparer.Ordinal));

    /// <summary>Renders a value with <see cref="CultureInfo.InvariantCulture"/>, so messages read the same on every host.</summary>
    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc cref="Text(int)"/>
    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc cref="Text(int)"/>
    private static string Text(CurrencyId value) => value.ToString();

    /// <inheritdoc cref="Text(int)"/>
    private static string Text(TimeSpan value) => value.ToString("c", CultureInfo.InvariantCulture);

    /// <inheritdoc cref="Text(int)"/>
    private static string Text(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
}
