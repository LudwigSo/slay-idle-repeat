using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.Tests.Content;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Testing;

using PlayerAggregate = SlayIdleRepeat.Core.Model.Player;

namespace SlayIdleRepeat.Application.Tests.UseCases;

/// <summary>
/// Real players and real runs for the orchestration cases: built through <c>GameRules.Apply</c> by
/// the domain harness, then written into the byte cache the store reads.
/// </summary>
/// <remarks>
/// <para>
/// The harness manufactures fixtures only. Nothing under test is ever driven through it — a case
/// about a use case sends its command to the use case.
/// </para>
/// <para>
/// Seed and start instant are fixed here so every run id, run seed and dice draw in this suite is
/// reproducible; a fixture that let either float would make a failure unreadable.
/// </para>
/// </remarks>
internal static class Worlds
{
    /// <summary>The root seed every fixture in this suite runs under.</summary>
    internal const ulong Seed = 0x5EED_0F1C_E0FFUL;

    /// <summary>The per-command seed a host issues with a meta command.</summary>
    internal const ulong MetaSeed = 0x00C0_FFEE_0BADUL;

    /// <summary>The chapter every fixture run is started on — the authored floor.</summary>
    internal const int Chapter = 1;

    /// <summary>2026-08-12 05:00 UTC — a Wednesday, so a game-day boundary that is not a week boundary.</summary>
    internal static readonly DateTimeOffset Start = new(2026, 8, 12, 5, 0, 0, TimeSpan.Zero);

    /// <summary>Cancellation none of these cases exercise, named once so the calls read.</summary>
    internal static readonly CancellationToken Cancel = CancellationToken.None;

    // Loaded once for the whole suite: the shipped data set is sixteen tuning files and nineteen
    // schemas, and re-reading it per case costs more than every assertion here put together.
    private static readonly Lazy<ContentSnapshot> LazyContent =
        new(() => ContentLoader.Load(RepoData.Source()).Require());

    /// <summary>The shipped content set every fixture reads.</summary>
    internal static ContentSnapshot Content => LazyContent.Value;

    /// <summary>A harness over the shipped content, at <see cref="Start"/>, with <see cref="Seed"/>.</summary>
    internal static InMemoryGame Game() => new(Content, Seed, new VirtualClock(Start));

    /// <summary>What a run costs, read from the shipped document rather than transcribed.</summary>
    internal static int RunEnergyPrice { get; } =
        Content.ReadInt32("tuning/progression.json#/energy/runCost");

    /// <summary>The same player, holding exactly one run's price in the main Energy bar.</summary>
    /// <remarks>
    /// 🔴 The row-writing half of the fixture problem <see cref="Fund"/> describes. Where a fixture
    /// stores a row rather than driving a harness, the price is written onto the row: sending
    /// <c>BEGIN_SESSION</c> instead would consume a wire sequence number and shift every later
    /// command in the case, which is a change to what the case is testing rather than to what it is
    /// paying with.
    /// </remarks>
    /// <param name="player">The starting player.</param>
    /// <exception cref="ArgumentNullException"><paramref name="player"/> is null.</exception>
    internal static PlayerAggregate HoldingARunsPrice(PlayerAggregate player)
    {
        ArgumentNullException.ThrowIfNull(player);

        var funded = PlayerAggregate.Rehydrate(
            player.ToSnapshot() with { Energy = new EnergyBanks(RunEnergyPrice, 0) }, Content);

        return funded.IsFailure
            ? throw new InvalidOperationException(
                "the funded fixture row does not rehydrate: " + funded.Error)
            : funded.Value;
    }

