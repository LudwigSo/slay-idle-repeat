using Shouldly;
using SlayIdleRepeat.Adapters.Content.LocalFile;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Testing;
using SlayIdleRepeat.Server.Composition;
using Xunit;

namespace SlayIdleRepeat.Server.Tests.Composition;

/// <summary>
/// The world-rows bridge: SliceKeys' two prefixes routed to the ports, everything else refused
/// loudly — over the in-memory fakes, exactly as the composition wires the real pair.
/// </summary>
public sealed class RepositoryWorldRowsTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(48);

    private static readonly Lazy<ContentSnapshot> Content = new(() =>
        ContentLoader.Load(new LocalFileContentSource(DataRoot())).Require());

    private static (RepositoryWorldRows Rows, InMemoryPlayerRepository Players, InMemoryRunStateStore Runs) Build()
    {
        var players = new InMemoryPlayerRepository();
        var runs = new InMemoryRunStateStore(new AdjustableClock());

        return (new RepositoryWorldRows(players, runs, Ttl), players, runs);
    }

    private static byte[] SliceBytesInARun(out PlayerId player)
    {
        var game = new InMemoryGame(Content.Value, 0x5EED_B71D_6EUL, new VirtualClock(
            new DateTimeOffset(2026, 8, 12, 5, 0, 0, TimeSpan.Zero)));
        player = game.CreatePlayer();

        Accept(game.Send(player, new StartRunCommand(1, DifficultyTier.NORMAL)));
        Accept(game.Send(player, new SkipDraftCommand()));

        var slice = game.State(player);

        return SnapshotCodec.EncodeSlice(new StoredSlice(slice.Player.ToSnapshot(), slice.Run?.ToSnapshot()));
    }

    [Fact]
    public async Task A_player_row_written_through_the_bridge_reads_back_byte_identical()
    {
        var (rows, _, _) = Build();
        var bytes = SliceBytesInARun(out var player);
        var key = SliceKeys.ForPlayer(player);

        await rows.WriteAsync(key, bytes, Cancel);

        (await rows.ReadAsync(key, Cancel)).ShouldBe(bytes,
            "the bridge stores through the repository and re-encodes on read; anything but the "
            + "canonical bytes back is a second serialisation of the row.");
    }

    [Fact]
    public async Task A_player_row_lands_in_the_repository_run_included()
    {
        var (rows, players, _) = Build();
        var bytes = SliceBytesInARun(out var player);

        await rows.WriteAsync(SliceKeys.ForPlayer(player), bytes, Cancel);

        var profile = await players.GetAsync(player, Cancel);
        profile.ShouldNotBeNull();
        profile.ActiveRun.ShouldNotBeNull(
            "the slice carried an active run and the pair moves together through the port.");
    }

    [Fact]
    public async Task A_never_written_player_key_is_a_miss_not_a_fault()
    {
        var (rows, _, _) = Build();

        (await rows.ReadAsync("player.PLAYER_unknown", Cancel)).ShouldBeNull();
    }

    [Fact]
    public async Task A_run_archive_round_trips_through_the_run_store()
    {
        var (rows, _, runs) = Build();
        var bytes = SliceBytesInARun(out _);
        var run = SnapshotCodec.DecodeSlice(bytes).Run!;
        var key = SliceKeys.ForRun(run.Id);

        await rows.WriteAsync(key, SnapshotCodec.EncodeRun(run), Cancel);

        (await rows.ReadAsync(key, Cancel)).ShouldBe(SnapshotCodec.EncodeRun(run));
        (await runs.GetAsync(run.Id, Cancel)).ShouldNotBeNull("the archive landed through the port.");

        await rows.DeleteAsync(key, Cancel);
        (await runs.GetAsync(run.Id, Cancel)).ShouldBeNull();
    }

    [Theory]
    [InlineData("profile.v1")]
    [InlineData("playerX")]
    [InlineData("player.")]
    [InlineData("run.")]
    public async Task A_key_outside_the_slice_vocabulary_is_refused_loudly(string key)
    {
        var (rows, _, _) = Build();

        await Should.ThrowAsync<InvalidOperationException>(async () => await rows.ReadAsync(key, Cancel));
        await Should.ThrowAsync<InvalidOperationException>(
            async () => await rows.WriteAsync(key, new byte[] { 1 }, Cancel));
        await Should.ThrowAsync<InvalidOperationException>(async () => await rows.DeleteAsync(key, Cancel));
    }

    [Fact]
    public async Task Deleting_a_player_key_is_refused()
    {
        var (rows, _, _) = Build();
        var bytes = SliceBytesInARun(out var player);
        var key = SliceKeys.ForPlayer(player);
        await rows.WriteAsync(key, bytes, Cancel);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await rows.DeleteAsync(key, Cancel),
            "nothing in the world store deletes a player; account deletion is its own governed "
            + "flow, and this door refusing it is what keeps that true.");
    }

    private static void Accept(CommandResult result)
    {
        if (!result.Accepted)
        {
            throw new InvalidOperationException("fixture command refused: " + result.Rejection);
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
            ? throw new InvalidOperationException("No SlayIdleRepeat.sln above " + AppContext.BaseDirectory)
            : Path.Combine(directory.FullName, "game-data");
    }
}
