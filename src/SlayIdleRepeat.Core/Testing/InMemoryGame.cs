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

/// <summary>The harness: the concrete artefact that makes "playable from Core alone" testable, and the only thing tests and the economy simulator need to construct.</summary>
/// <remarks>
/// <para>
/// It mutates state through <c>GameRules.Apply</c> and nothing else — no <c>Restore(...)</c>, no
/// <c>SetEnergy(...)</c>, no door onto an aggregate's internal mutators, even though this type sits
/// inside <c>Core</c> and could reach every one of them. An architecture rule forbids this
/// namespace from naming <c>Core/Rules/</c> or <c>Core/Handlers/</c>, so it can't call a handler
/// behind <c>Apply</c>'s back.
/// </para>
/// <para>
/// This harness's slice is always <c>(player, null)</c>, so all run commands are refused as a
/// loading defect before dispatch, and only the deferred meta commands answer <c>ILLEGAL_STATE</c>
/// — a distinction worth stating since the two look alike from outside. <c>START_RUN</c> in
/// particular cannot be sent through any caller today, since only <c>START_RUN</c> can create the
/// run its own kind requires and nothing here builds one to inject.
/// </para>
/// <para>
/// Zero-delta <c>energy_regen</c> rows in <see cref="Events"/> are intended: an idle player at a
/// full tank still emits one per command sent more than one regen interval after the last, since
/// the anchor moved even though nothing was gained.
/// </para>
/// </remarks>
public sealed class InMemoryGame
{
    private readonly Dictionary<PlayerId, PlayerSession> _players = [];

    /// <summary>Every event every command has produced, in the order the commands were applied.</summary>
    /// <remarks>
    /// A <c>List</c> behind a <see cref="ReadOnlyCollection{T}"/> rather than an exposed array,
    /// since this list is the assertion surface — a test that could rewrite it could pass by
    /// editing its own evidence.
    /// </remarks>
    private readonly List<DomainEvent> _events = [];

    private readonly ReadOnlyCollection<DomainEvent> _eventsView;

    /// <summary>The player ids in creation order, which <see cref="_players"/> cannot answer (dictionary key order is unspecified).</summary>
    private readonly List<PlayerId> _created = [];

    private readonly ReadOnlyCollection<PlayerId> _createdView;

    /// <summary>Builds a harness over a pre-built content set, a fixed seed and an explicit clock.</summary>
    /// <param name="content">The loaded, validated, version-stamped content every command reads.</param>
    /// <param name="seed">
    /// The simulation's root seed. Every per-command <c>GameContext.CommandSeed</c> is derived from
    /// it, so the whole run is reproducible byte-for-byte. Zero is a legitimate seed, not an absence.
    /// </param>
    /// <param name="clock">The clock. Advanced explicitly; nothing ever waits.</param>
    /// <param name="entitlements">The subscription entitlement resolved for this simulation, or <c>null</c> for a player without Plus.</param>
    /// <param name="flags">The kill switches, or <c>null</c> for none thrown.</param>
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

        // Defaults are the absence of a thing, never a plausible-looking invented value.
        Entitlements = entitlements ?? new Entitlements(hasPlus: false, expiresAtUtc: null);
        Flags = flags ?? new FeatureFlags(
            pvpEnabled: true, plusOfferEnabled: true, disabledAdPlacements: [], disabledChapters: []);

