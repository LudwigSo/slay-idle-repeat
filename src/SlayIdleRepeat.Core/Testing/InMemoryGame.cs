using System.Collections.ObjectModel;
using System.Globalization;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Testing;

/// <summary>
/// 🔒 `30` §6 — the harness. <em>"The concrete artefact that makes the claim testable. It is the
/// only thing tests and the economy simulator need to construct."</em>
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>This type is what completes
/// <c>DomainPurityTests.The_whole_game_is_playable_from_Core_alone</c>.</b> ⚠️ <b>Not "wakes", and
/// the difference is the finding.</b> That rule — `30` §9's "load-bearing" one — has two arms, and
/// only one of them was dead. The <em>closure</em> arm walks <c>Core</c>'s real assembly references
/// and has been asserting since M0: a package or project reference added to
/// <c>SlayIdleRepeat.Core.csproj</c> turned it red long before this type existed, whoever named the
/// reference. What was dead is the arm behind <c>harness is not null</c> — the one that says the
/// harness must exist in <c>Core</c> and be usable from outside it — because there was no such type,
/// which is why the rule's <em>name</em> was a claim nothing checked.
/// </para>
/// <para>
/// From this commit there is, it lives in the production <c>Core</c> assembly (`30` §11.4 puts
/// <c>Testing/</c> inside it), and everything it references therefore ships. Be deliberate about
/// what it pulls in: the closure arm is what stops a convenient <c>PackageReference</c>, and the
/// presence arm plus <c>GapRegister</c>'s `30` §6 transcription are what stop this file being
/// deleted with the rule still green.
/// </para>
/// <para>
/// 🔒 <b>It mutates state through <c>GameRules.Apply</c> and through nothing else.</b> `30` §11.2's
/// <em>"the only public way to change state in this game is <c>GameRules.Apply</c>"</em> is not a
/// convenience this harness may opt out of because it is a test tool — it is the property the
/// harness exists to demonstrate. There is no <c>Restore(PlayerSnapshot)</c>, no
/// <c>SetEnergy(...)</c> and no door onto an aggregate's <c>internal</c> mutators, even though
/// this type sits inside <c>Core</c> and could reach every one of them.
/// <c>Core_internal_layering_holds</c> gained a <c>Testing</c> row on this commit that makes the
/// narrower half structural: this namespace may not name <c>Core/Rules/</c> or
/// <c>Core/Handlers/</c>, so the harness cannot call <c>BeginSession.Handle</c> or
/// <c>EnergyMath.Grant</c> behind <c>Apply</c>'s back.
/// </para>
/// <para>
/// 🔒 <b>Three corrections to `30` §6's sketch, each already ruled and each recorded here rather
/// than left for a reader to trip over.</b>
/// </para>
/// <list type="number">
///   <item><b><c>ContentSnapshot.LoadFromDisk("game-data")</c> is a documented erratum</b> (M1
///   kickoff decision 4). Loading JSON is I/O and belongs in an adapter (`14` §6, `30` §3), and a
///   <c>Core</c> type that read a directory would fail
///   <c>The_whole_game_is_playable_from_Core_alone</c> the moment it needed anything to parse with.
///   This constructor takes a <b>pre-built</b> snapshot. <c>ContentSnapshot</c>'s public constructor
///   over in-memory documents is what lets <c>Core.Tests</c> stay hermetic; anything that wants the
///   real <c>game-data/</c> is a composition root over
///   <c>{ Core, Application, Adapters.Content.LocalFile }</c> — <c>EconomySim</c> (M6), the balance
///   harness (`05` §9), and one real-data smoke test that belongs in <c>Application.Tests</c> and
///   never in <c>Core.Tests</c>.</item>
///   <item><b><see cref="State"/> answers a <see cref="WorldSlice"/>, not a <c>Player</c>.</b> §6
///   was written before M1-06 authored `30` §4.1's slice and spells the assertion
///   <c>game.State(player).Energy</c>; it is <c>game.State(player).Player.Energy</c> here. The slice
///   is what <c>Apply</c> reads and returns, it is the unit `30` §4.1 makes the Application layer's
///   job to load, and it is where M3's <c>Run</c> arrives — a harness that stored bare aggregates
///   would have to grow a second door for the run and would then have two answers to "what is this
///   player's state".</item>
///   <item><b>The sketch's <c>Should().Be(...)</c> is FluentAssertions</b>, which M1-00 replaced
///   with Shouldly, and its <c>StartRun</c>/<c>RollDice</c>/<c>PickPerk</c> are not `14` §2.3's
///   names. Both are the assertions' problem rather than this type's; the registry is the authority
///   on the second (<c>GameRules</c>'s dispatch table).</item>
/// </list>
/// <para>
/// ⚠️ <b>What a "multi-day player" can actually be driven through today, stated plainly.</b>
/// <c>BEGIN_SESSION</c> is the <b>only</b> <c>Handled</c> row of `14` §2.3's forty-nine. So the loop
/// this harness runs is <em>advance the clock, send <c>BEGIN_SESSION</c>, observe the catch-up, the
/// daily free refill, the calendar and the counters</em> — the day cycle, Energy and the currency
/// seam, which is exactly what M1's exit criterion names. It is not a run: nothing in M1 can start
/// one.
/// </para>
/// <para>
/// 🔴 <b>And the other forty-eight rows do NOT all answer <c>ILLEGAL_STATE</c>. Twenty-nine do.</b>
/// This is worth stating exactly, because the obvious reading is wrong and M1-11's own first draft
/// had it wrong. <c>GameRules.Execute</c> refuses a <c>CommandKind.Run</c> command whose slice
/// carries no <c>Run</c> <b>before</b> it reaches the <c>IsHandled</c> branch, as a <em>loading
/// defect</em> — an <see cref="InvalidOperationException"/>, because `30` §4.1 makes loading the
/// right slice the Application layer's job and `14` §16.2's <c>RUN_NOT_FOUND</c> is a transport-tier
/// value <c>Apply</c> may not return. This harness's slice is always <c>(player, null)</c>, so
/// <b>all nineteen run rows throw</b> and only the twenty-nine deferred <em>meta</em> rows answer
/// <c>ILLEGAL_STATE</c>. That is the correct behaviour of both types and is pinned by
/// <c>InMemoryGameTests</c>.
/// </para>
/// <para>
/// 🔒 <b>What that means for M3, and it is a finding rather than a caveat.</b>
/// <c>START_RUN</c> is registered <c>CommandKind.Run</c> deliberately — it is the command that
/// commits <c>runSeed</c>, and `30` §3 gives the scope to run commands — so it is refused by the
/// same guard, <em>through any caller</em>, not merely through this harness: there is no
/// <c>WorldSlice</c> a caller can legally build that would let <c>START_RUN</c> through, because
/// only <c>START_RUN</c> can create the <c>Run</c> the guard demands. <b>M3-15 owns the ruling</b>
/// (reclassify the row, or give the dispatch table a state for a <c>Run</c>-kind row that
/// <em>opens</em> a run and has its scope built after the handler). ⚠️ Whichever it picks,
/// <b>nothing in this type moves</b>: <c>(player, null)</c> is already the right starting slice and
/// <see cref="Send"/> stores <c>CommandResult.NewState</c> unconditionally, so the instant
/// <c>Apply</c> returns a slice carrying a run, this harness carries it and every later run command
/// finds it. What must <b>not</b> happen is a door on this type that injects one — see the
/// <c>Apply</c>-only claim above.
/// </para>
/// <para>
/// ⚠️ <b>The zero-delta <c>energy_regen</c> rows in <see cref="Events"/> are intended</b> (recorded
/// assumption <b>A6</b>). An idle player at a full tank emits one per command sent more than one
/// regeneration interval after the last: the anchor still moved, so the accrual is published rather
/// than filtered. A harness that swallowed them would be hiding the rows `21` §8.3's
/// <c>income_attribution.csv</c> is a query over.
/// </para>
/// </remarks>
public sealed class InMemoryGame
{
    /// <summary>
    /// 🔒 `30` §6's <c>Dictionary&lt;PlayerId, Player&gt;</c>, keyed the same and holding the slice
    /// rather than the bare aggregate — see correction 2 in the type's remarks.
    /// </summary>
    private readonly Dictionary<PlayerId, PlayerSession> _players = [];

