using Shouldly;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Adapters.ObjectStore.S3;
using SlayIdleRepeat.Application.Ports.Server;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Server;

/// <summary>The at-rest compression: byte-identical round-trips, and genuinely gzip.</summary>
public sealed class BattleLogCompressionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(64 * 1024)]
    public void A_log_round_trips_byte_identical(int length)
    {
        var log = new byte[length];
        for (var index = 0; index < log.Length; index++)
        {
            log[index] = (byte)(index * 17);
        }

        BattleLogCompression.Decompress(BattleLogCompression.Compress(log)).ShouldBe(log);
    }

    [Fact]
    public void The_stored_form_is_gzip_by_its_published_magic_bytes()
    {
        // RFC 1952 §2.3.1: every gzip member opens with ID1=0x1f, ID2=0x8b.
        var stored = BattleLogCompression.Compress(new byte[] { 1, 2, 3 });

        stored.Length.ShouldBeGreaterThan(2);
        stored[0].ShouldBe((byte)0x1f);
        stored[1].ShouldBe((byte)0x8b);
    }

    [Fact]
    public void Bytes_that_are_not_gzip_are_refused_loudly()
    {
        Should.Throw<InvalidDataException>(
            () => BattleLogCompression.Decompress(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }),
            "a stored object that does not decompress is a corrupt log, and answering with "
            + "garbage bytes would hand the replayer a fight that never happened.");
    }
}

/// <summary>The id-to-object-name mapping — internal to the adapter, decided once.</summary>
public sealed class S3BattleLogStorageNameTests
{
    [Fact]
    public void The_storage_name_is_the_id_with_the_compressed_suffix()
    {
        S3BattleLogStore.StorageNameOf(new BattleLogId("RUN_7.b3")).ShouldBe("RUN_7.b3.gz");
    }

    [Fact]
    public void Two_ids_map_to_two_names()
    {
        S3BattleLogStore.StorageNameOf(new BattleLogId("LOG_a")).ShouldNotBe(
            S3BattleLogStore.StorageNameOf(new BattleLogId("LOG_b")));
    }

    [Fact]
    public void A_default_id_is_refused()
    {
        Should.Throw<ArgumentException>(() => S3BattleLogStore.StorageNameOf(default));
    }
}

/// <summary>The queue's own policy: what the shared suite deliberately leaves to it.</summary>
public sealed class QueuedBattleLogStoreTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task A_put_is_not_readable_until_the_drain_runs()
    {
        var inner = new InMemoryBattleLogStore();
        var queued = new QueuedBattleLogStore(inner, capacity: 8, new BattleLogLossCounter());
        var id = new BattleLogId("LOG_pending");

        await queued.PutAsync(id, new byte[] { 1 }, Cancel);

        (await inner.GetAsync(id, Cancel)).ShouldBeNull(
            "the put returned on acceptance; the upload is the drain's, and nothing else may block "
            + "on it.");

        (await queued.DrainPendingAsync(Cancel)).ShouldBe(1);
        (await inner.GetAsync(id, Cancel)).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_full_queue_drops_the_log_counts_it_and_never_blocks()
    {
        var inner = new InMemoryBattleLogStore();
        var losses = new BattleLogLossCounter();
        var queued = new QueuedBattleLogStore(inner, capacity: 2, losses);

        await queued.PutAsync(new BattleLogId("LOG_1"), new byte[] { 1 }, Cancel);
        await queued.PutAsync(new BattleLogId("LOG_2"), new byte[] { 2 }, Cancel);
        await queued.PutAsync(new BattleLogId("LOG_3"), new byte[] { 3 }, Cancel);

        losses.Count.ShouldBe(1L, "the third log found the queue full and was dropped, counted.");

        await queued.DrainPendingAsync(Cancel);
        (await inner.GetAsync(new BattleLogId("LOG_1"), Cancel)).ShouldNotBeNull();
        (await inner.GetAsync(new BattleLogId("LOG_2"), Cancel)).ShouldNotBeNull();
        (await inner.GetAsync(new BattleLogId("LOG_3"), Cancel)).ShouldBeNull(
            "the dropped log is gone — dropping newest keeps the queue's contents already accepted, "
            + "and battle-log loss never blocks progress.");
    }

    [Fact]
    public async Task Draining_an_empty_queue_uploads_nothing()
    {
        var queued = new QueuedBattleLogStore(
            new InMemoryBattleLogStore(), capacity: 2, new BattleLogLossCounter());

        (await queued.DrainPendingAsync(Cancel)).ShouldBe(0);
    }

    [Fact]
    public async Task The_running_drain_uploads_without_a_manual_drain_and_stops_on_cancel()
    {
        var inner = new InMemoryBattleLogStore();
        var queued = new QueuedBattleLogStore(inner, capacity: 8, new BattleLogLossCounter());
        using var stop = new CancellationTokenSource();
        var running = queued.RunAsync(stop.Token);
        var id = new BattleLogId("LOG_drained");

        await queued.PutAsync(id, new byte[] { 7 }, Cancel);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (await inner.GetAsync(id, Cancel) is null)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("the running drain did not upload within five seconds.");
            }

            await Task.Delay(10, Cancel);
        }

        await stop.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
    }

    [Fact]
    public void A_non_positive_capacity_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new QueuedBattleLogStore(
            new InMemoryBattleLogStore(), capacity: 0, new BattleLogLossCounter()));
    }
}