        _eventsView = new ReadOnlyCollection<DomainEvent>(_events);
        _createdView = new ReadOnlyCollection<PlayerId>(_created);
    }

    /// <summary>The clock, advanced explicitly: <c>game.Clock.Advance(...)</c>.</summary>
    public VirtualClock Clock { get; }

    /// <summary>The content set every command in this simulation reads.</summary>
    public ContentSnapshot Content { get; }

    /// <summary>The simulation's root seed. See the constructor.</summary>
    public ulong Seed { get; }

    /// <summary>The entitlement every command in this simulation is applied under.</summary>
    public Entitlements Entitlements { get; }

    /// <summary>The kill switches every command in this simulation is applied under.</summary>
    public FeatureFlags Flags { get; }

    /// <summary>Every <c>DomainEvent</c> accumulated so far, in command order — the assertion surface.</summary>
    /// <remarks>
    /// A live view: a test holding this reference sees new rows as commands are sent. Not
    /// attributed to a player, since a <c>DomainEvent</c> carries no player id — read
    /// <see cref="Send"/>'s <c>CommandResult</c> for one command's own events instead.
    /// </remarks>
    public IReadOnlyList<DomainEvent> Events => _eventsView;

    /// <summary>How many commands have been sent — accepted and refused alike.</summary>
    /// <remarks>
    /// A structural counter rather than a wall-clock timing, since wall-clock assertions aren't
    /// reproducible on a shared runner. Counts every call to <see cref="Send"/>, including refused
    /// ones — a refused command still pays the clone and the catch-up before dispatch says no.
    /// </remarks>
    public long CommandsIssued { get; private set; }

    /// <summary>
    /// Every player this harness has created, in creation order — see <see cref="_created"/> for why
    /// that is a list rather than the dictionary's keys.
    /// </summary>
    public IReadOnlyList<PlayerId> Players => _createdView;

    /// <summary>Creates a player and returns its identity.</summary>
    /// <param name="displayName">A display name, or <c>null</c> for one derived from the generated id. Stored and never interpreted.</param>
    /// <returns>The new player's identity, for <see cref="Send"/> and <see cref="State"/>.</returns>
    /// <remarks>
    /// <para>
    /// Every value in the starting row is either authored, derived from the clock, or the identity
    /// element — none is invented. Legend Level is the authored floor, never a literal 1. Wallet
    /// balances and Energy banks start at zero, forced rather than chosen: every currency movement
    /// must be attributed by a <c>CurrencyChanged</c> event, so a player that started with a balance
    /// would hold currency no row attributes. Period boundaries are derived from the clock's current
    /// instant, so a fresh player starts inside the game day they were created in.
    /// </para>
    /// <para>
    /// Built through <c>Player.Rehydrate</c>, the one validated construction path — the same door
    /// the persistence adapter uses, so a harness-created player is one the persistence layer could
    /// load back. The generated id is a counter, not a <c>Guid</c>: a run's seed hashes the player
    /// id, so a non-deterministic id would make the simulation irreproducible.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="displayName"/> is blank.</exception>
    /// <exception cref="MissingContentException">The content set does not author a Legend Level range.</exception>
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

        // The counter is read here and advanced only once the row has proven it rehydrates, so a
        // content set that cannot answer the Legend Level range does not silently consume an id.
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
            LoginCalendarDayClaimed: false,
            FeatCounters: new Dictionary<string, long>(StringComparer.Ordinal),
            PityCounters: new Dictionary<string, int>(StringComparer.Ordinal));

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

    /// <summary>The complete current state of one player: the slice, exactly as <c>Apply</c> last returned it.</summary>
    /// <param name="player">A player <see cref="CreatePlayer"/> returned.</param>
    /// <remarks>
    /// A snapshot in time, the opposite of the live <see cref="Events"/>: <c>Apply</c> returns a new
    /// slice each time and <see cref="Send"/> replaces the stored one, so a reference captured
    /// before a command will never change. Re-read after every <see cref="Send"/>; do not hold it.
    /// It is the harness's own aggregate, not a copy — a test with internals access could mutate it
    /// directly, bypassing <c>Apply</c> entirely, which nothing here prevents mechanically.
    /// </remarks>
    /// <exception cref="InvalidOperationException">This harness has no such player.</exception>
    public WorldSlice State(PlayerId player) => Session(player).Slice;

    /// <summary>Applies one command, through <c>GameRules.Apply</c> and through nothing else.</summary>
    /// <param name="player">A player <see cref="CreatePlayer"/> returned.</param>
    /// <param name="command">The command.</param>
    /// <returns>
    /// The <c>CommandResult</c> <c>Apply</c> produced: whether it was accepted, the domain-tier
    /// reason if not, the resulting slice and this command's events.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The resulting slice is stored unconditionally, so the next command continues from it — on a
    /// rejection <c>CommandResult.NewState</c> is provably the same slice that was handed in, so
    /// there's nothing to branch on.
    /// </para>
    /// <para>
    /// This harness is the server host for the simulation, so it issues meta commands a seed derived
    /// as <c>Hash64(seed, playerId, commandOrdinal)</c> — deterministic and per-player, so one
    /// profile's draws don't shift when another profile's commands interleave. Every meta command
    /// gets one, including non-drawing ones, since production has no column yet distinguishing
    /// which meta commands actually draw.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is null.</exception>
    /// <exception cref="InvalidOperationException">This harness has no such player.</exception>
    public CommandResult Send(PlayerId player, GameCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var session = Session(player);

        // The dispatch table's own answer, not a guess about the command's name. An unregistered
        // type gets null here and no seed; Apply refuses it with ILLEGAL_STATE on its own.
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

    /// <summary>The per-command seed a host would issue, derived rather than drawn.</summary>
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
    /// Renders a number with <see cref="CultureInfo.InvariantCulture"/>. Matters more here than in a
    /// diagnostic: this becomes part of a <see cref="PlayerId"/>, which gets hashed into the run
    /// seed, so a culture that grouped digits would change what the simulation draws.
    /// </summary>
    private static string Text(long value) => value.ToString("D8", CultureInfo.InvariantCulture);

    /// <summary>One player's simulation state: the slice <c>Apply</c> last returned, and how many commands this player has been sent.</summary>
    /// <remarks>A mutable class rather than a record: it's a bag of references written in place, so value equality would mean nothing.</remarks>
    private sealed class PlayerSession(WorldSlice slice)
    {
        internal WorldSlice Slice { get; set; } = slice;

        internal long CommandsSent { get; set; }
    }
}
