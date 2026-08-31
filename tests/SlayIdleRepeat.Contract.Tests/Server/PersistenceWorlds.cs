using SlayIdleRepeat.Adapters.Content.LocalFile;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Testing;

namespace SlayIdleRepeat.Contract.Tests.Server;

/// <summary>
/// Real profiles and runs for the persistence suites: built through <c>GameRules.Apply</c> by the
/// domain harness, so every stored shape here is one the game actually produces.
/// </summary>
internal static class PersistenceWorlds
{
    private const ulong Seed = 0x5EED_5709_E5UL;

    private static readonly DateTimeOffset Start = new(2026, 8, 12, 5, 0, 0, TimeSpan.Zero);

    private static readonly Lazy<ContentSnapshot> LazyContent = new(() =>
        ContentLoader.Load(new LocalFileContentSource(DataRoot())).Require());

    /// <summary>Cancellation none of these cases exercise, named once so the calls read.</summary>
    internal static readonly CancellationToken Cancel = CancellationToken.None;

    /// <summary>A fresh player's profile — no run, straight off account creation.</summary>
    internal static PlayerProfile FreshProfile(string? displayName = null)
    {
        var game = Game();
        var player = game.CreatePlayer(displayName);

        return ProfileOf(game, player);
    }

    /// <summary>A profile mid-run: the player and their active run, opening draft answered.</summary>
    internal static PlayerProfile ProfileInARun()
    {
        var game = Game();
        var player = game.CreatePlayer();

        Require(game.Send(player, new StartRunCommand(1, DifficultyTier.NORMAL)), "START_RUN");
        Require(game.Send(player, new SkipDraftCommand()), "SKIP_DRAFT");

        return ProfileOf(game, player);
    }

    /// <summary>A run snapshot the domain produced — the payload the run-store cases move around.</summary>
    internal static RunSnapshot ARun() =>
        ProfileInARun().ActiveRun
        ?? throw new InvalidOperationException("the run fixture produced a profile with no run.");

    /// <summary>S17's comparison: a profile's canonical stored bytes, from the one codec.</summary>
    internal static byte[] CanonicalBytes(PlayerProfile profile) =>
        SnapshotCodec.EncodeSlice(new StoredSlice(profile.Player, profile.ActiveRun));

    /// <summary>S17's comparison for a run row.</summary>
    internal static byte[] CanonicalBytes(RunSnapshot run) => SnapshotCodec.EncodeRun(run);

    private static InMemoryGame Game() => new(LazyContent.Value, Seed, new VirtualClock(Start));

    private static PlayerProfile ProfileOf(InMemoryGame game, PlayerId player)
    {
        var slice = game.State(player);

        return new PlayerProfile(slice.Player.ToSnapshot(), slice.Run?.ToSnapshot());
    }

    private static void Require(CommandResult result, string step)
    {
        if (!result.Accepted)
        {
            throw new InvalidOperationException(
                step + " was refused while building a fixture: " + result.Rejection);
        }
    }

    private static string DataRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SlayIdleRepeat.sln")))
        {
            directory = directory.Parent!;
        }

        return directory is null
            ? throw new InvalidOperationException(
                "No SlayIdleRepeat.sln above " + AppContext.BaseDirectory + " — these fixtures read " +
                "the real game-data tree from the checkout.")
            : Path.Combine(directory.FullName, "game-data");
    }
}
