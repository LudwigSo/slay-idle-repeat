using Shouldly;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Server;

/// <summary>
/// The lifetime semantics only these fakes can pin: expiry is real here, on a clock the case
/// controls. The shared suites deliberately leave expiry out — a live store expires on wall time —
/// so this is where "the 48 h window actually closes" is held for the fake half of each pair, and
/// the compose-boot probes hold it for the live half.
/// </summary>
public sealed class InMemoryStoreExpiryTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(48);

    [Fact]
    public async Task A_run_expires_when_its_lifetime_passes_and_not_a_moment_before()
    {
        var clock = new AdjustableClock();
        var store = new InMemoryRunStateStore(clock);
        var run = PersistenceWorlds.ARun();
        await store.SaveAsync(run, Ttl, PersistenceWorlds.Cancel);

        clock.Advance(Ttl - TimeSpan.FromMinutes(1));
        (await store.GetAsync(run.Id, PersistenceWorlds.Cancel)).ShouldNotBeNull(
            "one minute inside the window, the run is alive — an early expiry is a lost run.");

        clock.Advance(TimeSpan.FromMinutes(2));
        (await store.GetAsync(run.Id, PersistenceWorlds.Cancel)).ShouldBeNull(
            "past the window, an expired row answers as absent — enforcement at read, since no "
            + "retention rule schedules deletion.");
    }

    [Fact]
    public async Task A_save_restamps_the_sliding_lifetime()
    {
        var clock = new AdjustableClock();
        var store = new InMemoryRunStateStore(clock);
        var run = PersistenceWorlds.ARun();
        await store.SaveAsync(run, Ttl, PersistenceWorlds.Cancel);

        clock.Advance(TimeSpan.FromHours(47));
        await store.SaveAsync(run, Ttl, PersistenceWorlds.Cancel);
        clock.Advance(TimeSpan.FromHours(47));

        (await store.GetAsync(run.Id, PersistenceWorlds.Cancel)).ShouldNotBeNull(
            "94 hours after the first save but 47 after the second, the run lives: the lifetime "
            + "slides from the LAST accepted command, so an active run never expires under the "
            + "player.");
    }

    [Fact]
    public async Task A_recorded_outcome_expires_when_its_lifetime_passes()
    {
        var clock = new AdjustableClock();
        var store = new InMemoryIdempotencyStore(clock);
        var scope = IdempotencyScope.ForPlayer(new PlayerId("PLAYER_expiry"));
        var outcome = new RecordedCommandOutcome(
            new CommandId("CMD_expiry"), 1, "BEGIN_SESSION", "{}", """{"sequence":1}""");
        await store.RecordAsync(scope, outcome, Ttl, PersistenceWorlds.Cancel);

        clock.Advance(Ttl - TimeSpan.FromMinutes(1));
        (await store.GetRecordedOutcomeAsync(scope, outcome.CommandId, PersistenceWorlds.Cancel))
            .ShouldNotBeNull("inside the window a duplicate must replay, not re-apply.");

        clock.Advance(TimeSpan.FromMinutes(2));
        (await store.GetRecordedOutcomeAsync(scope, outcome.CommandId, PersistenceWorlds.Cancel))
            .ShouldBeNull("48 hours from the command, the record's window has closed.");
    }

    [Fact]
    public async Task An_expired_record_leaves_a_hole_in_the_outcomes_after_a_sequence()
    {
        var clock = new AdjustableClock();
        var store = new InMemoryIdempotencyStore(clock);
        var scope = IdempotencyScope.ForPlayer(new PlayerId("PLAYER_hole"));
        await store.RecordAsync(scope, Outcome(1), Ttl, PersistenceWorlds.Cancel);
        await store.RecordAsync(scope, Outcome(2), TimeSpan.FromHours(1), PersistenceWorlds.Cancel);
        await store.RecordAsync(scope, Outcome(3), Ttl, PersistenceWorlds.Cancel);

        clock.Advance(TimeSpan.FromHours(2));
        var missed = await store.ReadOutcomesAfterAsync(scope, 0, PersistenceWorlds.Cancel);

        missed.Select(o => o.Sequence).ShouldBe(new[] { 1L, 3L },
            "a lapsed record is simply absent, so the answer has a visible GAP at 2 — and that is "
            + "the point: a caller walking the sequences sees it and orders a full resync, where a "
            + "silently renumbered list of two would have read as a complete replay.");
    }

    private static RecordedCommandOutcome Outcome(long sequence) =>
        new(new CommandId("CMD_" + sequence), sequence, "BEGIN_SESSION", "{}",
            """{"sequence":""" + sequence + "}");
}
