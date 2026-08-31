using Shouldly;
using SlayIdleRepeat.Adapters.Cache.Redis;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Server;

/// <summary>A controllable byte surface for the decorator policy cases: a dictionary that can be told to fail.</summary>
internal sealed class ScriptedByteCache : IVolatileByteCache
{
    private readonly Dictionary<string, byte[]> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TimeSpan> _ttls = new(StringComparer.Ordinal);

    /// <summary>When set, every operation throws — the cache is down hard.</summary>
    internal bool Failing { get; set; }

    internal IReadOnlyDictionary<string, byte[]> Entries => _entries;

    /// <summary>What each entry was written with. How long a cached copy promises to exist is a rule, not a detail.</summary>
    internal IReadOnlyDictionary<string, TimeSpan> Ttls => _ttls;

    public Task<byte[]?> GetAsync(string key, CancellationToken ct)
    {
        RequireHealthy();
        return Task.FromResult(_entries.TryGetValue(key, out var value) ? value.ToArray() : (byte[]?)null);
    }

    public Task SetAsync(string key, ReadOnlyMemory<byte> value, TimeSpan ttl, CancellationToken ct)
    {
        RequireHealthy();
        _entries[key] = value.ToArray();
        _ttls[key] = ttl;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        RequireHealthy();
        _entries.Remove(key);
        return Task.CompletedTask;
    }

    private void RequireHealthy()
    {
        if (Failing)
        {
            throw new TimeoutException("scripted: the cache endpoint is not answering.");
        }
    }
}

/// <summary>The key scheme: one deciding place, distinct keys for distinct identities.</summary>
public sealed class RedisKeysTests
{
    [Fact]
    public void A_run_states_key_is_pinned()
    {
        RedisKeys.ForRunState(new RunId("RUN_7")).ShouldBe("sir:run:RUN_7");
    }

    [Fact]
    public void A_player_scoped_records_key_is_pinned()
    {
        RedisKeys.ForRecord(
                IdempotencyScope.ForPlayer(new PlayerId("PLAYER_1")), new CommandId("CMD_9"))
            .ShouldBe("sir:idem:player:PLAYER_1:CMD_9");
    }

    [Fact]
    public void A_run_scoped_records_key_is_pinned()
    {
        RedisKeys.ForRecord(
                IdempotencyScope.ForRun(new PlayerId("PLAYER_1"), new RunId("RUN_7")),
                new CommandId("CMD_9"))
            .ShouldBe("sir:idem:run:PLAYER_1:RUN_7:CMD_9");
    }

}

/// <summary>The record codec: byte round-trips of every field.</summary>
public sealed class RedisRecordCodecTests
{
    [Fact]
    public void A_record_round_trips_whole()
    {
        var record = new RecordedCommandOutcome(
            new CommandId("CMD_1"), 7, "START_RUN", """{"ChapterId":1}""",
            """{"sequence":7}""", "run:PLAYER_1:RUN_9");

        RedisRecordCodec.Decode(RedisRecordCodec.Encode(record)).ShouldBe(record);
    }

    [Fact]
    public void An_absent_opens_scope_round_trips_as_absent()
    {
        var record = new RecordedCommandOutcome(
            new CommandId("CMD_2"), 8, "ROLL_DICE", "{}", """{"sequence":8}""");

        RedisRecordCodec.Decode(RedisRecordCodec.Encode(record)).OpensScope.ShouldBeNull();
    }
}