    /// <summary>
    /// Every event every command has produced, in the order the commands were applied.
    /// </summary>
    /// <remarks>
    /// A <c>List</c> behind a <see cref="ReadOnlyCollection{T}"/> rather than an exposed array, for
    /// the reason <c>Player.WalletCurrencies</c> records: an <c>IReadOnlyList&lt;T&gt;</c> that
    /// <em>is</em> a <c>T[]</c> casts straight back and can be written through, and this list is the
    /// assertion surface — a test that could rewrite it is a test that can pass by editing its own
    /// evidence.
    /// </remarks>
    private readonly List<DomainEvent> _events = [];

    private readonly ReadOnlyCollection<DomainEvent> _eventsView;

    /// <summary>
    /// 🔒 The player ids in <b>creation order</b>, which <see cref="_players"/> cannot answer.
    /// </summary>
    /// <remarks>
    /// <c>Dictionary&lt;TKey, TValue&gt;.KeyCollection</c> enumerates in an order the BCL explicitly
    /// leaves unspecified. It happens to be insertion order while nothing is removed — and that is
    /// exactly the drift this repository refuses everywhere else: <c>Player.ReadCounters</c> copies
    /// into an ordinal dictionary for it, <c>CanonicalStateWriter.KeyOrderFor</c> defines an order
    /// for it, and <c>PlayerId</c>'s own remarks record that an
    /// <c>IReadOnlyDictionary&lt;PlayerId, …&gt;</c> in a snapshot is <em>refused</em> rather than
    /// hashed in an undefined order. It is not pedantry here either: `21` §9's sweep is the natural
    /// consumer of <see cref="Players"/>, and a wrapper that iterated it to drive N profiles would
    /// have a command order undefined by contract — which is "reproducible byte-for-byte" (`30` §6)
    /// resting on an implementation detail.
    /// </remarks>
    private readonly List<PlayerId> _created = [];

