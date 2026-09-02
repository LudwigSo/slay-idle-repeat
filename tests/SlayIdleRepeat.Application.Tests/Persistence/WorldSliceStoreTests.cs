using Shouldly;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Persistence;

/// <summary>
/// The store: one committed key per player, one archive key per finished run, and a commit order a
/// crash between the two writes cannot lose an archive through.
/// </summary>
public sealed class WorldSliceStoreTests
{
    [Fact]
    public async Task SaveAsync_commits_the_player_and_the_run_in_a_single_write()
    {
        var (game, player) = Worlds.InAPlayedRun();
        var slice = game.State(player);
        var cache = new RecordingCache(new InMemoryLocalCache());

        await new WorldSliceStore(cache).SaveAsync(slice, Worlds.Cancel);

        cache.Writes.ShouldBe(
            new[] { SliceKeys.ForPlayer(player) },
            "the player and the run they are in commit under one key in one last-write-wins write; " +
            "split across two keys a crash between them tears the pair apart in one direction or the " +
            "other, and no ordering repairs that.");

        var committed = SnapshotCodec.DecodeSlice(
            (await cache.ReadAsync(SliceKeys.ForPlayer(player), Worlds.Cancel))!);

        Worlds.Hash(committed).ShouldBe(
            Worlds.Hash(slice), "the committed row is not the slice that was handed in.");
    }

    [Fact]
    public async Task SaveAsync_writes_the_archive_row_before_the_committed_row()
    {
        var (game, player) = Worlds.AfterAnEndedRun();
        var slice = game.State(player);
        var cache = new RecordingCache(new InMemoryLocalCache());

        await new WorldSliceStore(cache).SaveAsync(slice, Worlds.Cancel);

        cache.Writes.ShouldBe(
            new[] { SliceKeys.ForRun(slice.Run!.Id), SliceKeys.ForPlayer(player) },
            "the archive is written first and the player row last. A crash between them leaves an " +
            "archived copy of a run the committed state does not yet call finished, and the retried " +
            "command rewrites the same bytes. The other order loses the archive permanently, with the " +
            "run already over and nothing left to copy from.");
    }

    [Fact]
    public async Task SaveAsync_archives_nothing_while_the_run_is_still_being_played()
    {
        var (game, player) = Worlds.InAPlayedRun();
        var slice = game.State(player);
        var cache = new RecordingCache(new InMemoryLocalCache());

        await new WorldSliceStore(cache).SaveAsync(slice, Worlds.Cancel);

        cache.Writes.ShouldNotContain(
            SliceKeys.ForRun(slice.Run!.Id),
            "the archive holds finished runs. Archiving a live one on every command would rewrite the " +
            "row the archive exists to keep stable, and would do it once per dice roll.");
    }

    [Fact]
    public async Task LoadAsync_returns_the_state_a_previous_SaveAsync_committed()
    {
        var (game, player) = Worlds.InAPlayedRun();
        var slice = game.State(player);
        var cache = new InMemoryLocalCache();

        await new WorldSliceStore(cache).SaveAsync(slice, Worlds.Cancel);

        // Reopen is what the next launch of the app opens: state that only survives inside one port
        // object has not been persisted at all.
        var loaded = await new WorldSliceStore(cache.Reopen())
            .LoadAsync(player, Worlds.Content, Worlds.Cancel);

        Worlds.Hash(loaded).ShouldBe(
            Worlds.Hash(slice), "what came back from the store is not what was committed to it.");
    }

    [Fact]
    public async Task LoadAsync_names_the_player_when_nothing_is_stored_for_them()
    {
        var store = new WorldSliceStore(new InMemoryLocalCache());

        var failure = await Should.ThrowAsync<InvalidOperationException>(
            () => store.LoadAsync(new PlayerId("PLAYER_00000404"), Worlds.Content, Worlds.Cancel));

        failure.Message.ShouldContain(
            "PLAYER_00000404",
            Case.Sensitive,
            "a player the store has never heard of is a miswired host, and the failure has to say which " +
            "player so the wiring can be found. Answering with a blank player would let the next " +
            "command overwrite a real account with a fresh one.");
    }

    [Fact]
    public async Task ReadSnapshotsAsync_answers_null_for_a_player_with_no_committed_row()
    {
        var store = new WorldSliceStore(new InMemoryLocalCache());

        var read = await store.ReadSnapshotsAsync(new PlayerId("PLAYER_00000404"), Worlds.Cancel);

        read.ShouldBeNull(
            "the read side reports an absence rather than throwing: a query for an unknown player is a " +
            "question with an answer, unlike a command addressed to one.");
    }

    [Fact]
    public async Task ReadArchivedRunAsync_answers_null_for_a_run_that_was_never_archived()
    {
        var store = new WorldSliceStore(new InMemoryLocalCache());

        var read = await store.ReadArchivedRunAsync(new RunId("RUN_NEVER_HAPPENED"), Worlds.Cancel);

        read.ShouldBeNull("nothing was archived under that identity.");
    }

    [Fact]
    public async Task ReadArchivedRunAsync_returns_the_run_a_previous_SaveAsync_archived()
    {
        var (game, player) = Worlds.AfterAnEndedRun();
        var slice = game.State(player);
        var cache = new InMemoryLocalCache();

        await new WorldSliceStore(cache).SaveAsync(slice, Worlds.Cancel);

        var archived = await new WorldSliceStore(cache.Reopen())
            .ReadArchivedRunAsync(slice.Run!.Id, Worlds.Cancel);

        archived.ShouldNotBeNull("the finished run was not archived at all.");
        Worlds.RunHash(slice.Player.ToSnapshot(), archived).ShouldBe(
            Worlds.RunHash(slice.Player.ToSnapshot(), slice.Run.ToSnapshot()),
            "the archived copy differs from the run that finished; the same player row is hashed on " +
            "both sides, so the run is the only thing that can have moved.");
    }
}
