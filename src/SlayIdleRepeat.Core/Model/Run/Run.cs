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
/// board, the drafted perks, the held consumables (with the armed Escape Rope flag), the pending
/// fork choice and the curses — five of §4's ten items — are all deferred with an entry in
/// <c>SlayIdleRepeat.Architecture.Tests.GapRegister</c>, each keyed on a type that must not yet
/// exist, so the build fails on the day each becomes writable. The run's <b>phase</b> is deferred
/// too, and that one has a consequence worth stating: without it <c>Apply</c> cannot produce
/// `14` §16.2's <c>RUN_ALREADY_ENDED</c> or <c>ILLEGAL_STATE</c>, and M3-05 pays a
/// <see cref="SnapshotSchema.SchemaVersion"/> bump for it.
/// </para>
/// </remarks>
public sealed class Run
{
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

    private DateTimeOffset _lastAppliedAtUtc;
    private int _position;
    private int _currentHp;
    private int _maxHp;

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
        Dictionary<string, long> adUses)
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
    }

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
    /// ⚠️ <b>No upper bound and no existence check, deliberately.</b> <c>content/chapters/</c> is
    /// empty and <c>chapter.schema.json</c> sits on <c>ContentLoader.SchemasAwaitingContent</c> for
    /// exactly that reason (M3-14). Hard-coding <c>8</c> in <c>Core</c> would put a content bound in
    /// code (`21` §3.1) and — worse — would be a <em>partial</em> invariant wearing the real one's
    /// name, the same trap as <see cref="Position"/>. The deferral already has a live, self-expiring
    /// mechanism: <c>RealDataSetTests.An_exemption_that_outlived_its_milestone_fails_the_build</c>
    /// fails the build when M3-14's exemption outlives its milestone, so no second mechanism is
    /// built here (steering S4).
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

    /// <summary>The linear node index the run stands on (`14` §2.3's <c>newPosition</c>).</summary>
    /// <remarks>
    /// ⚠️ <b>Stored, and only checked for non-negativity.</b> `30` §11.5 names <em>"a run's position
    /// is a valid node"</em> as an invariant of this aggregate and it <b>cannot be implemented
    /// today</b>: there is no board and no node identity until M3-01. Inventing a range check —
    /// "0..40", say — would be a partial invariant that reads like the real one and would be trusted
    /// as such by everything downstream, which is worse than an absent one. The real validation is
    /// registered as the <c>Board</c> entry in
    /// <c>SlayIdleRepeat.Architecture.Tests.GapRegister</c>, keyed on <c>NodeId</c>, so the build
    /// fails on the day node identity arrives.
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
        CopyAdUses(_adUses));

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

        // The two `is null` arms are unreachable while `faults` is empty — every path that returns
        // null also adds a fault — but they are written as a pattern rather than as two `!`
        // operators so the correlation is checked rather than asserted at the compiler.
        if (faults.Count > 0 || streams is null || adUses is null)
        {
            return Result<Run>.Failure(
                "This RunSnapshot is not a state the game can be in (" + Text(faults.Count) +
                " problem(s)): " + string.Join(" | ", faults));
        }

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
            adUses));
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
    /// <param name="position">The new linear node index. Never negative.</param>
    /// <remarks>
    /// ⚠️ Non-negativity is the <b>whole</b> check, and see <see cref="Position"/> for why: `30`
    /// §11.5's <em>"a run's position is a valid node"</em> needs node identity, which is M3-01's.
    /// Movement is not required to be forwards — `03` §1.1's Portal jumps and the board's back-edges
    /// move a run in both directions, so a monotonicity guard here would refuse legal play.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is negative.</exception>
    internal void MoveTo(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);

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
    /// The offending key is quoted <b>exactly</b> as it arrived, case and all. The registry is
    /// ordinal, so <c>DICE</c> and <c>dice</c> are two different questions, and a message that
    /// normalised the key would point a reader at a row the data does not carry.
    /// </remarks>
    private static void RequireRegisteredStream(string streamName, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(streamName, parameterName);

        if (RngStreams.IsRegistered(streamName))
        {
            return;
        }

        throw new ArgumentException(
            "'" + streamName + "' is not a row of the 14 §8.1 stream registry, which is the eight " +
            "fixed names (" + string.Join(", ", RngStreams.FixedNames) + ") plus minigame:{index} " +
            "for a non-negative index in canonical decimal form. The comparison is ordinal and " +
            "case-sensitive, and minigame:03 is deliberately a different string from minigame:3: a " +
            "name the registry does not recognise cannot be drawn from, so a run cannot stand at a " +
            "position in it either.",
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
                "deliberately no upper bound: content/chapters/ is empty and the schema sits on " +
                "ContentLoader.SchemasAwaitingContent (M3-14), so a ceiling here would be a content " +
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
        if (snapshot.Position < 0)
        {
            faults.Add(
                nameof(RunSnapshot.Position) + " is " + Text(snapshot.Position) + ". ⚠️ That is the " +
                "WHOLE position check: 30 §11.5's 'a run's position is a valid node' needs node " +
                "identity, which is M3-01's and is registered as the Board entry in the gap " +
                "register. A range check invented here would be a partial invariant wearing the " +
                "real one's name.");
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