    private readonly ReadOnlyCollection<PlayerId> _createdView;

    /// <summary>
    /// 🔒 `30` §6 — builds a harness over a pre-built content set, a fixed seed and an explicit
    /// clock.
    /// </summary>
    /// <param name="content">
    /// 🔒 The loaded, validated, version-stamped content every command reads (`30` §3). Pre-built:
    /// see correction 1 in the type's remarks.
    /// </param>
    /// <param name="seed">
    /// 🔒 The simulation's root seed. Every per-command <c>GameContext.CommandSeed</c> is derived
    /// from it, so the whole run is <em>"reproducible byte-for-byte"</em> (`30` §6). ⚠️ Zero is a
    /// legitimate seed and not an absence — the same rule `30` §3 states for <c>CommandSeed</c>
    /// itself.
    /// </param>
    /// <param name="clock">The clock. Advanced explicitly; nothing ever waits.</param>
    /// <param name="entitlements">
    /// The subscription entitlement the "server" resolved for this simulation, or <c>null</c> for a
    /// player without Plus. ⚠️ It is a constructor argument rather than a hard-coded value because
    /// `21` §9 sweeps <b>14 profiles</b>, and the free/Plus split is one of the axes — a harness
    /// that could only simulate a free player would make the economy simulator reach around it on
    /// its first day. `30` §3 puts entitlement on the <b>session</b>, so it is constant for the life
    /// of one harness, which is what a profile is.
    /// </param>
    /// <param name="flags">
    /// The `14` §14 kill switches, or <c>null</c> for none thrown. Same reasoning as
    /// <paramref name="entitlements"/>: `26` §8 kills an event mid-flight, and the milestone that
    /// wants to simulate that must not have to fork this type.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> or <paramref name="clock"/> is null.</exception>
    public InMemoryGame(
        ContentSnapshot content,
        ulong seed,
        VirtualClock clock,
        Entitlements? entitlements = null,
        FeatureFlags? flags = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(clock);

        Content = content;
        Seed = seed;
        Clock = clock;

        // 🔒 The DEFAULTS ARE THE ABSENCE OF A THING, never a plausible-looking value (steering S6).
        // "No Plus" and "no kill switch thrown" are what a player who has bought nothing and a
        // remote config that has killed nothing look like; neither is a number somebody chose.
        Entitlements = entitlements ?? new Entitlements(hasPlus: false, expiresAtUtc: null);
        Flags = flags ?? new FeatureFlags(
            pvpEnabled: true, plusOfferEnabled: true, disabledAdPlacements: [], disabledChapters: []);

        _eventsView = new ReadOnlyCollection<DomainEvent>(_events);
        _createdView = new ReadOnlyCollection<PlayerId>(_created);
    }

