using Shouldly;
using SlayIdleRepeat.Server.Composition;
using Xunit;

namespace SlayIdleRepeat.Server.Tests.Composition;

/// <summary>
/// The M5-05 placeholder against the load-bearing halves of <c>ILocalCachePort</c>'s contract. It
/// lives in the composition root rather than in <c>adapters/fakes</c>, so the shared contract
/// suite never sees it — these cases stand in for that suite until M5-05's real stores retire it.
/// </summary>
public sealed class PlaceholderVolatileWorldStoreTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task A_written_value_reads_back_and_a_miss_is_null_not_empty()
    {
        var store = new PlaceholderVolatileWorldStore();

        (await store.ReadAsync("missing", Cancel)).ShouldBeNull("a miss is null — never an empty array");

        await store.WriteAsync("row", new byte[] { 1, 2, 3 }, Cancel);
        (await store.ReadAsync("row", Cancel)).ShouldBe(new byte[] { 1, 2, 3 });

        await store.WriteAsync("empty", Array.Empty<byte>(), Cancel);
        (await store.ReadAsync("empty", Cancel)).ShouldBe(
            Array.Empty<byte>(), "a stored empty value is a hit, distinct from a miss");
    }

    [Fact]
    public async Task Reads_hand_out_copies_so_a_callers_mutation_cannot_reach_the_store()
    {
        var store = new PlaceholderVolatileWorldStore();
        await store.WriteAsync("row", new byte[] { 7 }, Cancel);

        var first = await store.ReadAsync("row", Cancel);
        first![0] = 0;

        (await store.ReadAsync("row", Cancel)).ShouldBe(new byte[] { 7 });
    }

    [Fact]
    public async Task Delete_makes_the_next_read_a_miss_and_deleting_a_missing_key_is_a_no_op()
    {
        var store = new PlaceholderVolatileWorldStore();
        await store.WriteAsync("row", new byte[] { 1 }, Cancel);

        await store.DeleteAsync("row", Cancel);
        (await store.ReadAsync("row", Cancel)).ShouldBeNull();

        await store.DeleteAsync("row", Cancel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a key")]
    [InlineData("a:b")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("../x")]
    public async Task A_key_outside_the_closed_key_space_is_refused_by_every_method(string key)
    {
        var store = new PlaceholderVolatileWorldStore();

        await Should.ThrowAsync<ArgumentException>(() => store.ReadAsync(key, Cancel));
        await Should.ThrowAsync<ArgumentException>(() => store.WriteAsync(key, new byte[] { 1 }, Cancel));
        await Should.ThrowAsync<ArgumentException>(() => store.DeleteAsync(key, Cancel));
    }
}
