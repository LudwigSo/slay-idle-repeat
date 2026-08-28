using Shouldly;
using SlayIdleRepeat.Adapters.Cache.Redis;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Server;

/// <summary>A unit of work that only remembers what it was asked to commit.</summary>
internal sealed class RecordingInnerUnitOfWork : IUnitOfWork
{
    private readonly List<CommandCommit> _commits = [];

    internal IReadOnlyList<CommandCommit> Commits => _commits;

    /// <inheritdoc/>
    public Task CommitAsync(CommandCommit commit, CancellationToken ct)
    {
        _commits.Add(commit);

        return Task.CompletedTask;
    }
}

/// <summary>
/// The population layer above the authoritative commit: the cache is filled after the command has
/// happened, and a cache that cannot be written costs a metric rather than the command.
/// </summary>
public sealed class RedisCommitCacheTests
{
    private static readonly PlayerId Player = new("PLAYER_commit-cache");

    private static readonly TimeSpan Ttl = TimeSpan.FromHours(48);

    [Fact]
    public async Task A_commit_lands_authoritatively_and_the_record_entry_is_populated_after_it()
    {
        var bytes = new ScriptedByteCache();
        var inner = new RecordingInnerUnitOfWork();
        var failures = new CacheFailureCounter();
        var cache = new RedisCommitCache(bytes, inner, failures, Ttl);
        var commit = Commit();

        await cache.CommitAsync(commit, PersistenceWorlds.Cancel);

        inner.Commits.ShouldHaveSingleItem(
            "the cache decides nothing about whether a command happened — the unit of work "
            + "underneath it does.");
        bytes.Entries.Keys.ShouldContain(
            RedisKeys.ForRecord(commit.Scope, commit.Outcome.CommandId),
            "a record that is never populated makes every duplicate pay a database read for bytes "
            + "that can never change.");
        failures.Count.ShouldBe(0L);
    }

    [Fact]
    public async Task A_cache_that_is_not_answering_costs_a_metric_and_not_the_command()
    {
        var bytes = new ScriptedByteCache { Failing = true };
        var inner = new RecordingInnerUnitOfWork();
        var failures = new CacheFailureCounter();
        var cache = new RedisCommitCache(bytes, inner, failures, Ttl);

        await cache.CommitAsync(Commit(), PersistenceWorlds.Cancel);

        inner.Commits.ShouldHaveSingleItem(
            "the command was already committed when the population was attempted, so raising here "
            + "would report a committed command as failed and invite the client to send it again.");
        failures.Count.ShouldBeGreaterThan(
            0L, "an absorbed failure that is never counted is an outage nobody can see.");
    }

    private static CommandCommit Commit() =>
        new(
            IdempotencyScope.ForPlayer(Player),
            new RecordedCommandOutcome(
                new CommandId("CMD_commit-cache"), 1, "BEGIN_SESSION",
                """{"ClientVersion":"0.1.0","ContentHash":"sha256:abc"}""",
                """{"protocolVersion":1,"sequence":1}"""),
            Ttl,
            PersistenceWorlds.FreshProfile(),
            Array.Empty<EconomyEventRecord>(),
            OpensScope: null);
}