    /// <summary>🔒 `30` §6 — the clock, advanced explicitly: <c>game.Clock.Advance(...)</c>.</summary>
    public VirtualClock Clock { get; }

    /// <summary>The content set every command in this simulation reads.</summary>
    public ContentSnapshot Content { get; }

    /// <summary>The simulation's root seed. See the constructor.</summary>
    public ulong Seed { get; }

    /// <summary>The entitlement every command in this simulation is applied under (`30` §3).</summary>
    public Entitlements Entitlements { get; }

    /// <summary>The `14` §14 kill switches every command in this simulation is applied under.</summary>
    public FeatureFlags Flags { get; }

    /// <summary>
    /// 🔒 `30` §6 — <em>"every <c>DomainEvent</c> accumulated and queryable — this is the assertion
    /// surface"</em>. In command order, and within one command in `30` §7's <c>Sequence</c> order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <b>live</b> view: a test that holds this reference and sends another command sees the new
    /// rows. That is what §6's <c>game.Events.OfType&lt;GearGranted&gt;()</c> reads as, and it is the
    /// opposite of <c>Player.Wallet</c>'s frozen view — stated here rather than left to be
    /// discovered, on the pattern the aggregate's two getters set.
    /// </para>
    /// <para>
    /// ⚠️ <b>Not attributed to a player, because a <c>DomainEvent</c> is not.</b> `30` §7's records
    /// carry a <c>Sequence</c> and their own payload and no player id, so this list cannot be split
    /// by player without inventing a field the document does not author (S6). Two things already
    /// answer the per-player question without one: <see cref="Send"/> returns the
    /// <c>CommandResult</c> for <em>that</em> command, which is where a per-player economy log is
    /// read from, and a simulation of N profiles is N harnesses — which is what `21` §9's sweep
    /// already is.
    /// </para>
    /// </remarks>
    public IReadOnlyList<DomainEvent> Events => _eventsView;

    /// <summary>
    /// 🔒 How many commands have been <b>sent</b> — accepted and refused alike — which is the
    /// <b>structural</b> half of `30` §6's speed claim.
    /// </summary>
    /// <remarks>
    /// It is here because a wall-clock assertion is not reproducible on a shared runner and a
    /// perf test that fails randomly gets disabled by whoever hits it at 3am. This counter is not
    /// noisy: <em>"180 days offline is one subtraction, not 180 iterations"</em>
    /// (<c>GameRules.AdvanceTime</c>) is a claim about how many times <c>Apply</c> runs, and that
    /// number is the same on every machine. A regression that turned the catch-up into a
    /// per-boundary loop would not move this counter — the loop would be <em>inside</em> one
    /// <c>Apply</c> — so it is asserted together with the boundary state a single command lands on;
    /// see <c>InMemoryGamePerformanceTests</c>.
    /// <para>
    /// ⚠️ It counts <em>every</em> call to <see cref="Send"/>, including the forty-eight rows that
    /// are refused. That is the number the speed claim is about — a refused command still pays the
    /// clone and the catch-up before the dispatch table says no.
    /// </para>
    /// </remarks>
    public long CommandsIssued { get; private set; }

