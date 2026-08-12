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
/// 🔒 <b>This type is what wakes
/// <c>DomainPurityTests.The_whole_game_is_playable_from_Core_alone</c>.</b> That rule — `30` §9's
/// "load-bearing" one — has been vacuous since M0-08 for the plainest possible reason: it asserts
/// that <em>this</em> type's assembly closure is exactly
/// <c>{ SlayIdleRepeat.Core, System.* }</c>, and there was no such type. From this commit there is,
/// it lives in the production <c>Core</c> assembly (`30` §11.4 puts <c>Testing/</c> inside it), and
/// everything it references therefore ships. Be deliberate about what it pulls in: a single
/// <c>ProjectReference</c> or <c>PackageReference</c> added to <c>SlayIdleRepeat.Core.csproj</c> to
/// make something here compile turns that rule red, which is precisely what it is for.
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
/// <c>BEGIN_SESSION</c> is the <b>only</b> <c>Handled</c> row of `14` §2.3's forty-nine; the other
/// forty-eight are <c>Deferred</c> and answer <c>ILLEGAL_STATE</c>. So the loop this harness runs is
/// <em>advance the clock, send <c>BEGIN_SESSION</c>, observe the catch-up, the daily free refill,
/// the calendar and the counters</em> — the day cycle, Energy and the currency seam, which is
/// exactly what M1's exit criterion names. It is not a run: nothing in M1 can start one. Every
/// command that becomes <c>Handled</c> in M3 and M4 is driven through <see cref="Send"/> with no
/// change to this type.
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

    private long _playersCreated;

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
    /// 🔒 How many commands have been applied — the <b>structural</b> half of `30` §6's speed claim.
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
    /// </remarks>
    public long CommandsIssued { get; private set; }

    /// <summary>Every player this harness has created, in creation order.</summary>
    public IReadOnlyCollection<PlayerId> Players => _players.Keys;

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

        var id = new PlayerId("PLAYER_" + Text(++_playersCreated));
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

        return id;
    }

    /// <summary>
    /// 🔒 `30` §6 — the complete current state of one player: `30` §4.1's slice, exactly as
    /// <c>Apply</c> last returned it.
    /// </summary>
    /// <param name="player">A player <see cref="CreatePlayer"/> returned.</param>
    /// <exception cref="InvalidOperationException">This harness has no such player.</exception>
    public WorldSlice State(PlayerId player) => Session(player).Slice;

    /// <summary>
    /// 🔒 `30` §6 — applies one command, through <c>GameRules.Apply</c> and through nothing else.
    /// </summary>
    /// <param name="player">A player <see cref="CreatePlayer"/> returned.</param>
    /// <param name="command">The command (`14` §2.3).</param>
    /// <returns>
    /// The <c>CommandResult</c> <c>Apply</c> produced: whether it was accepted, the domain-tier
    /// reason if not, the resulting slice and this command's events. ⚠️ A <c>Deferred</c> row answers
    /// <c>ILLEGAL_STATE</c> here — a <b>value</b>, not an exception (`30` §2.1's <b>P3</b>), which is
    /// forty-eight of `14` §2.3's forty-nine rows today.
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
    /// below reads it and nothing else moves.
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
