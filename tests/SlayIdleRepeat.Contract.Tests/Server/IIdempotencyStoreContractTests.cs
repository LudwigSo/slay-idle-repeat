using Shouldly;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Server;

/// <summary>
/// States what <see cref="IIdempotencyStore"/> <em>means</em> (<c>23</c> §4.2, §5 A8, <c>14</c>
/// §16.3): records keyed on (scope, commandId), a per-scope counter beside them, one atomic effect.
/// </summary>
/// <remarks>
/// The atomicity of <c>RecordAsync</c> is asserted through its observable half — after a record,
/// the counter IS the record's sequence — because the failure it forbids (record without advance,
/// advance without record) is only distinguishable mid-crash, which no fixture can schedule.
/// Record expiry is the fake's and the probes' business, for the run-store suite's reason.
/// </remarks>
[ContractSuiteFor(typeof(IIdempotencyStore))]
public abstract class IIdempotencyStoreContractTests
{
    /// <summary>A generous lifetime for the cases that are not about lifetimes.</summary>
    protected static readonly TimeSpan Ttl = TimeSpan.FromHours(48);

    private static readonly PlayerId Player = new("PLAYER_idem-suite");
    private static readonly RunId Run = new("RUN_idem-suite");

    /// <summary>A store under test, over a backing of its own.</summary>
    protected abstract IIdempotencyStore Create();

    private static RecordedCommandOutcome Outcome(long sequence, string commandId = "CMD_1") =>
        new(new CommandId(commandId), sequence, "BEGIN_SESSION",
            """{"ClientVersion":"0.1.0","ContentHash":"sha256:abc"}""",
            """{"protocolVersion":1,"sequence":""" + sequence + "}");

    [Fact]
    public async Task A_command_that_was_never_recorded_reads_back_as_null()
    {
        var store = Create();

        (await store.GetRecordedOutcomeAsync(
                IdempotencyScope.ForPlayer(Player), new CommandId("CMD_never"), PersistenceWorlds.Cancel))
            .ShouldBeNull();
    }

    [Fact]
    public async Task A_recorded_outcome_reads_back_whole()
    {
        var store = Create();
        var scope = IdempotencyScope.ForPlayer(Player);
        var outcome = Outcome(1) with { OpensScope = "run:PLAYER_idem-suite:RUN_idem-suite" };

        await store.RecordAsync(scope, outcome, Ttl, PersistenceWorlds.Cancel);

        (await store.GetRecordedOutcomeAsync(scope, outcome.CommandId, PersistenceWorlds.Cancel))
            .ShouldBe(outcome,
                "every field of the record is load-bearing: the payload identity is what an "
                + "IDEMPOTENCY_CONFLICT is decided on, the response body is what a duplicate "
                + "replays, and the opens-scope marker is what repairs a crash between append and "
                + "open. All members are value types or strings, so record equality is real here.");
    }

    [Fact]
    public async Task Recording_advances_the_scopes_counter_to_the_records_sequence()
    {
        var store = Create();
        var scope = IdempotencyScope.ForRun(Player, Run);
        await store.OpenScopeAsync(scope, PersistenceWorlds.Cancel);

        await store.RecordAsync(scope, Outcome(1), Ttl, PersistenceWorlds.Cancel);

        (await store.ReadLastSequenceAsync(scope, PersistenceWorlds.Cancel)).ShouldBe(1L,
            "the record and the advance are one effect — a counter still at 0 here is the torn "
            + "state that lets a retry double-apply a committed command.");
    }

    [Fact]
    public async Task A_scope_that_was_never_opened_has_no_counter()
    {
        var store = Create();

        (await store.ReadLastSequenceAsync(
                IdempotencyScope.ForRun(Player, new RunId("RUN_unknown")), PersistenceWorlds.Cancel))
            .ShouldBeNull(
                "an unknown run scope answering 0 instead of null would turn RUN_NOT_FOUND into "
                + "SEQUENCE_GAP for every command sent at an invented run id.");
    }