    /// <summary>
    /// Every player this harness has created, in creation order — see <see cref="_created"/> for why
    /// that is a list rather than the dictionary's keys.
    /// </summary>
    public IReadOnlyList<PlayerId> Players => _createdView;

    /// <summary>
    /// 🔒 `30` §6 — creates a player and returns its identity.
    /// </summary>
    /// <param name="displayName">
    /// A display name, or <c>null</c> for one derived from the generated id. Stored and never
    /// interpreted (`16` <b>O34</b> leaves the name lifecycle open; the profanity filter is M4-10's).
    /// </param>
    /// <returns>The new player's identity, for <see cref="Send"/> and <see cref="State"/>.</returns>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Every value in the starting row is either authored, derived from the clock, or the
    /// identity element — none of them is invented</b> (steering <b>S6</b>). <c>Player</c>'s own
    /// remarks name this task as one of the two that could decide a starting state (<em>"M4-10 /
    /// M1-11"</em>), so the restraint is deliberate rather than accidental:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>Legend Level</b> is <c>LegendTuning.Minimum</c> — `07` §1.1's authored floor, read
    ///   from <c>tuning/progression.json</c>, never the literal 1.</item>
    ///   <item><b>The six wallet balances and both Energy banks are ZERO, and that is forced rather
    ///   than chosen.</b> `30` §7 requires every currency movement to be attributed by a
    ///   <c>CurrencyChanged</c>, and `21` §8.3's <c>income_attribution.csv</c> is a query over those
    ///   rows — so a player who <em>started</em> with a balance would be holding currency no row
    ///   attributes, which is the one thing that whole chain exists to make impossible. A starting
    ///   grant is a grant: it needs a rule, a reason token and an event, and the milestone that
    ///   creates accounts (M4-10) owns writing one. ⚠️ The visible consequence, so nobody reads it
    ///   as a bug: `10` §3.2's budget line says <em>"120 (start)"</em>, and a player created here
    ///   reaches it on their first <c>BEGIN_SESSION</c> — `10` §3.1's daily free refill fills the
    ///   bar "to full" and is attributed <c>daily_free_refill</c>. The number the document names is
    ///   produced by a rule instead of being assumed by a constructor.</item>
    ///   <item><b>The two period boundaries</b> are <c>GameCalendar</c>'s answers for the clock's
    ///   current instant, so a fresh player is already inside the game day they were created in and
    ///   the first command does not clear a period they never played.</item>
    ///   <item><b>FTUE</b> is `19` D7's first beat, uncompleted. <b>The login calendar</b> is
    ///   `19` G's day 1, unclaimed — which is where every M1 player stays, because
    ///   <c>CLAIM_CALENDAR</c> is <c>Deferred</c> to M4-09 and `19` G pauses the calendar until the
    ///   open day is claimed. That is specified behaviour, not a defect.</item>
    /// </list>
    /// <para>
    /// 🔒 <b>Built through <c>Player.Rehydrate</c>, which is `30` §11.3's one validated construction
    /// path</b> — the same door the Postgres adapter uses. A harness with a private shortcut into
    /// the aggregate would be able to create players the persistence layer could never load back.
    /// </para>
    /// <para>
    /// ⚠️ <b>The generated id is a counter, not a <c>Guid</c>.</b> <c>Guid.NewGuid()</c> is a banned
    /// ambient API in <c>Core</c> (`14` §8.1) and would additionally make the simulation
    /// irreproducible — `02` §2's <c>runSeed</c> hashes the player id, so two runs of the same seed
    /// would draw different boards from M3 onwards.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="displayName"/> is blank.</exception>
    /// <exception cref="MissingContentException">
    /// The content set does not author `07` §1.1's Legend Level range. Raised out of
    /// <c>LegendTuning.Read</c>, on the same channel <c>Player.Rehydrate</c> documents: a corrupt
    /// row is one player's problem and is a <c>Result&lt;T&gt;</c>, while a data set that cannot say
    /// what Legend Level a player starts at is every player's problem and belongs at the composition
    /// root that loaded it.
    /// </exception>
    /// <exception cref="UnauthorisedTunableException">That range holds a deliberate <c>null</c>.</exception>
    /// <exception cref="InvalidTunableException">That range is authorised but unusable.</exception>
    /// <exception cref="InvalidOperationException">
    /// The starting row does not rehydrate — a defect in this method or a content set whose
    /// <c>legendLevel</c> range excludes its own minimum.
    /// </exception>
    public PlayerId CreatePlayer(string? displayName = null)
    {
        if (displayName is not null && string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException(
                "A display name is stored exactly as given and is never interpreted, but it is " +
                "never blank either — Player.Rehydrate refuses a row that renders as nothing on " +
                "every screen that shows one. Pass null for a generated name.",
                nameof(displayName));
        }

        // 🔒 The counter is READ here and advanced only once the row has proven it rehydrates, so a
        // content set that cannot answer 07 §1.1's Legend Level range does not silently consume ids.
        var id = new PlayerId("PLAYER_" + Text(_created.Count + 1));
        var nowUtc = Clock.NowUtc;
        var legend = LegendTuning.Read(Content);

        var snapshot = new PlayerSnapshot(
            SnapshotSchema.SchemaVersion,
            id,
            displayName ?? id.Value,
            legend.Minimum,
            LegendXp: 0L,
            RunsStarted: 0L,
            Player.WalletCurrencies.ToDictionary(currency => currency, _ => 0L),
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
            LoginCalendarDayClaimed: false);

        var player = Player.Rehydrate(snapshot, Content);

        if (player.IsFailure)
        {
            throw new InvalidOperationException(
                "The starting player row this harness built does not rehydrate: " + player.Error +
                " 30 §11.3 makes Rehydrate the one validated construction path, so this is either a " +
                "defect in InMemoryGame.CreatePlayer or a content set whose 07 §1.1 legendLevel " +
                "range does not contain its own minimum. It is NOT a state a caller can ask for.");
        }

        _players.Add(id, new PlayerSession(new WorldSlice(player.Value, null)));
        _created.Add(id);

        return id;
    }

