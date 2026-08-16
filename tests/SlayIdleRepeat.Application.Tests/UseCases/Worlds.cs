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

    /// <summary>A player standing at the trailhead of a fresh run.</summary>
    internal static (InMemoryGame Game, PlayerId Player) InARun()
    {
        var game = Game();
        var player = game.CreatePlayer();

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

        Accepted(game.Send(other, new StartRunCommand(Chapter, DifficultyTier.NORMAL)), "START_RUN (other player)");
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
