using Shouldly;
using SlayIdleRepeat.Application.Ports.Server;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Server;

/// <summary>
/// States what <see cref="IBattleLogStore"/> <em>means</em> (<c>23</c> §4.2, §5 A8): an id in,
/// the same bytes out — wherever and however a backing keeps them.
/// </summary>
/// <remarks>
/// The port allows acceptance and readability to be two moments (a queued backing), so every
/// round-trip case settles through <see cref="SettleAsync"/> between the put and the get. A direct
/// backing's fixture leaves it the no-op default; a queued fixture drains in it. The cases
/// therefore state the contract every implementation shares, with the queue's own drop-and-count
/// policy pinned in that implementation's unit tests.
/// </remarks>
[ContractSuiteFor(typeof(IBattleLogStore))]
public abstract class IBattleLogStoreContractTests
{
    /// <summary>A store under test, over a backing of its own.</summary>
    protected abstract IBattleLogStore Create();

    /// <summary>The moment after which everything put so far must be readable. No-op for a direct backing.</summary>
    protected virtual Task SettleAsync() => Task.CompletedTask;

    /// <summary>A long, position-dependent payload: any truncation, reorder or substitution changes it.</summary>
    private static byte[] Pattern(byte seed, int length = 16 * 1024)
    {
        var bytes = new byte[length];

        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)(seed + index * 31);
        }

        return bytes;
    }

    [Fact]
    public async Task A_log_that_was_never_put_reads_back_as_null()
    {
        var store = Create();

        (await store.GetAsync(new BattleLogId("LOG_never-stored"), PersistenceWorlds.Cancel))
            .ShouldBeNull();
    }

    [Fact]
    public async Task A_put_log_reads_back_byte_identical_once_settled()
    {
        var store = Create();
        var id = new BattleLogId("LOG_roundtrip");
        var log = Pattern(seed: 0x11);

        await store.PutAsync(id, log, PersistenceWorlds.Cancel);
        await SettleAsync();

        var read = await store.GetAsync(id, PersistenceWorlds.Cancel);
        read.ShouldNotBeNull();
        read.Value.ToArray().ShouldBe(log,
            "a replay is these bytes or nothing — a compressed-at-rest backing that returned its "
            + "own stored form instead of the log would hand the replayer a gzip header.");
    }

    [Fact]
    public async Task A_second_put_to_the_same_id_wins_once_settled()
    {
        var store = Create();
        var id = new BattleLogId("LOG_replaced");
        await store.PutAsync(id, Pattern(seed: 0x22), PersistenceWorlds.Cancel);

        await store.PutAsync(id, Pattern(seed: 0x33), PersistenceWorlds.Cancel);
        await SettleAsync();

        var read = await store.GetAsync(id, PersistenceWorlds.Cancel);
        read.ShouldNotBeNull();
        read.Value.ToArray().ShouldBe(Pattern(seed: 0x33));
    }

    [Fact]
    public async Task An_empty_log_is_not_a_miss()
    {
        var store = Create();
        var id = new BattleLogId("LOG_empty");

        await store.PutAsync(id, ReadOnlyMemory<byte>.Empty, PersistenceWorlds.Cancel);
        await SettleAsync();

        var read = await store.GetAsync(id, PersistenceWorlds.Cancel);
        read.ShouldNotBeNull(
            "an empty log was stored, so this is a hit — collapsing it into null makes 'nothing "
            + "was recorded' and 'a zero-length record exists' one answer.");
        read.Value.Length.ShouldBe(0);
    }

    [Fact]
    public async Task Two_ids_hold_two_logs()
    {
        var store = Create();
        var lower = new BattleLogId("log_case");
        var upper = new BattleLogId("LOG_case");

        await store.PutAsync(lower, new byte[] { 0xA1 }, PersistenceWorlds.Cancel);
        await store.PutAsync(upper, new byte[] { 0xB2 }, PersistenceWorlds.Cancel);
        await SettleAsync();

        (await store.GetAsync(lower, PersistenceWorlds.Cancel))!.Value.ToArray()
            .ShouldBe(new byte[] { 0xA1 },
                "ids differing only in case are two logs — a backing that lets its storage compare "
                + "names case-insensitively merges them on some deployments and not others.");
        (await store.GetAsync(upper, PersistenceWorlds.Cancel))!.Value.ToArray()
            .ShouldBe(new byte[] { 0xB2 });
    }

    [Fact]
    public async Task A_default_id_is_an_argument_fault_from_both_methods()
    {
        var store = Create();

        await Should.ThrowAsync<ArgumentException>(
            async () => await store.PutAsync(default, new byte[] { 1 }, PersistenceWorlds.Cancel),
            "default(BattleLogId) carries no text — a backing that derived a storage name from it "
            + "would store every such log under one name.");

        await Should.ThrowAsync<ArgumentException>(
            async () => await store.GetAsync(default, PersistenceWorlds.Cancel));
    }

    [Fact]
    public async Task An_already_cancelled_token_is_observed_by_both_methods()
    {
        var store = Create();
        var id = new BattleLogId("LOG_cancelled");
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await store.PutAsync(id, new byte[] { 1 }, source.Token));

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await store.GetAsync(id, source.Token));
    }
}