    /// <summary>
    /// 🔒 `30` §6 — the complete current state of one player: `30` §4.1's slice, exactly as
    /// <c>Apply</c> last returned it.
    /// </summary>
    /// <param name="player">A player <see cref="CreatePlayer"/> returned.</param>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>A snapshot in time, and the OPPOSITE of <see cref="Events"/> — stated because the two
    /// getters sit next to each other and the aggregate sets the precedent for saying which is
    /// which</b> (<c>Player.Wallet</c> is frozen, <c>Player.DailyCounters</c> is live). `30` §2.1's
    /// <b>P4</b> makes <c>Apply</c> return a <em>new</em> slice, and <see cref="Send"/> replaces the
    /// stored one with it — so a caller that captured this before a command holds a slice that will
    /// never change again. Re-read it after every <see cref="Send"/>; do not hold it.
    /// </para>
    /// <para>
    /// ⚠️ <b>It is the harness's own aggregate, not a copy, and that is the one route around the
    /// <c>Apply</c>-only claim at the top of this type.</b> `30` §11.3 makes the aggregate's mutators
    /// <c>internal</c>, so nothing outside <c>Core</c> can reach them through this reference — but
    /// <c>SlayIdleRepeat.Core.Tests</c> holds the one <c>InternalsVisibleTo</c> grant (`30` §11.3),
    /// and a test that called <c>State(p).Player.AccrueEnergy(...)</c> would change harness state
    /// without going through <c>Apply</c>, past the clone, the catch-up, the RNG fold and the event
    /// stamping. Nothing mechanical forbids it; the claim this type makes is about the doors it
    /// <em>declares</em>, and this is the door the aggregate declares. Copying the slice out on every
    /// read would cost two snapshot round trips per assertion on M1-11's own budget and would hand
    /// back an object that could not be compared by reference to <c>CommandResult.NewState</c>, which
    /// several tests do.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">This harness has no such player.</exception>
    public WorldSlice State(PlayerId player) => Session(player).Slice;