/// <summary>The run-state cache decorator's policy, over the scripted surface and the in-memory authority.</summary>
public sealed class RedisRunStateCacheTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(48);

    private static (RedisRunStateCache Cache, ScriptedByteCache Bytes, IRunStateStore Inner,
        CacheFailureCounter Failures) Build()
    {
        var bytes = new ScriptedByteCache();
        var inner = new InMemoryRunStateStore(new AdjustableClock());
        var failures = new CacheFailureCounter();

        return (new RedisRunStateCache(bytes, inner, failures), bytes, inner, failures);
    }

    [Fact]
    public async Task A_miss_falls_back_to_the_authority_and_repopulates()
    {
        var (cache, bytes, inner, _) = Build();
        var run = PersistenceWorlds.ARun();
        await inner.SaveAsync(run, Ttl, PersistenceWorlds.Cancel);

        var read = await cache.GetAsync(run.Id, PersistenceWorlds.Cancel);

        read.ShouldNotBeNull("the authority holds the run, so a cold cache costs latency, never the run.");
        bytes.Entries.Keys.ShouldContain(
            RedisKeys.ForRunState(run.Id),
            "the fallback repopulates, or every read after a flush pays the fallback forever.");
    }

    [Fact]
    public async Task A_hit_is_served_without_the_authority()
    {
        var (cache, bytes, _, _) = Build();
        var run = PersistenceWorlds.ARun();
        await cache.SaveAsync(run, Ttl, PersistenceWorlds.Cancel);

        // The same byte surface under an EMPTY authority: only the cache layer can answer this.
        var overEmptyAuthority = new RedisRunStateCache(
            bytes, new InMemoryRunStateStore(new AdjustableClock()), new CacheFailureCounter());

        var read = await overEmptyAuthority.GetAsync(run.Id, PersistenceWorlds.Cancel);
        read.ShouldNotBeNull("the entry is in the byte surface, and a hit never consults the authority.");
        PersistenceWorlds.CanonicalBytes(read).ShouldBe(PersistenceWorlds.CanonicalBytes(run));
    }

    [Fact]
    public async Task A_save_lands_in_the_authority_before_the_cache_can_lose_it()
    {
        var (cache, bytes, inner, failures) = Build();
        bytes.Failing = true;
        var run = PersistenceWorlds.ARun();

        await cache.SaveAsync(run, Ttl, PersistenceWorlds.Cancel);

        (await inner.GetAsync(run.Id, PersistenceWorlds.Cancel)).ShouldNotBeNull(
            "the authority committed although the cache is down — a failed cache write never fails "
            + "a command.");
        failures.Count.ShouldBe(1L, "the absorbed failure leaves its one trace here.");
    }

    [Fact]
    public async Task A_cache_that_is_down_hard_still_answers_reads_from_the_authority()
    {
        var (cache, bytes, inner, _) = Build();
        var run = PersistenceWorlds.ARun();
        await inner.SaveAsync(run, Ttl, PersistenceWorlds.Cancel);
        bytes.Failing = true;

        (await cache.GetAsync(run.Id, PersistenceWorlds.Cancel)).ShouldNotBeNull(
            "a throwing cache read is a miss with a counter, never a lost run.");
    }

    [Fact]
    public async Task An_absorbed_read_failure_is_counted()
    {
        var (cache, bytes, inner, failures) = Build();
        var run = PersistenceWorlds.ARun();
        await inner.SaveAsync(run, Ttl, PersistenceWorlds.Cancel);
        bytes.Failing = true;

        await cache.GetAsync(run.Id, PersistenceWorlds.Cancel);

        failures.Count.ShouldBe(1L,
            "an absorbed failure's only trace is the counter — reads included, or a cache that is "
            + "down for reads is invisible until someone wonders where the latency went.");
    }

    [Fact]
    public async Task A_delete_removes_the_run_from_both_layers()
    {
        var (cache, bytes, inner, _) = Build();
        var run = PersistenceWorlds.ARun();
        await cache.SaveAsync(run, Ttl, PersistenceWorlds.Cancel);

        await cache.DeleteAsync(run.Id, PersistenceWorlds.Cancel);

        (await inner.GetAsync(run.Id, PersistenceWorlds.Cancel)).ShouldBeNull();
        bytes.Entries.Keys.ShouldNotContain(
            RedisKeys.ForRunState(run.Id),
            "a cache entry outliving its row would serve a deleted run until its lifetime passed.");
    }

    [Fact]
    public async Task A_delete_with_the_cache_down_still_lands_in_the_authority_and_is_counted()
    {
        var (cache, bytes, inner, failures) = Build();
        var run = PersistenceWorlds.ARun();
        await cache.SaveAsync(run, Ttl, PersistenceWorlds.Cancel);
        bytes.Failing = true;

        await cache.DeleteAsync(run.Id, PersistenceWorlds.Cancel);

        (await inner.GetAsync(run.Id, PersistenceWorlds.Cancel)).ShouldBeNull(
            "the authority's delete must land whatever the cache is doing.");
        failures.Count.ShouldBe(1L,
            "the absorbed DEL failure is counted; the stale cache entry it leaves is bounded by "
            + "its own lifetime, which is the accepted cost of never failing a command on a cache.");
    }
}