    [Fact]
    public async Task Opening_a_scope_starts_its_counter_at_zero()
    {
        var store = Create();
        var scope = IdempotencyScope.ForRun(Player, Run);

        await store.OpenScopeAsync(scope, PersistenceWorlds.Cancel);

        (await store.ReadLastSequenceAsync(scope, PersistenceWorlds.Cancel)).ShouldBe(0L,
            "0 is the open scope whose first command is sequence 1; null is no scope at all.");
    }

    [Fact]
    public async Task Reopening_a_scope_resets_nothing()
    {
        var store = Create();
        var scope = IdempotencyScope.ForRun(Player, Run);
        await store.OpenScopeAsync(scope, PersistenceWorlds.Cancel);
        await store.RecordAsync(scope, Outcome(1), Ttl, PersistenceWorlds.Cancel);

        await store.OpenScopeAsync(scope, PersistenceWorlds.Cancel);

        (await store.ReadLastSequenceAsync(scope, PersistenceWorlds.Cancel)).ShouldBe(1L,
            "the replay path re-opens on every replayed opening acceptance, so an open that reset "
            + "would zero a live run's counter on a retried START_RUN.");
        (await store.GetRecordedOutcomeAsync(scope, Outcome(1).CommandId, PersistenceWorlds.Cancel))
            .ShouldNotBeNull("…and its records must survive the re-open too.");
    }

    [Fact]
    public async Task A_record_is_invisible_outside_its_scope()
    {
        var store = Create();
        var runScope = IdempotencyScope.ForRun(Player, Run);
        var playerScope = IdempotencyScope.ForPlayer(Player);
        var otherRunScope = IdempotencyScope.ForRun(Player, new RunId("RUN_other"));
        await store.OpenScopeAsync(runScope, PersistenceWorlds.Cancel);

        await store.RecordAsync(runScope, Outcome(1, "CMD_shared-id"), Ttl, PersistenceWorlds.Cancel);

        (await store.GetRecordedOutcomeAsync(playerScope, new CommandId("CMD_shared-id"), PersistenceWorlds.Cancel))
            .ShouldBeNull(
                "14 §16.3 scopes the command id to its domain: the same id in the player's lifetime "
                + "domain is a different command, and replaying the run one for it would answer a "
                + "meta command with a run outcome.");

        (await store.GetRecordedOutcomeAsync(otherRunScope, new CommandId("CMD_shared-id"), PersistenceWorlds.Cancel))
            .ShouldBeNull("…and another run's domain is just as foreign.");

        (await store.ReadLastSequenceAsync(playerScope, PersistenceWorlds.Cancel))
            .ShouldNotBe(1L, "the run record must not have advanced the player's lifetime counter.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_non_positive_lifetime_is_refused(int hours)
    {
        var store = Create();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            async () => await store.RecordAsync(
                IdempotencyScope.ForPlayer(Player), Outcome(1), TimeSpan.FromHours(hours),
                PersistenceWorlds.Cancel));
    }

    [Fact]
    public async Task A_null_outcome_is_a_null_argument_fault()
    {
        var store = Create();

        await Should.ThrowAsync<ArgumentNullException>(
            async () => await store.RecordAsync(
                IdempotencyScope.ForPlayer(Player), null!, Ttl, PersistenceWorlds.Cancel));
    }

    [Fact]
    public async Task An_already_cancelled_token_is_observed_by_every_method()
    {
        var store = Create();
        var scope = IdempotencyScope.ForPlayer(Player);
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await store.GetRecordedOutcomeAsync(scope, new CommandId("CMD_1"), source.Token));

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await store.RecordAsync(scope, Outcome(1), Ttl, source.Token));

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await store.ReadLastSequenceAsync(scope, source.Token));

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await store.OpenScopeAsync(scope, source.Token));
    }
}