    /// <summary>
    /// 🔒 `30` §6 — applies one command, through <c>GameRules.Apply</c> and through nothing else.
    /// </summary>
    /// <param name="player">A player <see cref="CreatePlayer"/> returned.</param>
    /// <param name="command">The command (`14` §2.3).</param>
    /// <returns>
    /// The <c>CommandResult</c> <c>Apply</c> produced: whether it was accepted, the domain-tier
    /// reason if not, the resulting slice and this command's events. ⚠️ A <b>deferred meta</b> row
    /// answers <c>ILLEGAL_STATE</c> here — a <b>value</b>, not an exception (`30` §2.1's <b>P3</b>) —
    /// which is twenty-nine of `14` §2.3's thirty meta rows today. The nineteen <b>run</b> rows do
    /// not reach that arm at all: they are refused earlier as a loading defect, and the type's own
    /// remarks say why and who owns it.
    /// </returns>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The resulting slice is stored, so the next command continues the session.</b> `30`
    /// §2.1's <b>P4</b> makes <c>Apply</c> return a <em>new</em> slice; a harness that re-sent
    /// against the slice it started with would be running a first command N times, which is exactly
    /// the shape that cannot tell "grants once per game day" from "grants on every command". It is
    /// stored unconditionally because on a rejection <c>CommandResult.NewState</c> <b>is</b> the
    /// slice that was handed in, unchanged and by construction — so the two arms are provably the
    /// same write, and a branch here would be a branch no test could distinguish.
    /// </para>
    /// <para>
    /// 🔒 <b>The <c>CommandSeed</c> rule, and the one thing it cannot yet know.</b> `30` §3 and
    /// `14` §8.1 run two regimes and they are exclusive: a <c>CommandKind.Run</c> command is handed
    /// <c>null</c> — its draws come from the run's committed <c>runSeed</c> and its persisted
    /// counters — and a <c>CommandKind.Meta</c> command is handed a server-issued seed. This harness
    /// <em>is</em> the server host for the simulation, so it issues one, derived as
    /// <c>Hash64(seed, playerId, commandOrdinal)</c>: deterministic, distinct per command, and
    /// <b>per player</b> rather than global, so one profile's draws do not shift when another
    /// profile's commands interleave (`21` §9 sweeps 14 profiles).
    /// </para>
    /// <para>
    /// ⚠️ <b>Every meta command gets one, including the twenty-one that draw nothing</b> — and that
    /// is a deferral with a named owner, not an oversight. `14` §2.3 marks exactly <b>nine</b> meta
    /// rows ⚄, and that column <em>does not exist in production</em>:
    /// <c>CommandSeedPin.SeedBearingMetaCommands</c> is a hand transcription in
    /// <c>SlayIdleRepeat.Core.Tests</c>, and <c>CommandVocabularyTests</c> records the fix as
    /// "declaring the ⚄ column on <c>CommandRegistration</c>, which is a forty-nine-row edit and the
    /// natural companion to <b>M5-03</b>'s wire envelope". A second transcription of the nine names
    /// here would be the drift S4 forbids, so this harness does not write one. The asymmetry it
    /// leaves is the safe one, and <c>CommandVocabularyTests</c> already states its two halves: a ⚄
    /// row handed <b>no</b> seed is caught loudly (<c>HandlerInput.MetaDraws</c> throws), while a
    /// non-drawing row handed one silently ignores it. When M5-03 lands the column, the ternary
    /// below reads it and nothing else moves — <c>kind</c> already comes off the same registration.
    /// </para>
    /// <para>
    /// ⚠️ <b>Two consequences of that, named rather than left to be discovered.</b> First, this
    /// harness therefore <b>cannot</b> exercise the pairing check <c>CommandSeedPin</c>'s own remarks
    /// carry as an open item (<em>"nothing checks, when <c>Apply</c> runs, that this context's seed
    /// matches this command's classification"</em>): it supplies a seed to every meta row, so it can
    /// never produce the mispairing. Nobody should read this type's determinism suite as covering it.
    /// Second, the ordinal below counts <b>every</b> command a player has been sent, not only the
    /// drawing ones — so when M4 lands the draws, inserting one extra non-drawing meta command into a
    /// profile's daily script re-rolls the seed of every later drawing command for that player. For
    /// `21` §9 that is the difference between a simulation diff caused by a balance change and one
    /// caused by a scripting change. There is no stable alternative today for the same reason there
    /// is no ⚄ column; it becomes one when M5-03 lands it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is null.</exception>
    /// <exception cref="InvalidOperationException">This harness has no such player.</exception>
    public CommandResult Send(PlayerId player, GameCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var session = Session(player);

        // 🔒 The dispatch table's own answer, not a guess about the command's name. A type no row
        // registers answers null here and is handed no seed — and Apply refuses it with
        // ILLEGAL_STATE, which is 30 §2.1's P3 and not this harness's business to pre-empt.
        var kind = GameRules.RegistrationFor(command.GetType())?.Kind;

        var context = new GameContext(
            Clock.NowUtc,
            kind == CommandKind.Meta ? CommandSeedFor(player, session.CommandsSent) : null,
            Content,
            Entitlements,
            Flags);

        var result = GameRules.Apply(session.Slice, command, context);

        session.Slice = result.NewState;
        session.CommandsSent++;
        CommandsIssued++;
        _events.AddRange(result.Events);

        return result;
    }