/// <summary>The idempotency cache decorator's policy: records cached, the counter never.</summary>
public sealed class RedisIdempotencyCacheTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(48);
    private static readonly PlayerId Player = new("PLAYER_cache");
    private static readonly IdempotencyScope Scope = IdempotencyScope.ForPlayer(Player);

    private static RecordedCommandOutcome Outcome(long sequence) =>
        new(new CommandId("CMD_" + sequence), sequence, "BEGIN_SESSION", "{}",
            """{"sequence":""" + sequence + "}");

    private static (RedisIdempotencyCache Cache, ScriptedByteCache Bytes, IIdempotencyStore Inner,
        CacheFailureCounter Failures) Build()
    {
        var bytes = new ScriptedByteCache();
        var inner = new InMemoryIdempotencyStore(new AdjustableClock());
        var failures = new CacheFailureCounter();

        return (new RedisIdempotencyCache(bytes, inner, failures), bytes, inner, failures);
    }

    [Fact]
    public async Task A_recorded_outcome_is_readable_from_the_cache_layer()
    {
        var (cache, bytes, _, _) = Build();
        var outcome = Outcome(1);

        await cache.RecordAsync(Scope, outcome, Ttl, PersistenceWorlds.Cancel);

        bytes.Entries.Keys.ShouldContain(RedisKeys.ForRecord(Scope, outcome.CommandId));

        // The same byte surface under an EMPTY authority: only the cache layer can answer this.
        var overEmptyAuthority = new RedisIdempotencyCache(
            bytes, new InMemoryIdempotencyStore(new AdjustableClock()), new CacheFailureCounter());

        (await overEmptyAuthority.GetRecordedOutcomeAsync(Scope, outcome.CommandId, PersistenceWorlds.Cancel))
            .ShouldBe(outcome,
                "the read must be served by the cache layer — a write-only cache would pass every "
                + "case that reads through a decorator over a live authority.");
    }

    [Fact]
    public async Task A_flushed_record_falls_back_to_the_authority_and_repopulates()
    {
        var (cache, bytes, _, _) = Build();
        var outcome = Outcome(1);
        await cache.RecordAsync(Scope, outcome, Ttl, PersistenceWorlds.Cancel);
        var key = RedisKeys.ForRecord(Scope, outcome.CommandId);
        await bytes.DeleteAsync(key, PersistenceWorlds.Cancel);

        (await cache.GetRecordedOutcomeAsync(Scope, outcome.CommandId, PersistenceWorlds.Cancel))
            .ShouldBe(outcome, "flushing the cache costs latency, never the replay.");
        bytes.Entries.Keys.ShouldContain(key, "…and the fallback repopulates.");
    }

    [Fact]
    public async Task A_record_lands_in_the_authority_although_the_cache_is_down()
    {
        var (cache, bytes, inner, failures) = Build();
        bytes.Failing = true;
        var outcome = Outcome(1);

        await cache.RecordAsync(Scope, outcome, Ttl, PersistenceWorlds.Cancel);

        (await inner.GetRecordedOutcomeAsync(Scope, outcome.CommandId, PersistenceWorlds.Cancel))
            .ShouldBe(outcome);
        failures.Count.ShouldBe(1L);
    }

    [Fact]
    public async Task The_sequence_counter_is_answered_by_the_authority_even_with_the_cache_down_hard()
    {
        var (cache, bytes, inner, failures) = Build();
        await inner.RecordAsync(Scope, Outcome(3), Ttl, PersistenceWorlds.Cancel);
        bytes.Failing = true;

        (await cache.ReadLastSequenceAsync(Scope, PersistenceWorlds.Cancel)).ShouldBe(3L,
            "the counter is deliberately never cached: a stale cached counter after a lost "
            + "write-behind would pass the gate for a sequence the authority already consumed, "
            + "which is a double-apply. Answering through a dead cache proves no cache is in the "
            + "path at all.");
        failures.Count.ShouldBe(0L, "no cache call means no absorbed failure to count.");
    }

    [Fact]
    public async Task The_missed_outcomes_are_answered_by_the_authority_even_with_the_cache_down_hard()
    {
        var (cache, bytes, inner, failures) = Build();
        await inner.RecordAsync(Scope, Outcome(1), Ttl, PersistenceWorlds.Cancel);
        await inner.RecordAsync(Scope, Outcome(2), Ttl, PersistenceWorlds.Cancel);
        bytes.Failing = true;

        (await cache.ReadOutcomesAfterAsync(Scope, 0, PersistenceWorlds.Cancel))
            .Select(o => o.Sequence)
            .ShouldBe(new[] { 1L, 2L },
                "a key-value cache cannot enumerate a scope by sequence, and it is rebuildable — "
                + "never a system of record — so it has no standing to say what a client missed. "
                + "Answering through a dead cache proves no cache is in the path at all.");
        failures.Count.ShouldBe(0L, "no cache call means no absorbed failure to count.");
    }

    [Fact]
    public async Task Opening_a_scope_reaches_the_authority()
    {
        var (cache, bytes, inner, _) = Build();
        bytes.Failing = true;
        var scope = IdempotencyScope.ForRun(Player, new RunId("RUN_open"));

        await cache.OpenScopeAsync(scope, PersistenceWorlds.Cancel);

        (await inner.ReadLastSequenceAsync(scope, PersistenceWorlds.Cancel)).ShouldBe(0L);
    }
}