    /// <summary>The day's free Energy refill, which is what pays for the runs the fixtures start.</summary>
    /// <remarks>
    /// 🔴 <b>A run costs Energy</b> — <c>Handlers.StartRun</c> charges <c>EnergyTuning.RunCost</c>
    /// through <c>EnergyMath.Spend</c> — and <c>Player.CreateStarting</c> opens both banks at zero,
    /// because every currency movement in this game has to be attributed by a <c>CurrencyChanged</c>
    /// and a starting balance would be one no row explains. <c>BEGIN_SESSION</c>'s first-login refill
    /// is the command that grants it, so the fixtures below send it exactly where a real profile
    /// would: once, before the first run.
    /// <para>
    /// The real command rather than a written balance: <c>Application.Tests</c> cannot see
    /// <c>Core</c>'s internals, so <c>Player.SetEnergy</c> is out of reach here — and driving the
    /// clock forward instead would move <c>NowUtc</c>, which the run seed is derived from, and change
    /// every board this suite generates.
    /// </para>
    /// </remarks>
    /// <param name="game">The harness.</param>
    /// <param name="player">The player to fund.</param>
    internal static void Fund(InMemoryGame game, PlayerId player)
    {
        ArgumentNullException.ThrowIfNull(game);

        Accepted(
            game.Send(player, new BeginSessionCommand(FixtureClientVersion, FixtureContentHash)),
            "BEGIN_SESSION");
    }

    /// <summary>The client version the fixture's <c>BEGIN_SESSION</c> announces. Never inspected by the domain.</summary>
    private const string FixtureClientVersion = "0.0.0-fixture";

    /// <summary>The content hash it announces. Checked at the wire tier, which no fixture here crosses.</summary>
    private const string FixtureContentHash = "fixture-content-hash";

    /// <summary>A player standing at the trailhead of a fresh run, its opening draft answered.</summary>
    /// <remarks>
    /// The skip is part of the fixture, not incidental: a run opens with a perk draft pending and
    /// the command gate admits only the three draft commands until it is resolved, so a fixture that
    /// stopped at <c>START_RUN</c> would refuse every command a case built on it sends. Skipping
    /// rather than picking keeps the run's perks empty, which is what every case here assumed when
    /// the opening draft did not exist.
    /// </remarks>
    internal static (InMemoryGame Game, PlayerId Player) InARun()
    {
        var game = Game();
        var player = game.CreatePlayer();

        Fund(game, player);
        Accepted(game.Send(player, new StartRunCommand(Chapter, DifficultyTier.NORMAL)), "START_RUN");
        Accepted(game.Send(player, new SkipDraftCommand()), "SKIP_DRAFT");

        return (game, player);
    }

    /// <summary>A player whose fresh run still has its opening draft open.</summary>
    /// <remarks>
    /// The state <see cref="InARun"/> passes through. Named separately for the cases whose subject is
    /// the draft itself, so neither fixture has to be read as the other with a step added or removed.
    /// </remarks>
    internal static (InMemoryGame Game, PlayerId Player) InARunWithItsOpeningDraftOpen()
    {
        var game = Game();
        var player = game.CreatePlayer();

        Fund(game, player);
        Accepted(game.Send(player, new StartRunCommand(Chapter, DifficultyTier.NORMAL)), "START_RUN");

        return (game, player);
    }

    /// <summary>A player whose run has been played far enough to carry draw counters and a pending tile.</summary>
    internal static (InMemoryGame Game, PlayerId Player) InAPlayedRun()
    {
        var (game, player) = InARun();

        Accepted(game.Send(player, new RollDiceCommand()), "ROLL_DICE");

        return (game, player);
    }

    /// <summary>A player whose run is over.</summary>
    internal static (InMemoryGame Game, PlayerId Player) AfterAnEndedRun()
    {
        var (game, player) = InARun();

        Accepted(game.Send(player, new AbandonRunCommand()), "ABANDON_RUN");

        return (game, player);
    }

    /// <summary>Two players in one simulation, so their ids genuinely differ, one of them holding a finished run.</summary>
    /// <remarks>
    /// One harness rather than two: the player id is a counter, so two harnesses under the same seed
    /// would hand back the same id twice and a case about telling two players apart would be comparing
    /// one player with themselves.
    /// </remarks>
    internal static (InMemoryGame Game, PlayerId Asking, PlayerId Other) TwoPlayersOneEndedRun()
    {
        var game = Game();
        var asking = game.CreatePlayer();
        var other = game.CreatePlayer();

        Fund(game, other);
        Accepted(game.Send(other, new StartRunCommand(Chapter, DifficultyTier.NORMAL)), "START_RUN (other player)");

        // ⚠️ The opening draft has to be answered before the run can be abandoned: the draft gate
        // admits only the three draft commands, and ABANDON_RUN is not one of them. Pre-existing
        // behaviour for every draft — the opening draft is simply the first place a fixture meets it.
        Accepted(game.Send(other, new SkipDraftCommand()), "SKIP_DRAFT (other player)");
        Accepted(game.Send(other, new AbandonRunCommand()), "ABANDON_RUN (other player)");

        return (game, asking, other);
    }