    /// <summary>
    /// 🔒 The per-command seed a host would issue, derived rather than drawn: the domain never
    /// invents entropy (`14` §8.1), and neither does the thing standing in for its host.
    /// </summary>
    /// <remarks>
    /// It is <c>Hash64.Of(...)</c> over the root seed, the player and the player's own command
    /// ordinal — not <c>Hash64.Of(seed, streamName, drawIndex)</c>, which is the <b>draw</b> shape.
    /// A draw would need a `14` §8.1 stream row, and inventing one for a harness is exactly the
    /// bookkeeping-shaped invention S6 forbids; this is a seed <em>derivation</em>, the same kind of
    /// thing as <c>SeedDerivation.RunSeed</c>.
    /// </remarks>
    private ulong CommandSeedFor(PlayerId player, long commandOrdinal) =>
        Hash64.Of(Seed, player.Value, commandOrdinal);

    private PlayerSession Session(PlayerId player) =>
        _players.TryGetValue(player, out var session)
            ? session
            : throw new InvalidOperationException(
                "This harness holds no player '" + player + "'. Players are created by " +
                "InMemoryGame.CreatePlayer, which returns the id to use here; an id from a " +
                "different harness names a different simulation, and default(PlayerId) names none " +
                "at all.");

    /// <summary>
    /// 🔒 Renders a number with <see cref="CultureInfo.InvariantCulture"/> — the reason every other
    /// <c>Core</c> type with a <c>Text</c> helper has one (`14` §8.2). It matters more here than in a
    /// diagnostic: this one is part of a <see cref="PlayerId"/>, which `02` §2 hashes into every
    /// <c>runSeed</c>, so a culture that grouped digits would change what the simulation draws.
    /// </summary>
    private static string Text(long value) => value.ToString("D8", CultureInfo.InvariantCulture);

    /// <summary>
    /// One player's simulation state: the slice <c>Apply</c> last returned, and how many commands
    /// this player has been sent.
    /// </summary>
    /// <remarks>
    /// The counter is per player rather than global so a player's <c>CommandSeed</c> sequence does
    /// not depend on how another player's commands were interleaved — see <see cref="Send"/>. A
    /// mutable class rather than a record for the reason <c>HandlerInput</c> is one: it is a bag of
    /// references written in place, and value equality over it would mean nothing.
    /// </remarks>
    private sealed class PlayerSession(WorldSlice slice)
    {
        internal WorldSlice Slice { get; set; } = slice;

        internal long CommandsSent { get; set; }
    }
}