    /// <summary>Everything ambient a command is applied under, at the harness's current instant.</summary>
    /// <param name="game">The harness the fixture was built on.</param>
    /// <param name="commandSeed">The per-command seed, for a meta command. <c>null</c> for a run command.</param>
    internal static GameContext Context(InMemoryGame game, ulong? commandSeed = null) =>
        new(game.Clock.NowUtc, commandSeed, game.Content, game.Entitlements, game.Flags);

    /// <summary>The stored shape of a slice: the two rows, as the codec writes them.</summary>
    internal static StoredSlice Stored(WorldSlice slice) =>
        new(slice.Player.ToSnapshot(), slice.Run?.ToSnapshot());

    /// <summary>A cache already holding one player's committed row.</summary>
    internal static InMemoryLocalCache CacheHolding(WorldSlice slice)
    {
        var cache = new InMemoryLocalCache();

        return cache.Seed(SliceKeys.ForPlayer(slice.Player.Id), SnapshotCodec.EncodeSlice(Stored(slice)));
    }

    /// <summary>The canonical hash of a whole slice — the only sound way to compare two of them.</summary>
    /// <remarks>
    /// Record equality on these rows is meaningless: their dictionary members compare by reference, so
    /// two rows carrying identical wallets are unequal and two rows sharing one dictionary are equal
    /// however far their other fields have drifted.
    /// </remarks>
    internal static string Hash(WorldSlice slice) => Hash(Stored(slice));

    /// <inheritdoc cref="Hash(WorldSlice)"/>
    internal static string Hash(StoredSlice stored) =>
        stored.Run is null
            ? CanonicalStateWriter.HashMetaCommandState(stored.Player)
            : CanonicalStateWriter.HashRunCommandState(stored.Player, stored.Run);

    /// <summary>
    /// The canonical hash of the state the domain reaches when <paramref name="command"/> is applied
    /// to <see cref="InARun"/> — computed independently of anything under test.
    /// </summary>
    /// <param name="command">A run command. Its context carries no per-command seed, which is what
    /// lets the harness's own context match <see cref="Context"/>'s exactly.</param>
    /// <remarks>
    /// A second harness under the same fixed seed and clock, driven through <c>GameRules.Apply</c>.
    /// Without it, "the persisted bytes are the state the domain returned" is checked against the use
    /// case's own report of what the domain returned, and a use case that committed and reported the
    /// same wrong slice would satisfy it.
    /// </remarks>
    internal static string HashAfterApplying(GameCommand command)
    {
        var (game, player) = InARun();

        Accepted(game.Send(player, command), command.GetType().Name);

        return Hash(game.State(player));
    }

    /// <summary>The canonical hash of a run alone, with a player pinned on both sides of the comparison.</summary>
    /// <remarks>
    /// The pair mode is the only public door onto a run's canonical bytes, so the run is hashed
    /// alongside one fixed player row. Passing the same <paramref name="pin"/> to both sides of a
    /// comparison leaves the run as the only thing that can differ.
    /// </remarks>
    internal static string RunHash(PlayerSnapshot pin, RunSnapshot run) =>
        CanonicalStateWriter.HashRunCommandState(pin, run);

    private static void Accepted(CommandResult result, string name)
    {
        if (!result.Accepted)
        {
            throw new InvalidOperationException(
                name + " was refused " + result.Rejection + ", so this fixture never reached the state " +
                "the cases built on it assume. Fix the fixture; do not weaken the cases.");
        }
    }
}
